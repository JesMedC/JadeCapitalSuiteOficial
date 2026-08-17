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
    private readonly IInstrumentRepository _instruments;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<StreamImportService> _logger;

    public StreamImportService(
        IImportJobRepository jobs, IImportRowDedupeService dedupe, ITradeRepository trades,
        IInstrumentRepository instruments, IUnitOfWork uow, IClock clock,
        ILogger<StreamImportService> logger)
    {
        _jobs = jobs;
        _dedupe = dedupe;
        _trades = trades;
        _instruments = instruments;
        _uow = uow;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<ImportJob>> ExecuteAsync(
        ImportJob job, Stream body, IImportRowParser parser, CancellationToken ct)
    {
        // Single-parser overload — kept for backward compat with 5a.1 callers.
        return await ExecuteInternalAsync(job, body, parser, fileName: null, ct);
    }

    /// <summary>
    /// Auto-detection overload (slice 5a.2). Picks the first parser whose
    /// <see cref="IImportRowParser.CanParse"/> returns &gt;= 0.8. Returns
    /// <c>Result.Failure("import.format_unrecognized")</c> if no parser
    /// matches.
    /// </summary>
    public async Task<Result<ImportJob>> ExecuteAsync(
        ImportJob job, Stream body, IEnumerable<IImportRowParser> parsers,
        string fileName, CancellationToken ct)
    {
        // Buffer the body into a seekable MemoryStream. Multipart bodies are
        // already buffered in memory by ASP.NET (max 10 MiB per
        // ImportJob.MaxFileSizeBytes), so this copy is cheap and lets the
        // parsers' CanParse sniff the same bytes that ParseAsync will read.
        MemoryStream? owned = null;
        var seekable = body as MemoryStream;
        if (seekable is null)
        {
            owned = new MemoryStream();
            await body.CopyToAsync(owned, ct);
            seekable = owned;
            seekable.Position = 0;
        }

        try
        {
            // Snapshot the first KiB for sniffing. The seekable stream's
            // position is reset to 0 before ParseAsync runs so the parser
            // sees the full body (header + data) from the start.
            using var head = BufferHeadForSniffing(seekable);
            seekable.Position = 0;

            var parser = head is null
                ? null
                : ImportParserDispatcher.SelectParser(parsers, fileName ?? string.Empty, head);

            if (parser is null)
            {
                job.Fail("Unknown file format — no parser matched (import.format_unrecognized).", _clock);
                await _jobs.UpdateAsync(job, ct);
                return Result.Failure<ImportJob>(Error.Validation(
                    "import.format_unrecognized",
                    "No parser matched the file format. Expected CSV, MT4, or MT5."));
            }

            return await ExecuteInternalAsync(job, seekable, parser, fileName, ct);
        }
        finally
        {
            owned?.Dispose();
        }
    }

    private async Task<Result<ImportJob>> ExecuteInternalAsync(
        ImportJob job, Stream body, IImportRowParser parser, string? fileName, CancellationToken ct)
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

        // Persist each new row as a Trade aggregate. We resolve InstrumentId
        // per-row via IInstrumentRepository.FindBySymbolAsync; symbols that
        // are not registered count as errored rows (no phantom instruments).
        var errored = 0;
        foreach (var row in dedupe.NewRows)
        {
            var instrument = await _instruments.FindBySymbolAsync(row.Symbol, ct);
            if (instrument is null)
            {
                errored++;
                continue;
            }

            var tradeResult = row.ImportFromRow(job.UserId, job.AccountId, instrument.Id, _clock);
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

    /// <summary>
    /// Snapshots the first KiB of <paramref name="body"/> into a seekable
    /// <see cref="MemoryStream"/> so each <see cref="IImportRowParser.CanParse"/>
    /// can sniff the head without consuming the body. Returns <c>null</c> if
    /// the body stream is not readable.
    /// </summary>
    private static MemoryStream? BufferHeadForSniffing(Stream body)
    {
        if (body is null || !body.CanRead) return null;
        const int headSize = 1024;
        var head = new MemoryStream(headSize);
        var buffer = new byte[headSize];
        var read = body.Read(buffer, 0, headSize);
        head.Write(buffer, 0, read);
        head.Position = 0;
        return head;
    }
}