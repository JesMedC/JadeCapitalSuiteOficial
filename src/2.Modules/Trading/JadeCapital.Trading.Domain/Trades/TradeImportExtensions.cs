using JadeCapital.Shared.Kernel.Imports;
using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.ValueObjects;

namespace JadeCapital.Trading.Domain.Trades;

/// <summary>
/// Mapping helpers that adapt the cross-module <see cref="ImportRow"/>
/// (Shared.Kernel/Imports) into a <see cref="Trade"/> aggregate.
///
/// <para>
/// The caller is responsible for resolving the InstrumentId for the row's
/// Symbol — the streaming pipeline does that lookup against
/// <c>trading.instruments</c> via <see cref="JadeCapital.Trading.Application.Abstractions.IInstrumentRepository"/>.
/// If no instrument is registered for the symbol, the row is treated as
/// an "errored" row by the pipeline (counted in <c>RowsErrored</c>) — the
/// domain does NOT silently create phantom instruments.
/// </para>
/// </summary>
public static class TradeImportExtensions
{
    /// <summary>
    /// Builds a <see cref="Trade"/> aggregate from a parsed row. The Trade
    /// factory validates volume/price/currency invariants and returns the
    /// same error codes a manual OpenTrade would — the streaming pipeline
    /// uses these failures to bump <c>RowsErrored</c>.
    /// </summary>
    public static Result<Trade> ImportFromRow(
        this ImportRow row, Guid userId, Guid accountId, Guid instrumentId, IClock clock)
    {
        // Symbol — required, may need to strip '/' for some brokers (EURUSD vs EUR/USD).
        var symbolResult = Symbol.Create(NormalizeSymbol(row.Symbol));
        if (symbolResult.IsFailure)
            return Result.Failure<Trade>(symbolResult.Error);

        var volumeResult = Money.Create(row.Volume, Currency.FromTrusted(row.VolumeCurrency));
        if (volumeResult.IsFailure)
            return Result.Failure<Trade>(volumeResult.Error);

        var entryResult = Money.Create(row.EntryPrice, Currency.FromTrusted(row.PnlCurrency));
        if (entryResult.IsFailure)
            return Result.Failure<Trade>(entryResult.Error);

        // Asset class — best-effort inference from the symbol shape.
        var assetClass = symbolResult.Value.DetectAssetClass();

        // Direction — pass through.
        var direction = row.Direction == ImportDirection.Long
            ? TradeDirection.Long
            : TradeDirection.Short;

// Open the trade. The Trade factory will validate currency match
            // (EntryPrice.Currency must equal the symbol's quotable).
            var tradeResult = Trade.Open(
                id: Guid.NewGuid(),
                accountId: accountId,
                // Bounded-correction fix (review f757b965 — R3-INSTRUMENT-FRESH-GUID-FK):
                // the streaming pipeline now resolves instrumentId via
                // IInstrumentRepository.FindBySymbolAsync and passes the
                // resolved Guid here. Missing instruments are treated as
                // errored rows by the caller, not silently materialized.
                instrumentId: instrumentId,
                userId: userId,
                symbol: symbolResult.Value,
                assetClass: assetClass,
                direction: direction,
                volume: volumeResult.Value,
                entryPrice: entryResult.Value,
                accountCurrency: row.PnlCurrency,
                strategy: null,
                notes: row.Notes,
                openedAt: row.OpenedAt);

        if (tradeResult.IsFailure)
            return Result.Failure<Trade>(tradeResult.Error);

        var trade = tradeResult.Value;

        // Close the trade if the row carries an exit price + closed timestamp.
        if (row.Status == ImportRowStatus.Closed
            && row.ExitPrice.HasValue
            && row.ClosedAt.HasValue)
        {
            var exitResult = Money.Create(row.ExitPrice.Value, Currency.FromTrusted(row.PnlCurrency));
            if (exitResult.IsFailure)
                return Result.Failure<Trade>(exitResult.Error);

            var closeResult = trade.Close(exitResult.Value, row.ClosedAt.Value, clock);
            if (closeResult.IsFailure)
                return Result.Failure<Trade>(closeResult.Error);
        }

        return Result.Success(trade);
    }

    /// <summary>
    /// Normalizes a symbol string by stripping the '/' separator (some
    /// brokers export "EURUSD", others "EUR/USD") and upper-casing.
    /// <see cref="Symbol.Create"/> already upper-cases + trims, so the
    /// only transform here is the slash removal.
    /// </summary>
    private static string NormalizeSymbol(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        return raw.Replace("/", "", StringComparison.Ordinal).Trim();
    }
}