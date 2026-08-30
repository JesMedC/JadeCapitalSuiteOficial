using FluentAssertions;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Identity.UnitTests.Audit;

/// <summary>
/// Integration tests for <see cref="AuditRetentionService"/> (Wave 9, slice 9b.1).
///
/// <para>
/// One RED scenario pinned here (per tasks.md §9b.1 Phase 4.1):
/// <b>PurgeOldAsync_DeletesOnlyRowsOlderThanCutoff_AndRespectsBatchLimit</b> —
/// verifies three independent behaviors in one test:
/// <list type="number">
///   <item>Cutoff boundary: only rows with <c>occurred_at &lt; cutoff</c> are
///         deleted; rows with <c>occurred_at &gt;= cutoff</c> are left untouched.</item>
///   <item>Idempotency: a second call with the same cutoff returns <c>0</c>
///         (no rows to delete).</item>
///   <item>Batch limit: the per-call delete is capped by <c>batchLimit</c>
///         (the BackgroundService loops until the return value drops below
///         the limit; the unit-level test verifies the cap is respected).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why SQLite in-memory</b>: mirrors the Wave 6 6d.2
/// <c>AuditLoggerTests</c> + Wave 9 9a.1 <c>AIRiskAdviceRepositoryIntegrationTests</c>
/// pattern. The production <see cref="AuditDbContext"/> + <c>AuditEventConfiguration</c>
/// are SQLite-compatible (no Npgsql-specific converters on the
/// <c>audit.events</c> table). The <see cref="ExecuteDeleteAsync"/> call
/// translates to a single DELETE statement on both Postgres and SQLite.
/// </para>
/// </summary>
public class AuditRetentionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public AuditRetentionServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private AuditDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        var db = new AuditDbContext(opts);
        db.Database.EnsureCreated();
        return db;
    }

    private static AuditEvent SeedEvent(DateTimeOffset occurredAt)
    {
        return AuditEvent.FromTrusted(
            id: Guid.NewGuid(),
            entityType: "TradeAttachment",
            entityId: Guid.NewGuid(),
            action: AuditAction.Updated,
            tenantId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            changesJson: null,
            occurredAt: occurredAt);
    }

    [Fact]
    public async Task PurgeOldAsync_DeletesOnlyRowsOlderThanCutoff_AndRespectsBatchLimit()
    {
        // Phase 4.1 #1: 5 events spanning -100d, -50d, -30d, -10d, -1d.
        // Cutoff = UtcNow - 60d. BatchLimit = 100. Expect 1 deletion
        // (the -100d event). The remaining 4 are >= cutoff.
        // Second call with the same cutoff returns 0 (idempotent).
        var db = BuildDb();
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddDays(-60);

        var oldEnough = SeedEvent(now.AddDays(-100));
        var tooRecent1 = SeedEvent(now.AddDays(-50));
        var tooRecent2 = SeedEvent(now.AddDays(-30));
        var tooRecent3 = SeedEvent(now.AddDays(-10));
        var tooRecent4 = SeedEvent(now.AddDays(-1));
        db.AuditEvents.AddRange(oldEnough, tooRecent1, tooRecent2, tooRecent3, tooRecent4);
        await db.SaveChangesAsync();

        var sut = new AuditRetentionService(db);

        // First call: only the -100d row is below cutoff.
        var deleted1 = await sut.PurgeOldAsync(cutoff, batchLimit: 100, CancellationToken.None);
        deleted1.Should().Be(1,
            "only the -100d row has occurred_at < cutoff; the other 4 are >= cutoff.");

        // Verify the right row was deleted.
        var remaining = await db.AuditEvents.ToListAsync();
        remaining.Should().HaveCount(4, "1 row was deleted; 4 remain.");
        remaining.Should().NotContain(e => e.Id == oldEnough.Id,
            "the -100d row must be deleted.");
        remaining.Should().Contain(e => e.Id == tooRecent1.Id);
        remaining.Should().Contain(e => e.Id == tooRecent2.Id);
        remaining.Should().Contain(e => e.Id == tooRecent3.Id);
        remaining.Should().Contain(e => e.Id == tooRecent4.Id);

        // Second call: idempotent — returns 0.
        var deleted2 = await sut.PurgeOldAsync(cutoff, batchLimit: 100, CancellationToken.None);
        deleted2.Should().Be(0,
            "second call with the same cutoff is idempotent — no rows to delete.");

        // Batch limit: seed 5 more rows below cutoff and assert at most
        // batchLimit=2 are deleted in one call.
        for (var i = 0; i < 5; i++)
            db.AuditEvents.Add(SeedEvent(now.AddDays(-65 - i)));
        await db.SaveChangesAsync();

        var deleted3 = await sut.PurgeOldAsync(cutoff, batchLimit: 2, CancellationToken.None);
        deleted3.Should().Be(2,
            "the BatchLimit caps the per-call delete to 2 rows; the BackgroundService loops until the return value drops below the limit.");
    }
}
