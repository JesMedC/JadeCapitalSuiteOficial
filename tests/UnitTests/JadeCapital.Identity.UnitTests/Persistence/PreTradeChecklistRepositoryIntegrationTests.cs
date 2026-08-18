using System.Reflection;
using FluentAssertions;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.PreTradeChecklists;
using JadeCapital.Trading.Infrastructure.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Identity.UnitTests.Persistence;

/// <summary>
/// Integration tests for the <see cref="PreTradeChecklistAuditDecorator"/>
/// wired via Scrutor (Wave 8, slice 8a.3).
///
/// <para>
/// Mirrors a simplified <c>TenantAuditDecorator</c> shape (Wave 6 6d.2) —
/// bespoke, smallest decorator in the wave. The
/// <see cref="IPreTradeChecklistRepository"/> interface has only
/// <c>AddAsync</c> + <c>ListByUserIdAsync</c>; the checklist is write-once
/// per the entity docstring ("UNA fila por trade — enforced por UNIQUE
/// INDEX sobre trade_id en la DB. La API no expone UPDATE del checklist").
/// There is no <c>UpdateAsync</c> or <c>DeleteAsync</c> on the interface.
/// </para>
///
/// <para>
/// The "write-once" design decision applies to the <b>checklist items
/// (children)</b>, not the aggregate itself (the aggregate is just the
/// header — emotionality + setup quality + RR + confluences snapshot).
/// The aggregate is created once at trade open + never modified.
/// </para>
///
/// Three RED scenarios pinned here (per tasks.md §8a.3 Phase 2):
/// <list type="number">
///   <item>Create checklist (<c>AddAsync</c>) → audit row with
///         <see cref="AuditAction.Created"/>, <c>EntityType = "PreTradeChecklist"</c>,
///         <c>TenantId</c> + <c>UserId</c> from <see cref="ITenantContext"/>,
///         <c>ChangesJson</c> = null.</item>
///   <item>Read list (<c>ListByUserIdAsync</c>) → NO audit event. Reads are
///         not audited (matches Wave 6 + 7 + 8a.1 + 8a.2 precedent).</item>
///   <item>Contract pin — reflection asserts <see cref="IPreTradeChecklistRepository"/>
///         exposes NO <c>UpdateAsync</c> or <c>DeleteAsync</c> methods. The
///         write-once invariant is enforced at the interface level (no
///         extension will silently add mutation methods without a
///         breaking-change review). Mirrors the 8a.1 <c>IAccountRepositoryContractTests</c>
///         + <c>IInstrumentRepositoryContractTests</c> rename-pin
///         precedent.</item>
/// </list>
///
/// <para>
/// <b>Why a focused <c>TestPreTradeChecklistDbContext</c></b>: mirrors the
/// 8a.1 AccountRepositoryIntegrationTests + 8a.2 AlertRepositoryIntegrationTests
/// + 8a.3 PlannerSessionRepositoryIntegrationTests pattern. The production
/// <c>TradingDbContext</c> pulls in Npgsql-specific converters (Money +
/// JournalEntry.Tags) that fail to compose on SQLite. A focused helper
/// DbContext keeps the model SQLite-compatible.
/// </para>
/// </summary>
public class PreTradeChecklistRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public PreTradeChecklistRepositoryIntegrationTests()
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
    /// SQLite-compatible test DbContext — maps only <see cref="PreTradeChecklist"/>
    /// + <see cref="AuditEvent"/>. Sidesteps the Npgsql-specific converters
    /// the production TradingDbContext pulls in. Maps the Submission VO
    /// as owned columns (matches the production EF configuration that
    /// persists the VO fields as columns on the row).
    /// </summary>
    private sealed class TestPreTradeChecklistDbContext : DbContext
    {
        public TestPreTradeChecklistDbContext(DbContextOptions<TestPreTradeChecklistDbContext> options) : base(options) { }

        public DbSet<PreTradeChecklist> PreTradeChecklists => Set<PreTradeChecklist>();
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("trading");
            modelBuilder.Entity<PreTradeChecklist>(b =>
            {
                b.ToTable("pre_trade_checklists");
                b.HasKey(c => c.Id);
                b.Property(c => c.Id).HasColumnName("id");
                b.Property(c => c.TradeId).HasColumnName("trade_id").IsRequired();
                b.Property(c => c.UserId).HasColumnName("user_id").IsRequired();
                b.Property(c => c.SubmittedAt).HasColumnName("submitted_at").IsRequired();
                b.Property(c => c.AIRiskAdvisoryJson).HasColumnName("ai_risk_advisory").HasColumnType("text");
                b.OwnsOne(c => c.Submission, sub =>
                    {
                        sub.Property(s => s.Emotionality).HasColumnName("emotionality").HasConversion<byte>().IsRequired();
                        sub.Property(s => s.SetupQuality).HasColumnName("setup_quality").HasConversion<byte>().IsRequired();
                        sub.Property(s => s.RiskRewardAtEntry).HasColumnName("risk_reward_at_entry").HasColumnType("decimal(8,2)").IsRequired();
                        sub.Property(s => s.RiskRewardTargetUsed).HasColumnName("risk_reward_target_used").HasColumnType("decimal(8,2)").IsRequired();
                        sub.Property(s => s.ConfluencesCount).HasColumnName("confluences_count").IsRequired();
                    });
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
    /// Test-only <see cref="IPreTradeChecklistRepository"/> impl — mirrors
    /// the production <c>ChecklistRepository</c> methods (AddAsync,
    /// ListByUserIdAsync). No Update / Delete methods to implement —
    /// the interface is write-once (matches the entity docstring).
    /// </summary>
    private sealed class TestPreTradeChecklistRepository : IPreTradeChecklistRepository
    {
        private readonly TestPreTradeChecklistDbContext _db;

        public TestPreTradeChecklistRepository(TestPreTradeChecklistDbContext db) { _db = db; }

        public async Task AddAsync(PreTradeChecklist checklist, CancellationToken ct)
            => await _db.PreTradeChecklists.AddAsync(checklist, ct);

        public async Task<IReadOnlyList<PreTradeChecklist>> ListByUserIdAsync(
            Guid userId, CancellationToken ct)
            => await _db.PreTradeChecklists
                .Where(c => c.UserId == userId)
                .ToListAsync(ct);
    }

    private (IServiceProvider sp, ITenantContext tenant, IClock clock, Guid currentUserId)
        BuildServices(Guid? currentUserId = null)
    {
        var checklistOpts = new DbContextOptionsBuilder<TestPreTradeChecklistDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new TestPreTradeChecklistDbContext(checklistOpts))
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

        services.AddDbContext<TestPreTradeChecklistDbContext>(opts => opts.UseSqlite(_connection));
        var auditContextOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        services.AddScoped<AuditDbContext>(_ => new AuditDbContext(auditContextOpts));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestPreTradeChecklistDbContext>());

        var tenantId = Guid.NewGuid();
        var userId = currentUserId ?? Guid.NewGuid();
        var tenant = new StaticTenantContext(new TenantId(tenantId), userId);
        services.AddSingleton<ITenantContext>(tenant);

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IPreTradeChecklistRepository, TestPreTradeChecklistRepository>();
        // The slice 8a.3 decorator registration — under test here.
        services.Decorate<IPreTradeChecklistRepository, PreTradeChecklistAuditDecorator>();

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

    private static PreTradeChecklist CreateChecklist(Guid tradeId, Guid userId, IClock clock)
        => PreTradeChecklist.Create(
            tradeId: tradeId,
            userId: userId,
            submission: new PreTradeChecklistSubmission(
                Emotionality: Emotionality.Confident,
                SetupQuality: SetupQuality.Good,
                RiskRewardAtEntry: 2.5m,
                RiskRewardTargetUsed: 2.0m,
                ConfluencesCount: 4),
            submittedAt: clock.UtcNow).Value;

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
    public async Task CreatePreTradeChecklist_WritesAuditEvent_WithActionCreated()
    {
        // Phase 2 #1: AddAsync → AuditAction.Created, no diff. The audit
        // row's EntityType is "PreTradeChecklist" + UserId/TenantId from
        // ITenantContext.
        var userId = Guid.NewGuid();
        var tradeId = Guid.NewGuid();
        var (sp, tenant, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPreTradeChecklistRepository>();
        var checklistDb = scope.ServiceProvider.GetRequiredService<TestPreTradeChecklistDbContext>();
        var auditDb = NewAuditDbContext();

        var checklist = CreateChecklist(tradeId, userId, clock);
        await repo.AddAsync(checklist, CancellationToken.None);
        await checklistDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(PreTradeChecklist));
        saved.EntityId.Should().Be(checklist.Id);
        saved.Action.Should().Be(AuditAction.Created);
        saved.ChangesJson.Should().BeNull("Created events carry no diff.");
        saved.TenantId.Should().Be(tenant.Current!.Value);
        saved.UserId.Should().Be(tenant.CurrentUserId);
    }

    [Fact]
    public async Task ListByUserIdAsync_WritesNoAuditEvent()
    {
        // Phase 2 #2: the bespoke user-scoped read (ListByUserIdAsync) emits
        // NO audit event. Reads are not audited (matches Wave 6 + 7 + 8a.1
        // + 8a.2 + 8a.3 PlannerSession precedent).
        var userId = Guid.NewGuid();
        var tradeId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPreTradeChecklistRepository>();
        var checklistDb = scope.ServiceProvider.GetRequiredService<TestPreTradeChecklistDbContext>();
        var auditDb = NewAuditDbContext();

        // Stage the checklist directly via the DbContext so the setup doesn't
        // emit audit events (the read test asserts NO audit events at all).
        var checklist = CreateChecklist(tradeId, userId, clock);
        checklistDb.PreTradeChecklists.Add(checklist);
        await checklistDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        var list = await repo.ListByUserIdAsync(userId, CancellationToken.None);
        list.Should().HaveCount(1);
        await auditDb.SaveChangesAsync();

        var auditRows = await auditDb.AuditEvents.ToListAsync();
        auditRows.Should().BeEmpty(
            "ListByUserIdAsync is a read — no audit event should be written " +
            "(matches the Wave 6 + 7 + 8a.1 + 8a.2 + 8a.3 PlannerSession precedent).");
    }

    [Fact]
    public void IPreTradeChecklistRepository_HasNoUpdateOrDeleteMethods_WriteOnceContractPin()
    {
        // Phase 2 #3 (contract pin): reflection asserts that
        // IPreTradeChecklistRepository exposes only AddAsync +
        // ListByUserIdAsync. The write-once invariant is enforced at the
        // interface level (no UpdateAsync or DeleteAsync method exists
        // on the contract) — a future extension that silently adds a
        // mutation method would break this test and require a
        // breaking-change review (matches the 8a.1
        // IAccountRepositoryContractTests.RemoveAsync_IsNotOnInterface_AfterRename
        // + IInstrumentRepositoryContractTests precedent).
        //
        // Walking the interface's full method set (including inherited
        // members) is required — Type.GetMethods() does NOT flatten
        // inherited interface methods by default (see the 8a.1
        // IAccountRepositoryContractTests lesson on
        // Concat(GetInterfaces()).SelectMany(t => t.GetMethods())).
        var interfaceType = typeof(IPreTradeChecklistRepository);
        var allMethods = interfaceType
            .GetMethods()
            .Concat(interfaceType.GetInterfaces().SelectMany(i => i.GetMethods()))
            .ToArray();

        allMethods.Should().NotContain(m => m.Name == "UpdateAsync",
            "IPreTradeChecklistRepository is write-once — no UpdateAsync on the interface " +
            "(the checklist is created once at OpenTrade time + never modified).");
        allMethods.Should().NotContain(m => m.Name == "DeleteAsync",
            "IPreTradeChecklistRepository is write-once — no DeleteAsync on the interface " +
            "(cleanup cascades via the FK to trading.trades with ON DELETE CASCADE).");
        allMethods.Should().Contain(m => m.Name == "AddAsync",
            "AddAsync is the sole mutation surface — the decorator wraps it with audit logging.");
        allMethods.Should().Contain(m => m.Name == "ListByUserIdAsync",
            "ListByUserIdAsync is the read surface — forwarded without audit.");
    }
}