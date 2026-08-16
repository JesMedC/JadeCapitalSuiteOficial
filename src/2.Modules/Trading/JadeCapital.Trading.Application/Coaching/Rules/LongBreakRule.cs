using JadeCapital.Shared.Kernel.Coaching;

namespace JadeCapital.Trading.Application.Coaching.Rules;

// ============================================================================
//  LongBreakRule — slice 2d.1 (Trader Journal Core).
//
//  STANDALONE — does NOT consume BehavioralEvent (the analyzer has no
//  notion of inactivity). Reads the user's trades directly from
//  <see cref="CoachingContext.Trades"/>.
//
//  Trigger (spec): "user hasn't opened a trade in 5+ calendar days AND
//  had ≥ 1 trade in the previous 30 days".
//
//  Calendar days are computed against the rule's evaluation reference
//  date — we use the latest trade's OpenedAt vs WindowEnd when no clock
//  is injected (the caller is expected to scope the window precisely).
//
//  For a fresh user with no trades this emits nothing; for a user who
//  traded yesterday it emits nothing; only true inactivity qualifies.
// ============================================================================

public sealed class LongBreakRule : ICoachingRule
{
    public string RuleId => "LongBreak";
    public int Priority => 300;

    // 5+ calendar days since the last trade's OpenedAt.
    private const int GapCalendarDays = 5;
    // The user must have traded at least once in the preceding 30 days
    // (i.e. this is a break, not the very first trade of the account).
    private const int PriorActivityLookbackDays = 30;

    public IReadOnlyList<CoachingPrompt> Evaluate(CoachingContext ctx)
    {
        if (ctx.Trades.Count == 0) return Array.Empty<CoachingPrompt>();

        // Order so the most recent OpenedAt is index 0.
        var ordered = ctx.Trades
            .OrderByDescending(t => t.OpenedAt)
            .ToList();
        var newest = ordered[0];
        var reference = ctx.WindowEnd;

        var gapDays = CalendarDaysBetween(DateOnly.FromDateTime(newest.OpenedAt.UtcDateTime), DateOnly.FromDateTime(reference.UtcDateTime));
        if (gapDays < GapCalendarDays) return Array.Empty<CoachingPrompt>();

        // Did the user trade in the prior 30 days before the newest?
        var priorWindowStart = DateOnly.FromDateTime(reference.UtcDateTime).AddDays(-PriorActivityLookbackDays);
        var newestDate = DateOnly.FromDateTime(newest.OpenedAt.UtcDateTime);
        var hadPriorActivity = ordered
            .Where(t => DateOnly.FromDateTime(t.OpenedAt.UtcDateTime) >= priorWindowStart
                        && DateOnly.FromDateTime(t.OpenedAt.UtcDateTime) < newestDate)
            .Any();
        if (!hadPriorActivity) return Array.Empty<CoachingPrompt>();

        return new[]
        {
            new CoachingPrompt(
                RuleId: RuleId,
                Severity: Severity.Low,
                Title: "Racha sin operar",
                Body: "5 días sin operar — ¿descanso intencional o falta de disciplina? Si es lo segundo, agendá una sesión de trading.",
                Cta: new Cta("/app/journal", "Reflexionar"),
                OccurredAt: reference),
        };
    }

    private static int CalendarDaysBetween(DateOnly earlier, DateOnly later)
        => later.DayNumber - earlier.DayNumber;
}
