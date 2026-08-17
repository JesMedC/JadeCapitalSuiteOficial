namespace JadeCapital.Trading.Contracts.Behavioral;

// ============================================================================
//  Behavioral Analytics DTOs — slice 2b.1 wire contract.
//
//  Mirror of:
//   src/2.Modules/Trading/JadeCapital.Trading.Domain/Behavioral/BehavioralModels.cs
//
//  The wire shape matches the JSON in `openspec/changes/2026-08-17-trader-journal-core/
//  specs/behavioral-analytics/spec.md`. Enums serialize as their string names
//  (Severity: "low" | "medium" | "high"; Period: "7d" | "30d" | "90d" | "all")
//  via System.Text.Json's default enum-string policy on the API host.
// ============================================================================

/// <summary>
/// Time window for the analyzer query. Maps to `?period=` in the URL.
///   - 7d / 30d / 90d: window = N days back from "now" (inclusive of today).
///   - all: full history (no lower bound).
/// </summary>
public enum BehavioralPeriod
{
    Days7 = 7,
    Days30 = 30,
    Days90 = 90,
    All = 0,
}

/// <summary>
/// Top-level response body for <c>GET /api/trades/behavioral</c>.
/// </summary>
public sealed record BehavioralAnalysisDto(
    string Period,
    string WindowStart,
    string WindowEnd,
    IReadOnlyList<BehavioralEventDto> Events,
    BehavioralAggregationsDto Aggregations);

/// <summary>
/// Single event emitted by one of the 5 detection rules. <c>TradeIds</c>
/// is the list of trade IDs that triggered the rule (typically the
/// triggering pair/triple, not the entire user history).
/// </summary>
public sealed record BehavioralEventDto(
    string RuleId,
    string Severity,
    IReadOnlyList<Guid> TradeIds,
    DateTimeOffset OccurredAt,
    string Message);

/// <summary>
/// Aggregations block. Today: emotionality bucketing.
/// </summary>
public sealed record BehavioralAggregationsDto(
    IReadOnlyDictionary<string, EmotionalityBucketDto> ByEmotionality);

/// <summary>
/// One emotionality bucket. Keys: <c>low_1_2</c>, <c>mid_3</c>, <c>high_4_5</c>.
/// <c>WinRate</c> is in [0, 1]. <c>TotalPnl</c> is the sum of
/// <c>Trade.PnL.Amount</c> in the bucket's account currency.
/// </summary>
public sealed record EmotionalityBucketDto(
    int Count,
    decimal WinRate,
    decimal TotalPnl);
