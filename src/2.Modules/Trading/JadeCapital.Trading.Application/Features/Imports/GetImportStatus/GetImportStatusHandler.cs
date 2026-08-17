using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Contracts.Imports;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Trading.Domain.Imports;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Imports.GetImportStatus;

/// <summary>
/// GET /api/imports/{id} — single-job status fetch. Cross-user isolation
/// is enforced: a job owned by another user returns NotFound (NOT Forbidden)
/// to avoid leaking existence.
/// </summary>
public sealed record GetImportStatusQuery(Guid ImportJobId, Guid UserId)
    : IRequest<Result<ImportJobDto>>;

public sealed class GetImportStatusHandler : IRequestHandler<GetImportStatusQuery, Result<ImportJobDto>>
{
    private readonly IImportJobRepository _jobs;

    public GetImportStatusHandler(IImportJobRepository jobs) { _jobs = jobs; }

    public async Task<Result<ImportJobDto>> Handle(GetImportStatusQuery req, CancellationToken ct)
    {
        var job = await _jobs.GetByIdAsync(req.ImportJobId, ct);

        // Cross-user isolation: collapse foreign-owned jobs to NotFound.
        if (job is null || job.UserId != req.UserId)
            return Result.Failure<ImportJobDto>(ImportJobErrors.Errors.NotFound);

        return Result.Success(ImportJobMappingExtensions.ToDto(job));
    }
}