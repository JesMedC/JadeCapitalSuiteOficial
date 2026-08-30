using JadeCapital.Shared.Kernel.Imports;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.SoftDelete;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Trading.Domain.Imports;

/// <summary>
/// Aggregate root of an import job — one row per upload attempt, observable
/// status (Pending → InProgress → Completed | Failed | Cancelled), and
/// monotonic row counters. Slice 5a.1 of Wave 5.
///
/// <para>
/// Invariants enforced here (the streaming pipeline and the
/// <c>trading.import_jobs</c> CHECK constraints mirror these):
/// <list type="bullet">
///   <item><c>0 &lt; FileSizeBytes &lt;= 10 MiB</c> (10 * 1024 * 1024).</item>
///   <item><c>FileSha256.Length == 64</c> (hex).</item>
///   <item>Status transitions are monotonic — no back-transitions.</item>
///   <item>Row counters are non-negative; after Complete, they reflect the final state.</item>
///   <item><c>Failed</c> preserves <c>RowsImported</c> from the last committed batch
///         (partial-success semantics — committed batches stay committed).</item>
/// </list>
/// </para>
/// </summary>
public sealed class ImportJob : AggregateRoot<Guid>, ISoftDelete
{
    public const int MaxFileSizeBytes = 10 * 1024 * 1024;  // 10 MiB
    public const int Sha256HexLength = 64;
    public const int MaxErrorMessageLength = 2000;

