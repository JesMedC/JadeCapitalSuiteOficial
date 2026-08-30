using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.MfeMae;
using MediatR;

namespace JadeCapital.Trading.Application.Features.MfeMae.GetTradeMfeMae;

// ============================================================================
//  GetTradeMfeMaeQuery — slice 2c (Trader Journal Core / MFE/MAE Charts).
//
//  GET /api/trades/{tradeId}/mfe-mae
//
//  Loads the requested trade (with cross-user ownership check) plus the
//  user's full closed-trade history to compute the 8-bucket aggregate
//  histograms for direction × {winners, losers} × {MFE, MAE}.
//
//  Cross-user scope is enforced at the repository layer
//  (FindByIdAsync + UserId ownership); missing trades and foreign-owned
//  trades both map to NotFound to avoid leaking trade existence.
//
//  The aggregate walks the user's history once. For Wave 2 the user
//  history is loaded via ListClosedByUserIdAsync which returns ALL
//  closed trades for that user (no pagination). Wave 3 will introduce
//  cursor pagination once any user crosses ~10k trades.
// ============================================================================

public sealed record GetTradeMfeMaeQuery(
    Guid TradeId,
    Guid UserId) : IRequest<Result<TradeMfeMaeDto>>;

public sealed class GetTradeMfeMaeHandler
    : IRequestHandler<GetTradeMfeMaeQuery, Result<TradeMfeMaeDto>>
{
    private readonly ITradeRepository _trades;

    public GetTradeMfeMaeHandler(ITradeRepository trades)
    {
        _trades = trades;
    }

    public async Task<Result<TradeMfeMaeDto>> Handle(
        GetTradeMfeMaeQuery req,
        CancellationToken ct)
    {
        // Ownership-checked lookup. Missing OR foreign-owned → NotFound (same
        // code as other endpoints; ProblemFromResult maps to 404).
        var trade = await _trades.FindByIdAsync(req.TradeId, ct);
        if (trade is null || trade.UserId != req.UserId)
            return Result.Failure<TradeMfeMaeDto>(TradingApplicationErrors.Trades.NotFound);

        // The aggregate only considers closed trades with computed
        // MFE/MAE. The repo's ListClosedByUserIdAsync returns every closed
        // trade (cancelled excluded). MfeMaeCalculator handles the case
        // where MfeAmount/MaeAmount are null on the requested trade (open).
        var history = await _trades.ListClosedByUserIdAsync(req.UserId, ct);

        var aggregate = MfeMaeAggregator.Aggregate(history);
        var dto = MfeMaeMapping.ToDto(trade, aggregate);

        return Result.Success(dto);
    }
}