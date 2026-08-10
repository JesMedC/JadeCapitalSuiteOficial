using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Shared.Kernel.Validation;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.Trades.CloseTrade;

/// <summary>
/// Cierra un trade abierto. Valida ownership (no leak existencia via NotFound
/// unificado para missing + foreign ownership) y calcula PnL via el aggregate.
/// </summary>
public sealed class CloseTradeHandler : IRequestHandler<CloseTradeCommand, Result<TradeDto>>
{
    private readonly ITradeRepository _trades;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<CloseTradeHandler> _logger;

    public CloseTradeHandler(
        ITradeRepository trades,
        IUnitOfWork uow,
        IClock clock,
        ILogger<CloseTradeHandler> logger)
    {
        _trades = trades;
        _uow = uow;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<TradeDto>> Handle(CloseTradeCommand req, CancellationToken ct)
    {
        var trade = await _trades.FindByIdAsync(req.TradeId, ct);
        if (trade is null || trade.UserId != req.UserId)
        {
            // Unificamos missing + foreign ownership en NotFound para no leak existencia.
            return Result.Failure<TradeDto>(TradingApplicationErrors.Trades.NotFound);
        }

        var currencyResult = Currency.Create(req.ExitPriceCurrency);
        if (currencyResult.IsFailure)
            return Result.Failure<TradeDto>(currencyResult.Error);

        var exitPriceResult = Money.Create(req.ExitPrice, currencyResult.Value);
        if (exitPriceResult.IsFailure)
            return Result.Failure<TradeDto>(exitPriceResult.Error);

        var now = _clock.UtcNow;
        var closeResult = trade.Close(exitPriceResult.Value, now, _clock);
        if (closeResult.IsFailure)
            return Result.Failure<TradeDto>(closeResult.Error);

        var saved = await _uow.SaveChangesAsync(ct);
        DomainGuard.EnsureSuccess(saved);

        _logger.LogInformation("Trade {TradeId} closed for user {UserId}.", trade.Id, trade.UserId);

        return Result.Success(trade.ToDto());
    }
}