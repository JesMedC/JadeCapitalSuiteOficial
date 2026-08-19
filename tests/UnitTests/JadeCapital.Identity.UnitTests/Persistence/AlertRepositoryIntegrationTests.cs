using FluentAssertions;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.Coaching;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Alerts;
using JadeCapital.Trading.Infrastructure.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using DomainSeverity = JadeCapital.Trading.Domain.Behavioral.Severity;

namespace JadeCapital.Identity.UnitTests.Persistence;

/// <summary>
/// Integration tests for the <see cref="AlertAuditDecorator"/> wired via
/// Scrutor (Wave 8, slice 8a.2).
///
/// <para>
/// Mirrors the <see cref="JournalEntryAuditDecorator"/> shape (bespoke —
/// does NOT use the generic <c>DecoratedRepository&lt;T&gt;</c> helper
/// because <see cref="IAlertRepository"/> is bespoke with the
/// <c>ListByUserAsync(userId, activeOnly, now, ct)</c> read method that
/// scopes the read to a specific user). The bespoke decorator preserves
/// the userId-scoped reads — extending <c>IRepository&lt;Alert&gt;</c>
/// would force a parameterless list that ignores cross-user scope.
/// </para>
///
/// <para>
/// <b>CRITICAL — slice 8a.2 bespoke deviation</b>: the
/// <see cref="IAlertRepository.AddAsync(Alert, CancellationToken)"/>
/// method returns <c>bool</c> (true = row inserted, false = row rejected
/// by the <c>ux_alerts_user_rule_day</c> UNIQUE INDEX dedup). The
/// decorator MUST inspect the return value after the inner call:
/// <list type="bullet">
///   <item><c>true</c> → emit <see cref="AuditAction.Created"/>.</item>
///   <item><c>false</c> → emit NO audit row (the row was not created;
///         the existing row's audit history is preserved).</item>
/// </list>
/// This matches the orchestrator's preflight decision 6 + design.md §3.
/// </para>
///
/// Five RED scenarios pinned here (per tasks.md §8a.2 Phase 1):
/// <list type="number">
///   <item>Create alert (AddAsync returns true, dedup miss) → audit row
///         with <see cref="AuditAction.Created"/>, <c>EntityType = "Alert"</c>,
///         <c>TenantId</c> + <c>UserId</c> from <see cref="ITenantContext"/>,
///         <c>ChangesJson</c> = null.</item>
///   <item>Create alert (AddAsync returns false, dedup hit) → NO audit
///         row written. The decorator silently skips when the inner
///         returns false (the row was not created — preserving the
///         existing row's audit history).</item>
///   <item>Acknowledge alert (UpdateAsync after <see cref="Alert.Acknowledge"/>)
///         → audit row with <see cref="AuditAction.Updated"/> + diff
///         identifying the <c>acknowledgedAt</c> transition (null → now).</item>
///   <item>Cross-tenant update attempt → <see cref="AuditAction.Denied"/>
///         audit row + <see cref="UnauthorizedAccessException"/> thrown.
///         The inner <c>UpdateAsync</c> is NEVER reached.</item>
///   <item><c>GetByIdAsync(id, userId, ct)</c> (the bespoke user-scoped
///         read) → no audit event. Reads are not audited (matches the
///         Wave 6 + 7a.1 + 7b.1 + 7b.2 + 8a.1 precedent).</item>
/// </list>
///
/// <para>
/// <b>Why a focused <c>TestAlertDbContext</c></b>: mirrors the
/// 8a.1 AccountRepositoryIntegrationTests.TestAccountDbContext +
/// JournalEntryRepositoryIntegrationTests.TestJournalDbContext pattern.
/// The production <c>TradingDbContext</c> pulls in Npgsql-specific
/// converters (Money complex type + JournalEntry.Tags) that fail to
/// compose on SQLite. A focused helper DbContext keeps the model
/// SQLite-compatible without touching the production schema.
/// </para>
/// </summary>
public class AlertRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public AlertRepositoryIntegrationTests()
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

    /// <summary>
    /// SQLite-compatible test DbContext — maps only <see cref="Alert"/>
    /// + <see cref="AuditEvent"/>. Sidesteps the Npgsql-specific converters
    /// the production TradingDbContext pulls in.
    /// </summary>
    private sealed class TestAlertDbContext : DbContext
    {
        public TestAlertDbContext(DbContextOptions<TestAlertDbContext> options) : base(options) { }

        public DbSet<Alert> Alerts => Set<Alert>();
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("trading");
            modelBuilder.Entity<Alert>(b =>
            {
                b.ToTable("alerts");
                b.HasKey(a => a.Id);
                b.Property(a => a.Id).HasColumnName("id");
                b.Property(a => a.UserId).HasColumnName("user_id").IsRequired();
                b.Property(a => a.RuleId).HasColumnName("rule_id").HasMaxLength(Alert.MaxRuleIdLength).IsRequired();
                b.Property(a => a.Severity).HasColumnName("severity").HasConversion<byte>().IsRequired();
                b.Property(a => a.Title).HasColumnName("title").HasMaxLength(Alert.MaxTitleLength).IsRequired();
                b.Property(a => a.Body).HasColumnName("body").HasMaxLength(Alert.MaxBodyLength).IsRequired();
                b.Property(a => a.CtaRoute).HasColumnName("cta_route").HasMaxLength(255).IsRequired();
                b.Property(a => a.CtaLabel).HasColumnName("cta_label").HasMaxLength(64).IsRequired();
                b.Property(a => a.AcknowledgedAt).HasColumnName("acknowledged_at");
                b.Property(a => a.ExpiresAt).HasColumnName("expires_at");
                b.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(a => a.UpdatedAt).HasColumnName("updated_at");
                b.Ignore(a => a.DomainEvents);
            });
            modelBuilder.Entity<AuditEvent>(b =>
            {
                b.ToTable("events");
                b.HasKey(e => e.Id);
                b.Property(e => e.Id).HasColumnName("id");
                b.Property(e => e.EntityType).HasColumnName("entity_type").HasMaxLength(80).IsRequired();
                b.Property(e => e.EntityId).HasColumnName("entity_id").IsRequired();
                b.Property(e => e.Action).HasColumnName("action").HasConversion<byte>().IsRequired();
                b.Property(e => e.TenantId).HasColumnName("tenant_id");
                b.Property(e => e.UserId).HasColumnName("user_id");
                b.Property(e => e.ChangesJson).HasColumnName("changes_json");
                b.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
                b.Ignore(e => e.CreatedAt);
                b.Ignore(e => e.UpdatedAt);
                b.Ignore(e => e.DomainEvents);
            });
        }
    }

    /// <summary>
    /// Test-only <see cref="IAlertRepository"/> impl — mirrors the
    /// production <c>AlertRepository</c> methods needed by the decorator
    /// + integration tests (AddAsync returning bool, UpdateAsync,
    /// GetByIdAsync, ListByUserAsync). The bespoke ListByUserAsync is NOT
    /// exercised by the audit-decorator integration tests but is required
    /// by the interface.
    ///
    /// <para>
    /// <c>DedupNext</c> flag controls whether <see cref="AddAsync"/> returns
    /// false (simulating the DB unique-violation dedup). When false (the
    /// default), the inner inserts and returns true.
    /// </para>
    /// </summary>
    private sealed class TestAlertRepository : IAlertRepository
    {
        private readonly TestAlertDbContext _db;
        public bool DedupNext { get; set; }

        public TestAlertRepository(TestAlertDbContext db) { _db = db; }

        // Bespoke read methods — not exercised by these tests, but required
        // by IAlertRepository. Stub implementations.
        public async Task<IReadOnlyList<Alert>> ListByUserAsync(
            Guid userId, bool activeOnly, DateTimeOffset now, CancellationToken ct)
            => await _db.Alerts
                .Where(a => a.UserId == userId
                    && (!activeOnly
                        || (a.AcknowledgedAt == null && (a.ExpiresAt == null || a.ExpiresAt > now))))
                .ToListAsync(ct);

        public Task<Alert?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct)
            => _db.Alerts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);

        public async Task<bool> AddAsync(Alert alert, CancellationToken ct)
        {
            if (DedupNext)
            {
                DedupNext = false;
                return false;
            }
            await _db.Alerts.AddAsync(alert, ct);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public Task UpdateAsync(Alert alert, CancellationToken ct)
        {
            var entry = _db.Entry(alert);
            if (entry.State == EntityState.Detached)
            {
                _db.Alerts.Update(alert);
            }
            return Task.CompletedTask;
        }
    }

    private (IServiceProvider sp, ITenantContext tenant, IClock clock, Guid currentUserId)
        BuildServices(Guid? currentUserId = null)
    {
        var alertOpts = new DbContextOptionsBuilder<TestAlertDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new TestAlertDbContext(alertOpts))
            db.Database.EnsureCreated();

        var auditOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new AuditDbContext(auditOpts))
            db.Database.EnsureCreated();

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

        services.AddDbContext<TestAlertDbContext>(opts => opts.UseSqlite(_connection));
        var auditContextOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        services.AddScoped<AuditDbContext>(_ => new AuditDbContext(auditContextOpts));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestAlertDbContext>());

        var tenantId = Guid.NewGuid();
        var userId = currentUserId ?? Guid.NewGuid();
        var tenant = new StaticTenantContext(new TenantId(tenantId), userId);
        services.AddSingleton<ITenantContext>(tenant);

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IAlertRepository, TestAlertRepository>();
        // The slice 8a.2 decorator registration — under test here.
        services.Decorate<IAlertRepository, AlertAuditDecorator>();

        var sp = services.BuildServiceProvider();
        return (sp, tenant, sp.GetRequiredService<IClock>(), userId);
    }

    private AuditDbContext NewAuditDbContext()
    {
        var opts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new AuditDbContext(opts);
    }

    private static Alert CreateAlert(Guid userId, IClock clock)
        => Alert.Create(
            userId: userId,
            ruleId: "NoTradesInDaysRule",
            severity: DomainSeverity.High,
            title: "No trades in 5 days",
            body: "You have not opened a trade in 5 days. Consider reviewing your setups.",
            ctaRoute: "/trades/recent",
            ctaLabel: "Open recent trades",
            expiresAt: null,
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
    public async Task CreateAlert_ReturnsTrue_WritesAuditEvent_WithActionCreated()
    {
        // Phase 1 #1: AddAsync → true (dedup miss) → AuditAction.Created.
        // No diff. EntityType = "Alert" + UserId/TenantId from ITenantContext.
        var userId = Guid.NewGuid();
        var (sp, tenant, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var alertDb = scope.ServiceProvider.GetRequiredService<TestAlertDbContext>();
        var auditDb = NewAuditDbContext();

        var alert = CreateAlert(userId, clock);
        var inserted = await repo.AddAsync(alert, CancellationToken.None);
        inserted.Should().BeTrue("the test inner is configured to insert (no dedup).");
        await alertDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(Alert));
        saved.EntityId.Should().Be(alert.Id);
        saved.Action.Should().Be(AuditAction.Created);
        saved.ChangesJson.Should().BeNull("Created events carry no diff.");
        saved.TenantId.Should().Be(tenant.Current!.Value);
        saved.UserId.Should().Be(tenant.CurrentUserId);
    }

    [Fact]
    public async Task CreateAlert_ReturnsFalse_DoesNotWriteAuditEvent()
    {
        // Phase 1 #2: AddAsync → false (dedup hit by the partial UNIQUE INDEX
        // ux_alerts_user_rule_day). The decorator MUST inspect the return
        // value and silently skip — no audit row written. This preserves
        // the existing row's audit history (the dedup target already has
        // its own Created audit row from the original insert).
        //
        // Uses BuildServicesWithDedup which configures the inner test
        // repository's DedupNext = true so the next AddAsync returns
        // false (simulating the production dedup).
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServicesWithDedup(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var alertDb = scope.ServiceProvider.GetRequiredService<TestAlertDbContext>();
        var auditDb = NewAuditDbContext();

        var alert = CreateAlert(userId, clock);
        var inserted = await repo.AddAsync(alert, CancellationToken.None);
        inserted.Should().BeFalse("the test inner is configured to dedup (return false).");
        await alertDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var auditRows = await auditDb.AuditEvents.ToListAsync();
        auditRows.Should().BeEmpty(
            "when AddAsync returns false (dedup hit), the decorator MUST NOT " +
            "emit a Created audit row — the row was not created and the " +
            "existing row's audit history is preserved.");
    }

    [Fact]
    public async Task AcknowledgeAlert_WritesAuditEvent_WithActionUpdatedAndDiff()
    {
        // Phase 1 #3: Acknowledge() mutates the alert (AcknowledgedAt:
        // null → now). UpdateAsync → AuditAction.Updated + diff that
        // identifies the acknowledgedAt field change.
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var alertDb = scope.ServiceProvider.GetRequiredService<TestAlertDbContext>();
        var auditDb = NewAuditDbContext();

        var alert = CreateAlert(userId, clock);
        await repo.AddAsync(alert, CancellationToken.None);
        await alertDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        var ack = alert.Acknowledge(clock);
        ack.IsSuccess.Should().BeTrue("Acknowledge is the production transition.");
        await repo.UpdateAsync(alert, CancellationToken.None);
        await alertDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Updated);
        saved.ChangesJson.Should().NotBeNullOrEmpty();
        saved.ChangesJson.Should().Contain("acknowledgedAt",
            "the diff payload identifies the acknowledgedAt field change " +
            "(null → now transition).");
    }

    [Fact]
    public async Task CrossTenantUpdate_LogsAuditEvent_WithActionDenied_AndThrowsUnauthorized()
    {
        // Phase 1 #4: a different user attempts to update another user's
        // alert (Acknowledge). The decorator detects the ownership
        // mismatch (alert.UserId != currentUserId), logs a Denied audit
        // row for the ATTEMPT (security trail), and throws
        // UnauthorizedAccessException. The inner UpdateAsync is NEVER
        // reached.
        var ownerUserId = Guid.NewGuid();
        var attackerUserId = Guid.NewGuid(); // != ownerUserId
        var (sp, _, clock, _) = BuildServices(currentUserId: attackerUserId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var alertDb = scope.ServiceProvider.GetRequiredService<TestAlertDbContext>();
        var auditDb = NewAuditDbContext();

        // Setup: create the alert as the OWNER (bypass the decorator for
        // the setup so the ownership check doesn't fire here).
        var alert = CreateAlert(ownerUserId, clock);
        alertDb.Alerts.Add(alert);
        await alertDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        // Attacker attempts to Acknowledge the owner's alert.
        var ack = alert.Acknowledge(clock);
        ack.IsSuccess.Should().BeTrue("Acknowledge itself is in-memory and doesn't enforce ownership.");
        Func<Task> act = async () => await repo.UpdateAsync(alert, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "the decorator rejects cross-tenant update attempts.");

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Denied);
        saved.EntityId.Should().Be(alert.Id);
        saved.UserId.Should().Be(attackerUserId,
            "the audit trail records WHO attempted the cross-tenant access, " +
            "not the owner.");
    }

    [Fact]
    public async Task GetByIdAsync_WritesNoAuditEvent()
    {
        // Phase 1 #5: the bespoke user-scoped read (GetByIdAsync) emits
        // NO audit event. Reads are not audited (matches the Wave 6 + 7 +
        // 8a.1 precedent: only mutations get audit rows).
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var alertDb = scope.ServiceProvider.GetRequiredService<TestAlertDbContext>();
        var auditDb = NewAuditDbContext();

        var alert = CreateAlert(userId, clock);
        alertDb.Alerts.Add(alert);
        await alertDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        var found = await repo.GetByIdAsync(alert.Id, userId, CancellationToken.None);
        found.Should().NotBeNull();
        await auditDb.SaveChangesAsync();

        var auditRows = await auditDb.AuditEvents.ToListAsync();
        auditRows.Should().BeEmpty(
            "GetByIdAsync is a read — no audit event should be written " +
            "(matches the Wave 6 + 7 + 8a.1 precedent).");
    }

    /// <summary>
    /// Variant of <see cref="BuildServices"/> that configures the test
    /// inner repository to simulate a dedup (return false on the next
    /// AddAsync call). The TestAlertRepository.DedupNext flag is set in
    /// the factory delegate so Scrutor's decorator wrap is preserved.
    /// </summary>
    private (IServiceProvider sp, ITenantContext tenant, IClock clock, Guid currentUserId)
        BuildServicesWithDedup(Guid? currentUserId = null)
    {
        var alertOpts = new DbContextOptionsBuilder<TestAlertDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new TestAlertDbContext(alertOpts))
            db.Database.EnsureCreated();

        var auditOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new AuditDbContext(auditOpts))
            db.Database.EnsureCreated();

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

        services.AddDbContext<TestAlertDbContext>(opts => opts.UseSqlite(_connection));
        var auditContextOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        services.AddScoped<AuditDbContext>(_ => new AuditDbContext(auditContextOpts));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestAlertDbContext>());

        var tenantId = Guid.NewGuid();
        var userId = currentUserId ?? Guid.NewGuid();
        var tenant = new StaticTenantContext(new TenantId(tenantId), userId);
        services.AddSingleton<ITenantContext>(tenant);

        services.AddScoped<IAuditLogger, AuditLogger>();
        // Configure the inner test repo to dedup on the next AddAsync call.
        services.AddScoped<IAlertRepository>(sp =>
        {
            var db = sp.GetRequiredService<TestAlertDbContext>();
            return new TestAlertRepository(db) { DedupNext = true };
        });
        services.Decorate<IAlertRepository, AlertAuditDecorator>();

        var sp2 = services.BuildServiceProvider();
        return (sp2, tenant, sp2.GetRequiredService<IClock>(), userId);
    }
}
