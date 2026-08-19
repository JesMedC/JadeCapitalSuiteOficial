using System.Text.Json;
using FluentAssertions;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Scanner;
using JadeCapital.Trading.Infrastructure.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Trading.UnitTests.Persistence;

/// <summary>
/// Integration tests for the <see cref="ScannerFilterAuditDecorator"/>
/// wired via Scrutor (Wave 9, slice 9a.2 — sub-scope A coverage extension).
///
/// <para>
/// <b>Decorator shape</b>: bespoke CRD-without-Delete:
/// <list type="bullet">
///   <item><see cref="IScannerFilterRepository.AddAsync"/> wraps with
///         <c>IsOwner</c> cross-tenant check + <see cref="AuditAction.Created"/>.
///         Created events carry no diff (<c>ChangesJson = null</c>).</item>
///   <item><see cref="IScannerFilterRepository.UpdateAsync"/> wraps with
///         <c>IsOwner</c> cross-tenant check + <see cref="AuditAction.Updated"/>
///         + a before/after diff JSON. The diff is computed via JSON
///         snapshot + property comparison (mirrors the generic
///         <c>JsonDiff</c> in <c>DecoratedRepository&lt;T&gt;</c> but
///         re-implemented locally because the decorator is bespoke).</item>
///   <item>Cross-tenant <c>UpdateAsync</c> emits <see cref="AuditAction.Denied"/>
///         + throws <see cref="UnauthorizedAccessException"/> before the
///         inner is reached.</item>
///   <item>Reads (<c>GetByIdAsync</c> + <c>GetByUserAndNameAsync</c> +
///         <c>ListByUserAsync</c>) are forwarded bare — no audit (matches
///         Wave 6 + 7 + 8a.x + 8b.1 + 9a.1 precedent).</item>
///   <item><see cref="IScannerFilterRepository.DeleteAsync"/> is the
///         defensive stub contributed by the <c>IRepository&lt;ScannerFilter&gt;</c>
///         extension (Wave 9 §9a.2 interface surgery). The decorator emits
///         <see cref="AuditAction.Failed"/> + throws
///         <see cref="NotSupportedException"/> before the inner is reached.
///         The inner production stub additionally throws
///         <see cref="NotSupportedException"/> if a future caller bypasses
///         the decorator.</item>
/// </list>
/// </para>
///
/// <para>
/// Four RED scenarios + 1 contract pin (pinned in
/// <c>IScannerFilterRepositoryContractTests.cs</c>) cover the full
/// decorator surface per tasks.md §9a.2 Phase 2.1:
/// <list type="number">
///   <item>Create (<c>AddAsync</c>) → <see cref="AuditAction.Created"/> +
///         <see cref="ITenantContext.CurrentUserId"/> + <see cref="ITenantContext.Current"/>.</item>
///   <item>Update (<c>UpdateAsync</c>) → <see cref="AuditAction.Updated"/> +
///         before/after diff JSON identifying the changed field.</item>
///   <item>Cross-tenant <c>UpdateAsync</c> → <see cref="AuditAction.Denied"/>
///         + <see cref="UnauthorizedAccessException"/> (security trail).</item>
///   <item>Reads (<c>GetByIdAsync</c> + <c>GetByUserAndNameAsync</c> +
///         <c>ListByUserAsync</c>) → NO audit event.</item>
/// </list>
/// </para>
/// <para>
/// The 5th scenario (DeleteAsync → <see cref="AuditAction.Failed"/> +
/// <see cref="NotSupportedException"/>) is covered by the contract pin
/// in <c>IScannerFilterRepositoryContractTests.DeleteAsync_IsNotOnInterface_DefensiveStub_Throws</c>.
/// The decorator + production <c>ScannerFilterRepository.DeleteAsync</c>
/// are both defensive stubs; the contract pin isolates the
/// <c>ScannerFilterAuditDecorator.DeleteAsync</c> behavior with
/// NSubstitute mocks (no SQLite, no DI) and asserts (a) the interface
/// shape, (b) the inner is never reached, (c) the
/// <see cref="AuditAction.Failed"/> audit row is emitted.
/// </para>
///
/// <para>
/// <b>Why a focused <c>TestScannerFilterDbContext</c></b>: mirrors the
/// Wave 9 9a.1 <c>TestAIRiskAdviceDbContext</c> + 8b.1
/// <c>TestStripeCustomerDbContext</c> + 8a.3
/// <c>TestPreTradeChecklistDbContext</c> pattern. The production
/// <c>TradingDbContext</c> pulls in Npgsql-specific converters via
/// <c>JournalEntry.Tags</c> + other Money/array mappings that fail to
/// compose on SQLite. A focused helper DbContext that maps only
/// <see cref="ScannerFilter"/> + <see cref="AuditEvent"/> keeps the
/// model SQLite-compatible without touching the production schema.
/// </para>
/// </summary>
public class ScannerFilterRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public ScannerFilterRepositoryIntegrationTests()
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
    /// SQLite-compatible test DbContext — maps only <see cref="ScannerFilter"/>
    /// + <see cref="AuditEvent"/>. Sidesteps the Npgsql-specific converters
    /// the production TradingDbContext pulls in.
    /// </summary>
    private sealed class TestScannerFilterDbContext : DbContext
    {
        public TestScannerFilterDbContext(DbContextOptions<TestScannerFilterDbContext> options) : base(options) { }

        public DbSet<ScannerFilter> ScannerFilters => Set<ScannerFilter>();
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("trading");
            modelBuilder.Entity<ScannerFilter>(b =>
            {
                b.ToTable("scanner_filters");
                b.HasKey(f => f.Id);
                b.Property(f => f.Id).HasColumnName("id");
                b.Property(f => f.UserId).HasColumnName("user_id").IsRequired();
                b.Property(f => f.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
                b.Property(f => f.MinSpread).HasColumnName("min_spread");
                b.Property(f => f.MaxSpread).HasColumnName("max_spread");
                b.Property(f => f.MinVolume).HasColumnName("min_volume");
                b.Property(f => f.MinRiskReward).HasColumnName("min_risk_reward");
                b.Property(f => f.VolatilityWindow).HasColumnName("volatility_window").HasConversion<byte>().IsRequired();
                b.Property(f => f.ActiveHours).HasColumnName("active_hours");
                b.Property(f => f.IsActive).HasColumnName("is_active").IsRequired();
                b.Property(f => f.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(f => f.UpdatedAt).HasColumnName("updated_at");
                b.Ignore(f => f.DomainEvents);
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
    /// Test-only <see cref="IScannerFilterRepository"/> impl — mirrors the
    /// production <c>ScannerFilterRepository</c> methods needed by the
    /// decorator + integration tests (GetByIdAsync, GetByUserAndNameAsync,
    /// ListByUserAsync, AddAsync, UpdateAsync, DeleteAsync).
    /// </summary>
    private sealed class TestScannerFilterRepository : IScannerFilterRepository
    {
        private readonly TestScannerFilterDbContext _db;
        public TestScannerFilterRepository(TestScannerFilterDbContext db) { _db = db; }

        public Task<ScannerFilter?> GetByIdAsync(Guid id, CancellationToken ct)
            => _db.ScannerFilters.FirstOrDefaultAsync(f => f.Id == id, ct);

        public Task<ScannerFilter?> GetByUserAndNameAsync(Guid userId, string name, CancellationToken ct)
            => _db.ScannerFilters.FirstOrDefaultAsync(f => f.UserId == userId && f.Name == name, ct);

        public async Task<IReadOnlyList<ScannerFilter>> ListByUserAsync(
            Guid userId, bool activeOnly, CancellationToken ct)
        {
            IQueryable<ScannerFilter> q = _db.ScannerFilters.Where(f => f.UserId == userId);
            if (activeOnly) q = q.Where(f => f.IsActive);
            return await q.OrderBy(f => f.Name).ToListAsync(ct);
        }

        public async Task AddAsync(ScannerFilter filter, CancellationToken ct)
            => await _db.ScannerFilters.AddAsync(filter, ct);

        public Task UpdateAsync(ScannerFilter filter, CancellationToken ct)
        {
            var entry = _db.Entry(filter);
            if (entry.State == EntityState.Detached)
                _db.ScannerFilters.Update(filter);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(ScannerFilter filter, CancellationToken ct)
            => throw new NotSupportedException(
                "ScannerFilter deletion is not supported — use Deactivate (IsActive = false).");
    }

    private (IServiceProvider sp, ITenantContext tenant, IClock clock, Guid currentUserId)
        BuildServices(Guid? currentUserId = null)
    {
        // EnsureCreated on a per-DbContext basis: EF's EnsureCreated
        // returns early if ANY table exists, so calling EnsureCreated on
        // AuditDbContext after TestScannerFilterDbContext already ran
        // would skip the audit.events table. We do the EnsureCreated for
        // each context explicitly via transient options builders.
        var scannerOpts = new DbContextOptionsBuilder<TestScannerFilterDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new TestScannerFilterDbContext(scannerOpts))
            db.Database.EnsureCreated();

        var auditOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new AuditDbContext(auditOpts))
            db.Database.EnsureCreated();

        // EF Core 9 SQLite EnsureCreated is all-or-nothing — once any table
        // exists, every subsequent EnsureCreated is a no-op. Force-create
        // the audit.events table via raw SQL matching AuditEventConfiguration
        // (same Wave 8 8b.1 + 9a.1 fixture fix).
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

        services.AddDbContext<TestScannerFilterDbContext>(opts => opts.UseSqlite(_connection));
        var auditContextOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        services.AddScoped<AuditDbContext>(_ => new AuditDbContext(auditContextOpts));
        // DbContext base-type alias — the ScannerFilterAuditDecorator
        // constructor takes a DbContext (base type) for the change
        // tracker. The TestScannerFilterDbContext serves both roles;
        // in production the alias points to TradingDbContext.
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestScannerFilterDbContext>());

        var tenantId = Guid.NewGuid();
        var userId = currentUserId ?? Guid.NewGuid();
        var tenant = new StaticTenantContext(new TenantId(tenantId), userId);
        services.AddSingleton<ITenantContext>(tenant);

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IScannerFilterRepository, TestScannerFilterRepository>();
        // The slice 9a.2 decorator registration — under test here.
        services.Decorate<IScannerFilterRepository, ScannerFilterAuditDecorator>();

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

    private static ScannerFilter CreateFilter(
        Guid userId, IClock clock, string name = "Initial Filter",
        decimal? minSpread = null, decimal? maxSpread = null,
        decimal? minVolume = null, decimal? minRiskReward = null,
        VolatilityWindow window = VolatilityWindow.Daily,
        string? activeHoursJson = null)
        => ScannerFilter.Create(
            userId: userId,
            name: name,
            minSpread: minSpread,
            maxSpread: maxSpread,
            minVolume: minVolume,
            minRiskReward: minRiskReward,
            window: window,
            activeHoursJson: activeHoursJson,
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
    public async Task AddScannerFilter_ByOwner_WritesAuditEvent_WithActionCreated()
    {
        // Phase 2.1 #1: AddAsync → AuditAction.Created, no diff. The audit
        // row's EntityType is "ScannerFilter" + UserId/TenantId from
        // ITenantContext. Created events carry no diff
        // (ChangesJson = null) — the entity is the audit-relevant record
        // itself.
        var userId = Guid.NewGuid();
        var (sp, tenant, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IScannerFilterRepository>();
        var scannerDb = scope.ServiceProvider.GetRequiredService<TestScannerFilterDbContext>();
        var auditDb = NewAuditDbContext();

        var filter = CreateFilter(userId, clock, name: "Momentum Filter");
        await repo.AddAsync(filter, CancellationToken.None);
        await scannerDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(ScannerFilter));
        saved.EntityId.Should().Be(filter.Id);
        saved.Action.Should().Be(AuditAction.Created);
        saved.ChangesJson.Should().BeNull("Created events carry no diff.");
        saved.TenantId.Should().Be(tenant.Current!.Value);
        saved.UserId.Should().Be(tenant.CurrentUserId);
    }

    [Fact]
    public async Task UpdateScannerFilter_ByOwner_WritesAuditEvent_WithActionUpdatedAndDiff()
    {
        // Phase 2.1 #2: UpdateAsync → AuditAction.Updated + a before/after
        // diff JSON identifying the changed fields. The diff is computed
        // via reflection-based property comparison on the post-mutation
        // snapshot (mirrors the Wave 7 7b.2 JournalEntryAuditDecorator +
        // 8a.1 AccountAuditDecorator pattern). The test pins the diff
        // includes the changed field name(s) — minSpread and volatilityWindow
        // are the two fields updated in this scenario.
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IScannerFilterRepository>();
        var scannerDb = scope.ServiceProvider.GetRequiredService<TestScannerFilterDbContext>();
        var auditDb = NewAuditDbContext();

        var filter = CreateFilter(userId, clock,
            name: "Volatility Filter",
            minSpread: 0.5m,
            maxSpread: 2.0m,
            minVolume: 100m,
            window: VolatilityWindow.Daily);
        await repo.AddAsync(filter, CancellationToken.None);
        await scannerDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents); // discard Created row
        await auditDb.SaveChangesAsync();

        filter.Update(
            name: "Volatility Filter",
            minSpread: 0.75m,
            maxSpread: 2.5m,
            minVolume: 100m,
            minRiskReward: null,
            window: VolatilityWindow.Weekly, // field change
            activeHoursJson: null,
            clock: clock);
        await repo.UpdateAsync(filter, CancellationToken.None);
        await scannerDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Updated);
        saved.ChangesJson.Should().NotBeNullOrEmpty(
            "Updated events must carry a before/after diff JSON so compliance officers can see WHAT changed.");
        saved.ChangesJson.Should().Contain("volatilityWindow",
            "the diff payload identifies the changed volatilityWindow field " +
            "(the test changed Daily → Weekly).");
        saved.ChangesJson.Should().Contain("minSpread",
            "the diff payload identifies the changed minSpread field " +
            "(the test changed 0.5 → 0.75).");
    }

    [Fact]
    public async Task CrossTenantUpdateScannerFilter_LogsAuditEvent_WithActionDenied_AndThrowsUnauthorized()
    {
        // Phase 2.1 #3: a different user attempts to update another user's
        // ScannerFilter. The decorator detects the ownership mismatch,
        // logs an audit event for the ATTEMPT (security trail), and
        // throws UnauthorizedAccessException. The inner UpdateAsync is
        // NEVER reached.
        //
        // NOTE: we deliberately do NOT mutate the filter before the
        // attempt. The cross-tenant check is based on filter.UserId vs
        // the caller's CurrentUserId — the mutation target is irrelevant.
        // Mutating before the attempt would leave the in-memory state
        // modified, and EF would commit the mutation on the next
        // SaveChangesAsync (which would falsely look like the decorator
        // let the change through).
        var ownerUserId = Guid.NewGuid();
        var attackerUserId = Guid.NewGuid(); // != ownerUserId
        var (sp, _, clock, _) = BuildServices(currentUserId: attackerUserId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IScannerFilterRepository>();
        var scannerDb = scope.ServiceProvider.GetRequiredService<TestScannerFilterDbContext>();
        var auditDb = NewAuditDbContext();

        // Setup: create the filter as the OWNER (bypass the decorator for
        // the setup so the ownership check doesn't fire here).
        var filter = CreateFilter(ownerUserId, clock, name: "Owner Filter");
        scannerDb.ScannerFilters.Add(filter);
        await scannerDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        Func<Task> act = async () => await repo.UpdateAsync(filter, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "the decorator rejects cross-tenant update attempts.");

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Denied);
        saved.EntityId.Should().Be(filter.Id);
        saved.UserId.Should().Be(attackerUserId,
            "the audit trail records WHO attempted the cross-tenant access, " +
            "not the owner.");
    }

    [Fact]
    public async Task ReadScannerFilters_DoNotEmitAuditEvent()
    {
        // Phase 2.1 #4: all 3 reads (GetByIdAsync + GetByUserAndNameAsync +
        // ListByUserAsync) emit NO audit event. Reads are not audited
        // (matches Wave 6 + 7 + 8a.x + 8b.1 + 9a.1 precedent).
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IScannerFilterRepository>();
        var scannerDb = scope.ServiceProvider.GetRequiredService<TestScannerFilterDbContext>();
        var auditDb = NewAuditDbContext();

        // Stage 2 filters directly via the DbContext so the setup
        // doesn't emit audit events (the read test asserts NO audit
        // events at all).
        var filter1 = CreateFilter(userId, clock, name: "Filter 1");
        var filter2 = CreateFilter(userId, clock, name: "Filter 2");
        scannerDb.ScannerFilters.AddRange(filter1, filter2);
        await scannerDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        // Exercise all 3 reads.
        var byId = await repo.GetByIdAsync(filter1.Id, CancellationToken.None);
        byId.Should().NotBeNull();

        var byName = await repo.GetByUserAndNameAsync(userId, "Filter 2", CancellationToken.None);
        byName.Should().NotBeNull();

        var list = await repo.ListByUserAsync(userId, activeOnly: true, CancellationToken.None);
        list.Should().HaveCount(2);

        await auditDb.SaveChangesAsync();

        var auditRows = await auditDb.AuditEvents.ToListAsync();
        auditRows.Should().BeEmpty(
            "GetByIdAsync + GetByUserAndNameAsync + ListByUserAsync are reads — no audit event " +
            "should be written (matches Wave 6 + 7 + 8a.x + 8b.1 + 9a.1 precedent).");
    }
}
