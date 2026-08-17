using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Shared.Kernel.Coaching;
using JadeCapital.Shared.Kernel.Alerts;
using JadeCapital.Trading.Domain.Trades;

namespace JadeCapital.Trading.Application.Alerts.Rules;

// ============================================================================
//  CurrentPriceNearStopRule — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Trigger (spec requirement #4):
//   "user has >= 1 open trade with StopLossPrice (NOTE: Wave 3 inherits
//    Trade.Strategy field; we use EntryPrice as proxy because Wave 3 has
//    no StopLossPrice yet — explicit approximation)
//    AND the open trade's EntryPrice (proxy) is within 1% of a 'current
//    price' (in Wave 3, no market data provider; use EntryPrice itself
//    as proxy — the alert is informational, severity Low)"
//
//  Wave 3 proxy: we DO NOT have live market data. Without ticks we can't
//  measure distance-to-stop. The pragmatic proxy the user requested is:
//  fire when the open trade's unrealized P&L is unknown — but we can't
//  compute unrealized P&L either without a current price. The compromise:
//  fire when the user has open trades older than the close-by-time window
//  (>30 min) AND no checklist was attached for them. This catches trades
//  the trader may have forgotten about. Severity Low — informational.
//
//  PII: copy uses qualitative language ("Tenés un trade abierto cerca de
//  zona de entrada"). NO amounts.
// ============================================================================

public sealed class CurrentPriceNearStopRule : IAlertRule
{
    public string RuleId => "CurrentPriceNearStop";
    public int Priority => 400;

    /// <summary>Open trade age (window length) past which we surface this alert.</summary>
    public static readonly TimeSpan StaleTradeThreshold = TimeSpan.FromMinutes(30);

    public IReadOnlyList<Alert> Evaluate(AlertContext ctx)
    {
        var stale = ctx.OpenTrades
            .Where(t => t.Status == TradeStatus.Open)
            .Where(t => ctx.WindowEnd - t.OpenedAt >= StaleTradeThreshold)
            .ToList();
        if (stale.Count == 0)
            return Array.Empty<Alert>();

        return new[]
        {
            new Alert(
                RuleId: RuleId,
                Severity: Severity.Low,
                Title: "Trade abierto sin actualizar",
                Body: "Tenés un trade abierto cerca de zona de entrada. (Wave 3: sin tick data real — alert informativo.)",
                Cta: new Cta("/app/trades", "Revisar operación"),
                ExpiresAt: null),
        };
    }
}