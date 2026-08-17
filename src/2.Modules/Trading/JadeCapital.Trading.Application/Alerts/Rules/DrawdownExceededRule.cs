using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Shared.Kernel.Coaching;
using JadeCapital.Shared.Kernel.Alerts;
using JadeCapital.Trading.Domain.Trades;

namespace JadeCapital.Trading.Application.Alerts.Rules;

// ============================================================================
//  DrawdownExceededRule — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Trigger (spec requirement #4):
//   "user's current drawdown (peak-to-trough on closed trades' account
//    equity) > Y% (default 5%)"
//  → emit a DrawdownExceeded alert with severity High.
//
//  Approximation (Wave 3 has no real equity curve):
//   - Build a running P&L series from the closed trades (OrderedBy ClosedAt).
//   - Compute peak equity (max running total) and trough equity after the
//     peak (min running total). The drawdown = (peak - trough) / peak.
//   - If peak <= 0 (user has not yet accumulated profits), drawdown is 0.
//   - The 5% threshold is configurable (5 in Wave 3). Any drawdown
//     strictly greater than 5% triggers.
//
//  PII: body uses qualitative copy ("DD actual > 5%. Revisa tu riesgo por
//  trade y considerá reducir tamaño.") — NO amounts, NO specific USD
//  figures (per spec requirement "PII-safe copy").
// ============================================================================

public sealed class DrawdownExceededRule : IAlertRule
{
    public string RuleId => "DrawdownExceeded";
    public int Priority => 200;

    /// <summary>5% drawdown threshold per the spec.</summary>
    public const decimal DrawdownThresholdPercent = 5m;

    private static readonly decimal ThresholdFraction = DrawdownThresholdPercent / 100m;

    public IReadOnlyList<Alert> Evaluate(AlertContext ctx)
    {
        // Need at least 1 closed trade with a P&L to compute drawdown.
        var withPnl = ctx.ClosedTrades
            .Where(t => t.Status == TradeStatus.Closed
                        && t.ClosedAt is not null
                        && t.PnL is not null)
            .OrderBy(t => t.ClosedAt!.Value)
            .ToList();
        if (withPnl.Count < 2)
            return Array.Empty<Alert>();

        // Build the running P&L curve.
        decimal running = 0m;
        decimal peak = 0m;
        decimal trough = 0m;
        foreach (var trade in withPnl)
        {
            running += trade.PnL!.Amount;
            if (running > peak)
            {
                peak = running;
                trough = running; // reset trough after new peak
            }
            else if (running < trough)
            {
                trough = running;
            }
        }

        if (peak <= 0m)
            return Array.Empty<Alert>(); // no profits accumulated → no drawdown to alert on

        var drawdownFraction = (peak - trough) / peak;
        if (drawdownFraction <= ThresholdFraction)
            return Array.Empty<Alert>();

        return new[]
        {
            new Alert(
                RuleId: RuleId,
                Severity: Severity.High,
                Title: "Drawdown elevado",
                Body: $"DD actual > {DrawdownThresholdPercent:0.#}%. Revisa tu riesgo por trade y considerá reducir tamaño.",
                Cta: new Cta("/app/trades", "Revisar operaciones"),
                ExpiresAt: null),
        };
    }
}