using JadeCapital.Shared.Kernel.Coaching;

namespace JadeCapital.Trading.Application.Coaching.Rules;

// ============================================================================
//  PreMarketPlanMissRule — slice 2d.1 (Trader Journal Core).
//
//  STANDALONE — reads the user's journal entry for "today" plus the
//  trades opened on that local date. We rely on the HANDLER to scope
//  the window to "today" (the journal collection is expected to be small
//  — typically zero or one entry per day). The rule picks the entry
//  whose LocalDate matches the latest trade's date, falling back to
//  WindowEnd when no trades exist (i.e. pure reflection without trades
//  does not trigger this rule).
//
//  Trigger: journal has a non-empty PremarketPlan AND tags, and at
//  least one trade today does not reference any tag/symbol from the
//  plan. Symbol comparison is case-insensitive (the journal stores
//  "EUR/USD"; the trade's Symbol.Value is "EUR/USD" too).
//
//  PII: the prompt copy intentionally omits any absolute PnL amount.
//  It only quotes the symbols the user mentioned vs the ones they
//  actually traded — those are qualitative markers, not amounts.
// ============================================================================

public sealed class PreMarketPlanMissRule : ICoachingRule
{
    public string RuleId => "PreMarketPlanMiss";
    public int Priority => 400;

    public IReadOnlyList<CoachingPrompt> Evaluate(CoachingContext ctx)
    {
        // Need at least one journal entry AND at least one trade today.
        if (ctx.Journals.Count == 0 || ctx.Trades.Count == 0)
        {
            return Array.Empty<CoachingPrompt>();
        }

        // Pick the journal for "today" — i.e. whichever LocalDate matches
        // any of the trades' calendar dates. The handler is expected to
        // provide today's entry; this rule is defensive against multi-day
        // windows where multiple journals would otherwise match.
        var tradeDates = ctx.Trades
            .Select(t => DateOnly.FromDateTime(t.OpenedAt.UtcDateTime))
            .Distinct()
            .ToList();
        var journal = ctx.Journals.FirstOrDefault(j => tradeDates.Contains(j.LocalDate.ToDateOnly()));
        if (journal is null) return Array.Empty<CoachingPrompt>();

        var plan = journal.PremarketPlan;
        if (string.IsNullOrWhiteSpace(plan)) return Array.Empty<CoachingPrompt>();

        var planSymbols = journal.Tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToUpperInvariant())
            .ToHashSet();

        var tradedSymbolsToday = ctx.Trades
            .Select(t => t.Symbol.Value.ToUpperInvariant())
            .ToHashSet();

        if (planSymbols.Count == 0) return Array.Empty<CoachingPrompt>();
        if (tradedSymbolsToday.Count == 0) return Array.Empty<CoachingPrompt>();

        // Trigger: at least one trade today is not mentioned in the plan's tags.
        var tradedNotInPlan = tradedSymbolsToday
            .Where(s => !planSymbols.Contains(s))
            .ToList();
        if (tradedNotInPlan.Count == 0) return Array.Empty<CoachingPrompt>();

        var mentioned = string.Join(" y ", planSymbols.OrderBy(s => s).Take(2));
        var traded = string.Join(", ", tradedNotInPlan.OrderBy(s => s).Take(2));
        var body = $"Tu plan de hoy mencionaba {mentioned}, pero operaste {traded}. ¿Estabas siguiendo tu plan?";

        return new[]
        {
            new CoachingPrompt(
                RuleId: RuleId,
                Severity: Severity.Low,
                Title: "Plan pre-mercado vs ejecución",
                Body: body,
                Cta: new Cta("/app/journal", "Ver plan de hoy"),
                OccurredAt: ctx.WindowEnd),
        };
    }
}
