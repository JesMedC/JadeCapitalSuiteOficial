namespace JadeCapital.Trading.Contracts.Imports;

/// <summary>
/// Wire shape returned by <c>GET /api/imports/{id}</c> and
/// <c>POST /api/imports/csv</c>. Mirrors the <c>trading.import_jobs</c>
/// table (16 columns from migration 0019_import_jobs.sql) projected into
/// a JSON-friendly record with strongly-typed enum encoding.
/// </summary>
public sealed record ImportJobDto(
    Guid Id,
    Guid UserId,
    Guid AccountId,
    byte Format,
    string FileName,
    long FileSizeBytes,
    string FileSha256,
    byte Status,
    int RowsTotal,
    int RowsImported,
    int RowsSkipped,
    int RowsErrored,
    string? ErrorMessage,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>
/// Response body for <c>POST /api/imports/csv</c> (202 Accepted). Returns
/// the job id immediately so the FE can poll <c>GET /api/imports/{id}</c>
/// for progress without waiting for the stream to finish.
/// </summary>
public sealed record BeginImportResponse(Guid ImportJobId);