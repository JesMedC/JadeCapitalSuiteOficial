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
/// <remarks>
/// <para>
/// <b>SKIP rationale — no audit decorator needed (Wave 9 9b.2 reconciliation).</b>
/// </para>
/// <para>
/// This interface exposes ONLY a read-side aggregate query
/// (<see cref="GetUsageAsync"/>) — no <c>AddAsync</c>, <c>UpdateAsync</c>,
/// or <c>DeleteAsync</c> exists on the surface. Wrapping it with an
/// audit decorator would be a no-op: the audit-write path is empty,
/// and the Wave 6/7/8 precedent (mirrored in the existing
/// "GetById is NOT audited" scenario across every decorator) is that
/// reads are never audited.
/// </para>
/// <para>
/// The right seam for <c>TradeAttachment</c> audit IS
/// <c>IAttachmentSweepRepository.SoftDeleteBatchAsync</c> (Wave 9 9a.3 —
/// see <c>AttachmentSweepAuditDecorator</c>). User-impacting soft-delete
/// happens there, not at this usage projection.
/// </para>
/// <para>
/// See <c>openspec/changes/2026-08-19-wave9-audit-finalization/specs/soft-delete-audit/spec.md</c>
/// § "REMOVED Requirements" for the full rationale + the
/// <c>git grep</c> verification that pinned the SKIP.
/// </para>
/// </remarks>
public interface ITradeAttachmentUsageRepository
{
    /// <summary>
    /// Returns the user's aggregate storage consumption across all uploaded
    /// (status = uploaded, is_active = true) attachments. Returns
    /// <c>(0, 0)</c> for users with no rows — never throws on empty.
    /// </summary>
    Task<(long TotalBytes, int Count)> GetUsageAsync(Guid userId, CancellationToken ct);
}