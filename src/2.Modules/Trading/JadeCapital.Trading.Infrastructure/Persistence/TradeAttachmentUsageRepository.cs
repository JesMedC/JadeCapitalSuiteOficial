using JadeCapital.Trading.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ITradeAttachmentUsageRepository"/>.
/// One aggregate query: <c>SUM(size_bytes) + COUNT(*)</c> over
/// <c>trade_attachments</c> filtered by <c>user_id</c> AND
/// <c>status = 'uploaded'</c> AND <c>is_active = true</c>.
///
/// Returns <c>(0, 0)</c> for users with no rows (no exception, no null).
/// </summary>
internal sealed class TradeAttachmentUsageRepository : ITradeAttachmentUsageRepository
{
    private readonly TradingDbContext _db;

    public TradeAttachmentUsageRepository(TradingDbContext db) { _db = db; }

    public async Task<(long TotalBytes, int Count)> GetUsageAsync(Guid userId, CancellationToken ct)
    {
        var rows = _db.TradeAttachments
            .Where(a => a.UserId == userId
                        && a.Status == Domain.TradeAttachments.TradeAttachmentStatus.Uploaded);

        var totalBytes = await rows.SumAsync(a => (long?)a.SizeBytes, ct) ?? 0L;
        var count = await rows.CountAsync(ct);

        return (totalBytes, count);
    }
}