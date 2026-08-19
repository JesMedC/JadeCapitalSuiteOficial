using FluentAssertions;
using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.RiskProfile;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scrutor;

namespace JadeCapital.Identity.UnitTests.Persistence;

/// <summary>
/// Integration tests for the <see cref="RiskProfileAuditDecorator"/> wired
/// via Scrutor (Wave 7, slice 7a.1).
///
/// <para>
/// The <c>RiskProfileAuditDecorator</c> is BESPOKE — it does NOT use the
/// generic <c>DecoratedRepository&lt;T&gt;</c> helper. The RiskProfile
/// canonical mutation surface is <c>MarkSupersededAsync</c> (NOT
/// <c>UpdateAsync</c>) — the supersede IS the termination. The decorator
/// wraps <c>MarkSupersededAsync</c> directly + emits
/// <see cref="AuditAction.Deleted"/> + a supersession diff.
/// </para>
///
/// Four RED scenarios pinned here (per orchestrator prompt §Phase 5.1):
/// <list type="number">
///   <item>Create profile → <c>audit.events</c> row with
///         <see cref="AuditAction.Created"/>.</item>
///   <item>Supersede profile (<c>MarkSupersededAsync</c>) →
///         <c>audit.events</c> row with <see cref="AuditAction.Deleted"/>
///         + a supersession diff payload showing the state transition.</item>
///   <item>Delete profile (the slice 7a.1 defensive stub) →
///         <c>audit.events</c> row with <see cref="AuditAction.Failed"/>
///         + the decorator RE-THROWS <see cref="NotSupportedException"/>
///         with the canonical message.</item>
///   <item>Cross-tenant isolation: the audit row's <c>TenantId</c> comes
///         from <see cref="ITenantContext.Current"/> (JWT-derived), NOT
///         from any aggregate-level field.</item>
/// </list>
///
/// <para>
/// <b>Why SQLite in-memory</b>: mirrors the 6d.2
/// <c>TenantRepositoryIntegrationTests</c> pattern. The decorator's audit
/// write path is exercised end-to-end without a real Postgres or
/// Testcontainers dependency.
/// </para>
/// </summary>
public class RiskProfileRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public RiskProfileRepositoryIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = OFF;";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private (IServiceProvider sp, ITenantContext tenant, IClock clock, Guid actorUserId) BuildServices()
    {
        var identityOpts = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlite(_connection).Options;
        using (var db = new IdentityDbContext(identityOpts))
            db.Database.EnsureCreated();

        var auditOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection).Options;
        using (var db = new AuditDbContext(auditOpts))
            db.Database.EnsureCreated();

        // Wave 6 6d.2 fixture fix: force-create the audit.events table.
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
                    changes_json TEXT NULL,
                    occurred_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_audit_events_entity
                    ON events (entity_type, entity_id);";
            cmd.ExecuteNonQuery();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        var fixedNow = new DateTimeOffset(2026, 8, 19, 14, 0, 0, TimeSpan.Zero);
        services.AddSingleton<IClock>(_ => new StaticClock(fixedNow));

        services.AddDbContext<IdentityDbContext>(opts => opts.UseSqlite(_connection));
        services.AddDbContext<AuditDbContext>(opts => opts.UseSqlite(_connection));

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenant = new StaticTenantContext(new TenantId(tenantId), userId);
        services.AddSingleton<ITenantContext>(tenant);

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IRiskProfileRepository, RiskProfileRepository>();
        // The slice 7a.1 decorator registration — under test here.
        services.Decorate<IRiskProfileRepository, RiskProfileAuditDecorator>();

        var sp = services.BuildServiceProvider();
        return (sp, tenant, sp.GetRequiredService<IClock>(), userId);
    }

    private static RiskProfile NewProfile(Guid userId, IClock clock)
        => RiskProfile.Create(
            id: Guid.NewGuid(),
            userId: userId,
            capital: JadeCapital.Shared.Kernel.Money.Money.FromTrusted(10_000m, "USD"),
            maxDrawdown: JadeCapital.Identity.Domain.RiskProfile.MaxDrawdownPercent.Create(5m).Value,
            riskPerTrade: JadeCapital.Identity.Domain.RiskProfile.RiskPerTradePercent.Create(0.5m).Value,
            rrTarget: JadeCapital.Identity.Domain.RiskProfile.RiskRewardRatio.Create(2m).Value,
            clock: clock).Value;

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
    public async Task CreateProfile_WritesAuditEvent_WithActionCreated()
    {
        // Phase 5 #1: AddAsync → AuditAction.Created, no diff.
        var (sp, _, clock, actorUserId) = BuildServices();
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRiskProfileRepository>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var profile = NewProfile(actorUserId, clock);
        await repo.AddAsync(profile, CancellationToken.None);
        await identityDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(RiskProfile));
        saved.EntityId.Should().Be(profile.Id);
        saved.Action.Should().Be(AuditAction.Created);
        saved.ChangesJson.Should().BeNull("Created events carry no diff.");
    }

    [Fact]
    public async Task SupersedeProfile_WritesAuditEvent_WithActionDeleted_AndSupersessionDiff()
    {
        // Phase 5 #2: MarkSupersededAsync → AuditAction.Deleted + supersession diff.
        // RiskProfile has no SupersededBy field — the supersession diff uses
        // IsActive (true → false) + SupersededAt (null → now).
        var (sp, _, clock, actorUserId) = BuildServices();
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRiskProfileRepository>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var profile = NewProfile(actorUserId, clock);
        await repo.AddAsync(profile, CancellationToken.None);
        await identityDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents); // discard Created row

        var result = await repo.MarkSupersededAsync(profile.Id, clock, CancellationToken.None);
        result.IsSuccess.Should().BeTrue("the supersede should succeed for the active profile.");
        await identityDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Deleted);
        saved.EntityId.Should().Be(profile.Id);
        saved.ChangesJson.Should().NotBeNullOrEmpty();
        // The supersession diff must mention the IsActive + SupersededAt
        // fields (the actual aggregate fields — RiskProfile has no
        // SupersededBy field; we deviate from design.md's suggested
        // 'SupersededBy' payload key because the entity doesn't have one).
        saved.ChangesJson.Should().Contain("isActive",
            "the diff payload identifies the IsActive state transition.");
        saved.ChangesJson.Should().Contain("supersededAt",
            "the diff payload identifies the SupersededAt timestamp transition.");
    }

    [Fact]
    public async Task DeleteProfile_WritesAuditEvent_WithActionFailed_AndRethrowsNotSupported()
    {
        // Phase 5 #3: DeleteAsync → AuditAction.Failed + re-throw NotSupportedException.
        var (sp, _, clock, actorUserId) = BuildServices();
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRiskProfileRepository>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var profile = NewProfile(actorUserId, clock);
        await repo.AddAsync(profile, CancellationToken.None);
        await identityDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents); // discard Created row

        var act = async () => await repo.DeleteAsync(profile, CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*MarkSupersededAsync*");

        await identityDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Failed);
        saved.EntityId.Should().Be(profile.Id);
    }

    [Fact]
    public async Task AuditEvent_TenantIdDerivesFrom_ITenantContextCurrent()
    {
        // Phase 5 #4: cross-tenant isolation — the audit row's TenantId
        // comes from ITenantContext.Current (JWT-derived), not from any
        // aggregate-level field. The RiskProfileAuditDecorator reads the
        // tenant context directly (it doesn't go through DecoratedRepository<T>
        // so the tenant context plumbing is bespoke).
        var (sp, tenant, clock, actorUserId) = BuildServices();
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRiskProfileRepository>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var profile = NewProfile(actorUserId, clock);
        await repo.AddAsync(profile, CancellationToken.None);
        await identityDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.TenantId.Should().Be(tenant.Current!.Value,
            "the audit row's TenantId comes from ITenantContext.Current (JWT-derived).");
    }
}