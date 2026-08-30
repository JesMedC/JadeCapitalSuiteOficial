using FluentAssertions;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Contracts.Planner;
using JadeCapital.Trading.Domain.Planner;
using JadeCapital.Trading.Infrastructure.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Identity.UnitTests.Persistence;

/// <summary>
/// Integration tests for the <see cref="PlannerSessionAuditDecorator"/> wired
/// via Scrutor (Wave 8, slice 8a.3).
///
/// <para>
/// Mirrors the <see cref="TradeAuditDecorator"/> shape (Wave 7 7b.1) — bespoke,
/// does NOT use the generic <c>DecoratedRepository&lt;T&gt;</c> helper because
/// <see cref="IPlannerSessionRepository"/> is bespoke with cross-user-scoped
/// read methods (<c>ListByUserAndWeekAsync</c>, <c>ExistsForDateAsync</c>,
/// <c>GetWeekComparisonAsync</c>). The bespoke decorator preserves the full
/// interface surface.
/// </para>
///
/// <para>
/// <b>CRITICAL — slice 8a.3 bespoke deviation (orchestrator preflight
/// decision 8)</b>: <see cref="IPlannerSessionRepository.UpdateAsync(PlannerSession, CancellationToken)"/>
/// emits <see cref="AuditAction.Updated"/> by default but is upgraded to
/// <see cref="AuditAction.Deleted"/> when the entity's
/// <see cref="PlannerSession.Status"/> == <see cref="PlannerStatus.Cancelled"/>
/// — the lifecycle-terminated value (mirrors the Wave 6 6d.2 rule
/// <c>Status ∈ {Cancelled, Terminated, Expired}</c>).
/// </para>
/// <para>
/// The decorator re-implements the <c>IsTerminated</c> reflection check
/// locally — <c>PlannerStatus.Cancelled</c> is the single terminated value
/// for this aggregate (no <see cref="PlannerStatus.Terminated"/> /
/// <see cref="PlannerStatus.Expired"/> in this enum; matches the design.md §5
/// decision).
/// </para>
///
/// Five RED scenarios pinned here (per tasks.md §8a.3 Phase 1):
/// <list type="number">
///   <item>Create session (<c>AddAsync</c>) → audit row with
///         <see cref="AuditAction.Created"/>, <c>EntityType = "PlannerSession"</c>,
///         <c>TenantId</c> + <c>UserId</c> from <see cref="ITenantContext"/>,
///         <c>ChangesJson</c> = null.</item>
///   <item>Update session (Notes + Status = Completed change) → audit row
///         with <see cref="AuditAction.Updated"/> + diff identifying the
///         changed fields. <b>NOT</b> Deleted — Completed is a non-terminated
///         status.</item>
///   <item>Update session with Status = Cancelled → audit row with
///         <see cref="AuditAction.Deleted"/> (UPGRADED via IsTerminated
///         reflection). This is the bespoke slice 8a.3 deviation.</item>
///   <item>Reads (<c>GetByIdAsync</c> + <c>ListByUserAndWeekAsync</c> +
///         <c>ExistsForDateAsync</c> + <c>GetWeekComparisonAsync</c>) →
///         NO audit events. Reads are not audited.</item>
///   <item>Cross-tenant update attempt → <see cref="AuditAction.Denied"/>
///         audit row + <see cref="UnauthorizedAccessException"/> thrown.
///         The inner <c>UpdateAsync</c> is NEVER reached.</item>
/// </list>
///
/// <para>
/// <b>Why a focused <c>TestPlannerSessionDbContext</c></b>: mirrors the
/// 8a.1 AccountRepositoryIntegrationTests + 8a.2 AlertRepositoryIntegrationTests
/// pattern. The production <c>TradingDbContext</c> pulls in Npgsql-specific
/// converters (Money complex type + JournalEntry.Tags) that fail to compose
/// on SQLite. A focused helper DbContext keeps the model SQLite-compatible
/// without touching the production schema.
/// </para>
/// </summary>
public class PlannerSessionRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public PlannerSessionRepositoryIntegrationTests()
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
    /// SQLite-compatible test DbContext — maps only <see cref="PlannerSession"/>
    /// + <see cref="AuditEvent"/>. Sidesteps the Npgsql-specific converters
    /// the production TradingDbContext pulls in. Mirrors the 8a.1
    /// <c>TestAccountDbContext</c> + 8a.2 <c>TestAlertDbContext</c> pattern.
    /// </summary>
    private sealed class TestPlannerSessionDbContext : DbContext
    {
        public TestPlannerSessionDbContext(DbContextOptions<TestPlannerSessionDbContext> options) : base(options) { }

        public DbSet<PlannerSession> PlannerSessions => Set<PlannerSession>();
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("trading");
            modelBuilder.Entity<PlannerSession>(b =>
            {
                b.ToTable("planner_sessions");
                b.HasKey(s => s.Id);
                b.Property(s => s.Id).HasColumnName("id");
                b.Property(s => s.UserId).HasColumnName("user_id").IsRequired();
                b.Property(s => s.SessionDate).HasColumnName("session_date").HasConversion(
                    v => v.ToDateOnly(),
                    v => LocalDate.From(v)).IsRequired();
                b.Property(s => s.PlannedStartTime).HasColumnName("planned_start_time");
                b.Property(s => s.PlannedEndTime).HasColumnName("planned_end_time");
                b.Property(s => s.Symbol).HasColumnName("symbol").HasMaxLength(20);
                b.Property(s => s.Status).HasColumnName("status").HasConversion<byte>().IsRequired();
                b.Property(s => s.Notes).HasColumnName("notes").HasMaxLength(500);
                b.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(s => s.UpdatedAt).HasColumnName("updated_at");
                b.Ignore(s => s.DomainEvents);
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
    /// Test-only <see cref="IPlannerSessionRepository"/> impl — mirrors the
    /// production <c>PlannerSessionRepository</c> methods needed by the
    /// decorator + integration tests (AddAsync, UpdateAsync, GetByIdAsync,
    /// ListByUserAndWeekAsync, ExistsForDateAsync, GetWeekComparisonAsync).
    /// The bespoke read methods (<c>ListByUserAndWeekAsync</c>,
    /// <c>ExistsForDateAsync</c>, <c>GetWeekComparisonAsync</c>) are exercised
    /// by the audit-decorator integration tests to verify the "reads forward
    /// without audit" invariant.
    /// </summary>
    private sealed class TestPlannerSessionRepository : IPlannerSessionRepository
    {
        private readonly TestPlannerSessionDbContext _db;

        public TestPlannerSessionRepository(TestPlannerSessionDbContext db) { _db = db; }

        public Task<PlannerSession?> GetByIdAsync(Guid sessionId, CancellationToken ct)
            => _db.PlannerSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        public async Task<IReadOnlyList<PlannerSession>> ListByUserAndWeekAsync(
            Guid userId, LocalDate weekStart, LocalDate weekEnd, CancellationToken ct)
            => await _db.PlannerSessions
                .Where(s => s.UserId == userId
                         && s.SessionDate >= weekStart
                         && s.SessionDate <= weekEnd)
                .OrderBy(s => s.SessionDate)
                .ToListAsync(ct);

        public Task<bool> ExistsForDateAsync(
            Guid userId, LocalDate sessionDate, CancellationToken ct)
            => _db.PlannerSessions
                .AnyAsync(s => s.UserId == userId && s.SessionDate == sessionDate, ct);

        public Task<PlannerWeekComparisonDto> GetWeekComparisonAsync(
            Guid userId, LocalDate weekStart, LocalDate weekEnd, CancellationToken ct)
        {
            // Stub: the integration tests don't exercise the comparison dto
            // contents (the read test only asserts NO audit event is emitted
            // — the inner return value is irrelevant). Return zeroed values.
            return Task.FromResult(new PlannerWeekComparisonDto(
                Planned: 0, Completed: 0, Skipped: 0, Cancelled: 0,
                ActualTrades: 0, TotalPnl: 0m));
        }

        public async Task AddAsync(PlannerSession session, CancellationToken ct)
            => await _db.PlannerSessions.AddAsync(session, ct);

        public Task UpdateAsync(PlannerSession session, CancellationToken ct)
        {
            var entry = _db.Entry(session);
            if (entry.State == EntityState.Detached)
            {
                _db.PlannerSessions.Update(session);
            }
            return Task.CompletedTask;
        }
    }

    private (IServiceProvider sp, ITenantContext tenant, IClock clock, Guid currentUserId)
        BuildServices(Guid? currentUserId = null)
    {
        var plannerOpts = new DbContextOptionsBuilder<TestPlannerSessionDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new TestPlannerSessionDbContext(plannerOpts))
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

        services.AddDbContext<TestPlannerSessionDbContext>(opts => opts.UseSqlite(_connection));
        var auditContextOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        services.AddScoped<AuditDbContext>(_ => new AuditDbContext(auditContextOpts));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestPlannerSessionDbContext>());

        var tenantId = Guid.NewGuid();
        var userId = currentUserId ?? Guid.NewGuid();
        var tenant = new StaticTenantContext(new TenantId(tenantId), userId);
        services.AddSingleton<ITenantContext>(tenant);

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IPlannerSessionRepository, TestPlannerSessionRepository>();
        // The slice 8a.3 decorator registration — under test here.
        services.Decorate<IPlannerSessionRepository, PlannerSessionAuditDecorator>();

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

    private static PlannerSession CreateSession(Guid userId, IClock clock, string? notes = null)
        => PlannerSession.Create(
            userId: userId,
            sessionDate: new LocalDate(2026, 8, 18),
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: "EURUSD",
            notes: notes,
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
    public async Task CreatePlannerSession_WritesAuditEvent_WithActionCreated()
    {
        // Phase 1 #1: AddAsync → AuditAction.Created, no diff. The audit
        // row's EntityType is "PlannerSession" + UserId/TenantId from
        // ITenantContext.
        var userId = Guid.NewGuid();
        var (sp, tenant, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPlannerSessionRepository>();
        var plannerDb = scope.ServiceProvider.GetRequiredService<TestPlannerSessionDbContext>();
        var auditDb = NewAuditDbContext();

        var session = CreateSession(userId, clock, notes: "Initial plan");
        await repo.AddAsync(session, CancellationToken.None);
        await plannerDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(PlannerSession));
        saved.EntityId.Should().Be(session.Id);
        saved.Action.Should().Be(AuditAction.Created);
        saved.ChangesJson.Should().BeNull("Created events carry no diff.");
        saved.TenantId.Should().Be(tenant.Current!.Value);
        saved.UserId.Should().Be(tenant.CurrentUserId);
    }

    [Fact]
    public async Task UpdatePlannerSession_Completed_WritesAuditEvent_WithActionUpdatedAndDiff()
    {
        // Phase 1 #2: UpdateAsync with Notes + Status=Completed change →
        // AuditAction.Updated + diff identifying the changed fields.
        // Completed is a NON-terminated status — the IsTerminated reflection
        // check returns false, so the action stays Updated (NOT Deleted).
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPlannerSessionRepository>();
        var plannerDb = scope.ServiceProvider.GetRequiredService<TestPlannerSessionDbContext>();
        var auditDb = NewAuditDbContext();

        var session = CreateSession(userId, clock, notes: "Original plan");
        await repo.AddAsync(session, CancellationToken.None);
        await plannerDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        // Mutate: change notes + mark completed.
        var update = session.Update(
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: "EURUSD",
            notes: "Updated plan with more detail",
            clock: clock);
        update.IsSuccess.Should().BeTrue();
        var mark = session.MarkCompleted(clock);
        mark.IsSuccess.Should().BeTrue();

        await repo.UpdateAsync(session, CancellationToken.None);
        await plannerDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Updated,
            "Completed is a NON-terminated status — IsTerminated returns false " +
            "and the action stays Updated (NOT Deleted).");
        saved.ChangesJson.Should().NotBeNullOrEmpty();
        saved.ChangesJson.Should().Contain("notes",
            "the diff payload identifies the notes field change.");
        saved.ChangesJson.Should().Contain("status",
            "the diff payload also identifies the status field change " +
            "(Planned → Completed).");
    }

    [Fact]
    public async Task UpdatePlannerSession_Cancelled_WritesAuditEvent_WithActionDeleted()
    {
        // Phase 1 #3: UpdateAsync with Status=Cancelled → AuditAction.Deleted
        // (UPGRADED via IsTerminated reflection). This is the bespoke slice
        // 8a.3 deviation — the only lifecycle-terminated value in PlannerStatus
        // is Cancelled (no Terminated / Expired). The decorator's local
        // IsTerminated returns true → action becomes Deleted (not Updated).
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPlannerSessionRepository>();
        var plannerDb = scope.ServiceProvider.GetRequiredService<TestPlannerSessionDbContext>();
        var auditDb = NewAuditDbContext();

        var session = CreateSession(userId, clock, notes: "Will be cancelled");
        await repo.AddAsync(session, CancellationToken.None);
        await plannerDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        var mark = session.MarkCancelled(clock);
        mark.IsSuccess.Should().BeTrue();

        await repo.UpdateAsync(session, CancellationToken.None);
        await plannerDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Deleted,
            "Status=Cancelled matches the IsTerminated reflection rule — the " +
            "decorator's local IsTerminated returns true and the action is " +
            "upgraded from Updated to Deleted (slice 8a.3 bespoke deviation, " +
            "mirrors Wave 7 7b.1 Trade IsTerminated rule).");
        saved.EntityId.Should().Be(session.Id);
    }

    [Fact]
    public async Task ReadMethods_WriteNoAuditEvent()
    {
        // Phase 1 #4: the bespoke read methods (GetByIdAsync,
        // ListByUserAndWeekAsync, ExistsForDateAsync, GetWeekComparisonAsync)
        // emit NO audit events. Reads are not audited (matches the Wave 6
        // + 7 + 8a.1 + 8a.2 precedent: only mutations get audit rows).
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPlannerSessionRepository>();
        var plannerDb = scope.ServiceProvider.GetRequiredService<TestPlannerSessionDbContext>();
        var auditDb = NewAuditDbContext();

        // Stage the session directly via the DbContext so the setup doesn't
        // emit audit events (the read tests assert NO audit events at all).
        var session = CreateSession(userId, clock, notes: "Read test session");
        plannerDb.PlannerSessions.Add(session);
        await plannerDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        // Exercise every bespoke read method.
        var found = await repo.GetByIdAsync(session.Id, CancellationToken.None);
        found.Should().NotBeNull();

        var list = await repo.ListByUserAndWeekAsync(
            userId,
            new LocalDate(2026, 8, 17),
            new LocalDate(2026, 8, 23),
            CancellationToken.None);
        list.Should().HaveCount(1);

        var exists = await repo.ExistsForDateAsync(userId, session.SessionDate, CancellationToken.None);
        exists.Should().BeTrue();

        var comparison = await repo.GetWeekComparisonAsync(
            userId,
            new LocalDate(2026, 8, 17),
            new LocalDate(2026, 8, 23),
            CancellationToken.None);
        comparison.Should().NotBeNull();

        await auditDb.SaveChangesAsync();

        var auditRows = await auditDb.AuditEvents.ToListAsync();
        auditRows.Should().BeEmpty(
            "all four read methods (GetByIdAsync, ListByUserAndWeekAsync, " +
            "ExistsForDateAsync, GetWeekComparisonAsync) MUST NOT emit audit " +
            "rows — reads are not audited (matches Wave 6 + 7 + 8a.1 + 8a.2 " +
            "precedent).");
    }

    [Fact]
    public async Task CrossTenantUpdate_LogsAuditEvent_WithActionDenied_AndThrowsUnauthorized()
    {
        // Phase 1 #5: a different user attempts to update another user's
        // planner session. The decorator detects the ownership mismatch
        // (session.UserId != currentUserId), logs a Denied audit row for the
        // ATTEMPT (security trail), and throws UnauthorizedAccessException.
        // The inner UpdateAsync is NEVER reached.
        var ownerUserId = Guid.NewGuid();
        var attackerUserId = Guid.NewGuid(); // != ownerUserId
        var (sp, _, clock, _) = BuildServices(currentUserId: attackerUserId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPlannerSessionRepository>();
        var plannerDb = scope.ServiceProvider.GetRequiredService<TestPlannerSessionDbContext>();
        var auditDb = NewAuditDbContext();

        // Setup: create the session as the OWNER (bypass the decorator for
        // the setup so the ownership check doesn't fire here).
        var session = CreateSession(ownerUserId, clock, notes: "Owner session");
        plannerDb.PlannerSessions.Add(session);
        await plannerDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        // Attacker attempts to update the owner's session.
        var update = session.Update(
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: "GBPUSD",
            notes: "Cross-tenant mutation attempt",
            clock: clock);
        update.IsSuccess.Should().BeTrue("Update itself is in-memory and doesn't enforce ownership.");
        Func<Task> act = async () => await repo.UpdateAsync(session, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "the decorator rejects cross-tenant update attempts.");

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Denied);
        saved.EntityId.Should().Be(session.Id);
        saved.UserId.Should().Be(attackerUserId,
            "the audit trail records WHO attempted the cross-tenant access, " +
            "not the owner.");
    }
}