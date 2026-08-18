using FluentAssertions;
using JadeCapital.Billing.Application.Abstractions;
using JadeCapital.Billing.Domain.Subscriptions;
using JadeCapital.Billing.Infrastructure.Audit;
using JadeCapital.Billing.Infrastructure.Persistence;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Identity.UnitTests.Persistence;

/// <summary>
/// Integration tests for the <see cref="SubscriptionAuditDecorator"/> wired
/// via Scrutor (Wave 6, slice 6d.2, Phases 3.5 + 3.6).
///
/// <para>
/// Three RED scenarios pinned here (per tasks.md line 482):
/// </para>
/// <list type="number">
///   <item>Create subscription → <c>audit.events</c> row with
///         <c>AuditAction.Created</c>.</item>
///   <item>Tier change via <see cref="Subscription.ChangeTier"/> →
///         <c>audit.events</c> row with <c>AuditAction.Updated</c> + a diff
///         showing the planCode transition.</item>
///   <item>Cancel via <see cref="Subscription.Cancel"/> →
///         <c>audit.events</c> row with <c>AuditAction.Deleted</c> + a
///         diff showing the status transition Active → Cancelled.</item>
/// </list>
///
/// <para>
/// <b>Why a fully independent test DbContext</b>: mirrors the
/// <c>ImportJobRepositoryIntegrationTests</c> pattern from Phases 3.3-3.4.
/// The production <c>BillingDbContext</c> has the Money complex-type
/// mapping for Plan.MonthlyPrice + other Npgsql-specific converters that
/// fail on SQLite. The test uses a focused helper DbContext with only
/// Subscription + Plan + AuditEvent mapped.
/// </para>
/// </summary>
public class SubscriptionRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public SubscriptionRepositoryIntegrationTests()
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
    /// SQLite-compatible test DbContext — maps only <see cref="Subscription"/>
    /// + <see cref="Plan"/> + <see cref="AuditEvent"/>. Sidesteps the
    /// Npgsql-specific converters the production BillingDbContext pulls in.
    /// </summary>
    private sealed class TestBillingDbContext : DbContext
    {
        public TestBillingDbContext(DbContextOptions<TestBillingDbContext> options) : base(options) { }

        public DbSet<Subscription> Subscriptions => Set<Subscription>();
        public DbSet<Plan> Plans => Set<Plan>();
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("billing");
            modelBuilder.Entity<Subscription>(b =>
            {
                b.ToTable("subscriptions");
                b.HasKey(s => s.Id);
                b.Property(s => s.Id).HasColumnName("id");
                b.Property(s => s.UserId).HasColumnName("user_id").IsRequired();
                b.Property(s => s.PlanCode).HasColumnName("plan_code").HasMaxLength(64).IsRequired()
                    .HasConversion(c => c.Value, v => PlanCode.FromTrusted(v));
                b.Property(s => s.Status).HasColumnName("status").HasConversion<byte>().IsRequired();
                b.Property(s => s.TrialEndsAt).HasColumnName("trial_ends_at");
                b.Property(s => s.StripeSubscriptionId).HasColumnName("stripe_subscription_id").HasMaxLength(128);
                b.Property(s => s.Version).HasColumnName("version").IsRequired();
                // CurrentPeriod (SubscriptionPeriod value object) — minimal
                // mapping for SQLite; the production config splits the VO
                // across 2 columns. The test only needs the period to round-
                // trip through SaveChanges without crashing.
                b.OwnsOne(s => s.CurrentPeriod, p =>
                {
                    p.Property(x => x.Start).HasColumnName("current_period_start");
                    p.Property(x => x.End).HasColumnName("current_period_end");
                });
                // History collection — internal navigation, not exposed to EF.
                b.Ignore("_history");
                b.Ignore(s => s.History);
                b.Ignore(s => s.LastHistoryEntry);
                b.Ignore(s => s.DomainEvents);
            });
            modelBuilder.Entity<Plan>(b =>
            {
                b.ToTable("plans");
                b.HasKey(p => p.Id);
                b.Property(p => p.Id).HasColumnName("id");
                // PlanCode is a value object — SQLite can't map it directly.
                // Use the inner Value (string) via a value converter.
                b.Property(p => p.Code).HasColumnName("code").HasMaxLength(64).IsRequired()
                    .HasConversion(c => c.Value, v => PlanCode.FromTrusted(v));
                b.Property(p => p.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
                // Money complex type — split Amount + CurrencyCode into 2
                // scalar columns for SQLite (matches the production Npgsql
                // mapping conceptually but sidesteps the SQLite OwnsOne
                // composition issue on the complex-type registration).
                b.OwnsOne(p => p.MonthlyPrice, m =>
                {
                    m.Property(x => x.Amount).HasColumnName("monthly_price_amount");
                    m.Property(x => x.CurrencyCode).HasColumnName("monthly_price_currency").HasMaxLength(8);
                });
                b.Property(p => p.IsEligibleForSelfService).HasColumnName("is_eligible_for_self_service").IsRequired();
                b.Property(p => p.IsDeprecated).HasColumnName("is_deprecated").IsRequired();
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
    /// Test-only <see cref="ISubscriptionRepository"/> impl — mirrors the
    /// production <c>SubscriptionRepository</c> against the test DbContext.
    /// </summary>
    private sealed class TestSubscriptionRepository : ISubscriptionRepository
    {
        private readonly TestBillingDbContext _db;
        public TestSubscriptionRepository(TestBillingDbContext db) { _db = db; }

        public Task<Subscription?> GetByIdAsync(Guid id, CancellationToken ct)
            => _db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);

        public async Task AddAsync(Subscription subscription, CancellationToken ct)
            => await _db.Subscriptions.AddAsync(subscription, ct);

        public Task UpdateAsync(Subscription subscription, CancellationToken ct)
        {
            var entry = _db.Entry(subscription);
            if (entry.State == EntityState.Detached)
                _db.Subscriptions.Update(subscription);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Subscription subscription, CancellationToken ct)
        {
            _db.Subscriptions.Remove(subscription);
            return Task.CompletedTask;
        }
    }

    private (IServiceProvider sp, ITenantContext tenant, IClock clock, Guid currentUserId)
        BuildServices(Guid? currentUserId = null)
    {
        var billingOpts = new DbContextOptionsBuilder<TestBillingDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new TestBillingDbContext(billingOpts))
            db.Database.EnsureCreated();

        var auditOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new AuditDbContext(auditOpts))
            db.Database.EnsureCreated();

        // FIX (slice 6d.2): EF Core 9 SQLite EnsureCreated is
        // "all-or-nothing" — force-create the events table via raw SQL.
        // Same fixture fix as the Tenant / ImportJob integration tests.
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
        services.AddSingleton<IClock>(_ => new StaticClock(
            new DateTimeOffset(2026, 8, 19, 14, 0, 0, TimeSpan.Zero)));

        services.AddDbContext<TestBillingDbContext>(opts => opts.UseSqlite(_connection));
        var auditContextOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        services.AddScoped<AuditDbContext>(_ => new AuditDbContext(auditContextOpts));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestBillingDbContext>());

        var tenantId = Guid.NewGuid();
        var userId = currentUserId ?? Guid.NewGuid();
        var tenant = new StaticTenantContext(new TenantId(tenantId), userId);
        services.AddSingleton<ITenantContext>(tenant);

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<ISubscriptionRepository, TestSubscriptionRepository>();
        services.Decorate<ISubscriptionRepository, SubscriptionAuditDecorator>();

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

    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 19, 14, 0, 0, TimeSpan.Zero);

    private static Plan NewProPlan()
    {
        var code = PlanCode.FromTrusted("pro");
        var price = Money.FromTrusted(99m, "USD");
        return Plan.FromTrusted(Guid.NewGuid(), code, "Pro Plan", price, true, false);
    }

    private static Plan NewEnterprisePlan()
    {
        var code = PlanCode.FromTrusted("enterprise");
        var price = Money.FromTrusted(499m, "USD");
        return Plan.FromTrusted(Guid.NewGuid(), code, "Enterprise Plan", price, true, false);
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
    public async Task CreateSubscription_WritesAuditEvent_WithActionCreated()
    {
        // Phase 3 #1: AddAsync → audit row with AuditAction.Created.
        var userId = Guid.NewGuid();
        var (sp, _, _, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();
        var billingDb = scope.ServiceProvider.GetRequiredService<TestBillingDbContext>();
        var auditDb = NewAuditDbContext();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var plan = NewProPlan();
        var subscription = Subscription.Create(
            Guid.NewGuid(), userId, plan, SubscriptionStatus.Active, trialEndsAt: null, utcNow: clock.UtcNow).Value;

        await repo.AddAsync(subscription, CancellationToken.None);
        await billingDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(Subscription));
        saved.EntityId.Should().Be(subscription.Id);
        saved.Action.Should().Be(AuditAction.Created);
        saved.ChangesJson.Should().BeNull("Created events carry no diff.");
    }

    [Fact]
    public async Task ChangeTier_WritesAuditEvent_WithActionUpdatedAndDiff()
    {
        // Phase 3 #2: ChangeTier → UpdateAsync → audit row with
        // AuditAction.Updated + planCode transition diff.
        var userId = Guid.NewGuid();
        var (sp, _, _, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();
        var billingDb = scope.ServiceProvider.GetRequiredService<TestBillingDbContext>();
        var auditDb = NewAuditDbContext();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var proPlan = NewProPlan();
        var enterprisePlan = NewEnterprisePlan();
        var subscription = Subscription.Create(
            Guid.NewGuid(), userId, proPlan, SubscriptionStatus.Active, trialEndsAt: null, utcNow: clock.UtcNow).Value;
        await repo.AddAsync(subscription, CancellationToken.None);
        await billingDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        var tierResult = subscription.ChangeTier(enterprisePlan, observedVersion: 1, actor: "test", utcNow: clock.UtcNow);
        tierResult.IsSuccess.Should().BeTrue();
        await repo.UpdateAsync(subscription, CancellationToken.None);
        await billingDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Updated);
        saved.EntityId.Should().Be(subscription.Id);
        saved.ChangesJson.Should().NotBeNullOrEmpty("the diff payload identifies the changed field.");
        saved.ChangesJson.Should().Contain("planCode", "the diff payload identifies the planCode transition.");
    }

    [Fact]
    public async Task CancelSubscription_WritesAuditEvent_WithActionDeleted()
    {
        // Phase 3 #3: Cancel → UpdateAsync → audit row with
        // AuditAction.Deleted + status transition diff (Active → Cancelled).
        var userId = Guid.NewGuid();
        var (sp, _, _, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();
        var billingDb = scope.ServiceProvider.GetRequiredService<TestBillingDbContext>();
        var auditDb = NewAuditDbContext();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var plan = NewProPlan();
        var subscription = Subscription.Create(
            Guid.NewGuid(), userId, plan, SubscriptionStatus.Active, trialEndsAt: null, utcNow: clock.UtcNow).Value;
        await repo.AddAsync(subscription, CancellationToken.None);
        await billingDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        var cancelResult = subscription.Cancel(
            reason: "test", observedVersion: 1, actor: "test", utcNow: clock.UtcNow);
        cancelResult.IsSuccess.Should().BeTrue();
        await repo.UpdateAsync(subscription, CancellationToken.None);
        await billingDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Deleted,
            "Cancel is a state-machine termination — semantically a delete.");
        saved.EntityId.Should().Be(subscription.Id);
        saved.ChangesJson.Should().NotBeNullOrEmpty("the diff payload identifies the status transition.");
        saved.ChangesJson.Should().Contain("status", "the diff payload identifies the status field.");
    }
}