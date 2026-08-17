using JadeCapital.Shared.Kernel.Coaching;
using JadeCapital.Shared.Kernel.MarketData;
using JadeCapital.Shared.Kernel.Alerts;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Trades;

namespace JadeCapital.Trading.Application.Alerts.Rules;

// ============================================================================
//  CurrentPriceNearStopRule — slice 4c (Realtime) rewrite.
//
//  Wave 3b used EntryPrice as a proxy for the current price (no market data
//  available; rule fired on a stale-trade heuristic). Slice 4c rewires the
//  rule to consume IQuoteProvider — the current price is now the real mid
//  (Bid+Ask)/2, and the rule fires when |currentPrice − EntryPrice| /
//  EntryPrice < 1%.
//
//  Trade.StopLossPrice does not exist in the domain (deferred to a future
//  slice), so EntryPrice remains the reference value the rule measures
//  distance to. The semantics change: the rule now answers "is the live
//  mid within 1% of where the user entered?" rather than "has this open
//  trade gone stale?". The IAlertRule interface is unchanged.
//
//  Failure isolation:
//   - provider returns null for a symbol → silent skip (no alert for that trade)
//   - provider throws                  → silent skip (no exception escapes)
//
//  PII: copy uses qualitative language + the live mid price. No trade
//  amounts, no entry prices, no PnL. The mid is shown so the trader can
//  eyeball the price without leaving the alert card.
// ============================================================================

public sealed class CurrentPriceNearStopRule : IAlertRule
{
    public string RuleId => "CurrentPriceNearStop";
    public int Priority => 400;

    /// <summary>
    /// Distance threshold (as a fraction of EntryPrice) within which the
    /// rule fires. Spec calls for 1% — the same threshold Wave 3b used
    /// for its stale-trade heuristic.
    /// </summary>
    public const decimal DistanceThreshold = 0.01m;

    private readonly IQuoteProvider _quotes;

    public CurrentPriceNearStopRule(IQuoteProvider quotes)
    {
        _quotes = quotes;
    }

    public IReadOnlyList<Alert> Evaluate(AlertContext ctx)
    {
        if (ctx.OpenTrades.Count == 0) return Array.Empty<Alert>();

        var alerts = new List<Alert>();
        foreach (var trade in ctx.OpenTrades.Where(t => t.Status == TradeStatus.Open))
        {
            // The Trade.Symbol value stores canonical "EUR/USD"; the
            // InMemoryQuoteProvider indexes on "EURUSD" (no slash). Strip
            // separators before lookup so future providers with the same
            // indexing scheme work without changes here.
            var symbol = NormalizeSymbol(trade.Symbol.Value);
            Quote? quote;
            try
            {
                quote = _quotes.GetQuoteAsync(symbol, default).GetAwaiter().GetResult();
            }
            catch
            {
                continue;
            }
            if (quote is null) continue;

            var mid = (quote.Bid + quote.Ask) / 2m;
            var distance = Math.Abs(mid - trade.EntryPrice.Amount) / trade.EntryPrice.Amount;
            if (distance >= DistanceThreshold) continue;

            alerts.Add(new Alert(
                RuleId: RuleId,
                Severity: Severity.Low,
                Title: "Precio actual cerca de tu entrada",
                Body: $"{symbol} cotiza a {mid} — el precio actual está dentro del 1% de tu precio de entrada.",
                Cta: new Cta("/app/trades", "Revisar operación"),
                ExpiresAt: null));
        }

        return alerts;
    }

    private static string NormalizeSymbol(string value) =>
        value.Replace("/", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
}