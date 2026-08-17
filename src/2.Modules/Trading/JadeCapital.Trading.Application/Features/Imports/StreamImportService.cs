using JadeCapital.Shared.Kernel.Imports;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Imports;
using JadeCapital.Trading.Domain.Trades;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.Imports;

/// <summary>
/// Streaming pipeline for an <see cref="ImportJob"/>. Reads rows from
/// <see cref="IImportRowParser.ParseAsync"/> in batches of 50, dedupes
/// against <c>trading.trades</c>, persists each batch in a single
/// transaction, and updates the job's counters. On batch failure the
/// job transitions to <see cref="ImportJobStatus.Failed"/> with the
/// counters from the last committed batch preserved (partial success).
///
/// <para>
/// The service is intentionally side-effect-only: it does NOT fire
/// domain events or send notifications — that's the responsibility of
/// an eventual outbox/handler that's added in a later slice.
/// </para>
/// </summary>
public sealed class StreamImportService
{
    /// <summary>50 rows per transactional commit — declared on the aggregate for symmetry.</summary>
    public const int BatchSize = 50;

    private readonly IImportJobRepository _jobs;
    private readonly IImportRowDedupeService _dedupe;
    private readonly ITradeRepository _trades;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<StreamImportService> _logger;

    public StreamImportService(
        IImportJobRepository jobs, IImportRowDedupeService dedupe, ITradeRepository trades,
        IUnitOfWork uow, IClock clock, ILogger<StreamImportService> logger)
    {
        _jobs = jobs;
        _dedupe = dedupe;
        _trades = trades;
        _uow = uow;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<ImportJob>> ExecuteAsync(
        ImportJob job, Stream body, IImportRowParser parser, CancellationToken ct)
    {
        // 1) Pending → InProgress (visible immediately to a polling client).
        var inProgressResult = job.MarkInProgress();
        if (inProgressResult.IsFailure)
            return Result.Failure<ImportJob>(inProgressResult.Error);
        await _jobs.UpdateAsync(job, ct);

        // 2) Stream rows in batches. The streaming dedupe is wired so the
        //    caller can pull rows lazily; we accumulate into a List<ImportRow>
        //    per batch and hand it off to PersistBatchAsync.
        var batch = new List<ImportRow>(BatchSize);
        var totalRows = 0;
        var imported = 0;
        var skipped = 0;

        try
        {
            await foreach (var row in parser.ParseAsync(body, ct))
            {
                batch.Add(row);
                totalRows++;

                if (batch.Count >= BatchSize)
                {
                    var (imp, skp) = await PersistBatchAsync(job, batch, ct);
                    imported += imp;
                    skipped += skp;
                    batch.Clear();
                }
            }

            // Tail batch.
            if (batch.Count > 0)
            {
                var (imp, skp) = await PersistBatchAsync(job, batch, ct);
                imported += imp;
                skipped += skp;
            }

            // 3) Complete. Job → Completed.
            var completeResult = job.Complete(_clock);
            if (completeResult.IsFailure)
                _logger.LogWarning("Complete transition failed: {Error}", completeResult.Error);

            await _jobs.UpdateAsync(job, ct);
            return Result.Success(job);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected mid-stream: leave job as InProgress; a future
            // re-poll or cleanup task can mark it Failed.
            await _jobs.UpdateAsync(job, ct);
            throw;
        }
        catch (Exception ex)
        {
            // 4) Batch failure → job → Failed. Committed batches stay committed.
            var failResult = job.Fail(ex.Message, _clock);
            if (failResult.IsFailure)
                _logger.LogWarning("Fail transition error: {Error}", failResult.Error);
            _logger.LogError(ex, "Import job {JobId} failed at row {TotalRows}", job.Id, totalRows);
            await _jobs.UpdateAsync(job, ct);
        }

        // All paths (try success / catch Failure) end here. OperationCanceledException
        // rethrows above. Caller inspects job.Status (Completed / Failed).
        return Result.Success(job);
    }

    /// <summary>
    /// Persists one batch. Returns (imported, skipped) — errored rows are
    /// counted as errored by the spec but for the current implementation the
    /// parser pre-filters invalid rows, so we only emit the two counters here.
    /// </summary>
    private async Task<(int imported, int skipped)> PersistBatchAsync(
        ImportJob job, List<ImportRow> batch, CancellationToken ct)
    {
        // Dedupe — returns (newRows, skippedCount). The dedupe service owns
        // the knowledge of how many rows already exist in trading.trades.
        var dedupe = await _dedupe.DedupeBatchAsync(job.UserId, job.AccountId, batch, ct);

        if (dedupe.NewRows.Count == 0)
        {
            // Update counters even when 0 new rows — the spec calls for
            // visible progress tracking.
            var recResult = job.RecordProgress(
                rowsTotal: job.RowsTotal + batch.Count,
                rowsImported: job.RowsImported,
                rowsSkipped: job.RowsSkipped + dedupe.SkippedCount,
                rowsErrored: job.RowsErrored);
            if (recResult.IsFailure) _logger.LogWarning("RecordProgress: {Error}", recResult.Error);
            await _jobs.UpdateAsync(job, ct);
            return (0, dedupe.SkippedCount);
        }

        // Persist each new row as a Trade aggregate.
        var errored = 0;
        foreach (var row in dedupe.NewRows)
        {
            var tradeResult = row.ImportFromRow(job.UserId, job.AccountId, _clock);
            if (tradeResult.IsSuccess)
            {
                await _trades.AddAsync(tradeResult.Value, ct);
            }
            else
            {
                errored++;
            }
        }

        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
            throw new InvalidOperationException($"Batch SaveChanges failed: {saved.Error.Message}");

        var imported = dedupe.NewRows.Count - errored;
        var recordResult = job.RecordProgress(
            rowsTotal: job.RowsTotal + batch.Count,
            rowsImported: job.RowsImported + imported,
            rowsSkipped: job.RowsSkipped + dedupe.SkippedCount,
            rowsErrored: job.RowsErrored + errored);
        if (recordResult.IsFailure) _logger.LogWarning("RecordProgress: {Error}", recordResult.Error);
        await _jobs.UpdateAsync(job, ct);

        return (imported, dedupe.SkippedCount);
    }

    private static async IAsyncEnumerable<ImportRow> ToAsync(
        IEnumerable<ImportRow> rows,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var r in rows)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return r;
        }
    }
}