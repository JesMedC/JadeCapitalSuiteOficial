using JadeCapital.Shared.Kernel.Imports;
using JadeCapital.Shared.Kernel.Repository;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Domain.Imports;

namespace JadeCapital.Trading.Application.Abstractions;

/// <summary>
/// Persistence abstraction for the <see cref="ImportJob"/> aggregate.
/// Cross-user isolation lives in the handlers (the <c>GetImportStatusHandler</c>
/// filters by <c>UserId</c> from the JWT claim; a row owned by another user
/// collapses to <c>NotFound</c> to avoid existence leaks).
/// </summary>
public interface IImportJobRepository : IRepository<ImportJob>
{
    /// <summary>
    /// SHA-256 idempotency lookup. Returns the most recent active job
    /// (<see cref="ImportJobStatus.Pending"/>, <see cref="ImportJobStatus.InProgress"/>,
    /// or <see cref="ImportJobStatus.Completed"/>) for the given
    /// <paramref name="userId"/> with the given <paramref name="sha256"/>.
    /// <see cref="ImportJobStatus.Failed"/> and
    /// <see cref="ImportJobStatus.Cancelled"/> jobs are excluded so the user
    /// can re-upload after a failure.
    /// </summary>
    Task<ImportJob?> FindActiveBySha256Async(Guid userId, string sha256, CancellationToken ct);
}

/// <summary>
/// Streamed-row dedupe: receives a batch of parsed rows and returns the
/// subset that does NOT yet exist in <c>trading.trades</c> for the user.
/// Used by <c>StreamImportService</c> in batches of 50 rows.
/// </summary>
public interface IImportRowDedupeService
{
    /// <summary>
    /// Returns a <see cref="DedupeBatchResult"/> describing the new (non-dupe)
    /// rows plus the count of rows that already existed in <c>trading.trades</c>.
    /// The dedupe key is <c>(user_id, account_id, ticket_id)</c> when the row
    /// carries a ticket id; otherwise
    /// <c>(user_id, account_id, opened_at, symbol, entry_price)</c>.
    /// </summary>
    Task<DedupeBatchResult> DedupeBatchAsync(
        Guid userId, Guid accountId, IReadOnlyList<ImportRow> batch, CancellationToken ct);
}

/// <summary>
/// Output of a single dedupe pass — the rows that are NEW (not in
/// <c>trading.trades</c>) plus the count of rows that already existed.
/// </summary>
public sealed record DedupeBatchResult(IReadOnlyList<ImportRow> NewRows, int SkippedCount);