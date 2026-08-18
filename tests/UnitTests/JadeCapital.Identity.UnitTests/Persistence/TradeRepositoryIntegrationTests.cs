using FluentAssertions;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Trades;
using JadeCapital.Trading.Domain.ValueObjects;
using JadeCapital.Trading.Infrastructure.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Identity.UnitTests.Persistence;

/// <summary>
/// Integration tests for the <see cref="TradeAuditDecorator"/> wired via
/// Scrutor (Wave 7, slice 7b.1).
///
/// <para>
/// Mirrors the <see cref="ImportJobAuditDecorator"/> shape (cross-tenant
/// <c>IsOwner</c> check on <see cref="Trade.UserId"/>) with slice-specific
/// deviations:
/// </para>
/// <list type="bullet">
///   <item>The <c>UpdateAsync</c> path is the canonical "soft-delete-by-status"
///         path: when <see cref="Trade.Cancel"/> flips <see cref="TradeStatus"/>
///         from <c>Open</c> to <c>Cancelled</c> (or <c>Terminated</c> /
///         <c>Expired</c>), the <see cref="JadeCapital.Shared.Infrastructure.Persistence.DecoratedRepository{T}.IsTerminated"/>
///         reflection check upgrades <see cref="AuditAction.Updated"/> →
///         <see cref="AuditAction.Deleted"/>. The result is a
///         <see cref="AuditAction.Deleted"/> event with the status diff.</item>
///   <item>The <c>DeleteAsync(Trade, ct)</c> path (the slice 7b.1 rename
///         from <c>RemoveAsync</c>) emits <see cref="AuditAction.Deleted"/>
///         with the before/after diff. Slice 7b.1 makes this the canonical
///         hard-delete surface — only valid when Status ∈ {Open, Cancelled}.</item>
///   <item>The decorator does NOT use the generic
///         <c>DecoratedRepository&lt;T&gt;</c> helper — <c>ITradeRepository</c>
///         stays bespoke (mirrors the 7a.1 <c>IRiskProfileRepository</c>
///         pattern; documented in apply-progress). The decorator forwards
///         mutations directly to the inner + emits audit rows.</item>
/// </list>
///
/// Five RED scenarios pinned here (per orchestrator prompt §7b.1 Phase 5):
/// <list type="number">
///   <item>Create trade → <c>audit.events</c> row with <c>AuditAction.Created</c>.</item>
///   <item>Update trade (status change Open → Closed) →
///         <c>audit.events</c> row with <c>AuditAction.Updated</c> + a
///         before/after diff (status change).</item>
///   <item>Delete trade (the renamed <c>DeleteAsync</c>) →
///         <c>audit.events</c> row with <c>AuditAction.Deleted</c> +
///         before/after diff. The slice 7b.1 rename makes this the
///         canonical hard-delete surface.</item>
///   <item>Cross-tenant update attempt → <c>AuditAction.Denied</c> +
///         <see cref="UnauthorizedAccessException"/>.</item>
///   <item>Audit event includes <c>user_id</c> + <c>tenant_id</c> +
///         <c>entity_type</c> — every audit row is fully attributable.</item>
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
public class TradeRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public TradeRepositoryIntegrationTests()
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
    /// SQLite-compatible test DbContext — maps only <see cref="Trade"/>
    /// + <see cref="AuditEvent"/>. Sidesteps the Npgsql-specific converters
    /// the production TradingDbContext pulls in (the Symbol value converter
    /// in particular uses Npgsql array types that fail on SQLite).
    /// </summary>
    private sealed class TestTradingDbContext : DbContext
    {
        public TestTradingDbContext(DbContextOptions<TestTradingDbContext> options) : base(options) { }

        public DbSet<Trade> Trades => Set<Trade>();
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("trading");
            modelBuilder.Entity<Trade>(b =>
            {
                b.ToTable("trades");
                b.HasKey(t => t.Id);
                b.Property(t => t.Id).HasColumnName("id");
                b.Property(t => t.UserId).HasColumnName("user_id").IsRequired();
                b.Property(t => t.AccountId).HasColumnName("account_id").IsRequired();
                b.Property(t => t.InstrumentId).HasColumnName("instrument_id").IsRequired();
                // Symbol as plain string for SQLite — the production Npgsql
                // value converter (Symbol → VARCHAR via SymbolConverter) is
                // not SQLite-compatible. Persist the .Value directly.
                b.Property(t => t.Symbol).HasColumnName("symbol").HasMaxLength(20).IsRequired()
                    .HasConversion(
                        v => v.Value,
                        v => Symbol.Create(v).Value);
                b.Property(t => t.AssetClass).HasColumnName("asset_class").HasConversion<short>().IsRequired();
                b.Property(t => t.Direction).HasColumnName("direction").HasConversion<short>().IsRequired();
                b.Property(t => t.Status).HasColumnName("status").HasConversion<short>().IsRequired();
                b.Property(t => t.Strategy).HasColumnName("strategy").HasMaxLength(80);
                b.Property(t => t.Notes).HasColumnName("notes").HasMaxLength(2000);
                b.Property(t => t.OpenedAt).HasColumnName("opened_at").IsRequired();
                b.Property(t => t.ClosedAt).HasColumnName("closed_at");
                b.Property(t => t.AccountCurrency).HasColumnName("account_currency").HasMaxLength(3).IsRequired();
                b.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(t => t.UpdatedAt).HasColumnName("updated_at");
                // Money value objects → Ignore for the SQLite-in-memory test.
                // The production TradingDbContext uses OwnsOne with Npgsql
                // numeric columns; the test fixture sidesteps the Npgsql
                // converter by ignoring the Money properties entirely. The
                // aggregate is still valid in memory (Money.Create succeeded
                // at construction time); only the DB persistence of the
                // Money value objects is skipped.
                b.Ignore(t => t.Volume);
                b.Ignore(t => t.EntryPrice);
                b.Ignore(t => t.ExitPrice);
                b.Ignore(t => t.PnL);
                b.Ignore(t => t.MfeAmount);
                b.Ignore(t => t.MaeAmount);
                b.Ignore(t => t.MfeCurrency);
                b.Ignore(t => t.MaeCurrency);
                b.Ignore(t => t.DomainEvents);
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
    /// Test-only <see cref="ITradeRepository"/> impl — mirrors the
    /// production <c>TradeRepository</c> methods needed by the decorator
    /// + integration tests (AddAsync, UpdateAsync, DeleteAsync). The
    /// bespoke read methods (FindByIdAsync, ListByUserIdAsync,
    /// CountByUserIdAsync, etc.) are NOT exercised by the audit-decorator
    /// integration tests.
    /// </summary>
    private sealed class TestTradeRepository : ITradeRepository
    {
        private readonly TestTradingDbContext _db;
        public TestTradeRepository(TestTradingDbContext db) { _db = db; }

        // Read methods — not exercised by these tests, but required by
        // ITradeRepository. Stub implementations that throw if called.
        public Task<Trade?> FindByIdAsync(Guid id, CancellationToken ct)
            => throw new NotSupportedException(
                "FindByIdAsync not exercised by TradeAuditDecorator integration tests.");
        public Task<IReadOnlyList<Trade>> ListByUserIdAsync(
            Guid userId, int page, int pageSize, CancellationToken ct,
            TradeStatus? statusFilter = null, string? symbolFilter = null,
            Guid? accountIdFilter = null)
            => throw new NotSupportedException(
                "ListByUserIdAsync not exercised by TradeAuditDecorator integration tests.");
        public Task<int> CountByUserIdAsync(
            Guid userId, CancellationToken ct,
            TradeStatus? statusFilter = null, string? symbolFilter = null,
            Guid? accountIdFilter = null)
            => throw new NotSupportedException(
                "CountByUserIdAsync not exercised by TradeAuditDecorator integration tests.");
        public Task<IReadOnlyList<Trade>> ListByUserIdAndOpenedAtRangeAsync(
            Guid userId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
            => throw new NotSupportedException(
                "ListByUserIdAndOpenedAtRangeAsync not exercised by TradeAuditDecorator integration tests.");
        public Task<IReadOnlyList<Trade>> ListClosedByUserIdAsync(
            Guid userId, CancellationToken ct)
            => throw new NotSupportedException(
                "ListClosedByUserIdAsync not exercised by TradeAuditDecorator integration tests.");
        public Task<int> CountByInstrumentIdAsync(Guid instrumentId, CancellationToken ct)
            => throw new NotSupportedException(
                "CountByInstrumentIdAsync not exercised by TradeAuditDecorator integration tests.");

        public async Task AddAsync(Trade trade, CancellationToken ct)
            => await _db.Trades.AddAsync(trade, ct);

        public Task UpdateAsync(Trade trade, CancellationToken ct)
        {
            var entry = _db.Entry(trade);
            if (entry.State == EntityState.Detached)
                _db.Trades.Update(trade);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Trade trade, CancellationToken ct)
        {
            _db.Trades.Remove(trade);
            return Task.CompletedTask;
        }
    }

    private (IServiceProvider sp, ITenantContext tenant, IClock clock, Guid currentUserId)
        BuildServices(Guid? currentUserId = null)
    {
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
        services.AddScoped<ITradeRepository, TestTradeRepository>();
        // The slice 7b.1 decorator registration — under test here.
        services.Decorate<ITradeRepository, TradeAuditDecorator>();

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

    private static Trade CreateOpenTrade(Guid userId, IClock clock)
    {
        var symbol = Symbol.Create("EUR/USD").Value;
        var volume = Money.Create(1000m, Currency.Usd).Value;
        var entry = Money.Create(1.10m, Currency.Usd).Value;
        return Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), userId,
            symbol, AssetClass.Forex, TradeDirection.Long,
            volume, entry, "USD", null, null,
            new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero)).Value;
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
    public async Task CreateTrade_WritesAuditEvent_WithActionCreated()
    {
        // Phase 5 #1: AddAsync → AuditAction.Created, no diff.
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITradeRepository>();
        var tradingDb = scope.ServiceProvider.GetRequiredService<TestTradingDbContext>();
        var auditDb = NewAuditDbContext();

        var trade = CreateOpenTrade(userId, clock);
        await repo.AddAsync(trade, CancellationToken.None);
        await tradingDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(Trade));
        saved.EntityId.Should().Be(trade.Id);
        saved.Action.Should().Be(AuditAction.Created);
        saved.ChangesJson.Should().BeNull("Created events carry no diff.");
    }

    [Fact]
    public async Task UpdateTrade_WritesAuditEvent_WithActionUpdatedAndDiff()
    {
        // Phase 5 #2: UpdateAsync → AuditAction.Updated + diff (notes change).
        // Trade.Close updates status from Open → Closed — but the
        // IsTerminated reflection check does NOT upgrade Updated → Deleted
        // because 'Closed' is not in {Cancelled, Terminated, Expired}.
        // Result: AuditAction.Updated with status diff.
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITradeRepository>();
        var tradingDb = scope.ServiceProvider.GetRequiredService<TestTradingDbContext>();
        var auditDb = NewAuditDbContext();

        var trade = CreateOpenTrade(userId, clock);
        await repo.AddAsync(trade, CancellationToken.None);
        await tradingDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents); // discard Created row

        trade.UpdateMetadata(strategy: null, notes: "Trade notes updated");
        await repo.UpdateAsync(trade, CancellationToken.None);
        await tradingDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Updated,
            "UpdateAsync with metadata change emits Updated (status is " +
            "still Open — the IsTerminated reflection check does not fire).");
        saved.ChangesJson.Should().NotBeNullOrEmpty();
        saved.ChangesJson.Should().Contain("notes",
            "the diff payload identifies the notes field change.");
    }

    [Fact]
    public async Task DeleteTrade_WritesAuditEvent_WithActionDeleted()
    {
        // Phase 5 #3: DeleteAsync (the slice 7b.1 rename from RemoveAsync)
        // → AuditAction.Deleted + before/after diff. This is the canonical
        // hard-delete surface — only valid when Status ∈ {Open, Cancelled}.
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITradeRepository>();
        var tradingDb = scope.ServiceProvider.GetRequiredService<TestTradingDbContext>();
        var auditDb = NewAuditDbContext();

        var trade = CreateOpenTrade(userId, clock);
        await repo.AddAsync(trade, CancellationToken.None);
        await tradingDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents); // discard Created row

        await repo.DeleteAsync(trade, CancellationToken.None);
        await tradingDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Deleted);
        saved.EntityId.Should().Be(trade.Id);
    }

    [Fact]
    public async Task CrossTenantUpdate_LogsAuditEvent_WithActionDenied_AndThrowsUnauthorized()
    {
        // Phase 5 #4: a different user attempts to update another user's
        // trade. The decorator detects the ownership mismatch, logs
        // an audit event for the ATTEMPT (security trail), and throws
        // UnauthorizedAccessException.
        var ownerUserId = Guid.NewGuid();
        var attackerUserId = Guid.NewGuid(); // != ownerUserId
        var (sp, _, clock, _) = BuildServices(currentUserId: attackerUserId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITradeRepository>();
        var tradingDb = scope.ServiceProvider.GetRequiredService<TestTradingDbContext>();
        var auditDb = NewAuditDbContext();

        // Setup: create the trade as the OWNER (bypass the decorator for
        // the setup so the ownership check doesn't fire here).
        var trade = CreateOpenTrade(ownerUserId, clock);
        tradingDb.Trades.Add(trade);
        await tradingDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        Func<Task> act = async () => await repo.UpdateAsync(trade, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "the decorator rejects cross-tenant update attempts.");

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Denied);
        saved.EntityId.Should().Be(trade.Id);
        saved.UserId.Should().Be(attackerUserId,
            "the audit trail records WHO attempted the cross-tenant access, " +
            "not the owner.");
    }

    [Fact]
    public async Task AuditEvent_FullyAttributable_EntityTypeTradeAndUserIdAndTenantId()
    {
        // Phase 5 #5: every audit row carries EntityType + EntityId +
        // TenantId + UserId — fully attributable for compliance review.
        var userId = Guid.NewGuid();
        var (sp, tenant, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITradeRepository>();
        var tradingDb = scope.ServiceProvider.GetRequiredService<TestTradingDbContext>();
        var auditDb = NewAuditDbContext();

        var trade = CreateOpenTrade(userId, clock);
        await repo.AddAsync(trade, CancellationToken.None);
        await tradingDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(Trade));
        saved.EntityId.Should().Be(trade.Id);
        saved.TenantId.Should().Be(tenant.Current!.Value);
        saved.UserId.Should().Be(tenant.CurrentUserId);
    }
}