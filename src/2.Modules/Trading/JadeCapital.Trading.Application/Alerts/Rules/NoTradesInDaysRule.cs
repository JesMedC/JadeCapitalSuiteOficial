using JadeCapital.Shared.Kernel.Coaching;
using JadeCapital.Shared.Kernel.Alerts;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Journal;
using JadeCapital.Trading.Domain.Trades;

namespace JadeCapital.Trading.Application.Alerts.Rules;

// ============================================================================
//  NoTradesInDaysRule — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Trigger (spec requirement #4):
//   "user's last closed trade was >= N days ago AND the user had >= 1 closed
//    trade in the previous 30 days"
//  → emit a NoTradesInDays alert with severity Low.
//
//  Adapted from the Wave 2 LongBreakRule (slice 2d). The difference: alerts
//  are about INACTIVITY AFTER ACTIVITY, not about a one-off LongBreak nudge
//  during dashboard open. We compare the most recent ClosedAt vs the
//  evaluation window's WindowEnd.
//
//  PII: body uses qualitative copy ("5 días sin operar — ¿descanso
//  intencional o falta de disciplina?"). NO amounts, NO percentages.
// ============================================================================

public sealed class NoTradesInDaysRule : IAlertRule
{
    public string RuleId => "NoTradesInDays";
    public int Priority => 100;

    // 5+ calendar days since the last trade's ClosedAt.
    private const int GapCalendarDays = 5;

    // The user must have closed >= 1 trade in the preceding 30 days (this is
    // a break, not the very first trade of the account).
    private const int PriorActivityLookbackDays = 30;

    private static readonly TimeSpan Gap = TimeSpan.FromDays(GapCalendarDays);

    public IReadOnlyList<Alert> Evaluate(AlertContext ctx)
    {
        if (ctx.ClosedTrades.Count == 0)
            return Array.Empty<Alert>();

        // Order so the most recent ClosedAt is index 0.
        var ordered = ctx.ClosedTrades
            .Where(t => t.ClosedAt is not null)
            .OrderByDescending(t => t.ClosedAt!.Value)
            .ToList();
        if (ordered.Count == 0)
            return Array.Empty<Alert>();

        var newest = ordered[0];
        var reference = ctx.WindowEnd;

        var gap = reference - newest.ClosedAt!.Value;
        if (gap < Gap)
            return Array.Empty<Alert>();

        // Did the user trade in the prior 30 days before the newest?
        var lookbackStart = reference.AddDays(-PriorActivityLookbackDays);
        var newestDate = newest.ClosedAt!.Value;
        var hadPriorActivity = ordered
            .Where(t => t.ClosedAt is not null
                        && t.ClosedAt.Value >= lookbackStart
                        && t.ClosedAt.Value < newestDate)
            .Any();
        if (!hadPriorActivity)
            return Array.Empty<Alert>();

        return new[]
        {
            new Alert(
                RuleId: RuleId,
                Severity: Severity.Low,
                Title: "Racha sin operar",
                Body: $"{GapCalendarDays} días sin operar — ¿descanso intencional o falta de disciplina?",
                Cta: new Cta("/app/journal", "Reflexionar"),
                ExpiresAt: null),
        };
    }
}