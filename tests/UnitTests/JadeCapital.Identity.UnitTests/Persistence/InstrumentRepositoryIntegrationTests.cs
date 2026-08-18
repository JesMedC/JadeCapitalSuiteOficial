using FluentAssertions;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Instruments;
using JadeCapital.Trading.Domain.ValueObjects;
using JadeCapital.Trading.Infrastructure.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Identity.UnitTests.Persistence;

/// <summary>
/// Integration tests for the <see cref="InstrumentAuditDecorator"/> wired
/// via Scrutor (Wave 8, slice 8a.1).
///
/// <para>
/// Mirrors the <see cref="AccountAuditDecorator"/> shape but WITHOUT the
/// cross-tenant <c>IsOwner</c> check: <see cref="Instrument"/> is a
/// <b>catalog entity</b> ("NO es Aggregate Root: es una Entity compartida
/// por todos los usuarios" — <c>Instrument.cs</c> docstring). The catalog
/// is shared across all users; admin mutations on the catalog are
/// legitimate. <see cref="Instrument.UserId"/> does not exist (the
/// <see cref="Instrument"/> aggregate does NOT carry a user FK).
/// </para>
///
/// <para>
/// Uses the generic <see cref="JadeCapital.Shared.Infrastructure.Persistence.DecoratedRepository{T}"/>
/// helper — the slice 8a.1 atomic rename <c>RemoveAsync</c> →
/// <c>DeleteAsync</c> + <c>IRepository&lt;Instrument&gt;</c> extension
/// make the <see cref="IInstrumentRepository"/> fit the canonical generic
/// CRUD surface.
/// </para>
///
/// <para>
/// Five RED scenarios pinned here (per tasks.md §8a.1 Phase 4):
/// <list type="number">
///   <item>Create instrument → <c>audit.events</c> row with
///         <see cref="AuditAction.Created"/>, <c>EntityType = "Instrument"</c>,
///         <c>TenantId</c> + <c>UserId</c> from <see cref="ITenantContext"/>,
///         <c>ChangesJson</c> = null.</item>
///   <item>Update instrument (payout change) → <c>audit.events</c> row with
///         <see cref="AuditAction.Updated"/> + a before/after diff that
///         identifies the <c>payoutPercent</c> field change.</item>
///   <item><c>DeleteAsync(Instrument, ct)</c> (the slice 8a.1 renamed
///         overload) → <c>audit.events</c> row with
///         <see cref="AuditAction.Deleted"/>.</item>
///   <item><c>FindByIdAsync</c> (the canonical read) → no audit event.
///         Reads are not audited (matches the Wave 6 + 7 precedent).</item>
///   <item>NO <c>IsOwner</c> check on <c>UpdateAsync</c>: a different
///         user (different <see cref="ITenantContext.CurrentUserId"/>) can
///         legitimately mutate the shared catalog — the decorator emits
///         <see cref="AuditAction.Updated"/> WITHOUT a
///         <see cref="UnauthorizedAccessException"/>. This is the
///         key deviation from <see cref="AccountAuditDecorator"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why a focused <c>TestInstrumentDbContext</c></b>: mirrors the
/// <c>AccountRepositoryIntegrationTests.TestAccountDbContext</c> +
/// <c>TradeRepositoryIntegrationTests.TestTradingDbContext</c> pattern.
/// The production <c>TradingDbContext</c> pulls in Npgsql-specific
/// converters (Money complex type + JournalEntry.Tags) that fail to
/// compose on SQLite. A focused helper DbContext keeps the model
/// SQLite-compatible without touching the production schema.
/// </para>
/// </summary>
public class InstrumentRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public InstrumentRepositoryIntegrationTests()
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
    /// SQLite-compatible test DbContext — maps only <see cref="Instrument"/>
    /// + <see cref="AuditEvent"/>. Sidesteps the Npgsql-specific converters
    /// the production TradingDbContext pulls in (Symbol value converter in
    /// particular uses Npgsql native arrays that fail on SQLite).
    /// </summary>
    private sealed class TestInstrumentDbContext : DbContext
    {
        public TestInstrumentDbContext(DbContextOptions<TestInstrumentDbContext> options) : base(options) { }

        public DbSet<Instrument> Instruments => Set<Instrument>();
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("trading");
            modelBuilder.Entity<Instrument>(b =>
            {
                b.ToTable("instruments");
                b.HasKey(i => i.Id);
                b.Property(i => i.Id).HasColumnName("id");
                // Symbol as plain string for SQLite — the production Npgsql
                // value converter (Symbol → VARCHAR via SymbolConverter) is
                // not SQLite-compatible. Persist the .Value directly.
                b.Property(i => i.Symbol).HasColumnName("symbol").HasMaxLength(20).IsRequired()
                    .HasConversion(
                        v => v.Value,
                        v => Symbol.FromTrusted(v));
                b.Property(i => i.AssetClasses).HasColumnName("asset_class").HasConversion<short>().IsRequired();
                b.Property(i => i.ContractSize).HasColumnName("contract_size").IsRequired();
                b.Property(i => i.DecimalPlaces).HasColumnName("decimal_places").IsRequired();
                b.Property(i => i.PipValue).HasColumnName("pip_value").IsRequired();
                b.Property(i => i.PayoutPercent).HasColumnName("payout_percent").IsRequired();
                b.Property(i => i.IsActive).HasColumnName("is_active").IsRequired();
                b.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(i => i.UpdatedAt).HasColumnName("updated_at");
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
    /// Test-only <see cref="IInstrumentRepository"/> impl — mirrors the
    /// production <c>InstrumentRepository</c> methods needed by the
    /// decorator + integration tests (AddAsync, UpdateAsync, DeleteAsync,
    /// FindByIdAsync, GetByIdAsync, FindBySymbolAsync, ListActiveAsync,
    /// ListAllAsync). The bespoke reads (FindBySymbolAsync, etc.) are
    /// NOT exercised by the audit-decorator integration tests but are
    /// required by the interface.
    /// </summary>
    private sealed class TestInstrumentRepository : IInstrumentRepository
    {
        private readonly TestInstrumentDbContext _db;
        public TestInstrumentRepository(TestInstrumentDbContext db) { _db = db; }

        // Bespoke read methods — not exercised by these tests, but required
        // by IInstrumentRepository. Stub implementations.
        public Task<Instrument?> FindByIdAsync(Guid id, CancellationToken ct)
            => _db.Instruments.FirstOrDefaultAsync(i => i.Id == id, ct);

        public Task<Instrument?> GetByIdAsync(Guid id, CancellationToken ct)
            => FindByIdAsync(id, ct);

        public Task<Instrument?> FindBySymbolAsync(string symbol, CancellationToken ct)
            => _db.Instruments.FirstOrDefaultAsync(
                i => i.Symbol.Value == symbol.Trim().ToUpperInvariant(), ct);

        public async Task<IReadOnlyList<Instrument>> ListActiveAsync(CancellationToken ct)
            => await _db.Instruments.Where(i => i.IsActive).ToListAsync(ct);

        public async Task<IReadOnlyList<Instrument>> ListAllAsync(CancellationToken ct)
            => await _db.Instruments.ToListAsync(ct);

        public async Task AddAsync(Instrument instrument, CancellationToken ct)
            => await _db.Instruments.AddAsync(instrument, ct);

        public Task UpdateAsync(Instrument instrument, CancellationToken ct)
        {
            var entry = _db.Entry(instrument);
            if (entry.State == EntityState.Detached)
            {
                _db.Instruments.Update(instrument);
            }
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Instrument instrument, CancellationToken ct)
        {
            _db.Instruments.Remove(instrument);
            return Task.CompletedTask;
        }
    }

    private (IServiceProvider sp, ITenantContext tenant, IClock clock, Guid currentUserId)
        BuildServices(Guid? currentUserId = null)
    {
        var instrumentOpts = new DbContextOptionsBuilder<TestInstrumentDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new TestInstrumentDbContext(instrumentOpts))
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

        services.AddDbContext<TestInstrumentDbContext>(opts => opts.UseSqlite(_connection));
        var auditContextOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        services.AddScoped<AuditDbContext>(_ => new AuditDbContext(auditContextOpts));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestInstrumentDbContext>());

        var tenantId = Guid.NewGuid();
        var userId = currentUserId ?? Guid.NewGuid();
        var tenant = new StaticTenantContext(new TenantId(tenantId), userId);
        services.AddSingleton<ITenantContext>(tenant);

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IInstrumentRepository, TestInstrumentRepository>();
        // The slice 8a.1 decorator registration — under test here.
        services.Decorate<IInstrumentRepository, InstrumentAuditDecorator>();

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

    private static Instrument CreateInstrument(IClock clock)
        => Instrument.Create(
            id: Guid.NewGuid(),
            symbol: "EUR/USD",
            assetClasses: AssetClass.Forex,
            contractSize: 100_000m,
            decimalPlaces: 5,
            pipValue: 10m,
            payoutPercent: 0.85m,
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
    public async Task CreateInstrument_WritesAuditEvent_WithActionCreated()
    {
        // Phase 4 #1: AddAsync → AuditAction.Created, no diff. The audit
        // row's EntityType is "Instrument" + UserId/TenantId from
        // ITenantContext.
        var userId = Guid.NewGuid();
        var (sp, tenant, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
        var instrumentDb = scope.ServiceProvider.GetRequiredService<TestInstrumentDbContext>();
        var auditDb = NewAuditDbContext();

        var instrument = CreateInstrument(clock);
        await repo.AddAsync(instrument, CancellationToken.None);
        await instrumentDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(Instrument));
        saved.EntityId.Should().Be(instrument.Id);
        saved.Action.Should().Be(AuditAction.Created);
        saved.ChangesJson.Should().BeNull("Created events carry no diff.");
        saved.TenantId.Should().Be(tenant.Current!.Value);
        saved.UserId.Should().Be(tenant.CurrentUserId);
    }

    [Fact]
    public async Task UpdateInstrument_WritesAuditEvent_WithActionUpdatedAndDiff()
    {
        // Phase 4 #2: UpdateAsync → AuditAction.Updated + diff
        // (payoutPercent change). The diff identifies the changed field
        // and shows the before/after values.
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
        var instrumentDb = scope.ServiceProvider.GetRequiredService<TestInstrumentDbContext>();
        var auditDb = NewAuditDbContext();

        var instrument = CreateInstrument(clock);
        await repo.AddAsync(instrument, CancellationToken.None);
        await instrumentDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        instrument.UpdateMetadata(
            symbol: "EUR/USD",
            assetClasses: AssetClass.Forex,
            contractSize: 100_000m,
            decimalPlaces: 5,
            pipValue: 10m,
            payoutPercent: 0.92m);
        await repo.UpdateAsync(instrument, CancellationToken.None);
        await instrumentDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Updated);
        saved.ChangesJson.Should().NotBeNullOrEmpty();
        saved.ChangesJson.Should().Contain("payoutPercent",
            "the diff payload identifies the payoutPercent field change.");
    }

    [Fact]
    public async Task DeleteInstrument_WritesAuditEvent_WithActionDeleted()
    {
        // Phase 4 #3: DeleteAsync(Instrument, ct) (the slice 8a.1 renamed
        // overload) → AuditAction.Deleted. The decorator emits the audit
        // row with the entity id.
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
        var instrumentDb = scope.ServiceProvider.GetRequiredService<TestInstrumentDbContext>();
        var auditDb = NewAuditDbContext();

        var instrument = CreateInstrument(clock);
        await repo.AddAsync(instrument, CancellationToken.None);
        await instrumentDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        await repo.DeleteAsync(instrument, CancellationToken.None);
        await instrumentDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Deleted);
        saved.EntityId.Should().Be(instrument.Id);
    }

    [Fact]
    public async Task FindByIdAsync_WritesNoAuditEvent()
    {
        // Phase 4 #4: the canonical read (FindByIdAsync) emits NO audit
        // event. Reads are not audited (matches the Wave 6 + 7 precedent).
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
        var instrumentDb = scope.ServiceProvider.GetRequiredService<TestInstrumentDbContext>();
        var auditDb = NewAuditDbContext();

        var instrument = CreateInstrument(clock);
        instrumentDb.Instruments.Add(instrument);
        await instrumentDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        var found = await repo.FindByIdAsync(instrument.Id, CancellationToken.None);
        found.Should().NotBeNull();
        await auditDb.SaveChangesAsync();

        var auditRows = await auditDb.AuditEvents.ToListAsync();
        auditRows.Should().BeEmpty(
            "FindByIdAsync is a read — no audit event should be written " +
            "(matches the Wave 6 + 7a.1 + 7b.1 precedent).");
    }

    [Fact]
    public async Task UpdateInstrument_DoesNotEnforceIsOwner_DifferentUserCanUpdateCatalog()
    {
        // Phase 4 #5: NO IsOwner check on UpdateAsync — the Instrument
        // catalog is shared across all users (Instrument is NOT a
        // user-owned aggregate; it carries no UserId FK). A different
        // user (different ITenantContext.CurrentUserId) can legitimately
        // mutate the catalog. The decorator emits AuditAction.Updated
        // WITHOUT a UnauthorizedAccessException.
        //
        // This is the key deviation from the AccountAuditDecorator
        // pattern (which DOES enforce cross-tenant isolation on
        // UpdateAsync). Catalog entities are tenant-agnostic by design.
        var instrumentOwnerUserId = Guid.NewGuid(); // arbitrary — Instrument has no UserId
        var differentUserId = Guid.NewGuid(); // != instrumentOwnerUserId
        var (sp, _, clock, _) = BuildServices(currentUserId: differentUserId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
        var instrumentDb = scope.ServiceProvider.GetRequiredService<TestInstrumentDbContext>();
        var auditDb = NewAuditDbContext();

        // Setup: create the instrument with a different acting user.
        var instrument = CreateInstrument(clock);
        await repo.AddAsync(instrument, CancellationToken.None);
        await instrumentDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        // A different user updates the catalog — should NOT throw.
        instrument.UpdateMetadata(
            symbol: "EUR/USD",
            assetClasses: AssetClass.Forex,
            contractSize: 100_000m,
            decimalPlaces: 5,
            pipValue: 10m,
            payoutPercent: 0.90m);
        Func<Task> act = async () => await repo.UpdateAsync(instrument, CancellationToken.None);

        await act.Should().NotThrowAsync(
            "the Instrument catalog is shared — no cross-tenant check on " +
            "UpdateAsync (mirrors TenantAuditDecorator precedent: Tenant " +
            "IS the tenant boundary; Instrument is catalog data, not user-scoped).");

        await instrumentDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Updated,
            "the catalog mutation emits an Updated audit row (NOT Denied).");
        saved.UserId.Should().Be(differentUserId,
            "the audit trail records the calling user who performed the " +
            "catalog mutation.");
    }
}