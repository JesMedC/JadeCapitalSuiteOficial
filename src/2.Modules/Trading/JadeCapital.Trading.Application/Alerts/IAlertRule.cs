using JadeCapital.Shared.Kernel.Alerts;
using JadeCapital.Trading.Domain.Behavioral;
using JadeCapital.Trading.Domain.Journal;
using JadeCapital.Trading.Domain.Trades;

namespace JadeCapital.Trading.Application.Alerts;

// ============================================================================
//  IAlertRule + AlertContext — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Lives in JadeCapital.Trading.Application (mirrors the ICoachingRule +
//  CoachingContext pattern from slice 2d). The interface + context
//  reference Trading-only aggregates (Trade, JournalEntry,
//  BehavioralAnalyticsResult); pulling these into Shared.Kernel would
//  invert the layer dependency. The Alert WIRE shape (JadeCapital.Shared.Kernel.Alerts.Alert)
//  lives in Shared.Kernel so non-Trading modules could reuse it.
//
//  The BackgroundService iterates every active user, builds an AlertContext
//  from their recent trades + journals + behavioral analytics, then asks
//  each rule whether to emit one or more Alerts. The registry aggregates
//  the results and persists them (with dedup via the partial UNIQUE INDEX
//  on (user_id, rule_id, UTC-date)).
//
//  Per-rule try/catch lives in the AlertRegistry (NOT in each rule): a
//  single rule throwing MUST NOT crash the host or skip subsequent rules.
//  This matches the spec requirement "Service survives transient errors".
// ============================================================================

/// <summary>
/// Read-only input passed to every <see cref="IAlertRule.Evaluate"/>.
/// Bundles everything a rule needs without dragging in repository or
/// infrastructure types. The handler in <c>AlertEvaluationService</c>
/// builds it from <c>ITradeRepository</c>, <c>IJournalEntryRepository</c>
/// and the in-memory <c>BehavioralAnalyzer</c>.
/// </summary>
public sealed record AlertContext(
    Guid UserId,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    IReadOnlyList<Trade> ClosedTrades,
    IReadOnlyList<Trade> OpenTrades,
    IReadOnlyList<JournalEntry> RecentJournals,
    BehavioralAnalyticsResult? BehavioralAnalytics);

/// <summary>
/// One detection-rule implementation. The registry iterates over a
/// collection in <see cref="Priority"/> ascending order so iteration is
/// stable. Each rule emits 0..N alerts based on the input context; the
/// aggregate returned to the user is sorted by Severity DESCENDING +
/// OccurredAt DESCENDING (registry responsibility, not the rule's).
/// </summary>
public interface IAlertRule
{
    /// <summary>Stable identifier (used in dedup UNIQUE INDEX and the wire shape).</summary>
    string RuleId { get; }

    /// <summary>Lower = fires first. Defaults to 100 if the rule has no preference.</summary>
    int Priority { get; }

    /// <summary>0..N alerts emitted by this rule. Empty list = nothing to say.</summary>
    IReadOnlyList<Alert> Evaluate(AlertContext ctx);
}