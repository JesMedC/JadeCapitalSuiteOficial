using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Contracts.Scanner;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Trading.Domain.Scanner;
using JadeCapital.Trading.Domain.Trades;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Scanner.RunScanner;

// POST /api/scanner/run  body { filterId, limit? }
public sealed record RunScannerQuery(Guid FilterId, Guid UserId, int Limit = 20)
    : IRequest<Result<IReadOnlyList<ScanResultDto>>>;

public sealed class RunScannerHandler : IRequestHandler<RunScannerQuery, Result<IReadOnlyList<ScanResultDto>>>
{
    private readonly IScannerFilterRepository _filterRepo;
    private readonly IScannerDataSource _dataSource;
    private readonly ITradeRepository _trades;

    public RunScannerHandler(IScannerFilterRepository filterRepo, IScannerDataSource dataSource, ITradeRepository trades)
    { _filterRepo = filterRepo; _dataSource = dataSource; _trades = trades; }

    public async Task<Result<IReadOnlyList<ScanResultDto>>> Handle(
        RunScannerQuery req, CancellationToken ct)
    {
        var filter = await _filterRepo.GetByIdAsync(req.FilterId, ct);
        if (filter is null || filter.UserId != req.UserId)
            return Result.Failure<IReadOnlyList<ScanResultDto>>(TradingDomainErrors.Scanner.NotFound);

        var instruments = await _dataSource.GetInstrumentsAsync(ct);
        var closedTrades = await _trades.ListClosedByUserIdAsync(req.UserId, ct);

        var results = ScannerService.Run(instruments, closedTrades, filter, req.Limit);
        var dtos = results.Select(r => new ScanResultDto(
            r.Symbol, r.AssetClass, r.HistoricalRiskReward, r.TotalTrades, r.TotalPnl, r.MatchedCriteria
        )).ToList();
        return Result.Success<IReadOnlyList<ScanResultDto>>(dtos);
    }
}
