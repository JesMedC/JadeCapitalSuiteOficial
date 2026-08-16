using JadeCapital.Shared.Kernel.Coaching;
using JadeCapital.Trading.Domain.Behavioral;
using JadeCapital.Trading.Domain.Journal;
using JadeCapital.Trading.Domain.Trades;

namespace JadeCapital.Trading.Application.Coaching;

// ============================================================================
//  CoachingRule contract — slice 2d.1 (Trader Journal Core).
//
//  Lives in JadeCapital.Trading.Application (NOT Shared.Kernel) because
//  the evaluation context references Trading-only aggregates. Putting
//  ICoachingRule here keeps the layer dependency pointing inward
//  (Application → Kernel) and lets the concrete rules use domain types
//  directly without casting across module boundaries.
//
//  Priority semantics: ordinal, lower-first. The registry sorts by
//  Priority ascending so iteration order is stable.
//
//  Severity semantics: ordinal, lower-first. The aggregate returned by
//  the registry is sorted by Severity DESCENDING (per spec requirement:
//  high urgency first). The rule itself decides the severity of each
//  prompt it emits — Priority is registry iteration order, Severity is
//  the user-visible urgency.
//
//  PII: <see cref="ICoachingRule.Evaluate"/> MUST NOT embed absolute P&L
//  numbers or percentages in the prompt copy. Use qualitative language
//  only.
// ============================================================================

/// <summary>
/// Read-only input passed to every <see cref="ICoachingRule.Evaluate"/>.
/// Bundles everything a rule needs without dragging in repository or
/// infrastructure types. The handler in
/// <see cref="JadeCapital.Trading.Application.Features.Coaching.GetPrompts"/>
/// builds it from <see cref="IJournalEntryRepository"/>,
/// <see cref="ITradeRepository"/> and the in-memory BehavioralAnalyzer.
/// </summary>
public sealed record CoachingContext(
    Guid UserId,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    IReadOnlyList<Trade> Trades,
    IReadOnlyList<JournalEntry> Journals,
    IReadOnlyList<BehavioralEvent> BehavioralEvents);

/// <summary>
/// One detection-rule implementation. The registry iterates over a
/// collection in <see cref="Priority"/> ascending order so iteration is
/// stable. The aggregate returned to the dashboard is sorted by severity
/// descending + occurred-at descending (registry responsibility, not the
/// rule's).
/// </summary>
public interface ICoachingRule
{
    string RuleId { get; }
    int Priority { get; }
    IReadOnlyList<CoachingPrompt> Evaluate(CoachingContext ctx);
}
