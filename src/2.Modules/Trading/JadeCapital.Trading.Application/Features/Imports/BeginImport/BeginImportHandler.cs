using JadeCapital.Shared.Kernel.Imports;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Contracts.Imports;
using JadeCapital.Trading.Domain.Imports;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Imports.BeginImport;

/// <summary>
/// POST /api/imports/csv — kicks off a new import. Validates the file
/// header, computes SHA-256, checks for an existing active job with the
/// same hash (idempotency), and persists a Pending <see cref="ImportJob"/>.
/// </summary>
public sealed record BeginImportCommand(
    Guid UserId,
    Guid AccountId,
    string FileName,
    long FileSizeBytes,
    string FileSha256,
    ImportFormat DetectedFormat) : IRequest<Result<ImportJobDto>>;

public sealed class BeginImportHandler : IRequestHandler<BeginImportCommand, Result<ImportJobDto>>
{
    private readonly IImportJobRepository _jobs;
    private readonly IAccountRepository _accounts;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public BeginImportHandler(
        IImportJobRepository jobs, IAccountRepository accounts, IUnitOfWork uow, IClock clock)
    {
        _jobs = jobs;
        _accounts = accounts;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<ImportJobDto>> Handle(BeginImportCommand req, CancellationToken ct)
    {
        // 1) Cross-file idempotency check — re-uploading the same file in the
        //    same session returns 409 with the existing job id.
        var existing = await _jobs.FindActiveBySha256Async(req.UserId, req.FileSha256, ct);
        if (existing is not null)
        {
            return Result.Failure<ImportJobDto>(Error.Conflict(
                "import_job.duplicate",
                $"An active import job with the same SHA-256 already exists: {existing.Id}"));
        }

        // 2) Aggregate factory validates file size, sha256 length, ids non-empty.
        //    Done BEFORE account lookup so a malformed payload doesn't trigger a DB round-trip.
        var jobResult = ImportJob.Begin(
            req.UserId, req.AccountId, req.DetectedFormat,
            req.FileName, req.FileSizeBytes, req.FileSha256, _clock);

        if (jobResult.IsFailure)
            return Result.Failure<ImportJobDto>(jobResult.Error);

        // 3) Account ownership check — the account MUST belong to the user
        //    (returns 404 instead of 403 to avoid leaking existence).
        var account = await _accounts.FindByIdAsync(req.AccountId, ct);
        if (account is null || account.UserId != req.UserId)
        {
            return Result.Failure<ImportJobDto>(Error.NotFound(
                "import_job.account_not_found", "Account not found in user's namespace."));
        }

        // 4) Persist. The actual streaming is kicked off by the endpoint layer
        //    after this returns — the handler is responsible only for the
        //    durable Pending row.
        await _jobs.AddAsync(jobResult.Value, ct);
        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
            return Result.Failure<ImportJobDto>(saved.Error);

        return Result.Success(ImportJobMappingExtensions.ToDto(jobResult.Value));
    }
}