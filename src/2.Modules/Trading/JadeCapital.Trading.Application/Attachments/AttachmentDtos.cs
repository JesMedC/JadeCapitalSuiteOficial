namespace JadeCapital.Trading.Application.Attachments;

/// <summary>
/// Pre-upload quota check result. Returned by
/// <c>AttachmentQuotaEnforcer.CheckAsync</c>; the upload handler maps
/// <see cref="Allowed"/> = false to HTTP 413 with the structured
/// <c>error.code = "attachment.quota_exceeded"</c>.
/// </summary>
public sealed record QuotaCheckResult(
    bool Allowed,
    long QuotaBytes,
    long UsedBytes,
    int AttachmentCount,
    long IncomingBytes,
    string? Reason)
{
    public static QuotaCheckResult Pass(long quotaBytes, long usedBytes, int count, long incoming) =>
        new(true, quotaBytes, usedBytes, count, incoming, Reason: null);

    public static QuotaCheckResult ExceededByBytes(long quotaBytes, long usedBytes, int count, long incoming, long remaining) =>
        new(false, quotaBytes, usedBytes, count, incoming,
            $"Total would be {usedBytes + incoming} bytes, exceeds quota of {quotaBytes} by {(usedBytes + incoming) - quotaBytes} bytes (remaining {remaining}).");

    public static QuotaCheckResult ExceededByCount(long quotaBytes, long usedBytes, int count, long incoming) =>
        new(false, quotaBytes, usedBytes, count, incoming,
            $"Attachment count would be {count + 1}, exceeds the cap of {int.MaxValue}.");
}

/// <summary>
/// Snapshot of the user's storage usage, returned by
/// <c>AttachmentUsageHandler</c>. Drives the FE banner + indicator
/// (slice 4d.2).
/// </summary>
public sealed record AttachmentUsageDto(
    long TotalBytes,
    int AttachmentCount,
    long QuotaBytes,
    int QuotaCount,
    decimal PercentFull)
{
    public static AttachmentUsageDto From(long usedBytes, int count, long quotaBytes, int quotaCount)
    {
        var pct = quotaBytes <= 0 ? 0m
            : Math.Round((decimal)usedBytes / quotaBytes * 100m, 2, MidpointRounding.AwayFromZero);
        return new AttachmentUsageDto(usedBytes, count, quotaBytes, quotaCount, pct);
    }
}