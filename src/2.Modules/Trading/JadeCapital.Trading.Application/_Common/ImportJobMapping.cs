using JadeCapital.Shared.Kernel.Imports;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Contracts.Imports;
using JadeCapital.Trading.Domain.Imports;

namespace JadeCapital.Trading.Application._Common;

/// <summary>
/// Mapping helpers for the import-job bounded context (slice 5a.1).
/// Mirror the wire shape declared in
/// <c>JadeCapital.Trading.Contracts.Imports.ImportJobDto</c>.
/// </summary>
public static class ImportJobMappingExtensions
{
    public static ImportJobDto ToDto(this ImportJob j) => new(
        Id: j.Id,
        UserId: j.UserId,
        AccountId: j.AccountId,
        Format: (byte)j.Format,
        FileName: j.FileName,
        FileSizeBytes: j.FileSizeBytes,
        FileSha256: j.FileSha256,
        Status: (byte)j.Status,
        RowsTotal: j.RowsTotal,
        RowsImported: j.RowsImported,
        RowsSkipped: j.RowsSkipped,
        RowsErrored: j.RowsErrored,
        ErrorMessage: j.ErrorMessage,
        StartedAt: j.StartedAt,
        FinishedAt: j.FinishedAt);
}