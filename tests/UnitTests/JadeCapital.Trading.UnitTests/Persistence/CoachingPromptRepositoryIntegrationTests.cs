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
/// Integration tests for the <see cref="CoachingPromptAuditDecorator"/>
/// wired via Scrutor (Wave 9, slice 9a.1).
///
/// <para>
/// Mirrors the <see cref="AIRiskAdviceAuditDecorator"/> shape (slice 9a.1
/// Phase 1) — bespoke, write-once. The <see cref="ICoachingPromptRepository"/>
/// interface exposes only <c>AddAsync</c> + <c>FindByUserAndDateAsync</c>
/// + <c>ListByUserAndWindowAsync</c>; the <see cref="CoachingPrompt"/>
/// aggregate is immutable after <see cref="CoachingPrompt.Create"/> per the
/// entity docstring ("Immutability: the aggregate has no public setters.
/// EF rehydration uses the Rehydrate factory which is reserved for the
/// repository and skips the validation guards"). No <c>UpdateAsync</c> or
/// <c>DeleteAsync</c> exists on the interface.
/// </para>
///
/// <para>
/// <b>Why this decorator is bespoke (does NOT use the generic
/// <c>DecoratedRepository&lt;T&gt;</c> helper)</b>: same rationale as
/// <see cref="AIRiskAdviceAuditDecorator"/> — the write-once interface is
/// smaller than the canonical <c>IRepository&lt;T&gt;</c> CRUD surface
/// (1 mutation + 2 reads); extending <c>IRepository&lt;T&gt;</c> would
/// silently add mutation methods the entity docstring forbids. Bespoke
/// decorator preserves the write-once invariant.
/// </para>
///
/// <para>
/// <b>Why IsOwner cross-tenant check on AddAsync</b>: per spec §9a.1
/// "Cross-tenant access MUST emit <c>AuditAction.Denied</c> and throw
/// <c>UnauthorizedAccessException</c>". Both <c>GenerateCoachingPromptHandler</c>
/// (manual trigger) and <c>CoachingPromptService</c> (daily BG service)
/// accept the userId as a parameter — a cross-tenant invocation could
/// submit a prompt carrying another tenant's user id. The decorator is the
/// only enforcement point (matches the <see cref="AIRiskAdviceAuditDecorator"/>
/// + Wave 8 8b.1 StripeCustomer precedent — the handler-side consistency
/// is not guaranteed for multi-entry-point mutations).
/// </para>
///
/// <list type="bullet">
///   <item>Wrapping <see cref="ICoachingPromptRepository.AddAsync"/> with the
///         <b>cross-tenant <c>IsOwner</c> check</b> +
///         <see cref="AuditAction.Created"/> audit logging. On
///         cross-tenant attempt: <see cref="AuditAction.Denied"/> +
///         <see cref="UnauthorizedAccessException"/>.</item>
///   <item>Forwarding <see cref="ICoachingPromptRepository.FindByUserAndDateAsync"/>
///         + <see cref="ICoachingPromptRepository.ListByUserAndWindowAsync"/>
///         to the inner without audit logging (matches Wave 6 + 7 + 8a.x +
///         8b.1 + 9a.1 precedent: reads are not audited).</item>
/// </list>
///
/// <para>
/// Three RED scenarios pinned here (per tasks.md §9a.1 Phase 2.1 + design.md
/// §9a.1 — bespoke write-once decorator; mirrors the Wave 9 9a.1 Phase 1
/// <see cref="AIRiskAdviceAuditDecorator"/> precedent):
/// <list type="number">
///   <item>Create prompt (<c>AddAsync</c>) → audit row with
///         <see cref="AuditAction.Created"/>,
///         <c>EntityType = "CoachingPrompt"</c>, <c>TenantId</c> +
///         <c>UserId</c> from <see cref="ITenantContext"/>,
///         <c>ChangesJson</c> = null.</item>
///   <item>Reads (<c>FindByUserAndDateAsync</c> +
///         <c>ListByUserAndWindowAsync</c>) → NO audit event. Reads
///         are not audited. The test stages the prompt directly via
///         the DbContext (to avoid emitting a Created audit at setup),
///         then <c>AuditEvents.Clear()</c> removes any incidental rows
///         before the read.</item>
///   <item>Contract pin — reflection asserts <see cref="ICoachingPromptRepository"/>
///         exposes NO <c>UpdateAsync</c> or <c>DeleteAsync</c> methods.
///         The write-once invariant is enforced at the interface level.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why a focused <c>TestCoachingPromptDbContext</c></b>: mirrors the
/// <see cref="AIRiskAdviceRepositoryIntegrationTests.TestAIRiskAdviceDbContext"/>
/// + 8b.1 <c>TestStripeCustomerDbContext</c> + 8a.3
/// <c>TestPreTradeChecklistDbContext</c> pattern. The production
/// <c>TradingDbContext</c> pulls in Npgsql-specific converters via
/// <c>JournalEntry.Tags</c> + other Money/array mappings that fail to
/// compose on SQLite. A focused helper DbContext that maps only
/// <see cref="CoachingPrompt"/> + <see cref="AuditEvent"/> keeps the model
/// SQLite-compatible without touching the production schema.
/// </para>
/// </summary>
public class CoachingPromptRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public CoachingPromptRepositoryIntegrationTests()
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
    /// SQLite-compatible test DbContext — maps only <see cref="CoachingPrompt"/>
    /// + <see cref="AuditEvent"/>. Sidesteps the Npgsql-specific converters
    /// the production TradingDbContext pulls in.
    /// </summary>
    private sealed class TestCoachingPromptDbContext : DbContext
    {
        public TestCoachingPromptDbContext(DbContextOptions<TestCoachingPromptDbContext> options) : base(options) { }

        public DbSet<CoachingPrompt> CoachingPrompts => Set<CoachingPrompt>();
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("trading");
            modelBuilder.Entity<CoachingPrompt>(b =>
            {
                b.ToTable("coaching_prompts_ai");
                b.HasKey(p => p.Id);
                b.Property(p => p.Id).HasColumnName("id");
                b.Property(p => p.UserId).HasColumnName("user_id").IsRequired();
                b.Property(p => p.PromptText).HasColumnName("prompt_text").HasMaxLength(4000).IsRequired();
                b.Property(p => p.ContextJson).HasColumnName("context_json").IsRequired();
                b.Property(p => p.ProviderResponseText).HasColumnName("provider_response_text").IsRequired();
                b.Property(p => p.Model).HasColumnName("model").HasMaxLength(64).IsRequired();
                b.Property(p => p.LatencyMs).HasColumnName("latency_ms").IsRequired();
                b.Property(p => p.Severity).HasColumnName("severity").HasConversion<byte>().IsRequired();
                b.Property(p => p.Kind).HasColumnName("kind").HasConversion<byte>().IsRequired();
                b.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(p => p.UpdatedAt).HasColumnName("updated_at");
                b.Ignore(p => p.DomainEvents);
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
    /// Test-only <see cref="ICoachingPromptRepository"/> impl — mirrors the
    /// production <c>CoachingPromptRepository</c> methods (AddAsync,
    /// FindByUserAndDateAsync, ListByUserAndWindowAsync). No Update / Delete
    /// methods to implement — the interface is write-once (matches the
    /// entity docstring).
    /// </summary>
    private sealed class TestCoachingPromptRepository : ICoachingPromptRepository
    {
        private readonly TestCoachingPromptDbContext _db;

        public TestCoachingPromptRepository(TestCoachingPromptDbContext db) { _db = db; }

        public async Task<CoachingPrompt?> FindByUserAndDateAsync(
            Guid userId,
            DateTimeOffset dayUtc,
            CancellationToken ct)
        {
            // SQLite's EF provider does NOT support DateTimeOffset
            // comparisons in WHERE clauses (InvalidOperationException).
            // The production repo translates to
            // [dayUtc.Date, dayUtc.Date + 1 day) on Postgres; the test
            // runs on SQLite. Resolve the user filter in SQL (cheap
            // index lookup) + filter the day window + order in memory
            // to preserve the production semantics without hitting the
            // SQLite DateTimeOffset limitation.
            var dayStart = new DateTimeOffset(dayUtc.Date, TimeSpan.Zero);
            var dayEnd = dayStart.AddDays(1);
            var rows = await _db.CoachingPrompts
                .Where(p => p.UserId == userId)
                .ToListAsync(ct);
            return rows
                .Where(p => p.CreatedAt >= dayStart && p.CreatedAt < dayEnd)
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefault();
        }

        public async Task<IReadOnlyList<CoachingPrompt>> ListByUserAndWindowAsync(
            Guid userId,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken ct)
        {
            // Same SQLite DateTimeOffset workaround as
            // FindByUserAndDateAsync — push the user filter to SQL,
            // materialize, then filter the window client-side and order
            // in memory.
            var rows = await _db.CoachingPrompts
                .Where(p => p.UserId == userId)
                .ToListAsync(ct);
            return rows
                .Where(p => p.CreatedAt >= from && p.CreatedAt < to)
                .OrderByDescending(p => p.CreatedAt)
                .ToList();
        }

        public async Task AddAsync(CoachingPrompt prompt, CancellationToken ct)
            => await _db.CoachingPrompts.AddAsync(prompt, ct);
    }

    private (IServiceProvider sp, ITenantContext tenant, IClock clock, Guid currentUserId)
        BuildServices(Guid? currentUserId = null)
    {
        var promptOpts = new DbContextOptionsBuilder<TestCoachingPromptDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new TestCoachingPromptDbContext(promptOpts))
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

        services.AddDbContext<TestCoachingPromptDbContext>(opts => opts.UseSqlite(_connection));
        var auditContextOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        services.AddScoped<AuditDbContext>(_ => new AuditDbContext(auditContextOpts));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestCoachingPromptDbContext>());

        var tenantId = Guid.NewGuid();
        var userId = currentUserId ?? Guid.NewGuid();
        var tenant = new StaticTenantContext(new TenantId(tenantId), userId);
        services.AddSingleton<ITenantContext>(tenant);

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<ICoachingPromptRepository, TestCoachingPromptRepository>();
        // The slice 9a.1 decorator registration — under test here.
        services.Decorate<ICoachingPromptRepository, CoachingPromptAuditDecorator>();

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

    private static CoachingPrompt CreatePrompt(Guid userId, IClock clock)
        => CoachingPrompt.Create(
            userId: userId,
            promptText: "Stay disciplined — close at your stop.",
            contextJson: "{\"feed\":\"test\"}",
            response: new PromptResponse("{\"text\":\"ok\"}", "test-model", 100, TimeSpan.FromMilliseconds(50)),
            severity: CoachingPromptSeverity.Medium,
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
    public async Task AddCoachingPrompt_ByOwner_WritesAuditEvent_WithActionCreated()
    {
        // Phase 2.1 #1: AddAsync → AuditAction.Created, no diff. The audit
        // row's EntityType is "CoachingPrompt" + UserId/TenantId from
        // ITenantContext. Created events carry no diff (ChangesJson = null).
        var userId = Guid.NewGuid();
        var (sp, tenant, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICoachingPromptRepository>();
        var promptDb = scope.ServiceProvider.GetRequiredService<TestCoachingPromptDbContext>();
        var auditDb = NewAuditDbContext();

        var prompt = CreatePrompt(userId, clock);
        await repo.AddAsync(prompt, CancellationToken.None);
        await promptDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(CoachingPrompt));
        saved.EntityId.Should().Be(prompt.Id);
        saved.Action.Should().Be(AuditAction.Created);
        saved.ChangesJson.Should().BeNull("Created events carry no diff.");
        saved.TenantId.Should().Be(tenant.Current!.Value);
        saved.UserId.Should().Be(tenant.CurrentUserId);
    }

    [Fact]
    public async Task ReadCoachingPrompts_ByUserAndWindow_DoesNotEmitAuditEvent()
    {
        // Phase 2.1 #2: the two user-scoped reads (FindByUserAndDateAsync +
        // ListByUserAndWindowAsync) emit NO audit event. Reads are not
        // audited (matches Wave 6 + 7 + 8a.x + 8b.1 + 9a.1 StripeCustomer
        // + 9a.1 AIRiskAdvice precedent).
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICoachingPromptRepository>();
        var promptDb = scope.ServiceProvider.GetRequiredService<TestCoachingPromptDbContext>();
        var auditDb = NewAuditDbContext();

        // Stage the prompt directly via the DbContext so the setup doesn't
        // emit audit events (the read test asserts NO audit events at all).
        var prompt = CreatePrompt(userId, clock);
        promptDb.CoachingPrompts.Add(prompt);
        await promptDb.SaveChangesAsync();

        // Clear any incidental audit events from the setup (defense in depth)
        // before exercising the reads.
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        // Exercise BOTH reads.
        var found = await repo.FindByUserAndDateAsync(
            userId,
            new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);
        found.Should().NotBeNull();

        var listed = await repo.ListByUserAndWindowAsync(
            userId,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);
        listed.Should().NotBeEmpty();

        await auditDb.SaveChangesAsync();

        var auditRows = await auditDb.AuditEvents.ToListAsync();
        auditRows.Should().BeEmpty(
            "FindByUserAndDateAsync + ListByUserAndWindowAsync are reads — no audit event should be written " +
            "(matches Wave 6 + 7 + 8a.x + 8b.1 + 9a.1 StripeCustomer + 9a.1 AIRiskAdvice precedent).");
    }

    [Fact]
    public void ICoachingPromptRepository_HasNoUpdateOrDeleteMethods_WriteOnceContractPin()
    {
        // Phase 2.1 #3 (contract pin): reflection asserts that
        // ICoachingPromptRepository exposes only AddAsync +
        // FindByUserAndDateAsync + ListByUserAndWindowAsync. The
        // write-once invariant is enforced at the interface level — a
        // future extension that silently adds a mutation method would
        // break this test and require a breaking-change review (matches
        // Wave 8 8b.1 IStripeCustomerRepository + 8a.3
        // IPreTradeChecklistRepository + 9a.1 IAIRiskAdviceRepository
        // contract pin precedent).
        var interfaceType = typeof(ICoachingPromptRepository);
        var allMethods = interfaceType
            .GetMethods()
            .Concat(interfaceType.GetInterfaces().SelectMany(i => i.GetMethods()))
            .ToArray();

        allMethods.Should().NotContain(m => m.Name == "UpdateAsync",
            "ICoachingPromptRepository is for an immutable aggregate — no UpdateAsync on the interface " +
            "(the CoachingPrompt has no public setters per the entity docstring; creates run once and " +
            "compliance officers rely on audit.events never receiving Updated/Deleted rows for " +
            "entity_type = 'CoachingPrompt').");
        allMethods.Should().NotContain(m => m.Name == "DeleteAsync",
            "ICoachingPromptRepository is for an immutable aggregate — no DeleteAsync on the interface " +
            "(the CoachingPrompt prompt is kept forever for the compliance trail).");
        allMethods.Should().Contain(m => m.Name == "AddAsync",
            "AddAsync is the sole mutation surface — the decorator wraps it with AuditAction.Created.");
        allMethods.Should().Contain(m => m.Name == "FindByUserAndDateAsync",
            "FindByUserAndDateAsync is one of the two read surfaces — forwarded without audit.");
        allMethods.Should().Contain(m => m.Name == "ListByUserAndWindowAsync",
            "ListByUserAndWindowAsync is the other read surface — forwarded without audit.");
    }
}
