using FluentAssertions;
using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Users;
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
/// Integration tests for the <see cref="UserAuditDecorator"/> wired via
/// Scrutor (Wave 7, slice 7a.1).
///
/// <para>
/// Five RED scenarios pinned here (per orchestrator prompt §Phase 4.1):
/// </para>
/// <list type="number">
///   <item>Create user → <c>audit.events</c> row with <c>AuditAction.Created</c>.</item>
///   <item>Update user → <c>audit.events</c> row with <c>AuditAction.Updated</c>
///         + a diff payload reflecting the change (e.g. displayName change).</item>
///   <item>Delete user (the slice 7a.1 defensive stub) → <c>audit.events</c>
///         row with <c>AuditAction.Failed</c> + the decorator RE-THROWS
///         <see cref="NotSupportedException"/> with the canonical message.</item>
///   <item>Cross-tenant isolation: the audit row's <c>TenantId</c> comes from
///         <see cref="ITenantContext.Current"/> (the JWT-derived tenant),
///         NOT from any user-level field.</item>
///   <item>The audit event includes the <c>tenant_id</c> + the actor's
///         <c>user_id</c> + the <c>entity_type</c> — every audit row is
///         fully attributable.</item>
/// </list>
///
/// <para>
/// <b>Why SQLite in-memory</b>: mirrors the 6d.2
/// <c>TenantRepositoryIntegrationTests</c> pattern. Lets the EF-level
/// behavior of the audit write path be exercised end-to-end without a
/// real Postgres or Testcontainers dependency.
/// </para>
/// </summary>
public class UserRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public UserRepositoryIntegrationTests()
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
        // IdentityDbContext + AuditDbContext share a single SQLite connection.
        var identityOpts = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlite(_connection).Options;
        using (var db = new IdentityDbContext(identityOpts))
            db.Database.EnsureCreated();

        var auditOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection).Options;
        using (var db = new AuditDbContext(auditOpts))
            db.Database.EnsureCreated();

        // Wave 6 6d.2 fixture fix: EF Core 9 SQLite EnsureCreated is
        // all-or-nothing; force-create the audit.events table via raw SQL.
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
        services.AddScoped<IUserRepository, UserRepository>();
        // The slice 7a.1 decorator registration — under test here.
        services.Decorate<IUserRepository, UserAuditDecorator>();

        var sp = services.BuildServiceProvider();
        return (sp, tenant, sp.GetRequiredService<IClock>(), userId);
    }

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
    public async Task CreateUser_WritesAuditEvent_WithActionCreated()
    {
        // Phase 4 #1: AddAsync → AuditAction.Created, no diff.
        var (sp, _, _, actorUserId) = BuildServices();
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var user = User.Register(
            actorUserId, "alice@example.com", "Alice", "hash", UserRole.Trader).Value;
        await repo.AddAsync(user, CancellationToken.None);
        await identityDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(User));
        saved.EntityId.Should().Be(user.Id);
        saved.Action.Should().Be(AuditAction.Created);
        saved.ChangesJson.Should().BeNull("Created events carry no diff.");
    }

    [Fact]
    public async Task UpdateUser_WritesAuditEvent_WithActionUpdatedAndDiff()
    {
        // Phase 4 #2: UpdateAsync → AuditAction.Updated + diff (displayName change).
        var (sp, _, _, actorUserId) = BuildServices();
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var user = User.Register(
            actorUserId, "alice@example.com", "Alice", "hash", UserRole.Trader).Value;
        await repo.AddAsync(user, CancellationToken.None);
        await identityDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents); // discard Created row

        user.ChangeDisplayName("Alice Cooper");
        await repo.UpdateAsync(user, CancellationToken.None);
        await identityDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Updated);
        saved.ChangesJson.Should().NotBeNullOrEmpty();
        saved.ChangesJson.Should().Contain("displayName",
            "the diff payload identifies the changed field.");
    }

    [Fact]
    public async Task DeleteUser_WritesAuditEvent_WithActionFailed_AndRethrowsNotSupported()
    {
        // Phase 4 #3: DeleteAsync → AuditAction.Failed + re-throw NotSupportedException.
        var (sp, _, _, actorUserId) = BuildServices();
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var user = User.Register(
            actorUserId, "alice@example.com", "Alice", "hash", UserRole.Trader).Value;
        await repo.AddAsync(user, CancellationToken.None);
        await identityDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents); // discard Created row

        var act = async () => await repo.DeleteAsync(user, CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Tenant reassignment*");

        await identityDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Failed);
        saved.EntityId.Should().Be(user.Id);
    }

    [Fact]
    public async Task AuditEvent_TenantIdDerivesFrom_ITenantContextCurrent()
    {
        // Phase 4 #4: cross-tenant isolation — the audit row's TenantId
        // comes from ITenantContext.Current (JWT-derived), not from any
        // aggregate-level field.
        var (sp, tenant, _, actorUserId) = BuildServices();
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var user = User.Register(
            actorUserId, "alice@example.com", "Alice", "hash", UserRole.Trader).Value;
        await repo.AddAsync(user, CancellationToken.None);
        await identityDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.TenantId.Should().Be(tenant.Current!.Value,
            "the audit row's TenantId comes from ITenantContext.Current (JWT-derived).");
    }

    [Fact]
    public async Task AuditEvent_FullyAttributable_EntityTypeUserAndUserId()
    {
        // Phase 4 #5: every audit row carries EntityType + EntityId + TenantId
        // + UserId — fully attributable for compliance review.
        var (sp, tenant, _, actorUserId) = BuildServices();
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var user = User.Register(
            actorUserId, "alice@example.com", "Alice", "hash", UserRole.Trader).Value;
        await repo.AddAsync(user, CancellationToken.None);
        await identityDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(User));
        saved.EntityId.Should().Be(user.Id);
        saved.TenantId.Should().Be(tenant.Current!.Value);
        saved.UserId.Should().Be(tenant.CurrentUserId);
    }
}