using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Contracts.Scanner;
using JadeCapital.Trading.Domain.Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Scanner.ListScannerFilters;

public sealed record ListScannerFiltersQuery(Guid UserId, bool ActiveOnly = false) : IRequest<Result<IReadOnlyList<ScannerFilterDto>>>;

public sealed class ListScannerFiltersHandler : IRequestHandler<ListScannerFiltersQuery, Result<IReadOnlyList<ScannerFilterDto>>>
{
    private readonly IScannerFilterRepository _repo;
    public ListScannerFiltersHandler(IScannerFilterRepository repo) { _repo = repo; }

    public async Task<Result<IReadOnlyList<ScannerFilterDto>>> Handle(
        ListScannerFiltersQuery req, CancellationToken ct)
    {
        var filters = await _repo.ListByUserAsync(req.UserId, req.ActiveOnly, ct);
        var dtos = filters.Select(ScannerMappingExtensions.ToDto).ToList();
        return Result.Success<IReadOnlyList<ScannerFilterDto>>(dtos);
    }
}
