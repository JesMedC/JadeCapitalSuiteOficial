using System.Reflection;
using FluentAssertions;
using JadeCapital.Billing.Application.Stripe;
using JadeCapital.Billing.Domain.Stripe;
using JadeCapital.Billing.Infrastructure.Audit;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Identity.UnitTests.Persistence;

/// <summary>
/// Integration tests for the <see cref="StripeCustomerAuditDecorator"/>
/// wired via Scrutor (Wave 8, slice 8b.1).
///
/// <para>
/// Mirrors a simplified <c>TenantAuditDecorator</c> shape (Wave 6 6d.2) —
/// bespoke, second-smallest decorator in the wave. The
/// <see cref="IStripeCustomerRepository"/> interface exposes only
/// <c>AddAsync</c> + <c>GetByUserIdAsync</c> + <c>GetByStripeCustomerIdAsync</c>;
/// the <see cref="StripeCustomer"/> aggregate is immutable after
/// <see cref="StripeCustomer.Create"/> per the entity docstring ("Aggregate
/// is immutable after Create — no mutators"). No <c>UpdateAsync</c> or
/// <c>DeleteAsync</c> exists on the interface.
/// </para>
///
/// <para>
/// <b>Why this decorator is bespoke (does NOT use the generic
/// <c>DecoratedRepository&lt;T&gt;</c> helper)</b>: the write-once interface
/// is smaller than the canonical <c>IRepository&lt;T&gt;</c> CRUD surface —
/// it has only 3 methods (1 mutation + 2 reads), no <c>GetByIdAsync</c> /
/// <c>UpdateAsync</c> / <c>DeleteAsync</c>. Extending <c>IRepository&lt;T&gt;</c>
/// would silently add mutation methods that the entity docstring forbids
/// (and break the no-Update-promise compliance officers rely on). Bespoke
/// decorator preserves the write-once invariant + wraps only
/// <c>AddAsync</c>.
/// </para>
///
/// <para>
/// <b>Why IsOwner cross-tenant check on AddAsync</b>: even though
/// <c>AddAsync</c> inserts a NEW row (no read-then-update race), the row's
/// <see cref="StripeCustomer.UserId"/> still comes from the handler. A
/// cross-tenant handler could submit a StripeCustomer with another tenant's
/// user id. Per spec §8b.1 (the spec's Requirement "Audit decorator for
/// StripeCustomer aggregate"): "Cross-tenant <c>IsOwner</c> check MUST
/// compare <c>customer.UserId</c> to <c>ITenantContext.CurrentUserId</c>;
/// on mismatch, emit <c>AuditAction.Denied</c> and throw
/// <c>UnauthorizedAccessException</c>." The decorator enforces this —
/// unlike the PreTradeChecklist precedent (where the foreign-key
/// consistency is guaranteed by the production OpenTradeHandler), the
/// StripeCustomer handler accepts the userId as a command parameter and
/// is the entry point that the production CreateCheckoutSessionHandler
/// drives from the authenticated context.
/// </para>
///
/// <list type="bullet">
///   <item>Wrapping <see cref="IStripeCustomerRepository.AddAsync"/> with
///         the <b>cross-tenant <c>IsOwner</c> check</b> +
///         <see cref="AuditAction.Created"/> audit logging. On
///         cross-tenant attempt: <see cref="AuditAction.Denied"/> +
///         <see cref="UnauthorizedAccessException"/>.</item>
///   <item>Forwarding <see cref="IStripeCustomerRepository.GetByUserIdAsync"/>
///         + <see cref="IStripeCustomerRepository.GetByStripeCustomerIdAsync"/>
///         to the inner without audit logging. Reads are not audited
///         (matches Wave 6 + 7 + 8a.1 + 8a.2 + 8a.3 precedent).</item>
/// </list>
///
/// <para>
/// Three RED scenarios pinned here (per tasks.md §8b.1 Phase 1.1 + design.md
/// §7 — bespoke immutable decorator; mirrors PreTradeChecklistAuditDecorator
/// precedent with the added IsOwner check):
/// <list type="number">
///   <item>Create customer (<c>AddAsync</c>) → audit row with
///         <see cref="AuditAction.Created"/>, <c>EntityType = "StripeCustomer"</c>,
///         <c>TenantId</c> + <c>UserId</c> from <see cref="ITenantContext"/>,
///         <c>ChangesJson</c> = null.</item>
///   <item>Read methods (<c>GetByUserIdAsync</c> +
///         <c>GetByStripeCustomerIdAsync</c>) → NO audit event. Reads are
///         not audited (matches Wave 6 + 7 + 8a.1 + 8a.2 + 8a.3
///         precedent).</item>
///   <item>Contract pin — reflection asserts <see cref="IStripeCustomerRepository"/>
///         exposes NO <c>UpdateAsync</c> or <c>DeleteAsync</c> methods. The
///         immutable-aggregate invariant is enforced at the interface level
///         (no extension will silently add mutation methods without a
///         breaking-change review). Mirrors the 8a.3
///         <c>PreTradeChecklistRepositoryIntegrationTests</c> +
///         8a.1 <c>IAccountRepositoryContractTests</c> rename-pin
///         precedent.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why a focused <c>TestStripeCustomerDbContext</c></b>: mirrors the 8a.3
/// <c>PreTradeChecklistRepositoryIntegrationTests.TestPreTradeChecklistDbContext</c>
/// pattern. The production <c>BillingDbContext</c> pulls in Npgsql-specific
/// converters via Money complex types on
/// <c>Subscription.Pricing</c> / <c>PlanPricing</c> that fail to compose
/// on SQLite. A focused helper DbContext that maps only
/// <see cref="StripeCustomer"/> + <see cref="AuditEvent"/> keeps the model
/// SQLite-compatible without touching the production schema. The
/// <see cref="StripeCustomerConfiguration"/> UNIQUE indexes on
/// <c>user_id</c> + <c>stripe_customer_id</c> are not asserted in these
/// tests (they are test-aggregate-population, not DB constraint, scenarios).
/// </para>
/// </summary>
public class StripeCustomerRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public StripeCustomerRepositoryIntegrationTests()
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
    /// SQLite-compatible test DbContext — maps only <see cref="StripeCustomer"/>
    /// + <see cref="AuditEvent"/>. Sidesteps the Npgsql-specific converters
    /// the production BillingDbContext pulls in via the Subscription/Plan
    /// Money complex types.
    /// </summary>
    private sealed class TestStripeCustomerDbContext : DbContext
    {
        public TestStripeCustomerDbContext(DbContextOptions<TestStripeCustomerDbContext> options) : base(options) { }

        public DbSet<StripeCustomer> StripeCustomers => Set<StripeCustomer>();
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("billing");
            modelBuilder.Entity<StripeCustomer>(b =>
            {
                b.ToTable("stripe_customers");
                b.HasKey(c => c.Id);
                b.Property(c => c.Id).HasColumnName("id");
                b.Property(c => c.UserId).HasColumnName("user_id").IsRequired();
                b.Property(c => c.StripeCustomerId).HasColumnName("stripe_customer_id").HasMaxLength(64).IsRequired();
                b.Property(c => c.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
                b.Property(c => c.DisplayName).HasColumnName("display_name").HasMaxLength(120);
                b.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(c => c.UpdatedAt).HasColumnName("updated_at");
                b.Ignore(c => c.DomainEvents);
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
    /// Test-only <see cref="IStripeCustomerRepository"/> impl — mirrors the
    /// production <c>StripeCustomerRepository</c> methods (AddAsync,
    /// GetByUserIdAsync, GetByStripeCustomerIdAsync). No Update / Delete
    /// methods to implement — the interface is immutable (matches the
    /// entity docstring).
    /// </summary>
    private sealed class TestStripeCustomerRepository : IStripeCustomerRepository
    {
        private readonly TestStripeCustomerDbContext _db;

        public TestStripeCustomerRepository(TestStripeCustomerDbContext db) { _db = db; }

        public Task<StripeCustomer?> GetByUserIdAsync(Guid userId, CancellationToken ct)
            => _db.StripeCustomers.FirstOrDefaultAsync(c => c.UserId == userId, ct);

        public Task<StripeCustomer?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct)
            => _db.StripeCustomers.FirstOrDefaultAsync(c => c.StripeCustomerId == stripeCustomerId, ct);

        public async Task AddAsync(StripeCustomer customer, CancellationToken ct)
            => await _db.StripeCustomers.AddAsync(customer, ct);
    }

    private (IServiceProvider sp, ITenantContext tenant, IClock clock, Guid currentUserId)
        BuildServices(Guid? currentUserId = null)
    {
        var customerOpts = new DbContextOptionsBuilder<TestStripeCustomerDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new TestStripeCustomerDbContext(customerOpts))
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

        services.AddDbContext<TestStripeCustomerDbContext>(opts => opts.UseSqlite(_connection));
        var auditContextOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        services.AddScoped<AuditDbContext>(_ => new AuditDbContext(auditContextOpts));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestStripeCustomerDbContext>());

        var tenantId = Guid.NewGuid();
        var userId = currentUserId ?? Guid.NewGuid();
        var tenant = new StaticTenantContext(new TenantId(tenantId), userId);
        services.AddSingleton<ITenantContext>(tenant);

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IStripeCustomerRepository, TestStripeCustomerRepository>();
        // The slice 8b.1 decorator registration — under test here.
        services.Decorate<IStripeCustomerRepository, StripeCustomerAuditDecorator>();

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

    private static StripeCustomer CreateCustomer(Guid userId, IClock clock, string stripeCustomerId = "cus_test_abc")
        => StripeCustomer.Create(
            id: Guid.NewGuid(),
            userId: userId,
            stripeCustomerId: stripeCustomerId,
            email: $"u{userId:N}@example.test",
            displayName: "Test User",
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
    public async Task CreateStripeCustomer_ByOwner_WritesAuditEvent_WithActionCreated()
    {
        // Phase 1 #1: AddAsync → AuditAction.Created, no diff. The audit
        // row's EntityType is "StripeCustomer" + UserId/TenantId from
        // ITenantContext. Created events carry no diff (ChangesJson = null).
        var userId = Guid.NewGuid();
        var (sp, tenant, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IStripeCustomerRepository>();
        var customerDb = scope.ServiceProvider.GetRequiredService<TestStripeCustomerDbContext>();
        var auditDb = NewAuditDbContext();

        var customer = CreateCustomer(userId, clock);
        await repo.AddAsync(customer, CancellationToken.None);
        await customerDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(StripeCustomer));
        saved.EntityId.Should().Be(customer.Id);
        saved.Action.Should().Be(AuditAction.Created);
        saved.ChangesJson.Should().BeNull("Created events carry no diff.");
        saved.TenantId.Should().Be(tenant.Current!.Value);
        saved.UserId.Should().Be(tenant.CurrentUserId);
    }

    [Fact]
    public async Task ReadStripeCustomer_ByUserIdOrStripeId_DoesNotEmitAuditEvent()
    {
        // Phase 1 #2: both bespoke read methods (GetByUserIdAsync +
        // GetByStripeCustomerIdAsync) emit NO audit event. Reads are not
        // audited (matches Wave 6 + 7 + 8a.1 + 8a.2 + 8a.3 + 8b.1
        // precedent). The decorator MUST forward both reads to the inner
        // repository without any audit interaction.
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IStripeCustomerRepository>();
        var customerDb = scope.ServiceProvider.GetRequiredService<TestStripeCustomerDbContext>();
        var auditDb = NewAuditDbContext();

        // Stage the customer directly via the DbContext so the setup doesn't
        // emit audit events (the read test asserts NO audit events at all).
        var customer = CreateCustomer(userId, clock);
        customerDb.StripeCustomers.Add(customer);
        await customerDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        var byUserId = await repo.GetByUserIdAsync(userId, CancellationToken.None);
        byUserId.Should().NotBeNull();
        var byStripeId = await repo.GetByStripeCustomerIdAsync(customer.StripeCustomerId, CancellationToken.None);
        byStripeId.Should().NotBeNull();
        await auditDb.SaveChangesAsync();

        var auditRows = await auditDb.AuditEvents.ToListAsync();
        auditRows.Should().BeEmpty(
            "GetByUserIdAsync and GetByStripeCustomerIdAsync are reads — no audit event " +
            "should be written (matches the Wave 6 + 7 + 8a.1 + 8a.2 + 8a.3 precedent).");
    }

    [Fact]
    public void IStripeCustomerRepository_HasNoUpdateOrDeleteMethods_ImmutableAggregateContractPin()
    {
        // Phase 1 #3 (contract pin): reflection asserts that
        // IStripeCustomerRepository exposes only AddAsync +
        // GetByUserIdAsync + GetByStripeCustomerIdAsync. The
        // immutable-aggregate invariant is enforced at the interface level
        // (no UpdateAsync or DeleteAsync method exists on the contract) —
        // a future extension that silently adds a mutation method would
        // break this test and require a breaking-change review (matches
        // the 8a.3 PreTradeChecklistRepositoryIntegrationTests +
        // 8a.1 IAccountRepositoryContractTests
        // RemoveAsync_IsNotOnInterface_AfterRename precedent).
        //
        // Walking the interface's full method set (including inherited
        // members) is required — Type.GetMethods() does NOT flatten
        // inherited interface methods by default (see the 8a.1
        // IAccountRepositoryContractTests lesson on
        // Concat(GetInterfaces()).SelectMany(t => t.GetMethods())).
        var interfaceType = typeof(IStripeCustomerRepository);
        var allMethods = interfaceType
            .GetMethods()
            .Concat(interfaceType.GetInterfaces().SelectMany(i => i.GetMethods()))
            .ToArray();

        allMethods.Should().NotContain(m => m.Name == "UpdateAsync",
            "IStripeCustomerRepository is for an immutable aggregate — no UpdateAsync on the interface " +
            "(the StripeCustomer is created once + never modified; compliance officers rely on " +
            "audit.events never receiving Updated/Deleted rows for entity_type = 'StripeCustomer').");
        allMethods.Should().NotContain(m => m.Name == "DeleteAsync",
            "IStripeCustomerRepository is for an immutable aggregate — no DeleteAsync on the interface " +
            "(a user unbinding from Stripe must be handled at the gateway/user level, not as a row delete).");
        allMethods.Should().Contain(m => m.Name == "AddAsync",
            "AddAsync is the sole mutation surface — the decorator wraps it with AuditAction.Created.");
        allMethods.Should().Contain(m => m.Name == "GetByUserIdAsync",
            "GetByUserIdAsync is one of the read surfaces — forwarded without audit.");
        allMethods.Should().Contain(m => m.Name == "GetByStripeCustomerIdAsync",
            "GetByStripeCustomerIdAsync is the other read surface — forwarded without audit.");
    }
}