    public Guid UserId { get; private set; }
    public Guid AccountId { get; private set; }
    public ImportFormat Format { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }
    public string FileSha256 { get; private set; } = string.Empty;
    public ImportJobStatus Status { get; private set; }
    public int RowsTotal { get; private set; }
    public int RowsImported { get; private set; }
    public int RowsSkipped { get; private set; }
    public int RowsErrored { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    // Wave 6, slice 6d.1 — ISoftDelete (append-only soft-delete with audit log).
    // The default value is false (newly created rows are live). EF Core materializes
    // these from the DB; private setters let EF hydrate without exposing mutators
    // to callers — the only public mutator is MarkDeleted.
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAtUtc { get; private set; }
    public Guid? DeletedByUserId { get; private set; }

    // EF Core.
    private ImportJob() { }

    private ImportJob(
        Guid id, Guid userId, Guid accountId, ImportFormat format,
        string fileName, long fileSizeBytes, string fileSha256,
        DateTimeOffset startedAt) : base(id)
    {
        UserId = userId;
        AccountId = accountId;
        Format = format;
        FileName = fileName;
        FileSizeBytes = fileSizeBytes;
        FileSha256 = fileSha256;
        Status = ImportJobStatus.Pending;
        StartedAt = startedAt;
    }

    /// <summary>
    /// Factory: creates a new Pending import job. Validates the
    /// pre-conditions (sha256 length, file size bounds, ids non-empty,
    /// file name non-blank). Does NOT check for sha256 idempotency —
    /// that's the responsibility of the application handler
    /// (<c>BeginImportHandler</c>) which queries the repository before
    /// calling this factory.
    /// </summary>
    public static Result<ImportJob> Begin(
        Guid userId, Guid accountId, ImportFormat format,
        string fileName, long fileSizeBytes, string fileSha256, IClock clock)
    {
        if (userId == Guid.Empty)
            return Result.Failure<ImportJob>(ImportJobErrors.Errors.UserIdRequired);

        if (accountId == Guid.Empty)
            return Result.Failure<ImportJob>(ImportJobErrors.Errors.AccountIdRequired);

        if (string.IsNullOrWhiteSpace(fileName))
            return Result.Failure<ImportJob>(ImportJobErrors.Errors.FileNameRequired);

        if (fileSizeBytes <= 0)
            return Result.Failure<ImportJob>(ImportJobErrors.Errors.FileSizeInvalid);

        if (fileSizeBytes > MaxFileSizeBytes)
            return Result.Failure<ImportJob>(ImportJobErrors.Errors.FileTooLarge);

        if (string.IsNullOrEmpty(fileSha256) || fileSha256.Length != Sha256HexLength)
            return Result.Failure<ImportJob>(ImportJobErrors.Errors.Sha256Invalid);

        var job = new ImportJob(
            id: Guid.NewGuid(),
            userId: userId,
            accountId: accountId,
            format: format,
            fileName: fileName.Trim(),
            fileSizeBytes: fileSizeBytes,
            fileSha256: fileSha256,
            startedAt: clock.UtcNow);

        return Result.Success(job);
    }

    /// <summary>
    /// Transitions Pending → InProgress. Idempotent: calling twice while
    /// already InProgress is a no-op (returns Success).
    /// </summary>
    public Result MarkInProgress()
    {
        if (Status == ImportJobStatus.Pending)
        {
            Status = ImportJobStatus.InProgress;
            Touch();
            return Result.Success();
        }

        if (Status == ImportJobStatus.InProgress)
            return Result.Success();

        return Result.Failure(ImportJobErrors.Errors.InvalidStatusTransition);
    }

    /// <summary>
    /// Records the cumulative row counters after a batch has committed.
    /// Called once per batch (default 50 rows). Counters are absolute
    /// (set, not added) — the streaming pipeline tracks running totals
    /// outside the aggregate and writes the latest snapshot.
    /// </summary>
    public Result RecordProgress(int rowsTotal, int rowsImported, int rowsSkipped, int rowsErrored)
    {
        if (rowsTotal < 0 || rowsImported < 0 || rowsSkipped < 0 || rowsErrored < 0)
            return Result.Failure(ImportJobErrors.Errors.RowsNonNegative);

        if (Status != ImportJobStatus.InProgress)
            return Result.Failure(ImportJobErrors.Errors.InvalidStatusTransition);

        RowsTotal = rowsTotal;
        RowsImported = rowsImported;
        RowsSkipped = rowsSkipped;
        RowsErrored = rowsErrored;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Transitions InProgress → Completed. Sets FinishedAt from the clock.
    /// </summary>
    public Result Complete(IClock clock)
    {
        if (Status != ImportJobStatus.InProgress)
            return Result.Failure(ImportJobErrors.Errors.InvalidStatusTransition);

        Status = ImportJobStatus.Completed;
        FinishedAt = clock.UtcNow;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Transitions InProgress → Failed. Preserves row counters (a partial
    /// success — committed batches stay committed). Captures the error
    /// message and FinishedAt for observability.
    /// </summary>
    public Result Fail(string errorMessage, IClock clock)
    {
        if (Status != ImportJobStatus.InProgress && Status != ImportJobStatus.Pending)
            return Result.Failure(ImportJobErrors.Errors.InvalidStatusTransition);

        if (!string.IsNullOrEmpty(errorMessage) && errorMessage.Length > MaxErrorMessageLength)
            return Result.Failure(ImportJobErrors.Errors.ErrorMessageTooLong);

        Status = ImportJobStatus.Failed;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage;
        FinishedAt = clock.UtcNow;
        Touch();
        return Result.Success();
    }

    // ============================================
    // Wave 6, slice 6d.1 — Soft-delete (ISoftDelete)
    // ============================================

    /// <summary>
    /// Marks the import job as soft-deleted (Wave 6, slice 6d.1).
    ///
    /// <para>
    /// Sets <see cref="IsDeleted"/> = true, <see cref="DeletedAtUtc"/> = clock.UtcNow,
    /// <see cref="DeletedByUserId"/> = <paramref name="userId"/>. The row is NOT
    /// removed from the DB — subsequent queries with the EF global filter
    /// (<c>!j.IsDeleted</c>) exclude it; queries with
    /// <c>IgnoreQueryFilters()</c> still see it.
    /// </para>
    ///
    /// <para>
    /// Idempotent: calling twice is a no-op (already-deleted guard). The
    /// <c>SoftDeleteHandler</c> in Identity.Application checks
    /// <see cref="IsDeleted"/> BEFORE calling this and returns 404 on the
    /// second attempt, so the idempotency here is defense-in-depth.
    /// </para>
    /// </summary>
    public Result MarkDeleted(Guid userId, IClock clock)
    {
        if (userId == Guid.Empty)
            return Result.Failure(ImportJobErrors.Errors.MarkDeletedUserIdRequired);

        if (IsDeleted)
            return Result.Failure(ImportJobErrors.Errors.AlreadyDeleted);

        IsDeleted = true;
        DeletedAtUtc = clock.UtcNow;
        DeletedByUserId = userId;
        Touch();
        return Result.Success();
    }
}