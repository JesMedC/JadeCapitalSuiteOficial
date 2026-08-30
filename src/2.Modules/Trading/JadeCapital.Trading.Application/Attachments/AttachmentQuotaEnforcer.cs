using JadeCapital.Identity.Contracts.Projections;
using JadeCapital.Shared.Kernel.Storage;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Attachments;

namespace JadeCapital.Trading.Application.Attachments;

/// <summary>
/// Pre-upload gate for attachment quota enforcement (slice 4d, Wave 4).
///
/// Called from <c>RequestAttachmentUploadHandler</c> BEFORE generating
/// the presigned PUT URL — if the user would exceed the cap (either
/// total bytes or attachment count), the handler returns HTTP 413 with
/// <c>error.code = "attachment.quota_exceeded"</c>.
///
/// The enforcer is intentionally a thin wrapper over the quota + usage
/// resolution: it does NOT mutate state, only reads. The
/// <c>ConfirmAttachmentUploadedHandler</c> updates the cached
/// <c>identity.users.attachment_used_bytes</c> after a successful upload.
///
/// Quota values:
/// <list type="bullet">
///   <item>Per-user cap from <see cref="IAttachmentQuotaReader"/>.
///   Falls back to <see cref="AttachmentQuota.Default"/> if the user has
///   no row (legacy data — default 50 MiB / 100).</item>
///   <item><c>attachment_used_bytes</c> from the same projection (cached
///   aggregate maintained by Confirm + sweep).</item>
///   <item><c>count</c> from <see cref="ITradeAttachmentUsageRepository"/>
///   (live ground truth from trade_attachments).</item>
/// </list>
/// </summary>
public sealed class AttachmentQuotaEnforcer
{
    private readonly IAttachmentQuotaReader _quotaReader;
    private readonly ITradeAttachmentUsageRepository _usage;
    private readonly AttachmentQuota _defaultQuota;

    public AttachmentQuotaEnforcer(
        IAttachmentQuotaReader quotaReader,
        ITradeAttachmentUsageRepository usage)
        : this(quotaReader, usage, AttachmentQuota.Default) { }

    // Internal ctor for tests — lets them inject a fixed default quota.
    internal AttachmentQuotaEnforcer(
        IAttachmentQuotaReader quotaReader,
        ITradeAttachmentUsageRepository usage,
        AttachmentQuota defaultQuota)
    {
        _quotaReader = quotaReader;
        _usage = usage;
        _defaultQuota = defaultQuota;
    }

    /// <summary>
    /// Returns <see cref="QuotaCheckResult.Allowed"/> = false if the user
    /// would exceed their quota (total bytes or attachment count) after
    /// the incoming upload. Returns <see cref="QuotaCheckResult.Allowed"/>
    /// = true otherwise.
    /// </summary>
    public async Task<QuotaCheckResult> CheckAsync(
        Guid userId,
        long incomingBytes,
        CancellationToken ct)
    {
        var quotaRow = await _quotaReader.GetQuotaAsync(userId, ct);
        var (usedBytes, count) = await _usage.GetUsageAsync(userId, ct);

        var quotaBytes = quotaRow?.QuotaBytes ?? _defaultQuota.MaxTotalBytes;
        var countCap = quotaRow is null
            ? _defaultQuota.MaxAttachmentCount
            : Math.Min(_defaultQuota.MaxAttachmentCount, _defaultQuota.MaxAttachmentCount); // same — count cap is global, not per-user.

        // Total bytes gate.
        var projectedTotal = usedBytes + incomingBytes;
        var remaining = quotaBytes - usedBytes;
        if (projectedTotal > quotaBytes)
        {
            return QuotaCheckResult.ExceededByBytes(quotaBytes, usedBytes, count, incomingBytes, Math.Max(0, remaining));
        }

        // Attachment count gate (per-user cap from default).
        if (count + 1 > countCap)
        {
            return QuotaCheckResult.ExceededByCount(quotaBytes, usedBytes, count, incomingBytes);
        }

        return QuotaCheckResult.Pass(quotaBytes, usedBytes, count, incomingBytes);
    }
}