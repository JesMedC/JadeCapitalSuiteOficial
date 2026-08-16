using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Shared.Kernel.Coaching;
using JadeCapital.Shared.Kernel.Alerts;
using JadeCapital.Trading.Domain.Trades;

namespace JadeCapital.Trading.Application.Alerts.Rules;

// ============================================================================
//  RRAverageBelowRule — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Trigger (spec requirement #4):
//   "user's average R/R on the last 10 closed trades < Z (default 1.5)"
//  → emit a RRAverageBelow alert with severity Medium.
//
//  R/R approximation (Wave 3 has no per-trade risk target):
//   - Reward = |pnl| when pnl > 0.
//   - Risk = |pnl| when pnl < 0.
//   - R/R_i = reward_i / max(risk_i, ε). For winners risk_i is 0 — we skip
//     pure winners (their R/R is unbounded). For losers risk_i is the
//     loss magnitude; the reward is implicit in the next winner.
//   - To keep the metric meaningful we compute R/R_i per round-trip
//     pair: for each loser + the next winner in the window, R/R =
//     |next_winner.pnl| / |loser.pnl|. If no such pair exists we have
//     no signal.
//   - The avg of those R/R values must be < 1.5 to fire.
//
//  This is intentionally simple — Wave 4+ can swap it for the per-trade
//  risk-target stored in pre_trade_checklists.
//
//  PII: body uses qualitative copy ("R/R promedio de los últimos 10 trades
//  está por debajo de 1.5") — NO amounts.
// ============================================================================

public sealed class RRAverageBelowRule : IAlertRule
{
    public string RuleId => "RRAverageBelow";
    public int Priority => 300;

    /// <summary>Window of the last N closed trades (per spec: N=10).</summary>
    public const int LookbackTradeCount = 10;

    /// <summary>Threshold below which we fire (per spec: Z=1.5).</summary>
    public const decimal Threshold = 1.5m;

    public IReadOnlyList<Alert> Evaluate(AlertContext ctx)
    {
        var ordered = ctx.ClosedTrades
            .Where(t => t.Status == TradeStatus.Closed
                        && t.ClosedAt is not null
                        && t.PnL is not null)
            .OrderByDescending(t => t.ClosedAt!.Value)
            .Take(LookbackTradeCount)
            .ToList();
        if (ordered.Count == 0)
            return Array.Empty<Alert>();

        // Pair each loser with the next winner (chronological forward) to
        // compute a round-trip R/R. ordered is newest→oldest, so we
        // reverse for chronological iteration.
        var chronological = ordered.OrderBy(t => t.ClosedAt!.Value).ToList();

        var rrPairs = new List<decimal>();
        for (var i = 0; i < chronological.Count; i++)
        {
            var loser = chronological[i];
            if (loser.PnL!.Amount >= 0) continue;
            var loss = Math.Abs(loser.PnL.Amount);
            if (loss <= 0) continue;

            // Find the next winner after this loser.
            for (var j = i + 1; j < chronological.Count; j++)
            {
                var next = chronological[j];
                if (next.PnL!.Amount > 0)
                {
                    rrPairs.Add(next.PnL.Amount / loss);
                    break;
                }
            }
        }
        if (rrPairs.Count == 0)
            return Array.Empty<Alert>();

        var avgRr = rrPairs.Average();
        if (avgRr >= Threshold)
            return Array.Empty<Alert>();

        return new[]
        {
            new Alert(
                RuleId: RuleId,
                Severity: Severity.Medium,
                Title: "Riesgo/beneficio bajo",
                Body: $"Tu R/R promedio de los últimos {LookbackTradeCount} trades está por debajo de {Threshold:0.#}. ¿Estás entrando con poca ventaja?",
                Cta: new Cta("/app/trades", "Revisar trades"),
                ExpiresAt: null),
        };
    }
}