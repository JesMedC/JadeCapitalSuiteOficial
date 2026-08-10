using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Trades.GetTradeById;

/// <summary>
/// Trae un trade por id validando ownership. Missing + foreign ownership se
/// unifican en NotFound para no leak existencia.
/// </summary>
public sealed class GetTradeByIdHandler : IRequestHandler<GetTradeByIdQuery, Result<TradeDto>>
{
    private readonly ITradeRepository _trades;

    public GetTradeByIdHandler(ITradeRepository trades)
    {
        _trades = trades;
    }

    public async Task<Result<TradeDto>> Handle(GetTradeByIdQuery req, CancellationToken ct)
    {
        var trade = await _trades.FindByIdAsync(req.TradeId, ct);
        if (trade is null || trade.UserId != req.UserId)
            return Result.Failure<TradeDto>(TradingApplicationErrors.Trades.NotFound);

        return Result.Success(trade.ToDto());
    }
}