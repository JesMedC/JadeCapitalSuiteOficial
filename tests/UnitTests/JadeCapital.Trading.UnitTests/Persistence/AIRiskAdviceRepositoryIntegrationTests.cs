using System.Reflection;
using FluentAssertions;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Ai;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Ai;
using JadeCapital.Trading.Infrastructure.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Trading.UnitTests.Persistence;

/// <summary>
/// Integration tests for the <see cref="AIRiskAdviceAuditDecorator"/>
/// wired via Scrutor (Wave 9, slice 9a.1).
///
/// <para>
/// Mirrors a simplified <c>StripeCustomerAuditDecorator</c> shape (Wave 8 8b.1)
/// — bespoke, write-once. The <see cref="IAIRiskAdviceRepository"/>
/// interface exposes only <c>AddAsync</c> + <c>FindByUserAndTradeAsync</c>;
/// the <see cref="AIRiskAdvice"/> aggregate is immutable after
/// <see cref="AIRiskAdvice.Create"/> per the entity docstring
/// ("Immutability: the aggregate has no public setters. EF rehydration uses
/// the Rehydrate factory which is reserved for the repository and skips the
/// validation guards"). No <c>UpdateAsync</c> or <c>DeleteAsync</c> exists
/// on the interface.
/// </para>
///
/// <para>
/// <b>Why this decorator is bespoke (does NOT use the generic
/// <c>DecoratedRepository&lt;T&gt;</c> helper)</b>: same rationale as
/// Wave 8 8b.1 — the write-once interface is smaller than the canonical
/// <c>IRepository&lt;T&gt;</c> CRUD surface (1 mutation + 1 read); extending
/// <c>IRepository&lt;T&gt;</c> would silently add mutation methods the
/// entity docstring forbids. Bespoke decorator preserves the write-once
/// invariant.
/// </para>
///
/// <para>
/// <b>Why IsOwner cross-tenant check on AddAsync</b>: per spec §9a.1
/// "Cross-tenant access MUST emit <c>AuditAction.Denied</c> and throw
/// <c>UnauthorizedAccessException</c>". Even though <c>AddAsync</c>
/// inserts a NEW row (no read-then-update race), the row's
/// <see cref="AIRiskAdvice.UserId"/> still comes from the caller.
/// <c>OllamaAIRiskAdvisor</c> and <c>GetPreTradeAdviceHandler</c> both
/// accept <c>userId</c> as a command parameter — a cross-tenant invocation
/// could submit an advice carrying another tenant's user id. The decorator
/// is the only enforcement point (matches Wave 8 8b.1 StripeCustomer
/// precedent — the handler-side consistency is not guaranteed).
/// </para>
///
/// <list type="bullet">
///   <item>Wrapping <see cref="IAIRiskAdviceRepository.AddAsync"/> with the
///         <b>cross-tenant <c>IsOwner</c> check</b> +
///         <see cref="AuditAction.Created"/> audit logging. On
///         cross-tenant attempt: <see cref="AuditAction.Denied"/> +
///         <see cref="UnauthorizedAccessException"/>.</item>
///   <item>Forwarding <see cref="IAIRiskAdviceRepository.FindByUserAndTradeAsync"/>
///         to the inner without audit logging (matches Wave 6 + 7 + 8a.x +
///         8b.1 + 9a.1 precedent: reads are not audited).</item>
/// </list>
///
/// <para>
/// Three RED scenarios pinned here (per tasks.md §9a.1 Phase 1.1 + design.md
/// §9a.1 — bespoke write-once decorator; mirrors the Wave 8 8b.1
/// <c>StripeCustomerAuditDecorator</c> precedent):
/// <list type="number">
///   <item>Create advice (<c>AddAsync</c>) → audit row with
///         <see cref="AuditAction.Created"/>,
///         <c>EntityType = "AIRiskAdvice"</c>, <c>TenantId</c> +
///         <c>UserId</c> from <see cref="ITenantContext"/>,
///         <c>ChangesJson</c> = null.</item>
///   <item>Read (<c>FindByUserAndTradeAsync</c>) → NO audit event.
///         Reads are not audited.</item>
///   <item>Contract pin — reflection asserts <see cref="IAIRiskAdviceRepository"/>
///         exposes NO <c>UpdateAsync</c> or <c>DeleteAsync</c> methods.
///         The write-once invariant is enforced at the interface level.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why a focused <c>TestAIRiskAdviceDbContext</c></b>: mirrors the Wave 8
/// 8b.1 <c>TestStripeCustomerDbContext</c> + 8a.3
/// <c>TestPreTradeChecklistDbContext</c> pattern. The production
/// <c>TradingDbContext</c> pulls in Npgsql-specific converters via
/// <c>JournalEntry.Tags</c> + other Money/array mappings that fail to
/// compose on SQLite. A focused helper DbContext that maps only
/// <see cref="AIRiskAdvice"/> + <see cref="AuditEvent"/> keeps the model
/// SQLite-compatible without touching the production schema.
/// </para>
/// </summary>
public class AIRiskAdviceRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public AIRiskAdviceRepositoryIntegrationTests()
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
    /// SQLite-compatible test DbContext — maps only <see cref="AIRiskAdvice"/>
    /// + <see cref="AuditEvent"/>. Sidesteps the Npgsql-specific converters
    /// the production TradingDbContext pulls in.
    /// </summary>
    private sealed class TestAIRiskAdviceDbContext : DbContext
    {
        public TestAIRiskAdviceDbContext(DbContextOptions<TestAIRiskAdviceDbContext> options) : base(options) { }

        public DbSet<AIRiskAdvice> AIRiskAdvices => Set<AIRiskAdvice>();
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("trading");
            modelBuilder.Entity<AIRiskAdvice>(b =>
            {
                b.ToTable("ai_risk_advice");
                b.HasKey(c => c.Id);
                b.Property(c => c.Id).HasColumnName("id");
                b.Property(c => c.UserId).HasColumnName("user_id").IsRequired();
                b.Property(c => c.TradeId).HasColumnName("trade_id");
                b.Property(c => c.ContextJson).HasColumnName("context_json").IsRequired();
                b.Property(c => c.ProviderResponseText).HasColumnName("provider_response_text").IsRequired();
                b.Property(c => c.ParsedAction).HasColumnName("parsed_action").HasConversion<byte>().IsRequired();
                b.Property(c => c.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
                b.Property(c => c.Model).HasColumnName("model").HasMaxLength(64).IsRequired();
                b.Property(c => c.LatencyMs).HasColumnName("latency_ms").IsRequired();
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
    /// Test-only <see cref="IAIRiskAdviceRepository"/> impl — mirrors the
    /// production <c>AIRiskAdviceRepository</c> methods (AddAsync,
    /// FindByUserAndTradeAsync). No Update / Delete methods to implement —
    /// the interface is write-once (matches the entity docstring).
    /// </summary>
    private sealed class TestAIRiskAdviceRepository : IAIRiskAdviceRepository
    {
        private readonly TestAIRiskAdviceDbContext _db;

        public TestAIRiskAdviceRepository(TestAIRiskAdviceDbContext db) { _db = db; }

        public async Task<AIRiskAdvice?> FindByUserAndTradeAsync(
            Guid userId,
            Guid tradeId,
            CancellationToken ct)
        {
            // SQLite's EF provider does NOT support DateTimeOffset in
            // ORDER BY clauses (NotSupportedException). The production
            // repo orders by CreatedAt DESC on Postgres; the test runs on
            // SQLite. The read assertion is only about "no audit event" —
            // ordering is irrelevant to the assertion. Use a client-side
            // materialization to preserve the production semantics without
            // hitting the SQLite ORDER BY limitation.
            var rows = await _db.AIRiskAdvices
                .Where(a => a.UserId == userId && a.TradeId == tradeId)
                .ToListAsync(ct);
            return rows.OrderByDescending(a => a.CreatedAt).FirstOrDefault();
        }

        public async Task AddAsync(AIRiskAdvice advice, CancellationToken ct)
            => await _db.AIRiskAdvices.AddAsync(advice, ct);
    }

    private (IServiceProvider sp, ITenantContext tenant, IClock clock, Guid currentUserId)
        BuildServices(Guid? currentUserId = null)
    {
        var adviceOpts = new DbContextOptionsBuilder<TestAIRiskAdviceDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new TestAIRiskAdviceDbContext(adviceOpts))
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

        services.AddDbContext<TestAIRiskAdviceDbContext>(opts => opts.UseSqlite(_connection));
        var auditContextOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        services.AddScoped<AuditDbContext>(_ => new AuditDbContext(auditContextOpts));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestAIRiskAdviceDbContext>());

        var tenantId = Guid.NewGuid();
        var userId = currentUserId ?? Guid.NewGuid();
        var tenant = new StaticTenantContext(new TenantId(tenantId), userId);
        services.AddSingleton<ITenantContext>(tenant);

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IAIRiskAdviceRepository, TestAIRiskAdviceRepository>();
        // The slice 9a.1 decorator registration — under test here.
        services.Decorate<IAIRiskAdviceRepository, AIRiskAdviceAuditDecorator>();

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

    private static AIRiskAdvice CreateAdvice(Guid userId, IClock clock, Guid? tradeId = null)
        => AIRiskAdvice.Create(
            userId: userId,
            tradeId: tradeId,
            contextJson: "{\"feed\":\"test\"}",
            response: new PromptResponse("{\"action\":\"allow\",\"reason\":\"ok\"}", "test-model", 100, TimeSpan.FromMilliseconds(50)),
            reason: "test reason",
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
    public async Task CreateAIRiskAdvice_ByOwner_WritesAuditEvent_WithActionCreated()
    {
        // Phase 1.1 #1: AddAsync → AuditAction.Created, no diff. The audit
        // row's EntityType is "AIRiskAdvice" + UserId/TenantId from
        // ITenantContext. Created events carry no diff (ChangesJson = null).
        var userId = Guid.NewGuid();
        var (sp, tenant, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAIRiskAdviceRepository>();
        var adviceDb = scope.ServiceProvider.GetRequiredService<TestAIRiskAdviceDbContext>();
        var auditDb = NewAuditDbContext();

        var advice = CreateAdvice(userId, clock);
        await repo.AddAsync(advice, CancellationToken.None);
        await adviceDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(AIRiskAdvice));
        saved.EntityId.Should().Be(advice.Id);
        saved.Action.Should().Be(AuditAction.Created);
        saved.ChangesJson.Should().BeNull("Created events carry no diff.");
        saved.TenantId.Should().Be(tenant.Current!.Value);
        saved.UserId.Should().Be(tenant.CurrentUserId);
    }

    [Fact]
    public async Task ReadAIRiskAdvice_ByUserAndTrade_DoesNotEmitAuditEvent()
    {
        // Phase 1.1 #2: the bespoke user-scoped read
        // (FindByUserAndTradeAsync) emits NO audit event. Reads are not
        // audited (matches Wave 6 + 7 + 8a.x + 8b.1 + 9a.1 StripeCustomer
        // precedent).
        var userId = Guid.NewGuid();
        var tradeId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAIRiskAdviceRepository>();
        var adviceDb = scope.ServiceProvider.GetRequiredService<TestAIRiskAdviceDbContext>();
        var auditDb = NewAuditDbContext();

        // Stage the advice directly via the DbContext so the setup doesn't
        // emit audit events (the read test asserts NO audit events at all).
        var advice = CreateAdvice(userId, clock, tradeId);
        adviceDb.AIRiskAdvices.Add(advice);
        await adviceDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        var found = await repo.FindByUserAndTradeAsync(userId, tradeId, CancellationToken.None);
        found.Should().NotBeNull();
        await auditDb.SaveChangesAsync();

        var auditRows = await auditDb.AuditEvents.ToListAsync();
        auditRows.Should().BeEmpty(
            "FindByUserAndTradeAsync is a read — no audit event should be written " +
            "(matches Wave 6 + 7 + 8a.x + 8b.1 + 9a.1 StripeCustomer precedent).");
    }

    [Fact]
    public void IAIRiskAdviceRepository_HasNoUpdateOrDeleteMethods_WriteOnceContractPin()
    {
        // Phase 1.1 #3 (contract pin): reflection asserts that
        // IAIRiskAdviceRepository exposes only AddAsync +
        // FindByUserAndTradeAsync. The write-once invariant is enforced at
        // the interface level — a future extension that silently adds a
        // mutation method would break this test and require a
        // breaking-change review (matches Wave 8 8b.1
        // IStripeCustomerRepository + 8a.3 IPreTradeChecklistRepository
        // contract pin precedent).
        //
        // Walking the interface's full method set (including inherited
        // members) is required — Type.GetMethods() does NOT flatten
        // inherited interface methods by default.
        var interfaceType = typeof(IAIRiskAdviceRepository);
        var allMethods = interfaceType
            .GetMethods()
            .Concat(interfaceType.GetInterfaces().SelectMany(i => i.GetMethods()))
            .ToArray();

        allMethods.Should().NotContain(m => m.Name == "UpdateAsync",
            "IAIRiskAdviceRepository is for an immutable aggregate — no UpdateAsync on the interface " +
            "(the AIRiskAdvice has no public setters per the entity docstring; creates run once and " +
            "compliance officers rely on audit.events never receiving Updated/Deleted rows for " +
            "entity_type = 'AIRiskAdvice').");
        allMethods.Should().NotContain(m => m.Name == "DeleteAsync",
            "IAIRiskAdviceRepository is for an immutable aggregate — no DeleteAsync on the interface " +
            "(the AIRiskAdvice advisory is kept forever for the compliance trail).");
        allMethods.Should().Contain(m => m.Name == "AddAsync",
            "AddAsync is the sole mutation surface — the decorator wraps it with AuditAction.Created.");
        allMethods.Should().Contain(m => m.Name == "FindByUserAndTradeAsync",
            "FindByUserAndTradeAsync is the read surface — forwarded without audit.");
    }
}
