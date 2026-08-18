using FluentAssertions;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Trading.Domain.Journal;
using JadeCapital.Trading.Infrastructure.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Identity.UnitTests.Persistence;

/// <summary>
/// Integration tests for the <see cref="JournalEntryAuditDecorator"/> wired via
/// Scrutor (Wave 7, slice 7b.2).
///
/// <para>
/// Mirrors the <see cref="RiskProfileAuditDecorator"/> shape (bespoke —
/// does NOT use the generic <c>DecoratedRepository&lt;T&gt;</c> helper
/// because <see cref="IJournalEntryRepository"/> is bespoke with
/// user-scoped read methods) with the slice-specific deviation: the
/// decorator wraps the new <c>DeleteAsync(JournalEntry, ct)</c> overload
/// (slice 7b.2 Phase 1 additive surface surgery) instead of a
/// Guid-only delete.
/// </para>
///
/// <para>
/// <b>Why bespoke (does NOT use <c>DecoratedRepository&lt;JournalEntry&gt;</c>)</b>:
/// <list type="bullet">
///   <item><see cref="IJournalEntryRepository"/> is bespoke (does NOT extend
///         <c>IRepository&lt;JournalEntry&gt;</c>). Extending the base would
///         require a <c>GetByIdAsync(Guid, ct)</c> method that ignores the
///         cross-user scope — a security regression (the canonical lookup is
///         the user-scoped <c>FindByIdAsync(entryId, userId, ct)</c>).</item>
///   <item>The orchestrator's prompt phase 2.2 mentions using the generic
///         helper, but the design.md (line 363-365) is explicit that the
///         decorator is bespoke, matching the 7a.1 RiskProfileAuditDecorator +
///         7b.1 TradeAuditDecorator precedent for bespoke repositories.</item>
///   <item>The decorator wraps <c>DeleteAsync(JournalEntry, ct)</c> (the
///         new Phase 1 overload) directly + emits <c>AuditAction.Deleted</c>
///         with a JSON snapshot of the pre-delete entry content. The new
///         overload internally calls <c>DeleteAsync(Guid, ct)</c>.</item>
/// </list>
/// </para>
///
/// Five RED scenarios pinned here (per tasks.md §7b.2 Phase 2 + Phase 3
/// combined — the orchestrator's prompt phase 2.1 is folded into the
/// integration tests per the 7b.1 precedent):
/// <list type="number">
///   <item>Create entry → <c>audit.events</c> row with
///         <see cref="AuditAction.Created"/>, <c>EntityType = "JournalEntry"</c>,
///         <c>UserId</c> + <c>TenantId</c> from <see cref="ITenantContext"/>.</item>
///   <item>Update entry (premarket_plan change) → <c>audit.events</c> row with
///         <see cref="AuditAction.Updated"/> + a before/after diff that
///         identifies the premarket_plan field change.</item>
///   <item><c>DeleteAsync(JournalEntry, ct)</c> (the new Phase 1 overload
///         wrapped by the decorator) → <c>audit.events</c> row with
///         <see cref="AuditAction.Deleted"/> + before/after diff capturing
///         the entry's content fields (premarket_plan, postmarket_reflection,
///         mood, tags).</item>
///   <item>Cross-tenant delete attempt → <see cref="AuditAction.Denied"/>
///         audit row + <see cref="UnauthorizedAccessException"/> thrown.
///         The inner <c>DeleteAsync</c> is NEVER reached.</item>
///   <item><c>FindByIdAsync</c> (the canonical read) → no audit event.
///         Reads are not audited (matches the Wave 6 + 7a.1 + 7b.1
///         precedent: only mutations get audit rows).</item>
/// </list>
///
/// <para>
/// <b>Why a focused <c>TestJournalDbContext</c></b>: mirrors the
/// 7b.1 TradeRepositoryIntegrationTests + StrategyRepositoryIntegrationTests
/// pattern. The production <c>TradingDbContext</c> pulls in Npgsql-specific
/// converters (JournalEntry.Tags <c>TEXT[]</c> array column) that fail to
/// compose on SQLite. A focused helper DbContext keeps the model
/// SQLite-compatible without touching the production schema. The
/// <c>Tags</c> + <c>DomainEvents</c> properties are <c>Ignore()</c>'d
/// in the test fixture.
/// </para>
/// </summary>
public class JournalEntryRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public JournalEntryRepositoryIntegrationTests()
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
    /// SQLite-compatible test DbContext — maps only <see cref="JournalEntry"/>
    /// + <see cref="AuditEvent"/>. Sidesteps the Npgsql-specific converters
    /// the production TradingDbContext pulls in (the JournalEntry.Tags
    /// <c>TEXT[]</c> array column in particular uses Npgsql native arrays
    /// that fail on SQLite).
    /// </summary>
    private sealed class TestJournalDbContext : DbContext
    {
        public TestJournalDbContext(DbContextOptions<TestJournalDbContext> options) : base(options) { }

        public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("trading");
            modelBuilder.Entity<JournalEntry>(b =>
            {
                b.ToTable("journal_entries");
                b.HasKey(j => j.Id);
                b.Property(j => j.Id).HasColumnName("id");
                b.Property(j => j.UserId).HasColumnName("user_id").IsRequired();
                b.Property(j => j.LocalDate)
                    .HasColumnName("local_date")
                    .HasConversion(
                        v => v.ToDateOnly(),
                        v => LocalDate.From(v))
                    .IsRequired();
                b.Property(j => j.Timezone)
                    .HasColumnName("timezone")
                    .HasMaxLength(64)
                    .IsRequired();
                b.Property(j => j.MoodPre)
                    .HasColumnName("mood_pre")
                    .HasConversion<byte?>(
                        v => v.HasValue ? v.Value.Value : null,
                        v => v.HasValue ? Mood.FromTrusted(v.Value) : null)
                    .IsRequired(false);
                b.Property(j => j.MoodDuring)
                    .HasColumnName("mood_during")
                    .HasConversion<byte?>(
                        v => v.HasValue ? v.Value.Value : null,
                        v => v.HasValue ? Mood.FromTrusted(v.Value) : null)
                    .IsRequired(false);
                b.Property(j => j.MoodPost)
                    .HasColumnName("mood_post")
                    .HasConversion<byte?>(
                        v => v.HasValue ? v.Value.Value : null,
                        v => v.HasValue ? Mood.FromTrusted(v.Value) : null)
                    .IsRequired(false);
                b.Property(j => j.PremarketPlan)
                    .HasColumnName("premarket_plan")
                    .IsRequired(false);
                b.Property(j => j.PostmarketReflection)
                    .HasColumnName("postmarket_reflection")
                    .IsRequired(false);
                // Npgsql-specific: TEXT[] array column. Ignore for SQLite
                // (matches the Wave 6 6d.2 precedent for ImportJob.Tags).
                b.Ignore(j => j.Tags);
                b.Property(j => j.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(j => j.UpdatedAt).HasColumnName("updated_at").IsRequired(false);
                b.Ignore(j => j.DomainEvents);
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
    /// Test-only <see cref="IJournalEntryRepository"/> impl — mirrors the
    /// production <c>JournalEntryRepository</c> methods needed by the
    /// decorator + integration tests (AddAsync, UpdateAsync, DeleteAsync
    /// by Guid + DeleteAsync by JournalEntry, FindByIdAsync). The other
    /// reads (GetByUserAndDateAsync, ListByRangeAsync) are NOT exercised
    /// by the audit-decorator integration tests.
    /// </summary>
    private sealed class TestJournalEntryRepository : IJournalEntryRepository
    {
        private readonly TestJournalDbContext _db;
        public TestJournalEntryRepository(TestJournalDbContext db) { _db = db; }

        // Read methods — not exercised by these tests, but required by
        // IJournalEntryRepository. Stub implementations that throw if called.
        public Task<JournalEntry?> GetByUserAndDateAsync(
            Guid userId, LocalDate localDate, CancellationToken ct)
            => throw new NotSupportedException(
                "GetByUserAndDateAsync not exercised by JournalEntryAuditDecorator integration tests.");
        public Task<IReadOnlyList<JournalEntry>> ListByRangeAsync(
            Guid userId, LocalDate from, LocalDate to, CancellationToken ct)
            => throw new NotSupportedException(
                "ListByRangeAsync not exercised by JournalEntryAuditDecorator integration tests.");

        public Task<JournalEntry?> FindByIdAsync(
            Guid entryId, Guid userId, CancellationToken ct)
            => _db.JournalEntries.FirstOrDefaultAsync(j => j.Id == entryId && j.UserId == userId, ct);

        public async Task<JournalEntry> AddAsync(JournalEntry entry, CancellationToken ct)
        {
            await _db.JournalEntries.AddAsync(entry, ct);
            return entry;
        }

        public Task<JournalEntry> UpdateAsync(JournalEntry entry, CancellationToken ct)
        {
            var entryState = _db.Entry(entry);
            if (entryState.State == EntityState.Detached)
            {
                _db.JournalEntries.Update(entry);
            }
            return Task.FromResult(entry);
        }

        public async Task DeleteAsync(Guid entryId, CancellationToken ct)
        {
            var entry = await _db.JournalEntries.FirstOrDefaultAsync(j => j.Id == entryId, ct);
            if (entry is not null)
            {
                _db.JournalEntries.Remove(entry);
            }
        }

        public Task DeleteAsync(JournalEntry entry, CancellationToken ct)
            => DeleteAsync(entry.Id, ct);
    }

    private (IServiceProvider sp, ITenantContext tenant, IClock clock, Guid currentUserId)
        BuildServices(Guid? currentUserId = null)
    {
        // EnsureCreated on a per-DbContext basis: EF's EnsureCreated
        // returns early if ANY table exists, so calling EnsureCreated on
        // AuditDbContext after TestJournalDbContext already ran would
        // skip the audit.events table. We do the EnsureCreated for each
        // context explicitly via transient options builders.
        var journalOpts = new DbContextOptionsBuilder<TestJournalDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new TestJournalDbContext(journalOpts))
            db.Database.EnsureCreated();

        var auditOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new AuditDbContext(auditOpts))
            db.Database.EnsureCreated();

        // EF Core 9 SQLite EnsureCreated is all-or-nothing — once any table
        // exists, every subsequent EnsureCreated is a no-op. Force-create
        // the audit.events table via raw SQL matching AuditEventConfiguration
        // (same Wave 6 6d.2 fixture fix as TradeRepositoryIntegrationTests).
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

        services.AddDbContext<TestJournalDbContext>(opts => opts.UseSqlite(_connection));
        var auditContextOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        services.AddScoped<AuditDbContext>(_ => new AuditDbContext(auditContextOpts));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestJournalDbContext>());

        var tenantId = Guid.NewGuid();
        var userId = currentUserId ?? Guid.NewGuid();
        var tenant = new StaticTenantContext(new TenantId(tenantId), userId);
        services.AddSingleton<ITenantContext>(tenant);

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IJournalEntryRepository, TestJournalEntryRepository>();
        // The slice 7b.2 decorator registration — under test here.
        services.Decorate<IJournalEntryRepository, JournalEntryAuditDecorator>();

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

    private static JournalEntry CreateJournalEntry(
        Guid userId, IClock clock, string? premarketPlan = "Initial premarket plan")
        => JournalEntry.CreateOrUpdate(
            userId: userId,
            localDate: new LocalDate(2026, 6, 15),
            timezone: "America/New_York",
            moodPre: Mood.Create(3).Value,
            moodDuring: null,
            moodPost: null,
            premarketPlan: premarketPlan,
            postmarketReflection: null,
            tags: null,
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
    public async Task CreateJournalEntry_WritesAuditEvent_WithActionCreated()
    {
        // Phase 2 + 3 #1: AddAsync → AuditAction.Created, no diff. The
        // audit row's EntityType is "JournalEntry" + UserId/TenantId from
        // ITenantContext.
        var userId = Guid.NewGuid();
        var (sp, tenant, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IJournalEntryRepository>();
        var journalDb = scope.ServiceProvider.GetRequiredService<TestJournalDbContext>();
        var auditDb = NewAuditDbContext();

        var entry = CreateJournalEntry(userId, clock);
        await repo.AddAsync(entry, CancellationToken.None);
        await journalDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(JournalEntry));
        saved.EntityId.Should().Be(entry.Id);
        saved.Action.Should().Be(AuditAction.Created);
        saved.ChangesJson.Should().BeNull("Created events carry no diff.");
        saved.TenantId.Should().Be(tenant.Current!.Value);
        saved.UserId.Should().Be(tenant.CurrentUserId);
    }

    [Fact]
    public async Task UpdateJournalEntry_WritesAuditEvent_WithActionUpdatedAndDiff()
    {
        // Phase 2 + 3 #2: UpdateAsync → AuditAction.Updated + diff
        // (premarket_plan change). The diff identifies the changed field
        // and shows the before/after values.
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IJournalEntryRepository>();
        var journalDb = scope.ServiceProvider.GetRequiredService<TestJournalDbContext>();
        var auditDb = NewAuditDbContext();

        var entry = CreateJournalEntry(userId, clock, premarketPlan: "Original plan");
        await repo.AddAsync(entry, CancellationToken.None);
        await journalDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents); // discard Created row

        entry.Update(
            moodPre: Mood.Create(4).Value,
            moodDuring: null,
            moodPost: null,
            premarketPlan: "Updated premarket plan",
            postmarketReflection: null,
            tags: null,
            clock: clock);
        await repo.UpdateAsync(entry, CancellationToken.None);
        await journalDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Updated);
        saved.ChangesJson.Should().NotBeNullOrEmpty();
        saved.ChangesJson.Should().Contain("premarketPlan",
            "the diff payload identifies the premarketPlan field change.");
    }

    [Fact]
    public async Task DeleteJournalEntry_WritesAuditEvent_WithActionDeletedAndDiff()
    {
        // Phase 2 + 3 #3: DeleteAsync(JournalEntry, ct) (the slice 7b.2
        // Phase 1 additive overload) → AuditAction.Deleted + before/after
        // diff capturing the entry's content fields. The decorator wraps
        // the new overload directly + emits the audit row.
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IJournalEntryRepository>();
        var journalDb = scope.ServiceProvider.GetRequiredService<TestJournalDbContext>();
        var auditDb = NewAuditDbContext();

        var entry = CreateJournalEntry(userId, clock);
        await repo.AddAsync(entry, CancellationToken.None);
        await journalDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents); // discard Created row

        await repo.DeleteAsync(entry, CancellationToken.None);
        await journalDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Deleted);
        saved.EntityId.Should().Be(entry.Id);
        saved.ChangesJson.Should().NotBeNullOrEmpty(
            "DeleteAsync emits a before/after diff so compliance can see " +
            "what the entry contained before the hard delete.");
    }

    [Fact]
    public async Task CrossTenantDelete_LogsAuditEvent_WithActionDenied_AndThrowsUnauthorized()
    {
        // Phase 2 + 3 #4: a different user attempts to delete another user's
        // entry. The decorator detects the ownership mismatch, logs an
        // audit event for the ATTEMPT (security trail), and throws
        // UnauthorizedAccessException. The inner DeleteAsync is NEVER
        // reached, so the journal entry stays in the DB.
        var ownerUserId = Guid.NewGuid();
        var attackerUserId = Guid.NewGuid(); // != ownerUserId
        var (sp, _, clock, _) = BuildServices(currentUserId: attackerUserId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IJournalEntryRepository>();
        var journalDb = scope.ServiceProvider.GetRequiredService<TestJournalDbContext>();
        var auditDb = NewAuditDbContext();

        // Setup: create the entry as the OWNER (bypass the decorator for
        // the setup so the ownership check doesn't fire here).
        var entry = CreateJournalEntry(ownerUserId, clock);
        journalDb.JournalEntries.Add(entry);
        await journalDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        Func<Task> act = async () => await repo.DeleteAsync(entry, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "the decorator rejects cross-tenant delete attempts.");

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Denied);
        saved.EntityId.Should().Be(entry.Id);
        saved.UserId.Should().Be(attackerUserId,
            "the audit trail records WHO attempted the cross-tenant access, " +
            "not the owner.");
    }

    [Fact]
    public async Task FindByIdAsync_WritesNoAuditEvent()
    {
        // Phase 2 + 3 #5: the canonical read (FindByIdAsync) emits NO
        // audit event. Reads are not audited (matches the Wave 6 + 7a.1
        // + 7b.1 precedent: only mutations get audit rows).
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IJournalEntryRepository>();
        var journalDb = scope.ServiceProvider.GetRequiredService<TestJournalDbContext>();
        var auditDb = NewAuditDbContext();

        var entry = CreateJournalEntry(userId, clock);
        journalDb.JournalEntries.Add(entry);
        await journalDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        var found = await repo.FindByIdAsync(entry.Id, userId, CancellationToken.None);
        found.Should().NotBeNull();
        await auditDb.SaveChangesAsync();

        var auditRows = await auditDb.AuditEvents.ToListAsync();
        auditRows.Should().BeEmpty(
            "FindByIdAsync is a read — no audit event should be written " +
            "(matches the Wave 6 + 7a.1 + 7b.1 precedent).");
    }
}
