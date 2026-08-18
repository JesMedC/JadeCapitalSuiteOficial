using FluentAssertions;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.Imports;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Imports;
using JadeCapital.Trading.Infrastructure.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Identity.UnitTests.Persistence;

/// <summary>
/// Integration tests for the <see cref="ImportJobAuditDecorator"/> wired via
/// Scrutor (Wave 6, slice 6d.2, Phases 3.3 + 3.4).
///
/// <para>
/// Three RED scenarios pinned here (per tasks.md line 480):
/// </para>
/// <list type="number">
///   <item>Create import job → <c>audit.events</c> row with
///         <c>AuditAction.Created</c>.</item>
///   <item>Soft-delete via <see cref="ImportJob.MarkDeleted"/> → <c>audit.events</c>
///         row with <c>AuditAction.Deleted</c> + a before/after diff
///         showing <c>isDeleted: false → true</c>.</item>
///   <item>Cross-tenant delete attempt → <see cref="UnauthorizedAccessException"/>
///         (the "404" surface from tasks.md) + an audit row recording the
///         attempt itself.</item>
/// </list>
///
/// <para>
/// <b>Why a fully independent test DbContext</b>: the production
/// <c>TradingDbContext</c> has DbSet declarations for Trade/Account/etc.
/// that pull in Npgsql-specific array converters (Money complex type
/// + JournalEntry.Tags) — these fail to compose on SQLite. Subclassing
/// TradingDbContext still inherits the DbSet surface. A focused helper
/// DbContext keeps the model SQLite-compatible without touching the
/// production schema. Same pattern as
/// <c>ImportJobSoftDeleteQueryFilterTests.ImportJobOnlyDbContext</c>.
/// </para>
/// <para>
/// <b>Why a test-specific ImportJobRepository</b>: the production
/// <c>ImportJobRepository</c> takes a <c>TradingDbContext</c> dependency.
/// The test uses an independent DbContext (not a subclass), so the
/// production repo's constructor signature doesn't bind. The test repo
/// is a 50-line shim that mirrors the production methods against the
/// test DbContext.
/// </para>
/// </summary>
public class ImportJobRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public ImportJobRepositoryIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        // SQLite enforces FKs by default; the test only needs import_jobs +
        // audit events, so disable FKs.
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
    /// SQLite-compatible test DbContext — maps only <see cref="ImportJob"/>
    /// + <see cref="AuditEvent"/>. Sidesteps the Npgsql-specific converters
    /// the production TradingDbContext pulls in.
    /// </summary>
    private sealed class TestTradingDbContext : DbContext
    {
        public TestTradingDbContext(DbContextOptions<TestTradingDbContext> options) : base(options) { }

        public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("trading");
            modelBuilder.Entity<ImportJob>(b =>
            {
                b.ToTable("import_jobs");
                b.HasKey(j => j.Id);
                b.Property(j => j.Id).HasColumnName("id");
                b.Property(j => j.UserId).HasColumnName("user_id").IsRequired();
                b.Property(j => j.AccountId).HasColumnName("account_id").IsRequired();
                b.Property(j => j.Format).HasColumnName("format").HasConversion<byte>().IsRequired();
                b.Property(j => j.FileName).HasColumnName("file_name").HasMaxLength(500).IsRequired();
                b.Property(j => j.FileSizeBytes).HasColumnName("file_size_bytes").IsRequired();
                b.Property(j => j.FileSha256).HasColumnName("file_sha256").HasMaxLength(64).IsRequired();
                b.Property(j => j.Status).HasColumnName("status").HasConversion<byte>().IsRequired();
                b.Property(j => j.RowsTotal).HasColumnName("rows_total").IsRequired();
                b.Property(j => j.RowsImported).HasColumnName("rows_imported").IsRequired();
                b.Property(j => j.RowsSkipped).HasColumnName("rows_skipped").IsRequired();
                b.Property(j => j.RowsErrored).HasColumnName("rows_errored").IsRequired();
                b.Property(j => j.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
                b.Property(j => j.StartedAt).HasColumnName("started_at").IsRequired();
                b.Property(j => j.FinishedAt).HasColumnName("finished_at");
                b.Property(j => j.IsDeleted).HasColumnName("is_deleted").IsRequired();
                b.Property(j => j.DeletedAtUtc).HasColumnName("deleted_at");
                b.Property(j => j.DeletedByUserId).HasColumnName("deleted_by_user_id");
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
    /// Test-only <see cref="IImportJobRepository"/> impl — mirrors the
    /// production <c>ImportJobRepository</c> against the test DbContext.
    /// </summary>
    private sealed class TestImportJobRepository : IImportJobRepository
    {
        private readonly TestTradingDbContext _db;
        public TestImportJobRepository(TestTradingDbContext db) { _db = db; }

        public Task<ImportJob?> GetByIdAsync(Guid id, CancellationToken ct)
            => _db.ImportJobs.FirstOrDefaultAsync(j => j.Id == id, ct);

        public Task<ImportJob?> FindActiveBySha256Async(Guid userId, string sha256, CancellationToken ct)
            => _db.ImportJobs
                .Where(j => j.UserId == userId
                    && j.FileSha256 == sha256
                    && (j.Status == ImportJobStatus.Pending
                        || j.Status == ImportJobStatus.InProgress
                        || j.Status == ImportJobStatus.Completed))
                .OrderByDescending(j => j.StartedAt)
                .FirstOrDefaultAsync(ct);

        public async Task AddAsync(ImportJob job, CancellationToken ct)
            => await _db.ImportJobs.AddAsync(job, ct);

        public Task UpdateAsync(ImportJob job, CancellationToken ct)
        {
            var entry = _db.Entry(job);
            if (entry.State == EntityState.Detached)
                _db.ImportJobs.Update(job);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(ImportJob job, CancellationToken ct)
        {
            _db.ImportJobs.Remove(job);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Builds the test service collection. Wires the test-specific
    /// <see cref="TestImportJobRepository"/> + <see cref="ImportJobAuditDecorator"/>
    /// + <see cref="AuditLogger"/> + <see cref="TestTradingDbContext"/> +
    /// <see cref="AuditDbContext"/> on a shared SQLite connection.
    /// <paramref name="currentUserId"/> controls the tenant-context's user
    /// id (defaults to a fresh Guid).
    /// </summary>
    private (IServiceProvider sp, ITenantContext tenant, IClock clock, Guid currentUserId)
        BuildServices(Guid? currentUserId = null)
    {
        // EnsureCreated on a per-DbContext basis: EF's EnsureCreated
        // returns early if ANY table exists, so calling EnsureCreated on
        // AuditDbContext after TestTradingDbContext already ran would
        // skip the audit.events table. We do the EnsureCreated for each
        // context explicitly via transient options builders.
        var tradingOpts = new DbContextOptionsBuilder<TestTradingDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new TestTradingDbContext(tradingOpts))
            db.Database.EnsureCreated();

        var auditOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new AuditDbContext(auditOpts))
            db.Database.EnsureCreated();

        // FIX (slice 6d.2): EF Core 9 SQLite EnsureCreated is
        // "all-or-nothing" — once any table exists, every subsequent
        // EnsureCreated is a no-op. The first call (TestTradingDbContext)
        // creates trading.import_jobs; the second (AuditDbContext) was a
        // no-op so audit.events was never created. Force-create it via
        // raw SQL matching AuditEventConfiguration. Same fixture fix as
        // TenantRepositoryIntegrationTests (Phase 3.2 deviation).
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

        // Only ONE DbContext via DI for the ImportJob-tracking context
        // (TestTradingDbContext). AuditDbContext is also registered — the
        // AuditLogger needs it. The DbContext base type is aliased to
        // TestTradingDbContext so the decorator's DbContext parameter
        // resolves uniquely. In production the alias points to
        // TradingDbContext; the decorator doesn't care which concrete
        // DbContext implements the alias — it just needs the change
        // tracker.
        services.AddDbContext<TestTradingDbContext>(opts => opts.UseSqlite(_connection));
        var auditContextOpts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        services.AddScoped<AuditDbContext>(_ => new AuditDbContext(auditContextOpts));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestTradingDbContext>());

        var tenantId = Guid.NewGuid();
        var userId = currentUserId ?? Guid.NewGuid();
        var tenant = new StaticTenantContext(new TenantId(tenantId), userId);
        services.AddSingleton<ITenantContext>(tenant);

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IImportJobRepository, TestImportJobRepository>();
        services.Decorate<IImportJobRepository, ImportJobAuditDecorator>();

        var sp = services.BuildServiceProvider();
        return (sp, tenant, sp.GetRequiredService<IClock>(), userId);
    }

    /// <summary>
    /// Helper: creates a fresh AuditDbContext on the shared connection for
    /// verification queries. Not registered via DI because adding a 2nd
    /// DbContext would break ImportJobAuditDecorator's DbContext resolution.
    /// </summary>
    private AuditDbContext NewAuditDbContext()
    {
        var opts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new AuditDbContext(opts);
    }

    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 19, 14, 0, 0, TimeSpan.Zero);

    private const string Sha256Hex = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
    private static readonly Guid AccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");

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
    public async Task CreateImportJob_WritesAuditEvent_WithActionCreated()
    {
        // Phase 3 #1: AddAsync → audit row with AuditAction.Created.
        var userId = Guid.NewGuid();
        var (sp, _, _, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IImportJobRepository>();
        var tradingDb = scope.ServiceProvider.GetRequiredService<TestTradingDbContext>();
        var auditDb = NewAuditDbContext();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var job = ImportJob.Begin(userId, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, clock).Value;

        await repo.AddAsync(job, CancellationToken.None);
        await tradingDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(nameof(ImportJob));
        saved.EntityId.Should().Be(job.Id);
        saved.Action.Should().Be(AuditAction.Created);
        saved.ChangesJson.Should().BeNull("Created events carry no diff.");
    }

    [Fact]
    public async Task SoftDeleteImportJob_WritesAuditEvent_WithBeforeAndAfterDiff()
    {
        // Phase 3 #2: MarkDeleted → UpdateAsync → audit row with
        // AuditAction.Deleted + before/after diff (isDeleted false→true).
        var userId = Guid.NewGuid();
        var (sp, _, _, _) = BuildServices(currentUserId: userId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IImportJobRepository>();
        var tradingDb = scope.ServiceProvider.GetRequiredService<TestTradingDbContext>();
        var auditDb = NewAuditDbContext();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var job = ImportJob.Begin(userId, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, clock).Value;
        await repo.AddAsync(job, CancellationToken.None);
        await tradingDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents); // discard Created row
        await auditDb.SaveChangesAsync();

        job.MarkDeleted(userId, clock);
        await repo.UpdateAsync(job, CancellationToken.None);
        await tradingDb.SaveChangesAsync();
        await auditDb.SaveChangesAsync();

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Deleted);
        saved.EntityId.Should().Be(job.Id);
        saved.ChangesJson.Should().NotBeNullOrEmpty("the diff payload identifies the changed field.");
        saved.ChangesJson.Should().Contain("isDeleted", "the diff payload identifies the isDeleted field.");
    }

    [Fact]
    public async Task CrossTenantDelete_LogsAuditEvent_AndThrowsUnauthorized()
    {
        // Phase 3 #3: a different user attempts to delete another user's
        // job. The decorator detects the ownership mismatch, logs an audit
        // event for the ATTEMPT (security trail), and throws
        // UnauthorizedAccessException — the "404 surface" tasks.md §6d.2
        // calls for. No row in trading.import_jobs is touched.
        var ownerUserId = Guid.NewGuid();
        var attackerUserId = Guid.NewGuid(); // ≠ ownerUserId
        var (sp, _, _, _) = BuildServices(currentUserId: attackerUserId);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IImportJobRepository>();
        var tradingDb = scope.ServiceProvider.GetRequiredService<TestTradingDbContext>();
        var auditDb = NewAuditDbContext();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        // Setup: create the job as the OWNER (bypass the decorator for
        // the setup so the ownership check doesn't fire here).
        var job = ImportJob.Begin(ownerUserId, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, clock).Value;
        tradingDb.ImportJobs.Add(job);
        await tradingDb.SaveChangesAsync();
        auditDb.AuditEvents.RemoveRange(auditDb.AuditEvents);
        await auditDb.SaveChangesAsync();

        Func<Task> act = async () => await repo.DeleteAsync(job, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "the decorator rejects cross-tenant delete attempts.");

        var saved = await auditDb.AuditEvents.SingleAsync();
        saved.Action.Should().Be(AuditAction.Deleted);
        saved.EntityId.Should().Be(job.Id);
        saved.UserId.Should().Be(attackerUserId,
            "the audit trail records WHO attempted the cross-tenant access, not the owner.");

        // Confirm the row was not actually deleted (cross-tenant attempt
        // is a no-op on the DB).
        var stillPresent = await tradingDb.ImportJobs.IgnoreQueryFilters().SingleAsync(j => j.Id == job.Id);
        stillPresent.IsDeleted.Should().BeFalse(
            "the cross-tenant attempt MUST NOT mutate the row — only log the attempt.");
    }
}