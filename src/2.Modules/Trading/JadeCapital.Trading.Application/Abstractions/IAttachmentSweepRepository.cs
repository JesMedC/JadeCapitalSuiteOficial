namespace JadeCapital.Trading.Application.Abstractions;

/// <summary>
/// Per-user snapshot of attachments eligible for the daily lifecycle sweep.
/// The BackgroundService loads these in pages (skip+take) and processes
/// each batch — soft-deletes the DB row, deletes the MinIO object, and
/// emits an audit row at the end.
///
/// Lives in Application.Abstractions so the BackgroundService (in
/// Infrastructure) can resolve it via DI without leaking MinIO types into
/// the application boundary.
/// </summary>
public sealed record ExpiredAttachmentSweepRow(
    Guid AttachmentId,
    Guid UserId,
    Guid ReviewId,
    string ObjectKey,
    long SizeBytes,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Repository for the daily sweep. Returns rows in pages so the service
/// can process them in bounded batches (avoid loading the entire expired
/// set into memory).
/// </summary>
public interface IAttachmentSweepRepository
{
    /// <summary>
    /// Returns a page of attachments where <c>expires_at &lt; asOf</c> AND
    /// <c>is_active = true</c>. Ordered by <c>expires_at ASC</c> so the
    /// oldest expired rows process first.
    /// </summary>
    Task<IReadOnlyList<ExpiredAttachmentSweepRow>> GetExpiredBatchAsync(
        DateTimeOffset asOf, int skip, int take, CancellationToken ct);

    /// <summary>
    /// Soft-deletes a batch of attachments in a single SQL UPDATE
    /// (<c>UPDATE trade_attachments SET is_active = false WHERE id = ANY(@ids)</c>).
    /// Idempotent — re-running on already-soft-deleted rows is a no-op.
    /// </summary>
    Task<int> SoftDeleteBatchAsync(IReadOnlyList<Guid> attachmentIds, CancellationToken ct);

    /// <summary>
    /// Inserts an audit row in <c>trading.attachments_quota_audit</c>.
    /// One row per user per sweep day.
    /// </summary>
    Task InsertAuditAsync(
        Guid userId,
        DateTimeOffset ranAt,
        int cleanedCount,
        long cleanedBytes,
        int remainingCount,
        long remainingBytes,
        string? skippedReason,
        string? errorMessage,
        CancellationToken ct);

    /// <summary>
    /// Returns the ground-truth aggregate for a user (sum + count of
    /// is_active=true AND status=uploaded rows). Used by the sweep to
    /// emit <c>remaining_bytes</c> + <c>remaining_count</c> in the audit
    /// row + to refresh the cached <c>identity.users.attachment_used_bytes</c>.
    /// </summary>
    Task<(long TotalBytes, int Count)> GetUserAggregateAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Returns the distinct user ids that have any uploaded attachments
    /// (active or expired). The sweep iterates these so each user gets
    /// an audit row even if they have nothing to clean today.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetActiveUserIdsAsync(CancellationToken ct);
}