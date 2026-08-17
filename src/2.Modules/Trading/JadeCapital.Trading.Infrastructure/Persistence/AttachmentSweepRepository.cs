using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.AttachmentAudits;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IAttachmentSweepRepository"/>.
/// Lives in Infrastructure so the BackgroundService doesn't need to know
/// about EF Core.
///
/// The sweep query is range-scanned via
/// <c>ix_trade_attachments_expires_sweep</c> (migration 0018) — index hit,
/// not a full table scan.
/// </summary>
internal sealed class AttachmentSweepRepository : IAttachmentSweepRepository
{
    private readonly TradingDbContext _db;

    public AttachmentSweepRepository(TradingDbContext db) { _db = db; }

    public async Task<IReadOnlyList<ExpiredAttachmentSweepRow>> GetExpiredBatchAsync(
        DateTimeOffset asOf, int skip, int take, CancellationToken ct)
    {
        return await _db.TradeAttachments
            .Where(a => a.IsActive && a.ExpiresAt != null && a.ExpiresAt < asOf)
            .OrderBy(a => a.ExpiresAt)
            .Skip(skip)
            .Take(take)
            .Select(a => new ExpiredAttachmentSweepRow(
                a.Id, a.UserId, a.ReviewId, a.ObjectKey, a.SizeBytes, a.ExpiresAt!.Value))
            .ToListAsync(ct);
    }

    public async Task<int> SoftDeleteBatchAsync(IReadOnlyList<Guid> attachmentIds, CancellationToken ct)
    {
        if (attachmentIds.Count == 0) return 0;

        // Soft-delete via IsActive flag. We don't call TradeAttachment.MarkFailed
        // here because the row stays as "uploaded" in status — the sweep
        // is a lifecycle event, not a failure. Only IsActive flips to false.
        // EF tracks the entity; calling Update on each row + SaveChanges is
        // the simplest path. For very large batches a raw SQL UPDATE with
        // ANY(@ids) would be faster, but Wave 4d batches are <= 100 rows.
        var rows = await _db.TradeAttachments
            .Where(a => attachmentIds.Contains(a.Id))
            .ToListAsync(ct);

        var now = System.DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            row.MarkSwept(now); // domain method — flips IsActive + Touch.
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
        var row = new AttachmentQuotaAudit(
            Guid.NewGuid(), userId, ranAt,
            cleanedCount, cleanedBytes,
            remainingCount, remainingBytes,
            skippedReason, errorMessage);
        await _db.AttachmentQuotaAudits.AddAsync(row, ct);
    }

    public async Task<(long TotalBytes, int Count)> GetUserAggregateAsync(Guid userId, CancellationToken ct)
    {
        var rows = _db.TradeAttachments
            .Where(a => a.UserId == userId
                        && a.IsActive
                        && a.Status == Domain.TradeAttachments.TradeAttachmentStatus.Uploaded);

        var totalBytes = await rows.SumAsync(a => (long?)a.SizeBytes, ct) ?? 0L;
        var count = await rows.CountAsync(ct);
        return (totalBytes, count);
    }

    public async Task<IReadOnlyList<Guid>> GetActiveUserIdsAsync(CancellationToken ct)
    {
        return await _db.TradeAttachments
            .Where(a => a.IsActive && a.ExpiresAt != null)
            .Select(a => a.UserId)
            .Distinct()
            .ToListAsync(ct);
    }
}