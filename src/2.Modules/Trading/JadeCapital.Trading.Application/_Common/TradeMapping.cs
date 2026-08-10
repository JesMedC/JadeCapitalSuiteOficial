using JadeCapital.Trading.Domain.Trades;

namespace JadeCapital.Trading.Application._Common;

/// <summary>
/// Extension centralizada para mapear aggregate Trade -&gt; TradeDto.
/// Vive en Application porque es contrato de capa: handlers, queries y
/// (futuros) projections de EF lo consumen.
/// </summary>
internal static class TradeMapping
{
    public static TradeDto ToDto(this Trade trade) => new(
        trade.Id,
        trade.UserId,
        trade.Symbol.Value,
        trade.AssetClass,
        trade.Direction,
        trade.Status,
        trade.Volume.Amount,
        trade.Volume.Currency.Code,
        trade.EntryPrice.Amount,
        trade.EntryPrice.Currency.Code,
        trade.ExitPrice?.Amount,
        trade.ExitPrice?.Currency.Code,
        trade.PnL?.Amount,
        trade.PnL?.Currency.Code,
        trade.AccountCurrency,
        trade.Strategy,
        trade.Notes,
        trade.OpenedAt,
        trade.ClosedAt,
        trade.CreatedAt,
        trade.UpdatedAt ?? trade.CreatedAt);
}