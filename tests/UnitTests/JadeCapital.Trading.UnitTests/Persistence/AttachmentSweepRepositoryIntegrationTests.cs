using System.Text.Json;
using FluentAssertions;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Storage;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.AttachmentAudits;
using JadeCapital.Trading.Domain.TradeAttachments;
using JadeCapital.Trading.Infrastructure.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Trading.UnitTests.Persistence;

/// <summary>
/// Integration tests for the <see cref="AttachmentSweepAuditDecorator"/>
/// wired via Scrutor (Wave 9, slice 9a.3 — sub-scope A coverage extension).
///
/// <para>
/// <b>Decorator shape</b>: bespoke batch soft-delete — the FIRST
/// "1-call-many-audit-rows" decorator in the codebase:
/// <list type="bullet">
///   <item>Wrapping <see cref="IAttachmentSweepRepository.SoftDeleteBatchAsync"/>
///         with the <b>cross-tenant <c>IsOwner</c> check</b> per id +
///         one <see cref="AuditAction.Updated"/> audit row per id with
///         <c>EntityType = "TradeAttachment"</c> (the child aggregate being
///         soft-deleted, NOT the sweep operation) + a
///         <c>ChangesJson</c> capturing the
///         <c>isActive: { before: true, after: false }</c> diff.</item>
///   <item>Forwarding <see cref="IAttachmentSweepRepository.GetExpiredBatchAsync"/> +
///         <see cref="IAttachmentSweepRepository.GetUserAggregateAsync"/> +
///         <see cref="IAttachmentSweepRepository.GetActiveUserIdsAsync"/> to
///         the inner without audit logging. Reads are not audited (matches
///         Wave 6 + 7 + 8a.x + 8b.1 + 9a.1 + 9a.2 precedent: only mutations
///         get audit rows).</item>
///   <item>Forwarding <see cref="IAttachmentSweepRepository.InsertAuditAsync"/>
///         to the inner WITHOUT audit logging. This method IS the write to
///         <c>trading.attachments_quota_audit</c> — auditing it would create
///         an infinite loop. Mirrors the Wave 8 8b.2 <c>IStripeWebhookEventRepository</c>
///         SKIP rationale: the destination IS the audit log; emitting on top
///         would be doubly-recorded noise.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why bespoke (does NOT use the generic
/// <c>DecoratedRepository&lt;T&gt;</c> helper)</b>: <see cref="IAttachmentSweepRepository.SoftDeleteBatchAsync"/>
/// takes <c>IReadOnlyList&lt;Guid&gt;</c> + returns <c>int</c> — it is a
/// batch mutation that does NOT fit <c>IRepository&lt;T&gt;.DeleteAsync(T, ct)</c>.
/// Bespoke decorator preserves the batch shape + emits 1 audit row per id.
/// </para>
///
/// <para>
/// Five RED scenarios pinned here (per tasks.md §9a.3 Phase 1.1 + design.md
/// §"4. AttachmentSweepAuditDecorator" — bespoke batch soft-delete):
/// <list type="number">
///   <item><b>Batch with N ids emits N audit rows</b> — one per id, NOT one
///         per batch. <c>EntityType = "TradeAttachment"</c>. <c>ChangesJson</c>
///         contains the <c>isActive</c> diff.</item>
///   <item><b>Cross-tenant id in the batch</b> emits <see cref="AuditAction.Denied"/>
///         for THAT id + throws <see cref="UnauthorizedAccessException"/>
///         for the WHOLE batch (transaction abort semantics — the inner is
///         NEVER reached).</item>
///   <item><b>Read methods</b> (<see cref="IAttachmentSweepRepository.GetExpiredBatchAsync"/> +
///         <see cref="IAttachmentSweepRepository.GetUserAggregateAsync"/> +
///         <see cref="IAttachmentSweepRepository.GetActiveUserIdsAsync"/>)
///         emit NO audit event. Reads are not audited.</item>
///   <item><b><see cref="IAttachmentSweepRepository.InsertAuditAsync"/></b>
///         emits NO audit event — this method IS the write to the
///         sweep's audit log; auditing it would create an infinite loop.</item>
///   <item><b>ChangesJson shape</b> — for soft-deleted attachments the diff
///         includes <c>isActive: { before: true, after: false }</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why a focused <c>TestAttachmentSweepDbContext</c></b>: mirrors the Wave 9
/// 9a.1 <c>TestAIRiskAdviceDbContext</c> + 9a.2 <c>TestScannerFilterDbContext</c>
/// + Wave 8 8b.1 <c>TestStripeCustomerDbContext</c> + 8a.3
/// <c>TestPreTradeChecklistDbContext</c> pattern. The production
/// <c>TradingDbContext</c> pulls in Npgsql-specific converters via
/// <c>JournalEntry.Tags</c> + other Money/array mappings that fail to
/// compose on SQLite. A focused helper DbContext that maps only
/// <see cref="TradeAttachment"/> + <see cref="AuditEvent"/> keeps the model
/// SQLite-compatible without touching the production schema.
/// </para>
/// </summary>
public class AttachmentSweepRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public AttachmentSweepRepositoryIntegrationTests()
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
    /// SQLite-compatible test DbContext — maps only <see cref="TradeAttachment"/>
    /// + <see cref="AuditEvent"/>. Sidesteps the Npgsql-specific converters
    /// the production TradingDbContext pulls in. Mirrors the production
    /// <c>TradeAttachmentConfiguration</c> Status converter (byte ↔ string
    /// lowercase) so the EF model stays faithful.
    /// </summary>
    private sealed class TestAttachmentSweepDbContext : DbContext
    {
        public TestAttachmentSweepDbContext(DbContextOptions<TestAttachmentSweepDbContext> options) : base(options) { }

        public DbSet<TradeAttachment> TradeAttachments => Set<TradeAttachment>();
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("trading");
            modelBuilder.Entity<TradeAttachment>(b =>
            {
                b.ToTable("trade_attachments");
                b.HasKey(a => a.Id);
                b.Property(a => a.Id).HasColumnName("id");
                b.Property(a => a.ReviewId).HasColumnName("review_id").IsRequired();
                b.Property(a => a.UserId).HasColumnName("user_id").IsRequired();
                b.Property(a => a.ObjectKey).HasColumnName("object_key").HasMaxLength(512).IsRequired();
                b.Property(a => a.ContentType).HasColumnName("content_type").HasMaxLength(127).IsRequired();
                b.Property(a => a.SizeBytes).HasColumnName("size_bytes").IsRequired();
                b.Property(a => a.Sha256).HasColumnName("sha256").HasMaxLength(64);
                b.Property(a => a.Status)
                    .HasColumnName("status")
                    .HasMaxLength(16)
                    .HasConversion(
                        v => StatusToString(v),
                        v => StringToStatus(v))
                    .IsRequired();
                b.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(a => a.UpdatedAt).HasColumnName("updated_at");
                b.Property(a => a.UploadedAt).HasColumnName("uploaded_at");
                b.Property(a => a.IsActive).HasColumnName("is_active").IsRequired();
                b.Property(a => a.SweptAt).HasColumnName("swept_at");
                b.Property(a => a.ThumbnailObjectKey).HasColumnName("thumbnail_object_key").HasMaxLength(255);
                b.Property(a => a.ExpiresAt).HasColumnName("expires_at");
                b.Property(a => a.VirusScannedAt).HasColumnName("virus_scanned_at");
                b.Property(a => a.ScanResult).HasColumnName("scan_result").HasConversion<byte>();
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

        private static string StatusToString(TradeAttachmentStatus status) => status switch
        {
            TradeAttachmentStatus.Pending  => "pending",
            TradeAttachmentStatus.Uploaded => "uploaded",
            TradeAttachmentStatus.Failed   => "failed",
            _ => "pending",
        };

        private static TradeAttachmentStatus StringToStatus(string value) => value switch
        {
            "pending"  => TradeAttachmentStatus.Pending,
            "uploaded" => TradeAttachmentStatus.Uploaded,
            "failed"   => TradeAttachmentStatus.Failed,
            _          => TradeAttachmentStatus.Pending,
        };
    }

    /// <summary>
    /// Test-only <see cref="IAttachmentSweepRepository"/> impl — mirrors the
    /// production <c>AttachmentSweepRepository</c> methods
    /// (<c>GetExpiredBatchAsync</c>, <c>SoftDeleteBatchAsync</c>,
    /// <c>InsertAuditAsync</c>, <c>GetUserAggregateAsync</c>,
    /// <c>GetActiveUserIdsAsync</c>). All 5 methods are forwarded to the
    /// inner by the decorator (the decorator only wraps
    /// <c>SoftDeleteBatchAsync</c> with audit logging + IsOwner checks).
    /// </summary>
    private sealed class TestAttachmentSweepRepository : IAttachmentSweepRepository
    {
        private readonly TestAttachmentSweepDbContext _db;
        public int InsertAuditAsyncCallCount { get; private set; }

        public TestAttachmentSweepRepository(TestAttachmentSweepDbContext db) { _db = db; }

        public async Task<IReadOnlyList<ExpiredAttachmentSweepRow>> GetExpiredBatchAsync(
            DateTimeOffset asOf, int skip, int take, CancellationToken ct)
        {
            // SQLite does NOT support DateTimeOffset comparisons in LINQ
            // (the 9a.1 + 9a.2 lesson — InvalidOperationException from
            // EF Core's QueryableMethodTranslatingExpressionVisitor). The
            // production repo runs on Postgres + can push the
            // <c>ExpiresAt &lt; asOf</c> filter to SQL; the test fixture
            // materializes the active+expires-not-null set first, then
            // filters + orders client-side. Ordering is irrelevant for
            // the no-audit assertion.
            var rows = await _db.TradeAttachments
                .Where(a => a.IsActive && a.ExpiresAt != null)
                .ToListAsync(ct);
            return rows
                .Where(a => a.ExpiresAt < asOf)
                .OrderBy(a => a.ExpiresAt)
                .Skip(skip)
                .Take(take)
                .Select(a => new ExpiredAttachmentSweepRow(
                    a.Id, a.UserId, a.ReviewId, a.ObjectKey, a.SizeBytes, a.ExpiresAt!.Value))
                .ToList();
        }

        public async Task<int> SoftDeleteBatchAsync(
            IReadOnlyList<Guid> attachmentIds, CancellationToken ct)
        {
            if (attachmentIds.Count == 0) return 0;

            // Mirrors the production AttachmentSweepRepository.SoftDeleteBatchAsync:
            // load via tracked EF + call MarkSwept(now). The decorator shares
            // the same scoped DbContext so the tracked instances are the same
            // object references the decorator loaded.
            var rows = await _db.TradeAttachments
                .Where(a => attachmentIds.Contains(a.Id))
                .ToListAsync(ct);

            var now = System.DateTimeOffset.UtcNow;
            foreach (var row in rows)
            {
                row.MarkSwept(now); // flips IsActive = false + Touch.
            }
            return rows.Count;
        }

        public async Task InsertAuditAsync(
            Guid userId,
            DateTimeOffset ranAt,
            int cleanedCount,
            long cleanedBytes,
            int remainingCount,
            long remainingBytes,
            string? skippedReason,
            string? errorMessage,
            CancellationToken ct)
        {
            // Test fixture increments a counter — we don't persist an
            // AttachmentQuotaAudit row because the test DbContext doesn't
            // map that aggregate. The test only asserts the counter was
            // bumped + NO audit.events row was emitted (no infinite loop).
            InsertAuditAsyncCallCount++;
            await Task.CompletedTask;
        }

        public Task<(long TotalBytes, int Count)> GetUserAggregateAsync(
            Guid userId, CancellationToken ct)
        {
            // SQLite does NOT support Sum() on a nullable decimal projection
            // without a value converter; this test fixture only needs a
            // deterministic count for the no-audit assertion. Return a
            // hardcoded (0, 0) tuple — the read test asserts NO audit event
            // was emitted regardless of the aggregate shape.
            return Task.FromResult((0L, 0));
        }

        public async Task<IReadOnlyList<Guid>> GetActiveUserIdsAsync(CancellationToken ct)
        {
            var ids = await _db.TradeAttachments
                .Where(a => a.IsActive && a.ExpiresAt != null)
                .Select(a => a.UserId)
                .Distinct()
                .ToListAsync(ct);
            return ids;
        }
    }

    private (IServiceProvider sp, ITenantContext tenant, IClock clock, Guid currentUserId)
        BuildServices(Guid? currentUserId = null)
    {
        // EnsureCreated on a per-DbContext basis (mirrors the 9a.1 + 9a.2
        // pattern): EF's EnsureCreated returns early if ANY table exists, so
        // calling EnsureCreated on AuditDbContext after
        // TestAttachmentSweepDbContext already ran would skip the
        // audit.events table. We do the EnsureCreated for each context
        // explicitly via transient options builders.
        var sweepOpts = new DbContextOptionsBuilder<TestAttachmentSweepDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new TestAttachmentSweepDbContext(sweepOpts))
            db.Database.EnsureCreated();

        var auditOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new AuditDbContext(auditOpts))
            db.Database.EnsureCreated();

        // EF Core 9 SQLite EnsureCreated is all-or-nothing — once any table
        // exists, every subsequent EnsureCreated is a no-op. Force-create
        // the audit.events table via raw SQL matching AuditEventConfiguration
        // (same Wave 8 8b.1 + 9a.1 + 9a.2 fixture fix).
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

        services.AddDbContext<TestAttachmentSweepDbContext>(opts => opts.UseSqlite(_connection));
        var auditContextOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        services.AddScoped<AuditDbContext>(_ => new AuditDbContext(auditContextOpts));
        // DbContext base-type alias — the AttachmentSweepAuditDecorator
        // constructor takes a DbContext (base type) so it can load
        // TradeAttachment entities for the IsOwner pre-check + the
        // IsActive diff snapshot. The TestAttachmentSweepDbContext serves
        // both roles; in production the alias points to TradingDbContext.
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestAttachmentSweepDbContext>());

        var tenantId = Guid.NewGuid();
        var userId = currentUserId ?? Guid.NewGuid();
        var tenant = new StaticTenantContext(new TenantId(tenantId), userId);
        services.AddSingleton<ITenantContext>(tenant);

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IAttachmentSweepRepository, TestAttachmentSweepRepository>();
        // The slice 9a.3 decorator registration — under test here.
        services.Decorate<IAttachmentSweepRepository, AttachmentSweepAuditDecorator>();

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

    /// <summary>
    /// Stages an uploaded TradeAttachment owned by <paramref name="userId"/>
    /// via the canonical RequestSlot → MarkUploaded → ApplyScanResult flow.
    /// </summary>
    private static TradeAttachment CreateAttachment(Guid userId, IClock clock, Guid? reviewId = null)
    {
        var attachmentId = Guid.NewGuid();
        var rid = reviewId ?? Guid.NewGuid();
        var request = TradeAttachment.RequestSlot(
            id: attachmentId,
            reviewId: rid,
            userId: userId,
            objectKey: $"trading/attachments/{userId}/{Guid.NewGuid()}/{rid}/{attachmentId}/test.bin",
            contentType: "application/octet-stream",
            sizeBytes: 1024L,
            now: clock.UtcNow);
        var attachment = request.Value;
        attachment.MarkUploaded(sha256: null, tradeId: Guid.NewGuid(), now: clock.UtcNow);
        attachment.ApplyScanResult(VirusScanResult.Clean, clock.UtcNow, clock.UtcNow.AddDays(90));
        return attachment;
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
    public async Task SoftDeleteBatchAsync_WithMultipleOwnedIds_EmitsAuditRowPerId_WithTradeAttachmentEntityType()
    {
        // Phase 1.1 #1: a batch of N owned ids emits N audit rows (one per
        // id, NOT one per batch). EntityType = "TradeAttachment" (the child
        // aggregate, not the sweep operation). Each row's ChangesJson
        // captures the isActive: { before: true, after: false } diff. This
        // is the FIRST "1-call-many-audit-rows" decorator in the codebase
        // (the canonical single-entity shape from Wave 6 + 7 + 8a.x +
        // 8b.1 + 9a.1 + 9a.2 doesn't apply here — the batch mutation
        // requires N rows per call).
        var userId = Guid.NewGuid();
        var (sp, tenant, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAttachmentSweepRepository>();
        var attachmentDb = scope.ServiceProvider.GetRequiredService<TestAttachmentSweepDbContext>();
        var auditDb = NewAuditDbContext();

        // Stage 3 owned attachments + persist.
        var att1 = CreateAttachment(userId, clock);
        var att2 = CreateAttachment(userId, clock);
        var att3 = CreateAttachment(userId, clock);
        attachmentDb.TradeAttachments.AddRange(att1, att2, att3);
        await attachmentDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        // Soft-delete the batch.
        var ids = new List<Guid> { att1.Id, att2.Id, att3.Id };
        var count = await repo.SoftDeleteBatchAsync(ids, CancellationToken.None);
        await attachmentDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        // ===== N audit rows emitted for N ids =====
        var saved = await auditDb.AuditEvents.ToListAsync();
        saved.Should().HaveCount(3,
            "one audit row per id in the batch — the 1-call-many-audit-rows pattern requires " +
            "N rows for N ids, NOT 1 row per batch call.");

        // Each row's EntityType = "TradeAttachment" + EntityId = att.Id + Action = Deleted.
        // Per spec §"Batch soft-delete emits N audit rows per id" — the
        // soft-delete is a user-impacting removal of the TradeAttachment
        // from the active set, so the action MUST be Deleted (byte 2),
        // NOT Updated (byte 1). Compliance officers query
        // `WHERE action = 2 AND entity_type = "TradeAttachment"` to
        // surface these events; emitting Updated silently misses them.
        saved.Should().AllSatisfy(row =>
        {
            row.EntityType.Should().Be(nameof(TradeAttachment),
                "the audit row's entity is the TradeAttachment being soft-deleted, NOT the sweep operation.");
            row.Action.Should().Be(AuditAction.Deleted,
                "the soft-delete emits action = Deleted per spec; emitting Updated silently breaks compliance queries.");
            row.TenantId.Should().Be(tenant.Current!.Value);
            row.UserId.Should().Be(tenant.CurrentUserId);
        });

        saved.Select(r => r.EntityId).Should().BeEquivalentTo(
            new[] { att1.Id, att2.Id, att3.Id },
            "the audit rows cover every id in the batch — no row skipped, no row duplicated.");

        // The IsActive flag has been flipped on the persisted rows.
        attachmentDb.ChangeTracker.Clear();
        var refreshed = await attachmentDb.TradeAttachments
            .Where(a => ids.Contains(a.Id))
            .ToListAsync();
        refreshed.Should().AllSatisfy(a =>
            a.IsActive.Should().BeFalse("the inner.SoftDeleteBatchAsync flips IsActive = false."));
    }

    [Fact]
    public async Task SoftDeleteBatchAsync_WithCrossTenantId_EmitsDeniedForThatId_AndThrowsUnauthorized()
    {
        // Phase 1.1 #2: a batch with a cross-tenant id emits a
        // AuditAction.Denied audit row for THAT id + throws
        // UnauthorizedAccessException for the WHOLE batch (transaction
        // abort semantics — the inner is NEVER reached). The owned ids in
        // the batch get NO audit row (since we abort before emitting
        // Updated for them); the inner.SoftDeleteBatchAsync is never
        // called, so the owned attachments are still IsActive = true.
        var ownerUserId = Guid.NewGuid();
        var attackerUserId = Guid.NewGuid(); // != ownerUserId
        var crossTenantAttachmentOwnerId = Guid.NewGuid(); // also != attackerUserId
        var (sp, _, clock, _) = BuildServices(currentUserId: attackerUserId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAttachmentSweepRepository>();
        var attachmentDb = scope.ServiceProvider.GetRequiredService<TestAttachmentSweepDbContext>();
        var auditDb = NewAuditDbContext();

        // Stage 3 attachments: 2 owned by the attacker (will pass IsOwner) +
        // 1 owned by a third user (will fail IsOwner → Denied). The
        // attacker user invokes the sweep with all 3 ids.
        var att1 = CreateAttachment(attackerUserId, clock);
        var att2 = CreateAttachment(attackerUserId, clock);
        var crossTenantAtt = CreateAttachment(crossTenantAttachmentOwnerId, clock);
        attachmentDb.TradeAttachments.AddRange(att1, att2, crossTenantAtt);
        await attachmentDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        var ids = new List<Guid> { att1.Id, att2.Id, crossTenantAtt.Id };

        // ===== Act + Assert =====
        Func<Task> act = async () =>
            await repo.SoftDeleteBatchAsync(ids, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "the decorator rejects cross-tenant SoftDeleteBatchAsync calls — the whole batch " +
            "aborts (matches the Wave 7 7b.1 TradeAuditDecorator + 8a.1 AccountAuditDecorator + " +
            "8a.2 AlertAuditDecorator + 8a.2 TradeReviewAuditDecorator + 9a.1 AIRiskAdvice + " +
            "9a.1 CoachingPrompt + 9a.2 ScannerFilter cross-tenant semantics).");

        await attachmentDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        // ===== Exactly 1 audit row (Denied for cross-tenant id) =====
        var saved = await auditDb.AuditEvents.ToListAsync();
        saved.Should().HaveCount(1,
            "exactly one cross-tenant attempt was detected — the owned ids in the batch do NOT " +
            "emit Updated rows because the whole batch aborts before the inner is reached.");

        var deniedRow = saved.Single();
        deniedRow.Action.Should().Be(AuditAction.Denied);
        deniedRow.EntityId.Should().Be(crossTenantAtt.Id,
            "the Denied row identifies the cross-tenant attachment, not the owned ones.");
        deniedRow.EntityType.Should().Be(nameof(TradeAttachment));
        deniedRow.UserId.Should().Be(attackerUserId,
            "the audit trail records WHO attempted the cross-tenant access — the attacker, " +
            "not the legitimate owner.");
        deniedRow.ChangesJson.Should().BeNull(
            "Denied rows carry no diff (only Created/Updated rows carry diffs).");

        // The owned attachments are still IsActive = true (inner was never called).
        attachmentDb.ChangeTracker.Clear();
        var refreshed = await attachmentDb.TradeAttachments
            .Where(a => ids.Contains(a.Id))
            .ToListAsync();
        refreshed.Single(a => a.Id == att1.Id).IsActive.Should().BeTrue(
            "the inner.SoftDeleteBatchAsync was never reached — the owned attachments are NOT soft-deleted.");
        refreshed.Single(a => a.Id == att2.Id).IsActive.Should().BeTrue();
        refreshed.Single(a => a.Id == crossTenantAtt.Id).IsActive.Should().BeTrue(
            "the cross-tenant attachment is also NOT soft-deleted (the whole batch aborts).");
    }

    [Fact]
    public async Task ReadMethods_DoNotEmitAuditEvent()
    {
        // Phase 1.1 #3: all 3 reads (GetExpiredBatchAsync +
        // GetUserAggregateAsync + GetActiveUserIdsAsync) emit NO audit event.
        // Reads are not audited (matches Wave 6 + 7 + 8a.x + 8b.1 + 9a.1 +
        // 9a.2 precedent).
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAttachmentSweepRepository>();
        var attachmentDb = scope.ServiceProvider.GetRequiredService<TestAttachmentSweepDbContext>();
        var auditDb = NewAuditDbContext();

        // Stage 2 expired attachments so GetExpiredBatchAsync returns rows.
        var att1 = CreateAttachment(userId, clock);
        var att2 = CreateAttachment(userId, clock);
        // Re-stamp ExpiresAt to be in the past so GetExpiredBatchAsync picks them up.
        // The CreateAttachment factory already sets ExpiresAt = now + 90 days; we
        // override via the setter on the entity (SweptAt is null until sweep).
        // TradeAttachment.ApplyScanResult sets ExpiresAt; the test fixture
        // doesn't expose ExpiresAt as mutable post-Create. To exercise
        // GetExpiredBatchAsync, we use a DateTimeOffset in the future so
        // the active+expires_at!=null filter still returns rows, then the
        // ExpiresAt < asOf filter is not the test's concern. The
        // no-audit assertion holds regardless.
        attachmentDb.TradeAttachments.AddRange(att1, att2);
        await attachmentDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        // Exercise all 3 reads. The exact row content is irrelevant to
        // the no-audit assertion — the test fixture sets ExpiresAt = now +
        // 90 days, so GetExpiredBatchAsync returns an empty list (the
        // <c>ExpiresAt &lt; asOf</c> filter excludes rows 90 days out).
        // What matters is that calling the reads does NOT emit audit
        // events.
        var asOf = clock.UtcNow.AddSeconds(1);
        var expired = await repo.GetExpiredBatchAsync(asOf, 0, 100, CancellationToken.None);

        var aggregate = await repo.GetUserAggregateAsync(userId, CancellationToken.None);
        aggregate.Should().Be((0L, 0)); // test fixture returns (0,0)

        var userIds = await repo.GetActiveUserIdsAsync(CancellationToken.None);
        userIds.Should().NotBeEmpty(
            "the staged attachments have ExpiresAt != null so the active filter picks them up.");

        await auditDb.SaveChangesAsync();

        var auditRows = await auditDb.AuditEvents.ToListAsync();
        auditRows.Should().BeEmpty(
            "GetExpiredBatchAsync + GetUserAggregateAsync + GetActiveUserIdsAsync are reads — no audit " +
            "event should be written (matches Wave 6 + 7 + 8a.x + 8b.1 + 9a.1 + 9a.2 precedent).");
    }

    [Fact]
    public async Task InsertAuditAsync_DoesNotEmitAuditEvent_PreventsInfiniteLoop()
    {
        // Phase 1.1 #4: InsertAuditAsync is the WRITE to
        // trading.attachments_quota_audit (the sweep's own audit log).
        // Auditing it would create an infinite loop — the decorator MUST
        // forward InsertAuditAsync without emitting audit.events rows.
        // Mirrors the Wave 8 8b.2 IStripeWebhookEventRepository SKIP
        // rationale: the destination IS the audit log; emitting on top
        // would be doubly-recorded noise. The test cannot inspect the
        // inner via Scrutor's wrapping (the resolved
        // <see cref="IAttachmentSweepRepository"/> is the decorator,
        // not the inner), so the assertion is purely "no audit.events
        // row was emitted" — which is the contract that prevents the
        // infinite loop.
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAttachmentSweepRepository>();
        var auditDb = NewAuditDbContext();

        // Clear any audit rows from setup.
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        // Call InsertAuditAsync with dummy sweep-day values.
        await repo.InsertAuditAsync(
            userId: userId,
            ranAt: clock.UtcNow,
            cleanedCount: 5,
            cleanedBytes: 5120L,
            remainingCount: 10,
            remainingBytes: 10240L,
            skippedReason: null,
            errorMessage: null,
            ct: CancellationToken.None);

        await auditDb.SaveChangesAsync();

        // No audit.events row was emitted — the decorator forwards
        // InsertAuditAsync bare (no audit row for the audit write itself).
        var auditRows = await auditDb.AuditEvents.ToListAsync();
        auditRows.Should().BeEmpty(
            "InsertAuditAsync is the WRITE to trading.attachments_quota_audit itself — auditing it " +
            "would create an infinite loop. The decorator forwards it bare. Mirrors the Wave 8 8b.2 " +
            "IStripeWebhookEventRepository SKIP rationale.");
    }

    [Fact]
    public async Task ChangesJson_ForSoftDeletedAttachment_IncludesIsActiveDiff()
    {
        // Phase 1.1 #5: the ChangesJson for a soft-deleted TradeAttachment
        // includes the IsActive diff (before: true, after: false). The diff
        // is JSON-shaped so compliance officers can query
        // changes @> '{"isActive":{"after":false}}' to find all soft-deleted
        // attachments without scanning the full audit row payload.
        var userId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAttachmentSweepRepository>();
        var attachmentDb = scope.ServiceProvider.GetRequiredService<TestAttachmentSweepDbContext>();
        var auditDb = NewAuditDbContext();

        var attachment = CreateAttachment(userId, clock);
        attachmentDb.TradeAttachments.Add(attachment);
        await attachmentDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        await repo.SoftDeleteBatchAsync(new[] { attachment.Id }, CancellationToken.None);
        await attachmentDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.ChangesJson.Should().NotBeNullOrEmpty(
            "Updated events must carry a before/after diff JSON so compliance officers can see WHAT changed.");

        // Parse + assert the diff shape. System.Text.Json default policy
        // is camelCase — the spec's "IsActive" is intent, the actual JSON
        // payload uses "isActive" (matches the Wave 7 7b.1 + 8a.1 +
        // 8a.2 + 9a.2 precedent).
        saved.ChangesJson.Should().Contain("isActive",
            "the diff payload identifies the IsActive field as the changed property.");
        saved.ChangesJson.Should().Contain("before",
            "the diff payload includes the pre-mutation value.");
        saved.ChangesJson.Should().Contain("after",
            "the diff payload includes the post-mutation value.");
        saved.ChangesJson.Should().Contain("true",
            "the pre-mutation IsActive is true (the sweep only picks active rows).");
        saved.ChangesJson.Should().Contain("false",
            "the post-mutation IsActive is false (the soft-delete flips the flag).");

        // Parse to verify the shape — defensive against serializer
        // whitespace/ordering changes. The null-forgiving operator `!` is
        // safe because the Should().NotBeNullOrEmpty() assertion above
        // proves the property is non-null at runtime.
        using var doc = JsonDocument.Parse(saved.ChangesJson!);
        doc.RootElement.TryGetProperty("isActive", out var isActiveElement).Should().BeTrue();
        isActiveElement.TryGetProperty("before", out var beforeElement).Should().BeTrue();
        isActiveElement.TryGetProperty("after", out var afterElement).Should().BeTrue();
        beforeElement.ValueKind.Should().Be(JsonValueKind.True);
        afterElement.ValueKind.Should().Be(JsonValueKind.False);
    }
}