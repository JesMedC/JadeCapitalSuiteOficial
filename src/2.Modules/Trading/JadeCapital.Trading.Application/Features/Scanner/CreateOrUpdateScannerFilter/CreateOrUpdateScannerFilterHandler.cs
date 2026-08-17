using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Contracts.Scanner;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Trading.Domain.Scanner;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Scanner.CreateOrUpdateScannerFilter;

// POST/PUT /api/scanner/filters
public sealed record CreateOrUpdateScannerFilterCommand(
    Guid UserId,
    string Name,
    decimal? MinSpread,
    decimal? MaxSpread,
    decimal? MinVolume,
    decimal? MinRiskReward,
    byte VolatilityWindow,
    string? ActiveHours) : IRequest<Result<ScannerFilterDto>>;

public sealed class CreateOrUpdateScannerFilterHandler
    : IRequestHandler<CreateOrUpdateScannerFilterCommand, Result<ScannerFilterDto>>
{
    private readonly IScannerFilterRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public CreateOrUpdateScannerFilterHandler(IScannerFilterRepository repo, IUnitOfWork uow, IClock clock)
    { _repo = repo; _uow = uow; _clock = clock; }

    public async Task<Result<ScannerFilterDto>> Handle(
        CreateOrUpdateScannerFilterCommand req, CancellationToken ct)
    {
        var window = (VolatilityWindow)req.VolatilityWindow;

        var existing = await _repo.GetByUserAndNameAsync(req.UserId, req.Name, ct);
        Result<ScannerFilter> result;
        if (existing is null)
        {
            result = ScannerFilter.Create(
                req.UserId, req.Name, req.MinSpread, req.MaxSpread, req.MinVolume,
                req.MinRiskReward, window, req.ActiveHours, _clock);
            if (result.IsFailure) return Result.Failure<ScannerFilterDto>(result.Error);
            await _repo.AddAsync(result.Value, ct);
        }
        else
        {
            var updateResult = existing.Update(
                req.Name, req.MinSpread, req.MaxSpread, req.MinVolume,
                req.MinRiskReward, window, req.ActiveHours, _clock);
            if (updateResult.IsFailure) return Result.Failure<ScannerFilterDto>(updateResult.Error);
            await _repo.UpdateAsync(existing, ct);
        }

        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure) return Result.Failure<ScannerFilterDto>(saved.Error);
        var dto = ScannerMappingExtensions.ToDto(existing!);
        return Result.Success(dto);
    }
}
