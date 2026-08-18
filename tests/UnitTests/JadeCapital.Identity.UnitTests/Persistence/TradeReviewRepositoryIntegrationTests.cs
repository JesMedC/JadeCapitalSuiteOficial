using FluentAssertions;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Trading.Domain.TradeAttachments;
using JadeCapital.Trading.Domain.TradeReviews;
using JadeCapital.Trading.Infrastructure.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Identity.UnitTests.Persistence;

/// <summary>
/// Integration tests for the <see cref="TradeReviewAuditDecorator"/> wired
/// via Scrutor (Wave 8, slice 8a.2).
///
/// <para>
/// Mirrors the <see cref="JournalEntryAuditDecorator"/> shape (bespoke —
/// does NOT use the generic <c>DecoratedRepository&lt;T&gt;</c> helper
/// because <see cref="ITradeReviewRepository"/> is bespoke with
/// attachment ops + user-scoped read methods). The bespoke decorator
/// preserves the full interface surface.
/// </para>
///
/// <para>
/// <b>CRITICAL — slice 8a.2 attachment ops deviation</b>: the
/// <see cref="ITradeReviewRepository"/> exposes first-class attachment
/// methods (<c>AddAttachmentAsync</c> / <c>UpdateAttachmentAsync</c> /
/// <c>RemoveAttachmentAsync</c>) for the <see cref="TradeAttachment"/>
/// child entity. Per orchestrator preflight decision 7, the decorator
/// forwards these to the inner WITHOUT emitting audit rows. Rationale:
/// <c>TradeAttachment</c> is a child entity of the review, not a
/// separately-audited aggregate. The review's Update events + handler-side
/// MinIO cleanup log already capture attachment lifecycle. Adding
/// per-attachment audit rows would be noisy without proportional
/// compliance value.
/// </para>
///
/// Five RED scenarios pinned here (per tasks.md §8a.2 Phase 2):
/// <list type="number">
///   <item>Create review (<c>AddAsync</c>) → audit row with
///         <see cref="AuditAction.Created"/>, <c>EntityType = "TradeReview"</c>,
///         <c>TenantId</c> + <c>UserId</c> from <see cref="ITenantContext"/>,
///         <c>ChangesJson</c> = null.</item>
///   <item>Update review (<see cref="TradeReview.Update"/> with
///         <c>SetupUsed</c> + <c>Rating</c> change) → audit row with
///         <see cref="AuditAction.Updated"/> + diff that identifies
///         the changed fields.</item>
///   <item>Attachment ops (<c>AddAttachmentAsync</c> /
///         <c>UpdateAttachmentAsync</c> / <c>RemoveAttachmentAsync</c>)
///         are forwarded to the inner WITHOUT emitting audit rows. The
///         child-entity decision: the review's Update events + handler-side
///         MinIO cleanup log already capture attachment lifecycle.</item>
///   <item>Cross-tenant update attempt → <see cref="AuditAction.Denied"/>
///         audit row + <see cref="UnauthorizedAccessException"/> thrown.
///         The inner <c>UpdateAsync</c> is NEVER reached.</item>
///   <item><c>FindByIdAsync</c> (the bespoke user-scoped read) → no audit
///         event. Reads are not audited (matches the Wave 6 + 7 + 8a.1
///         + 8a.2 precedent: only mutations get audit rows).</item>
/// </list>
///
/// <para>
/// <b>Why a focused <c>TestTradeReviewDbContext</c></b>: mirrors the
/// 8a.1 AccountRepositoryIntegrationTests.TestAccountDbContext +
/// 8a.2 AlertRepositoryIntegrationTests.TestAlertDbContext pattern. A
/// focused helper DbContext keeps the model SQLite-compatible without
/// touching the production schema (Npgsql-specific converters).
/// </para>
/// </summary>
public class TradeReviewRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public TradeReviewRepositoryIntegrationTests()
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
    /// SQLite-compatible test DbContext — maps only <see cref="TradeReview"/>
    /// + <see cref="TradeAttachment"/> + <see cref="AuditEvent"/>. Sidesteps
    /// the Npgsql-specific converters the production TradingDbContext pulls in.
    /// </summary>
    private sealed class TestTradeReviewDbContext : DbContext
    {
        public TestTradeReviewDbContext(DbContextOptions<TestTradeReviewDbContext> options) : base(options) { }

        public DbSet<TradeReview> TradeReviews => Set<TradeReview>();
        public DbSet<TradeAttachment> TradeAttachments => Set<TradeAttachment>();
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("trading");
            modelBuilder.Entity<TradeReview>(b =>
            {
                b.ToTable("trade_reviews");
                b.HasKey(r => r.Id);
                b.Property(r => r.Id).HasColumnName("id");
                b.Property(r => r.TradeId).HasColumnName("trade_id").IsRequired();
                b.Property(r => r.UserId).HasColumnName("user_id").IsRequired();
                b.Property(r => r.Emotionality).HasColumnName("emotionality").HasConversion<byte>().IsRequired();
                b.Property(r => r.SetupUsed).HasColumnName("setup_used").HasMaxLength(64);
                b.Property(r => r.Lessons).HasColumnName("lessons").HasColumnType("text");
                b.Property(r => r.Rating).HasColumnName("rating");
                b.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired();
                b.Ignore(r => r.DomainEvents);
            });
            modelBuilder.Entity<TradeAttachment>(b =>
            {
                b.ToTable("trade_attachments");
                b.HasKey(a => a.Id);
                b.Property(a => a.Id).HasColumnName("id");
                b.Property(a => a.ReviewId).HasColumnName("review_id").IsRequired();
                b.Property(a => a.UserId).HasColumnName("user_id").IsRequired();
                b.Property(a => a.ObjectKey).HasColumnName("object_key").HasMaxLength(512).IsRequired();
                b.Property(a => a.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
                b.Property(a => a.SizeBytes).HasColumnName("size_bytes").IsRequired();
                b.Property(a => a.Sha256).HasColumnName("sha256").HasMaxLength(64);
                b.Property(a => a.Status).HasColumnName("status").HasConversion<byte>().IsRequired();
                b.Property(a => a.UploadedAt).HasColumnName("uploaded_at");
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
    /// Test-only <see cref="ITradeReviewRepository"/> impl — mirrors the
    /// production <c>TradeReviewRepository</c> methods needed by the
    /// decorator + integration tests (AddAsync, UpdateAsync,
    /// AddAttachmentAsync, UpdateAttachmentAsync, RemoveAttachmentAsync,
    /// FindByIdAsync, FindByTradeIdAsync). The bespoke reads +
    /// attachment ops are exercised by the audit-decorator integration
    /// tests to verify the "attachment ops forward without audit"
    /// invariant.
    /// </summary>
    private sealed class TestTradeReviewRepository : ITradeReviewRepository
    {
        private readonly TestTradeReviewDbContext _db;
        public TestTradeReviewRepository(TestTradeReviewDbContext db) { _db = db; }

        public async Task AddAsync(TradeReview review, CancellationToken ct)
            => await _db.TradeReviews.AddAsync(review, ct);

        public Task<TradeReview?> FindByTradeIdAsync(
            Guid tradeId, Guid userId, CancellationToken ct)
            => _db.TradeReviews.FirstOrDefaultAsync(
                r => r.TradeId == tradeId && r.UserId == userId, ct);

        public Task<TradeReview?> FindByIdAsync(
            Guid reviewId, Guid userId, CancellationToken ct)
            => _db.TradeReviews.FirstOrDefaultAsync(
                r => r.Id == reviewId && r.UserId == userId, ct);

        public Task UpdateAsync(TradeReview review, CancellationToken ct)
        {
            var entry = _db.Entry(review);
            if (entry.State == EntityState.Detached)
            {
                _db.TradeReviews.Update(review);
            }
            return Task.CompletedTask;
        }

        public async Task AddAttachmentAsync(TradeAttachment attachment, CancellationToken ct)
            => await _db.TradeAttachments.AddAsync(attachment, ct);

        public async Task<IReadOnlyList<TradeAttachment>> ListAttachmentsByReviewIdAsync(
            Guid reviewId, Guid userId, CancellationToken ct)
            => await _db.TradeAttachments
                .Where(a => a.ReviewId == reviewId && a.UserId == userId)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync(ct);

        public Task<int> CountAttachmentsByReviewIdAsync(
            Guid reviewId, CancellationToken ct)
            => _db.TradeAttachments.CountAsync(a => a.ReviewId == reviewId, ct);

        public Task<TradeAttachment?> FindAttachmentByIdAsync(
            Guid attachmentId, Guid userId, CancellationToken ct)
            => _db.TradeAttachments.FirstOrDefaultAsync(
                a => a.Id == attachmentId && a.UserId == userId, ct);

        public Task UpdateAttachmentAsync(TradeAttachment attachment, CancellationToken ct)
        {
            var entry = _db.Entry(attachment);
            if (entry.State == EntityState.Detached)
            {
                _db.TradeAttachments.Update(attachment);
            }
            return Task.CompletedTask;
        }

        public async Task<string?> RemoveAttachmentAsync(
            Guid attachmentId, Guid userId, CancellationToken ct)
        {
            var row = await _db.TradeAttachments.FirstOrDefaultAsync(
                a => a.Id == attachmentId && a.UserId == userId, ct);
            if (row is null) return null;
            var key = row.ObjectKey;
            _db.TradeAttachments.Remove(row);
            return key;
        }

        public async Task<Guid?> GetTradeIdByAttachmentIdAsync(
            Guid attachmentId, CancellationToken ct)
        {
            var tradeId = await _db.TradeAttachments
                .Where(a => a.Id == attachmentId)
                .Join(_db.TradeReviews,
                      a => a.ReviewId,
                      r => r.Id,
                      (a, r) => r.TradeId)
                .FirstOrDefaultAsync(ct);
            return tradeId == Guid.Empty ? null : tradeId;
        }
    }

    private (IServiceProvider sp, ITenantContext tenant, IClock clock, Guid currentUserId)
        BuildServices(Guid? currentUserId = null)
    {
        var reviewOpts = new DbContextOptionsBuilder<TestTradeReviewDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new TestTradeReviewDbContext(reviewOpts))
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

        services.AddDbContext<TestTradeReviewDbContext>(opts => opts.UseSqlite(_connection));
        var auditContextOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        services.AddScoped<AuditDbContext>(_ => new AuditDbContext(auditContextOpts));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestTradeReviewDbContext>());

        var tenantId = Guid.NewGuid();
        var userId = currentUserId ?? Guid.NewGuid();
        var tenant = new StaticTenantContext(new TenantId(tenantId), userId);
        services.AddSingleton<ITenantContext>(tenant);

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<ITradeReviewRepository, TestTradeReviewRepository>();
        // The slice 8a.2 decorator registration — under test here.
        services.Decorate<ITradeReviewRepository, TradeReviewAuditDecorator>();

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

    private static TradeReview CreateReview(Guid tradeId, Guid userId, IClock clock)
        => TradeReview.Create(
            id: Guid.NewGuid(),
            tradeId: tradeId,
            userId: userId,
            emotionality: (byte)4, // Confident
            rating: (byte)4,
            setupUsed: "Original setup: breakout pullback",
            lessons: "Patience paid off.",
            tradeIsClosed: true,
            now: clock.UtcNow).Value;

    private static TradeAttachment CreateAttachment(Guid reviewId, Guid userId)
        => TradeAttachment.RequestSlot(
            id: Guid.NewGuid(),
            reviewId: reviewId,
            userId: userId,
            objectKey: $"trading/attachments/{userId}/{Guid.NewGuid()}/{reviewId}/{Guid.NewGuid()}/chart.png",
            contentType: "image/png",
            sizeBytes: 1024L,
            now: new DateTimeOffset(2026, 8, 19, 14, 0, 0, TimeSpan.Zero)).Value;

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
    public async Task CreateReview_WritesAuditEvent_WithActionCreated()
    {
        // Phase 2 #1: AddAsync → AuditAction.Created, no diff. The audit
        // row's EntityType is "TradeReview" + UserId/TenantId from
        // ITenantContext.
        var userId = Guid.NewGuid();
        var tradeId = Guid.NewGuid();
        var (sp, tenant, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITradeReviewRepository>();
        var reviewDb = scope.ServiceProvider.GetRequiredService<TestTradeReviewDbContext>();
        var auditDb = NewAuditDbContext();

        var review = CreateReview(tradeId, userId, clock);
        await repo.AddAsync(review, CancellationToken.None);
        await reviewDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(TradeReview));
        saved.EntityId.Should().Be(review.Id);
        saved.Action.Should().Be(AuditAction.Created);
        saved.ChangesJson.Should().BeNull("Created events carry no diff.");
        saved.TenantId.Should().Be(tenant.Current!.Value);
        saved.UserId.Should().Be(tenant.CurrentUserId);
    }

    [Fact]
    public async Task UpdateReview_WritesAuditEvent_WithActionUpdatedAndDiff()
    {
        // Phase 2 #2: UpdateAsync with SetupUsed + Rating change →
        // AuditAction.Updated + diff identifying the changed fields.
        var userId = Guid.NewGuid();
        var tradeId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITradeReviewRepository>();
        var reviewDb = scope.ServiceProvider.GetRequiredService<TestTradeReviewDbContext>();
        var auditDb = NewAuditDbContext();

        var review = CreateReview(tradeId, userId, clock);
        await repo.AddAsync(review, CancellationToken.None);
        await reviewDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        var update = review.Update(
            setupUsed: "Updated setup: mean reversion at session high",
            lessons: review.Lessons,
            rating: (byte)5,
            now: clock.UtcNow);
        update.IsSuccess.Should().BeTrue("Update is the production mutation path.");
        await repo.UpdateAsync(review, CancellationToken.None);
        await reviewDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Updated);
        saved.ChangesJson.Should().NotBeNullOrEmpty();
        saved.ChangesJson.Should().Contain("setupUsed",
            "the diff payload identifies the setupUsed field change.");
        saved.ChangesJson.Should().Contain("rating",
            "the diff payload also identifies the rating field change.");
    }

    [Fact]
    public async Task AttachmentOps_ForwardWithoutAudit()
    {
        // Phase 2 #3: AddAttachmentAsync + UpdateAttachmentAsync +
        // RemoveAttachmentAsync are forwarded to the inner WITHOUT emitting
        // audit rows. Per orchestrator preflight decision 7: TradeAttachment
        // is a child entity of the review, not a separately-audited
        // aggregate. The review's Update events + handler-side MinIO
        // cleanup log already capture attachment lifecycle.
        var userId = Guid.NewGuid();
        var tradeId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITradeReviewRepository>();
        var reviewDb = scope.ServiceProvider.GetRequiredService<TestTradeReviewDbContext>();
        var auditDb = NewAuditDbContext();

        // Setup: create a review so we have a parent for attachments.
        var review = CreateReview(tradeId, userId, clock);
        await repo.AddAsync(review, CancellationToken.None);
        await reviewDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        // AddAttachmentAsync — forwarded without audit.
        var attachment = CreateAttachment(review.Id, userId);
        await repo.AddAttachmentAsync(attachment, CancellationToken.None);
        await reviewDb.SaveChangesAsync();

        // UpdateAttachmentAsync — forwarded without audit.
        var markUploaded = attachment.MarkUploaded(
            sha256: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            tradeId: tradeId,
            now: clock.UtcNow);
        markUploaded.IsSuccess.Should().BeTrue();
        await repo.UpdateAttachmentAsync(attachment, CancellationToken.None);
        await reviewDb.SaveChangesAsync();

        // RemoveAttachmentAsync — forwarded without audit.
        var key = await repo.RemoveAttachmentAsync(attachment.Id, userId, CancellationToken.None);
        key.Should().NotBeNull("the attachment existed and was deleted.");
        await reviewDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var auditRows = await auditDb.AuditEvents.ToListAsync();
        auditRows.Should().BeEmpty(
            "attachment ops (AddAttachmentAsync + UpdateAttachmentAsync + " +
            "RemoveAttachmentAsync) MUST NOT emit audit rows — TradeAttachment " +
            "is a child entity of the review (orchestrator preflight decision 7).");
    }

    [Fact]
    public async Task CrossTenantUpdate_LogsAuditEvent_WithActionDenied_AndThrowsUnauthorized()
    {
        // Phase 2 #4: a different user attempts to update another user's
        // review. The decorator detects the ownership mismatch
        // (review.UserId != currentUserId), logs a Denied audit row for
        // the ATTEMPT (security trail), and throws
        // UnauthorizedAccessException. The inner UpdateAsync is NEVER
        // reached.
        var ownerUserId = Guid.NewGuid();
        var attackerUserId = Guid.NewGuid(); // != ownerUserId
        var tradeId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: attackerUserId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITradeReviewRepository>();
        var reviewDb = scope.ServiceProvider.GetRequiredService<TestTradeReviewDbContext>();
        var auditDb = NewAuditDbContext();

        // Setup: create the review as the OWNER (bypass the decorator for
        // the setup so the ownership check doesn't fire here).
        var review = CreateReview(tradeId, ownerUserId, clock);
        reviewDb.TradeReviews.Add(review);
        await reviewDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        // Attacker attempts to update the owner's review.
        var update = review.Update(
            setupUsed: "Cross-tenant mutation attempt",
            lessons: review.Lessons,
            rating: (byte)1,
            now: clock.UtcNow);
        update.IsSuccess.Should().BeTrue("Update itself is in-memory and doesn't enforce ownership.");
        Func<Task> act = async () => await repo.UpdateAsync(review, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "the decorator rejects cross-tenant update attempts.");

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Denied);
        saved.EntityId.Should().Be(review.Id);
        saved.UserId.Should().Be(attackerUserId,
            "the audit trail records WHO attempted the cross-tenant access, " +
            "not the owner.");
    }

    [Fact]
    public async Task FindByIdAsync_WritesNoAuditEvent()
    {
        // Phase 2 #5: the bespoke user-scoped read (FindByIdAsync) emits
        // NO audit event. Reads are not audited (matches the Wave 6 + 7 +
        // 8a.1 + 8a.2 precedent: only mutations get audit rows).
        var userId = Guid.NewGuid();
        var tradeId = Guid.NewGuid();
        var (sp, _, clock, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITradeReviewRepository>();
        var reviewDb = scope.ServiceProvider.GetRequiredService<TestTradeReviewDbContext>();
        var auditDb = NewAuditDbContext();

        var review = CreateReview(tradeId, userId, clock);
        reviewDb.TradeReviews.Add(review);
        await reviewDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        var found = await repo.FindByIdAsync(review.Id, userId, CancellationToken.None);
        found.Should().NotBeNull();
        await auditDb.SaveChangesAsync();

        var auditRows = await auditDb.AuditEvents.ToListAsync();
        auditRows.Should().BeEmpty(
            "FindByIdAsync is a read — no audit event should be written " +
            "(matches the Wave 6 + 7 + 8a.1 + 8a.2 precedent).");
    }
}
