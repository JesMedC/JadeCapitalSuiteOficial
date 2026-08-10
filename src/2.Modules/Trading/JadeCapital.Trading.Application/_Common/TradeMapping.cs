using JadeCapital.Trading.Domain.Instruments;
using JadeCapital.Trading.Domain.Trades;

namespace JadeCapital.Trading.Application._Common;

/// <summary>
/// Extension centralizada para mapear aggregate Trade -&gt; TradeDto.
/// Vive en Application porque es contrato de capa: handlers, queries y
/// (futuros) projections de EF lo consumen.
///
/// AccountName / Instrument son opcionales: el handler que los conozca
/// (e.g. OpenTrade despues de cargar el Instrument) los pasa; los demas
/// pasan null y la proyeccion queda pendiente para Sprint 1.5B
/// (queries que hagan JOIN con accounts/instruments).
/// </summary>
internal static class TradeMapping
{
    public static TradeDto ToDto(
        this Trade trade,
        string? accountName = null,
        InstrumentSummaryDto? instrument = null)
        => new(
            trade.Id,
            trade.UserId,
            trade.AccountId,
            accountName ?? string.Empty,
            trade.InstrumentId,
            instrument ?? InstrumentToDto(null),
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

    private static InstrumentSummaryDto InstrumentToDto(Instrument? instrument)
        => instrument is null
            ? new InstrumentSummaryDto(string.Empty, default, 0m, 0, 0m, 0m)
            : new InstrumentSummaryDto(
                instrument.Symbol.Value,
                instrument.AssetClasses,
                instrument.ContractSize,
                instrument.DecimalPlaces,
                instrument.PipValue,
                instrument.PayoutPercent);
}
