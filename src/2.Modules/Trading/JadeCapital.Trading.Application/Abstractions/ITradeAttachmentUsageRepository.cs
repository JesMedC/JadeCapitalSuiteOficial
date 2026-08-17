using JadeCapital.Identity.Contracts.Projections;

namespace JadeCapital.Trading.Application.Abstractions;

/// <summary>
/// Aggregation of the user's per-attachment counts + bytes. Lives in
/// <c>Trading.Application.Abstractions</c> because only Trading owns the
/// <c>trading.trade_attachments</c> table — Identity has no business
/// knowing the trade shape.
///
/// Implementation lives in <c>Trading.Infrastructure.Persistence</c>
/// as <c>TradeAttachmentUsageRepository</c>. It runs a single
/// <c>SUM(bytes) + COUNT(*)</c> over <c>trade_attachments</c> filtered
/// by <c>user_id</c> AND <c>status = 'uploaded'</c> AND <c>is_active = true</c>.
/// </summary>
public interface ITradeAttachmentUsageRepository
{
    /// <summary>
    /// Returns the user's aggregate storage consumption across all uploaded
    /// (status = uploaded, is_active = true) attachments. Returns
    /// <c>(0, 0)</c> for users with no rows — never throws on empty.
    /// </summary>
    Task<(long TotalBytes, int Count)> GetUsageAsync(Guid userId, CancellationToken ct);
}