using FluentAssertions;
using JadeCapital.Shared.Kernel.Imports;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Domain.Imports;
using JadeCapital.Trading.Infrastructure.Persistence;
using JadeCapital.Trading.Infrastructure.Persistence.Configurations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.UnitTests.Imports;

/// <summary>
/// Tests for the EF global query filter on <see cref="ImportJob"/> (Wave 6, slice 6d.1).
///
/// <para>
/// The soft-delete filter is applied via <c>b.HasQueryFilter(j =&gt; !j.IsDeleted)</c>
/// in <see cref="ImportJobConfiguration"/>. Five RED scenarios pinned here:
/// </para>
/// <list type="number">
///   <item>A regular query returns only non-deleted rows.</item>
///   <item>A soft-deleted entity is excluded from a regular query.</item>
///   <item><c>IgnoreQueryFilters()</c> returns ALL rows (including soft-deleted).</item>
///   <item><c>Count()</c> returns the count of non-deleted rows.</item>
///   <item>Async enumeration excludes soft-deleted rows.</item>
/// </list>
///
/// <para>
/// <b>Why SQLite in-memory</b>: mirrors the Identity.UnitTests
/// <c>BackfillTenantsRunnerTests</c> pattern (also SQLite-backed). Lets us
/// exercise the EF <c>HasQueryFilter</c> behavior end-to-end without a real
/// Postgres or Testcontainers dependency. SQLite stores Guid as TEXT — the
/// filter predicate is provider-agnostic, so it works the same way.
/// </para>
///
/// <para>
/// <b>Why a dedicated DbContext</b>: the production
/// <see cref="TradingDbContext"/> is scoped to the Trading module and
/// registers every Trading entity. For these focused tests we want a
/// minimal DbContext that ONLY maps <see cref="ImportJob"/> — so the
/// query filter is the only thing under test, and we don't need to
/// spin up the full Trading schema (accounts, instruments, trades, etc.).
/// </para>
/// </summary>
public class ImportJobSoftDeleteQueryFilterTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public ImportJobSoftDeleteQueryFilterTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Minimal DbContext that ONLY maps <see cref="ImportJob"/> via the
    /// production <see cref="ImportJobConfiguration"/>. Lets the query
    /// filter test focus on the soft-delete behavior without needing
    /// the full Trading schema (accounts, trades, etc.).
    /// </summary>
    private sealed class ImportJobOnlyDbContext : DbContext
    {
        public ImportJobOnlyDbContext(DbContextOptions<ImportJobOnlyDbContext> options)
            : base(options) { }

        public DbSet<ImportJob> ImportJobs => Set<ImportJob>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("trading");
            modelBuilder.ApplyConfiguration(new ImportJobConfiguration());
        }
    }

    private static ImportJobOnlyDbContext BuildDb(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ImportJobOnlyDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new ImportJobOnlyDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static readonly IClock Clock = new StaticClock(
        new DateTimeOffset(2026, 8, 19, 14, 0, 0, TimeSpan.Zero));

    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Sha256Hex = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    private static ImportJob NewJob()
        => ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, Clock).Value;

    [Fact]
    public async Task Query_ReturnsOnlyNonDeletedRows()
    {
        // Phase 4 #1: 3 non-deleted + 1 soft-deleted → query returns 3.
        using var db = BuildDb(_connection);
        db.ImportJobs.Add(NewJob());
        db.ImportJobs.Add(NewJob());
        db.ImportJobs.Add(NewJob());
        var deleted = NewJob();
        deleted.MarkDeleted(UserId, Clock);
        db.ImportJobs.Add(deleted);
        await db.SaveChangesAsync();

        var live = await db.ImportJobs.ToListAsync();

        live.Should().HaveCount(3, "the EF global query filter excludes soft-deleted rows from regular queries.");
    }

    [Fact]
    public async Task GetById_SoftDeletedEntity_IsExcluded()
    {
        // Phase 4 #2: GetById on a soft-deleted entity returns null
        // (the EF filter strips it before the WHERE id=? predicate runs).
        using var db = BuildDb(_connection);
        var job = NewJob();
        db.ImportJobs.Add(job);
        await db.SaveChangesAsync();

        // Mark + persist the delete.
        job.MarkDeleted(UserId, Clock);
        await db.SaveChangesAsync();

        var found = await db.ImportJobs.SingleOrDefaultAsync(j => j.Id == job.Id);

        found.Should().BeNull("the soft-delete filter excludes the row from a regular lookup.");
    }

    [Fact]
    public async Task IgnoreQueryFilters_ReturnsAllRows()
    {
        // Phase 4 #3: IgnoreQueryFilters bypasses the soft-delete filter,
        // returning every row including soft-deleted ones. Used by tests,
        // migrations, and admin tooling.
        using var db = BuildDb(_connection);
        db.ImportJobs.Add(NewJob());
        db.ImportJobs.Add(NewJob());
        var deleted = NewJob();
        deleted.MarkDeleted(UserId, Clock);
        db.ImportJobs.Add(deleted);
        await db.SaveChangesAsync();

        var all = await db.ImportJobs.IgnoreQueryFilters().ToListAsync();

        all.Should().HaveCount(3, "IgnoreQueryFilters bypasses the soft-delete filter.");
    }

    [Fact]
    public async Task Count_ReturnsOnlyNonDeletedRows()
    {
        // Phase 4 #4: Count() respects the query filter.
        using var db = BuildDb(_connection);
        db.ImportJobs.Add(NewJob());
        db.ImportJobs.Add(NewJob());
        db.ImportJobs.Add(NewJob());
        var deleted = NewJob();
        deleted.MarkDeleted(UserId, Clock);
        db.ImportJobs.Add(deleted);
        await db.SaveChangesAsync();

        var count = await db.ImportJobs.CountAsync();

        count.Should().Be(3, "Count() applies the soft-delete filter.");
    }

    [Fact]
    public async Task AsyncEnumeration_ExcludesSoftDeletedRows()
    {
        // Phase 4 #5: async enumeration (ToListAsync) applies the filter.
        using var db = BuildDb(_connection);
        for (var i = 0; i < 5; i++) db.ImportJobs.Add(NewJob());
        var deleted = NewJob();
        deleted.MarkDeleted(UserId, Clock);
        db.ImportJobs.Add(deleted);
        await db.SaveChangesAsync();

        var ids = new List<Guid>();
        await foreach (var job in db.ImportJobs.AsAsyncEnumerable())
        {
            ids.Add(job.Id);
        }

        ids.Should().HaveCount(5, "async enumeration honors the soft-delete filter.");
        ids.Should().NotContain(deleted.Id);
    }

    /// <summary>
    /// Static clock for deterministic test time.
    /// </summary>
    private sealed class StaticClock : IClock
    {
        public StaticClock(DateTimeOffset now) { UtcNow = now; }
        public DateTimeOffset UtcNow { get; }
    }
}
