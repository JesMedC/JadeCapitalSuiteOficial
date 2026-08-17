namespace JadeCapital.Trading.Domain.Behavioral;

// ============================================================================
//  Behavioral severity — slice 2b.1 (Trader Journal Core).
//
//  Three-level scale aligned with the spec:
//   - Low (1): informational — visible in patterns dashboard, no action.
//   - Medium (2): caution — pattern that historically correlates with
//     worse risk-adjusted returns. Coaching prompts (slice 2d) can surface.
//   - High (3): critical — pattern that statistically precedes large drawdowns.
//     Used by tilt-sequence and overtrading-severe-day rules.
//
//  Persisted nowhere in Wave 2 (events are computed on read); declared as a
//  byte so Wave 3's `trading.behavioral_events` table can store it as
//  SMALLINT without refactoring the enum.
// ============================================================================

public enum Severity : byte
{
    Low = 1,
    Medium = 2,
    High = 3,
}

/// <summary>
/// One detection produced by the analyzer. Carries enough context for the
/// frontend to render the patterns card (ruleId → icon, severity → color,
/// tradeIds → "view trade" link) and for the coaching prompt registry
/// (slice 2d) to compose human-readable messaging.
/// </summary>
public sealed record BehavioralEvent(
    string RuleId,
    Severity Severity,
    IReadOnlyList<Guid> TradeIds,
    DateTimeOffset OccurredAt,
    string Message);

/// <summary>
/// Aggregated stats for one emotionality bucket. WinRate is the
/// wins / count ratio in [0, 1] (0 when count = 0). TotalPnl is the
/// sum of <c>Trade.PnL.Amount</c> across the bucket in the same
/// currency (the analyzer does NOT convert across currencies — only
/// trades whose volume currency equals the user's primary account
/// currency are aggregated in Wave 2).
/// </summary>
public sealed record EmotionalityBucket(
    int Count,
    decimal WinRate,
    decimal TotalPnl);

/// <summary>
/// Aggregations block of the response. Today: only emotionality bucketing.
/// Wave 3 may add per-rule totals (e.g. "revenge trades in last 30d").
/// </summary>
public sealed record BehavioralAggregations(
    IReadOnlyDictionary<string, EmotionalityBucket> ByEmotionality);

/// <summary>
/// Top-level analyzer result. Empty arrays (not nulls) when the user has
/// no trades in the window — the spec requires `200 OK` with a fully
/// populated shape even when there's no data.
/// </summary>
public sealed record BehavioralAnalyticsResult(
    IReadOnlyList<BehavioralEvent> Events,
    BehavioralAggregations Aggregations);
