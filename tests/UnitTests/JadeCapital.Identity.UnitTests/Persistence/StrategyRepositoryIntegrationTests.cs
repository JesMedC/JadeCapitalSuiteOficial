using FluentAssertions;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Contracts.Strategies;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Strategies;
using JadeCapital.Trading.Infrastructure.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Identity.UnitTests.Persistence;

/// <summary>
/// Integration tests for the <see cref="StrategyAuditDecorator"/> wired via
/// Scrutor (Wave 7, slice 7b.1).
///
/// <para>
/// Mirrors the <see cref="ImportJobAuditDecorator"/> shape (cross-tenant
/// <c>IsOwner</c> check on <see cref="Strategy.UserId"/>) with one slice-
/// specific deviation: the <c>Deactivate + UpdateAsync</c> path emits
/// <see cref="AuditAction.Updated"/> with an <c>isActive: true → false</c>
/// diff, NOT <see cref="AuditAction.Deleted"/>. The
/// <c>DecoratedRepository&lt;T&gt;.IsTerminated</c> reflection check stays
/// unchanged (it only fires on <c>IsDeleted == true</c> or
/// <c>Status ∈ {Cancelled, Terminated, Expired}</c> — neither matches
/// Strategy's IsActive flag).
/// </para>
///
/// Five RED scenarios pinned here (per tasks.md §7b.1 Phase 4):
/// <list type="number">
///   <item>Create strategy → <c>audit.events</c> row with
///         <see cref="AuditAction.Created"/>.</item>
///   <item>Update strategy (name + description) → <c>audit.events</c> row
///         with <see cref="AuditAction.Updated"/> + a before/after diff.</item>
///   <item>Deactivate (Strategy.Deactivate(clock) + UpdateAsync) →
///         <c>audit.events</c> row with <see cref="AuditAction.Updated"/>
///         + an <c>isActive: true → false</c> diff. NOT
///         <see cref="AuditAction.Deleted"/> (per Wave 7 user decision
///         #3: deactivate is a soft-delete via flag, not a Delete).</item>
///   <item>DeleteAsync (the slice 7b.1 defensive stub) → <c>audit.events</c>
///         row with <see cref="AuditAction.Failed"/> + the decorator
///         RE-THROWS <see cref="NotSupportedException"/> with the canonical
///         message.</item>
///   <item>Cross-tenant update attempt → <see cref="UnauthorizedAccessException"/>
///         + an <see cref="AuditAction.Denied"/> audit row for the
///         attempt itself (security trail).</item>
/// </list>
///
/// <para>
/// <b>Why a focused <c>TestTradingDbContext</c></b>: mirrors the
/// 6d.2 ImportJobRepositoryIntegrationTests pattern. The production
/// <c>TradingDbContext</c> pulls in Npgsql-specific array converters
/// (Money complex type + JournalEntry.Tags) that fail to compose on
/// SQLite. A focused helper DbContext keeps the model SQLite-compatible
/// without touching the production schema.
/// </para>
/// </summary>
public class StrategyRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public StrategyRepositoryIntegrationTests()
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
    /// SQLite-compatible test DbContext — maps only <see cref="Strategy"/>
    /// + <see cref="AuditEvent"/>. Sidesteps the Npgsql-specific converters
    /// the production TradingDbContext pulls in.
    /// </summary>
    private sealed class TestTradingDbContext : DbContext
    {
        public TestTradingDbContext(DbContextOptions<TestTradingDbContext> options) : base(options) { }

        public DbSet<Strategy> Strategies => Set<Strategy>();
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("trading");
            modelBuilder.Entity<Strategy>(b =>
            {
                b.ToTable("strategies");
                b.HasKey(s => s.Id);
                b.Property(s => s.Id).HasColumnName("id");
                b.Property(s => s.UserId).HasColumnName("user_id").IsRequired();
                b.Property(s => s.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
                b.Property(s => s.Description).HasColumnName("description").HasMaxLength(1000);
                b.Property(s => s.Symbol).HasColumnName("symbol").HasMaxLength(20);
                b.Property(s => s.Timeframe).HasColumnName("timeframe").HasConversion<byte?>();
                b.Property(s => s.Rules).HasColumnName("rules").HasMaxLength(2000);
                b.Property(s => s.IsActive).HasColumnName("is_active").IsRequired();
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
                b.Property(e => e.ChangesJson).HasColumnName("changes");
                b.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
                b.Ignore(e => e.CreatedAt);
                b.Ignore(e => e.UpdatedAt);
                b.Ignore(e => e.DomainEvents);
            });
        }
    }

    /// <summary>
    /// Test-only <see cref="IStrategyRepository"/> impl — mirrors the
    /// production <c>StrategyRepository</c> methods needed by the decorator
    /// + integration tests (GetByIdAsync, AddAsync, UpdateAsync, DeleteAsync).
    /// Excludes the bespoke analytics query (GetAnalyticsAsync) and the
    /// list/exists reads — they're not exercised by the audit-decorator
    /// integration tests.
    /// </summary>
    private sealed class TestStrategyRepository : IStrategyRepository
    {
        private readonly TestTradingDbContext _db;
        public TestStrategyRepository(TestTradingDbContext db) { _db = db; }

        public Task<Strategy?> GetByIdAsync(Guid id, CancellationToken ct)
            => _db.Strategies.FirstOrDefaultAsync(s => s.Id == id, ct);

        public async Task<IReadOnlyList<Strategy>> ListByUserAsync(
            Guid userId, bool activeOnly, CancellationToken ct)
        {
            IQueryable<Strategy> q = _db.Strategies.Where(s => s.UserId == userId);
            if (activeOnly) q = q.Where(s => s.IsActive);
            return await q.OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt).ToListAsync(ct);
        }

        public Task<bool> ExistsByNameAsync(Guid userId, string name, CancellationToken ct)
            => _db.Strategies.AnyAsync(s => s.UserId == userId && s.IsActive && s.Name == name, ct);

        public Task<StrategyAnalyticsDto> GetAnalyticsAsync(
            Guid userId, Guid strategyId, CancellationToken ct)
            => throw new NotSupportedException(
                "GetAnalyticsAsync not exercised by StrategyAuditDecorator integration tests.");

        public async Task AddAsync(Strategy strategy, CancellationToken ct)
            => await _db.Strategies.AddAsync(strategy, ct);

        public Task UpdateAsync(Strategy strategy, CancellationToken ct)
        {
            var entry = _db.Entry(strategy);
            if (entry.State == EntityState.Detached)
                _db.Strategies.Update(strategy);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Strategy strategy, CancellationToken ct)
            => throw new NotSupportedException(
                "Strategy deletion happens via Deactivation, not direct delete");
    }

    private (IServiceProvider sp, ITenantContext tenant, IClock clock, Guid currentUserId)
        BuildServices(Guid? currentUserId = null)
    {
        // EnsureCreated on a per-DbContext basis: EF's EnsureCreated
        // returns early if ANY table exists, so calling EnsureCreated on
        // AuditDbContext after TestTradingDbContext already ran would
        // skip the audit.events table. We do the EnsureCreated for each
        // context explicitly via transient options builders.
        var tradingOpts = new DbContextOptionsBuilder<TestTradingDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new TestTradingDbContext(tradingOpts))
            db.Database.EnsureCreated();

        var auditOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new AuditDbContext(auditOpts))
            db.Database.EnsureCreated();

        // EF Core 9 SQLite EnsureCreated is all-or-nothing — once any table
        // exists, every subsequent EnsureCreated is a no-op. Force-create
        // the audit.events table via raw SQL matching AuditEventConfiguration
        // (same Wave 6 6d.2 fixture fix as ImportJobRepositoryIntegrationTests).
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
                    ON events (entity_type, entity_id);";
            cmd.ExecuteNonQuery();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        var fixedNow = new DateTimeOffset(2026, 8, 19, 14, 0, 0, TimeSpan.Zero);
        services.AddSingleton<IClock>(_ => new StaticClock(fixedNow));

        // Same DbContext base-type alias pattern as
        // ImportJobRepositoryIntegrationTests — the decorator needs a
        // DbContext for the change tracker. TestTradingDbContext serves
        // both roles; in production the alias points to TradingDbContext.
        services.AddDbContext<TestTradingDbContext>(opts => opts.UseSqlite(_connection));
        var auditContextOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        services.AddScoped<AuditDbContext>(_ => new AuditDbContext(auditContextOpts));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestTradingDbContext>());

        var tenantId = Guid.NewGuid();
        var userId = currentUserId ?? Guid.NewGuid();
        var tenant = new StaticTenantContext(new TenantId(tenantId), userId);
        services.AddSingleton<ITenantContext>(tenant);

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IStrategyRepository, TestStrategyRepository>();
        // The slice 7b.1 decorator registration — under test here.
        services.Decorate<IStrategyRepository, StrategyAuditDecorator>();

        var sp = services.BuildServiceProvider();
        return (sp, tenant, sp.GetRequiredService<IClock>(), userId);
    }

    /// <summary>
    /// Helper: creates a fresh AuditDbContext on the shared connection for
    /// verification queries.
    /// </summary>
    private AuditDbContext NewAuditDbContext()
    {
        var opts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new AuditDbContext(opts);
    }

    private static Strategy CreateStrategy(Guid userId, IClock clock, string name = "Initial Name")
        => Strategy.Create(
            userId: userId,
            name: name,
            description: "Initial description",
            symbol: "EUR/USD",
            timeframe: null,
            rules: null,
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
    public async Task CreateStrategy_WritesAuditEvent_WithActionCreated()
    {
        // Phase 4 #1: AddAsync → AuditAction.Created, no diff.
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IStrategyRepository>();
        var tradingDb = scope.ServiceProvider.GetRequiredService<TestTradingDbContext>();
        var auditDb = NewAuditDbContext();

        var strategy = CreateStrategy(userId, clock);
        await repo.AddAsync(strategy, CancellationToken.None);
        await tradingDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(Strategy));
        saved.EntityId.Should().Be(strategy.Id);
        saved.Action.Should().Be(AuditAction.Created);
        saved.ChangesJson.Should().BeNull("Created events carry no diff.");
    }

    [Fact]
    public async Task UpdateStrategy_WritesAuditEvent_WithActionUpdatedAndDiff()
    {
        // Phase 4 #2: UpdateAsync → AuditAction.Updated + diff
        // (name + description change).
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IStrategyRepository>();
        var tradingDb = scope.ServiceProvider.GetRequiredService<TestTradingDbContext>();
        var auditDb = NewAuditDbContext();

        var strategy = CreateStrategy(userId, clock);
        await repo.AddAsync(strategy, CancellationToken.None);
        await tradingDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents); // discard Created row

        strategy.Update(
            name: "Renamed Strategy",
            description: "Updated description",
            symbol: "EUR/USD",
            timeframe: null,
            rules: null,
            clock: clock);
        await repo.UpdateAsync(strategy, CancellationToken.None);
        await tradingDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Updated);
        saved.ChangesJson.Should().NotBeNullOrEmpty();
        saved.ChangesJson.Should().Contain("name",
            "the diff payload identifies the changed name field.");
    }

    [Fact]
    public async Task DeactivateStrategy_WritesAuditEvent_WithUpdatedAndIsActiveDiff_NotDeleted()
    {
        // Phase 4 #3: Deactivate(clock) + UpdateAsync → AuditAction.Updated
        // with isActive: true → false diff. NOT AuditAction.Deleted —
        // the DecoratedRepository<T>.IsTerminated reflection check stays
        // unchanged (it only fires on IsDeleted==true or Status in
        // {Cancelled, Terminated, Expired}; Strategy's IsActive flag is
        // not covered). This is the Wave 7 user decision #3 contract:
        // soft-delete-by-flag is a state change, NOT a deletion event.
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IStrategyRepository>();
        var tradingDb = scope.ServiceProvider.GetRequiredService<TestTradingDbContext>();
        var auditDb = NewAuditDbContext();

        var strategy = CreateStrategy(userId, clock);
        await repo.AddAsync(strategy, CancellationToken.None);
        await tradingDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents); // discard Created row

        strategy.Deactivate(clock);
        await repo.UpdateAsync(strategy, CancellationToken.None);
        await tradingDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Updated,
            "Wave 7 user decision #3: Deactivate is a soft-delete-by-flag, " +
            "not a Deleted event. The IsTerminated reflection check stays " +
            "unchanged in DecoratedRepository<T>.");
        saved.ChangesJson.Should().NotBeNullOrEmpty();
        saved.ChangesJson.Should().Contain("isActive",
            "the diff payload identifies the isActive state transition.");
        saved.ChangesJson.Should().Contain("true").And.Contain("false",
            "the diff payload shows the true → false transition.");
    }

    [Fact]
    public async Task DeleteStrategy_WritesAuditEvent_WithActionFailed_AndRethrowsNotSupported()
    {
        // Phase 4 #4: DeleteAsync → AuditAction.Failed + re-throw.
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IStrategyRepository>();
        var tradingDb = scope.ServiceProvider.GetRequiredService<TestTradingDbContext>();
        var auditDb = NewAuditDbContext();

        var strategy = CreateStrategy(userId, clock);
        await repo.AddAsync(strategy, CancellationToken.None);
        await tradingDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents); // discard Created row

        var act = async () => await repo.DeleteAsync(strategy, CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Deactivat*");

        await tradingDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Failed);
        saved.EntityId.Should().Be(strategy.Id);
    }

    [Fact]
    public async Task CrossTenantUpdate_LogsAuditEvent_WithActionDenied_AndThrowsUnauthorized()
    {
        // Phase 4 #5: a different user attempts to update another user's
        // strategy. The decorator detects the ownership mismatch, logs
        // an audit event for the ATTEMPT (security trail), and throws
        // UnauthorizedAccessException. The inner UpdateAsync is NEVER
        // reached, so EF's change tracker does NOT mark the entity as
        // Modified via the inner path.
        //
        // NOTE: we deliberately do NOT mutate the strategy before the
        // attempt. The cross-tenant check is based on strategy.UserId vs
        // the caller's CurrentUserId — the mutation target is irrelevant.
        // Mutating before the attempt would leave the in-memory state
        // modified, and EF would commit the mutation on the next
        // SaveChangesAsync (which would falsely look like the decorator
        // let the change through).
        var ownerUserId = Guid.NewGuid();
        var attackerUserId = Guid.NewGuid(); // != ownerUserId
        var (sp, _, clock, _) = BuildServices(currentUserId: attackerUserId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IStrategyRepository>();
        var tradingDb = scope.ServiceProvider.GetRequiredService<TestTradingDbContext>();
        var auditDb = NewAuditDbContext();

        // Setup: create the strategy as the OWNER (bypass the decorator
        // for the setup so the ownership check doesn't fire here).
        var strategy = CreateStrategy(ownerUserId, clock);
        tradingDb.Strategies.Add(strategy);
        await tradingDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        Func<Task> act = async () => await repo.UpdateAsync(strategy, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "the decorator rejects cross-tenant update attempts.");

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Denied);
        saved.EntityId.Should().Be(strategy.Id);
        saved.UserId.Should().Be(attackerUserId,
            "the audit trail records WHO attempted the cross-tenant access, " +
            "not the owner.");
    }
}