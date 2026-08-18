using FluentAssertions;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Accounts;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Infrastructure.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Identity.UnitTests.Persistence;

/// <summary>
/// Integration tests for the <see cref="AccountAuditDecorator"/> wired via
/// Scrutor (Wave 8, slice 8a.1).
///
/// <para>
/// Mirrors the <see cref="ImportJobAuditDecorator"/> shape (cross-tenant
/// <c>IsOwner</c> check on <see cref="Account.UserId"/>) but uses the
/// generic <see cref="JadeCapital.Shared.Infrastructure.Persistence.DecoratedRepository{T}"/>
/// helper — the slice 8a.1 atomic rename <c>RemoveAsync</c> → <c>DeleteAsync</c>
/// + <c>IRepository&lt;Account&gt;</c> extension make the
/// <see cref="IAccountRepository"/> fit the canonical generic CRUD surface.
/// </para>
///
/// <para>
/// Five RED scenarios pinned here (per tasks.md §8a.1 Phase 3):
/// <list type="number">
///   <item>Create account → <c>audit.events</c> row with
///         <see cref="AuditAction.Created"/>, <c>EntityType = "Account"</c>,
///         <c>TenantId</c> + <c>UserId</c> from <see cref="ITenantContext"/>,
///         <c>ChangesJson</c> = null.</item>
///   <item>Update account (name change) → <c>audit.events</c> row with
///         <see cref="AuditAction.Updated"/> + a before/after diff that
///         identifies the <c>name</c> field change.</item>
///   <item><c>DeleteAsync(Account, ct)</c> (the slice 8a.1 renamed
///         overload) → <c>audit.events</c> row with
///         <see cref="AuditAction.Deleted"/> + null diff (hard delete;
///         pre-mutation snapshot via the change tracker).</item>
///   <item>Cross-tenant update attempt → <see cref="AuditAction.Denied"/>
///         audit row + <see cref="UnauthorizedAccessException"/> thrown.
///         The inner <c>UpdateAsync</c> is NEVER reached.</item>
///   <item><c>FindByIdAsync</c> (the canonical read) → no audit event.
///         Reads are not audited (matches the Wave 6 + 7 precedent: only
///         mutations get audit rows).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why a focused <c>TestAccountDbContext</c></b>: mirrors the
/// <c>JournalEntryRepositoryIntegrationTests.TestJournalDbContext</c> +
/// <c>TradeRepositoryIntegrationTests.TestTradingDbContext</c> pattern. The
/// production <c>TradingDbContext</c> pulls in Npgsql-specific converters
/// (Money complex type + JournalEntry.Tags) that fail to compose on
/// SQLite. A focused helper DbContext keeps the model SQLite-compatible
/// without touching the production schema.
/// </para>
/// </summary>
public class AccountRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public AccountRepositoryIntegrationTests()
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
    /// SQLite-compatible test DbContext — maps only <see cref="Account"/>
    /// + <see cref="AuditEvent"/>. Sidesteps the Npgsql-specific converters
    /// the production TradingDbContext pulls in.
    /// </summary>
    private sealed class TestAccountDbContext : DbContext
    {
        public TestAccountDbContext(DbContextOptions<TestAccountDbContext> options) : base(options) { }

        public DbSet<Account> Accounts => Set<Account>();
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("trading");
            modelBuilder.Entity<Account>(b =>
            {
                b.ToTable("accounts");
                b.HasKey(a => a.Id);
                b.Property(a => a.Id).HasColumnName("id");
                b.Property(a => a.UserId).HasColumnName("user_id").IsRequired();
                b.Property(a => a.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
                b.Property(a => a.Broker).HasColumnName("broker").HasMaxLength(80).IsRequired();
                b.Property(a => a.MarketType).HasColumnName("market_type").HasConversion<short>().IsRequired();
                b.Property(a => a.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
                b.Property(a => a.InitialBalance).HasColumnName("initial_balance").IsRequired();
                b.Property(a => a.Leverage).HasColumnName("leverage");
                b.Property(a => a.IsActive).HasColumnName("is_active").IsRequired();
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
                b.Property(e => e.ChangesJson).HasColumnName("changes");
                b.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
                b.Ignore(e => e.CreatedAt);
                b.Ignore(e => e.UpdatedAt);
                b.Ignore(e => e.DomainEvents);
            });
        }
    }

    /// <summary>
    /// Test-only <see cref="IAccountRepository"/> impl — mirrors the
    /// production <c>AccountRepository</c> methods needed by the
    /// decorator + integration tests (AddAsync, UpdateAsync, DeleteAsync,
    /// FindByIdAsync, GetByIdAsync, ListByUserIdAsync). The bespoke
    /// ListByUserIdAsync is NOT exercised by the audit-decorator
    /// integration tests but is required by the interface.
    /// </summary>
    private sealed class TestAccountRepository : IAccountRepository
    {
        private readonly TestAccountDbContext _db;
        public TestAccountRepository(TestAccountDbContext db) { _db = db; }

        // Bespoke read methods — not exercised by these tests, but required
        // by IAccountRepository. Stub implementations.
        public Task<Account?> FindByIdAsync(Guid id, CancellationToken ct)
            => _db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct);

        public Task<Account?> GetByIdAsync(Guid id, CancellationToken ct)
            => FindByIdAsync(id, ct);

        public async Task<IReadOnlyList<Account>> ListByUserIdAsync(Guid userId, CancellationToken ct)
            => await _db.Accounts.Where(a => a.UserId == userId).ToListAsync(ct);

        public async Task AddAsync(Account account, CancellationToken ct)
            => await _db.Accounts.AddAsync(account, ct);

        public Task UpdateAsync(Account account, CancellationToken ct)
        {
            var entry = _db.Entry(account);
            if (entry.State == EntityState.Detached)
            {
                _db.Accounts.Update(account);
            }
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Account account, CancellationToken ct)
        {
            _db.Accounts.Remove(account);
            return Task.CompletedTask;
        }
    }

    private (IServiceProvider sp, ITenantContext tenant, IClock clock, Guid currentUserId)
        BuildServices(Guid? currentUserId = null)
    {
        var accountOpts = new DbContextOptionsBuilder<TestAccountDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new TestAccountDbContext(accountOpts))
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

        services.AddDbContext<TestAccountDbContext>(opts => opts.UseSqlite(_connection));
        var auditContextOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        services.AddScoped<AuditDbContext>(_ => new AuditDbContext(auditContextOpts));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestAccountDbContext>());

        var tenantId = Guid.NewGuid();
        var userId = currentUserId ?? Guid.NewGuid();
        var tenant = new StaticTenantContext(new TenantId(tenantId), userId);
        services.AddSingleton<ITenantContext>(tenant);

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IAccountRepository, TestAccountRepository>();
        // The slice 8a.1 decorator registration — under test here.
        services.Decorate<IAccountRepository, AccountAuditDecorator>();

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

    private static Account CreateAccount(
        Guid userId, IClock clock, string name = "IC Markets EUR")
        => Account.Open(
            id: Guid.NewGuid(),
            userId: userId,
            name: name,
            broker: "IC Markets",
            marketType: MarketType.Forex,
            currency: "EUR",
            initialBalance: 10_000m,
            leverage: 100m,
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
    public async Task CreateAccount_WritesAuditEvent_WithActionCreated()
    {
        // Phase 3 #1: AddAsync → AuditAction.Created, no diff. The audit
        // row's EntityType is "Account" + UserId/TenantId from ITenantContext.
        var userId = Guid.NewGuid();
        var (sp, tenant, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var accountDb = scope.ServiceProvider.GetRequiredService<TestAccountDbContext>();
        var auditDb = NewAuditDbContext();

        var account = CreateAccount(userId, clock);
        await repo.AddAsync(account, CancellationToken.None);
        await accountDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(Account));
        saved.EntityId.Should().Be(account.Id);
        saved.Action.Should().Be(AuditAction.Created);
        saved.ChangesJson.Should().BeNull("Created events carry no diff.");
        saved.TenantId.Should().Be(tenant.Current!.Value);
        saved.UserId.Should().Be(tenant.CurrentUserId);
    }

    [Fact]
    public async Task UpdateAccount_WritesAuditEvent_WithActionUpdatedAndDiff()
    {
        // Phase 3 #2: UpdateAsync → AuditAction.Updated + diff (name change).
        // The diff identifies the changed field and shows the before/after
        // values.
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var accountDb = scope.ServiceProvider.GetRequiredService<TestAccountDbContext>();
        var auditDb = NewAuditDbContext();

        var account = CreateAccount(userId, clock, name: "Original name");
        await repo.AddAsync(account, CancellationToken.None);
        await accountDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        account.UpdateMetadata(
            name: "Updated account name",
            broker: account.Broker,
            marketType: account.MarketType,
            currency: account.Currency,
            leverage: account.Leverage);
        await repo.UpdateAsync(account, CancellationToken.None);
        await accountDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Updated);
        saved.ChangesJson.Should().NotBeNullOrEmpty();
        saved.ChangesJson.Should().Contain("name",
            "the diff payload identifies the name field change.");
    }

    [Fact]
    public async Task DeleteAccount_WritesAuditEvent_WithActionDeleted()
    {
        // Phase 3 #3: DeleteAsync(Account, ct) (the slice 8a.1 renamed
        // overload) → AuditAction.Deleted. The decorator emits the audit
        // row with the entity id; the change tracker provides the
        // pre-mutation snapshot for the diff.
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var accountDb = scope.ServiceProvider.GetRequiredService<TestAccountDbContext>();
        var auditDb = NewAuditDbContext();

        var account = CreateAccount(userId, clock);
        await repo.AddAsync(account, CancellationToken.None);
        await accountDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        await repo.DeleteAsync(account, CancellationToken.None);
        await accountDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Deleted);
        saved.EntityId.Should().Be(account.Id);
    }

    [Fact]
    public async Task CrossTenantUpdate_LogsAuditEvent_WithActionDenied_AndThrowsUnauthorized()
    {
        // Phase 3 #4: a different user attempts to update another user's
        // account. The decorator detects the ownership mismatch, logs an
        // audit event for the ATTEMPT (security trail), and throws
        // UnauthorizedAccessException. The inner UpdateAsync is NEVER
        // reached.
        var ownerUserId = Guid.NewGuid();
        var attackerUserId = Guid.NewGuid(); // != ownerUserId
        var (sp, _, clock, _) = BuildServices(currentUserId: attackerUserId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var accountDb = scope.ServiceProvider.GetRequiredService<TestAccountDbContext>();
        var auditDb = NewAuditDbContext();

        // Setup: create the account as the OWNER (bypass the decorator for
        // the setup so the ownership check doesn't fire here).
        var account = CreateAccount(ownerUserId, clock);
        accountDb.Accounts.Add(account);
        await accountDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        account.UpdateMetadata(
            name: "Cross-tenant mutation attempt",
            broker: account.Broker,
            marketType: account.MarketType,
            currency: account.Currency,
            leverage: account.Leverage);
        Func<Task> act = async () => await repo.UpdateAsync(account, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "the decorator rejects cross-tenant update attempts.");

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Denied);
        saved.EntityId.Should().Be(account.Id);
        saved.UserId.Should().Be(attackerUserId,
            "the audit trail records WHO attempted the cross-tenant access, " +
            "not the owner.");
    }

    [Fact]
    public async Task FindByIdAsync_WritesNoAuditEvent()
    {
        // Phase 3 #5: the canonical read (FindByIdAsync) emits NO audit
        // event. Reads are not audited (matches the Wave 6 + 7 precedent:
        // only mutations get audit rows).
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var accountDb = scope.ServiceProvider.GetRequiredService<TestAccountDbContext>();
        var auditDb = NewAuditDbContext();

        var account = CreateAccount(userId, clock);
        accountDb.Accounts.Add(account);
        await accountDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        var found = await repo.FindByIdAsync(account.Id, CancellationToken.None);
        found.Should().NotBeNull();
        await auditDb.SaveChangesAsync();

        var auditRows = await auditDb.AuditEvents.ToListAsync();
        auditRows.Should().BeEmpty(
            "FindByIdAsync is a read — no audit event should be written " +
            "(matches the Wave 6 + 7a.1 + 7b.1 precedent).");
    }
}