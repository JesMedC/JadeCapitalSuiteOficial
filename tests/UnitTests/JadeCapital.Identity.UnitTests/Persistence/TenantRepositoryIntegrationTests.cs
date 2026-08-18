using FluentAssertions;
using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Identity.Infrastructure.Persistence.Configurations;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Scrutor;

namespace JadeCapital.Identity.UnitTests.Persistence;

/// <summary>
/// Integration tests for the <see cref="TenantAuditDecorator"/> wired via
/// Scrutor (Wave 6, slice 6d.2).
///
/// <para>
/// Five RED scenarios pinned here (per tasks.md line 478):
/// </para>
/// <list type="number">
///   <item>Create tenant → <c>audit.events</c> row with <c>AuditAction.Created</c>.</item>
///   <item>Update tenant → <c>audit.events</c> row with <c>AuditAction.Updated</c>
///         + a diff payload reflecting the change.</item>
///   <item>Delete tenant → <c>audit.events</c> row with <c>AuditAction.Deleted</c>.</item>
///   <item>Cross-tenant isolation: the audit row's <c>TenantId</c> comes from
///         <see cref="ITenantContext.Current"/> (the JWT-derived tenant), NOT
///         from any aggregate-level field.</item>
///   <item>The audit event includes the tenant_id + the actor's user_id + the
///         entity type — every audit row is fully attributable.</item>
/// </list>
///
/// <para>
/// <b>Why SQLite in-memory</b>: mirrors the 6d.1 <c>ImportJobSoftDeleteQueryFilterTests</c>
/// pattern. Lets the EF-level behavior of the audit write path be exercised
/// end-to-end without a real Postgres or Testcontainers dependency.
/// </para>
/// <para>
/// <b>Why two DbContexts in one provider</b>: the audit write path uses
/// <see cref="AuditDbContext"/> (separate schema), while the tenant
/// repository uses <see cref="IdentityDbContext"/>. The test wires both via
/// the same SQLite connection so the test verifies the cross-DbContext flow
/// the production code uses.
/// </para>
/// </summary>
public class TenantRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public TenantRepositoryIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        // SQLite enforces FKs by default; the IdentityDbContext model wires
        // a FK from tenants to users. The test only needs tenants + audit
        // events, so we disable FKs for the in-memory connection.
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = OFF;";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Builds the test service collection. Wires the production
    /// <see cref="TenantRepository"/> + <see cref="TenantAuditDecorator"/>
    /// + <see cref="AuditLogger"/> + both DbContexts on a shared SQLite
    /// connection. This is the closest-to-production setup the slice has.
    /// </summary>
    private (IServiceProvider sp, ITenantContext tenant, IClock clock, IAuditLogger audit)
        BuildServices()
    {
        // EnsureCreated on a per-DbContext basis: EF's EnsureCreated
        // returns early if ANY table exists, so calling EnsureCreated on
        // AuditDbContext after IdentityDbContext already ran would skip the
        // audit.events table. The 6d.1 ImportJobSoftDeleteQueryFilterTests
        // pattern uses a separate DbContext for the same reason. We do the
        // EnsureCreated for each context explicitly via a transient options
        // builder that doesn't pollute the DI container.
        var identityOpts = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new IdentityDbContext(identityOpts))
            db.Database.EnsureCreated();

        var auditOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new AuditDbContext(auditOpts))
            db.Database.EnsureCreated();

        // FIX (Wave 6, slice 6d.2): EF Core 9 SQLite EnsureCreated is
        // "all-or-nothing" — once ANY table exists on the shared
        // connection, every subsequent EnsureCreated call is a no-op.
        // The first call above (IdentityDbContext) created the `tenants`
        // table; the second (AuditDbContext) was a no-op so the
        // `events` table was never created. Force-create it via raw SQL
        // matching AuditEventConfiguration. Documented as a deviation in
        // apply-progress-wave6-slice-6d-2.md (Phase 3.2 fixture fix).
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS events (
                    id BLOB NOT NULL PRIMARY KEY,
                    entity_type TEXT NOT NULL,
                    entity_id BLOB NOT NULL,
                    action INTEGER NOT NULL,
                    tenant_id BLOB NULL,
                    user_id BLOB NULL,
                    changes TEXT NULL,
                    occurred_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_audit_events_entity
                    ON events (entity_type, entity_id);
                CREATE INDEX IF NOT EXISTS ix_audit_events_tenant_time
                    ON events (tenant_id, occurred_at);
                CREATE INDEX IF NOT EXISTS ix_audit_events_user
                    ON events (user_id);";
            cmd.ExecuteNonQuery();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(_ => new StaticClock(
            new DateTimeOffset(2026, 8, 19, 14, 0, 0, TimeSpan.Zero)));

        // IdentityDbContext — tenant table lives here.
        services.AddDbContext<IdentityDbContext>(opts =>
            opts.UseSqlite(_connection));
        // AuditDbContext — audit.events table lives here.
        services.AddDbContext<AuditDbContext>(opts =>
            opts.UseSqlite(_connection));

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenant = new StaticTenantContext(
            new TenantId(tenantId), userId);
        services.AddSingleton<ITenantContext>(tenant);

        // Register the real AuditLogger (so the SQLite audit.events table
        // receives the writes end-to-end) + the per-aggregate audit decorator.
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.Decorate<ITenantRepository, TenantAuditDecorator>();

        var sp = services.BuildServiceProvider();
        // AuditLogger singleton for direct verification queries.
        var audit = sp.GetRequiredService<IAuditLogger>();
        return (sp, tenant, sp.GetRequiredService<IClock>(), audit);
    }

    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 19, 14, 0, 0, TimeSpan.Zero);

    private sealed class StaticClock : IClock
    {
        public StaticClock(DateTimeOffset now) { UtcNow = now; }
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class StaticTenantContext : ITenantContext
    {
        public StaticTenantContext(TenantId? current, Guid? currentUserId)
        {
            Current = current;
            CurrentUserId = currentUserId;
        }
        public TenantId? Current { get; }
        public Guid? CurrentUserId { get; }
        public bool IsSuperAdmin => false;
    }

    [Fact]
    public async Task CreateTenant_WritesAuditEvent_WithActionCreated()
    {
        // Phase 3 #1: AddAsync → audit row with AuditAction.Created.
        var (sp, tenant, _, _) = BuildServices();
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var t = Tenant.Create(Guid.NewGuid(), "Acme", "acme", Guid.NewGuid(), TenantPlan.Personal, clock).Value;

        await repo.AddAsync(t, CancellationToken.None);
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(Tenant));
        saved.EntityId.Should().Be(t.Id);
        saved.Action.Should().Be(AuditAction.Created);
        saved.ChangesJson.Should().BeNull("Created events carry no diff.");
    }

    [Fact]
    public async Task UpdateTenant_WritesAuditEvent_WithActionUpdatedAndDiff()
    {
        // Phase 3 #2: UpdateAsync → audit row with AuditAction.Updated + diff.
        var (sp, tenant, _, _) = BuildServices();
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var t = Tenant.Create(Guid.NewGuid(), "Acme", "acme", Guid.NewGuid(), TenantPlan.Personal, clock).Value;
        await repo.AddAsync(t, CancellationToken.None);
        await identityDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents); // discard Created row

        t.Rename("Acme Corp", clock);
        await repo.UpdateAsync(t, CancellationToken.None);
        await identityDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Updated);
        saved.ChangesJson.Should().NotBeNullOrEmpty();
        saved.ChangesJson.Should().Contain("name", "the diff payload identifies the changed field.");
    }

    [Fact]
    public async Task DeleteTenant_WritesAuditEvent_WithActionDeleted()
    {
        // Phase 3 #3: DeleteAsync → audit row with AuditAction.Deleted.
        var (sp, tenant, _, _) = BuildServices();
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var t = Tenant.Create(Guid.NewGuid(), "Acme", "acme", Guid.NewGuid(), TenantPlan.Personal, clock).Value;
        await repo.AddAsync(t, CancellationToken.None);
        await identityDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents); // discard Created row

        await repo.DeleteAsync(t, CancellationToken.None);
        await identityDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Deleted);
        saved.EntityId.Should().Be(t.Id);
    }

    [Fact]
    public async Task AuditEvent_TenantIdDerivesFrom_ITenantContextCurrent()
    {
        // Phase 3 #4: cross-tenant isolation enforced at the audit row.
        var (sp, tenant, _, _) = BuildServices();
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var t = Tenant.Create(Guid.NewGuid(), "Acme", "acme", Guid.NewGuid(), TenantPlan.Personal, clock).Value;

        await repo.AddAsync(t, CancellationToken.None);
        await identityDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.TenantId.Should().Be(tenant.Current!.Value,
            "the audit row's TenantId comes from ITenantContext.Current (JWT-derived).");
    }

    [Fact]
    public async Task AuditEvent_FullyAttributable_EntityTypeTenantAndUser()
    {
        // Phase 3 #5: every audit row carries EntityType + EntityId + TenantId
        // + UserId — fully attributable for compliance review.
        var (sp, tenant, _, _) = BuildServices();
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var t = Tenant.Create(Guid.NewGuid(), "Acme", "acme", Guid.NewGuid(), TenantPlan.Personal, clock).Value;

        await repo.AddAsync(t, CancellationToken.None);
        await identityDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(Tenant));
        saved.EntityId.Should().Be(t.Id);
        saved.TenantId.Should().Be(tenant.Current!.Value);
        saved.UserId.Should().Be(tenant.CurrentUserId);
        saved.OccurredAt.Should().Be(FixedNow);
    }
}