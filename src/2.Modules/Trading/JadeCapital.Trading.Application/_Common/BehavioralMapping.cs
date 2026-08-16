using JadeCapital.Trading.Contracts.Behavioral;
using JadeCapital.Trading.Domain.Behavioral;

namespace JadeCapital.Trading.Application._Common;

// ============================================================================
//  BehavioralMapping — slice 2b.1 (Trader Journal Core).
//
//  Wire projection for the analyzer result. Stays in Application (not
//  Domain) because it depends on the Contracts DTO shape and the
//  period enum, which are HTTP- / wire-layer concerns.
//
//  All timestamps serialize as <see cref="DateTimeOffset"/> with the
//  original UTC offset (System.Text.Json renders them as ISO 8601 with
//  offset suffix, e.g. "2026-08-12T10:00:00+00:00").
// ============================================================================

public static class BehavioralMapping
{
    public static BehavioralAnalysisDto ToDto(
        this BehavioralAnalyticsResult result,
        BehavioralPeriod period,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        var periodString = period switch
        {
            BehavioralPeriod.Days7  => "7d",
            BehavioralPeriod.Days30 => "30d",
            BehavioralPeriod.Days90 => "90d",
            BehavioralPeriod.All   => "all",
            _                       => "30d",
        };

        var eventDtos = result.Events.Select(e => new BehavioralEventDto(
            RuleId: e.RuleId,
            Severity: SeverityToWire(e.Severity),
            TradeIds: e.TradeIds,
            OccurredAt: e.OccurredAt,
            Message: e.Message)).ToList();

        var buckets = result.Aggregations.ByEmotionality.ToDictionary(
            kv => kv.Key,
            kv => new EmotionalityBucketDto(
                Count: kv.Value.Count,
                WinRate: kv.Value.WinRate,
                TotalPnl: kv.Value.TotalPnl));

        return new BehavioralAnalysisDto(
            Period: periodString,
            WindowStart: windowStart.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            WindowEnd: windowEnd.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Events: eventDtos,
            Aggregations: new BehavioralAggregationsDto(buckets));
    }

    private static string SeverityToWire(Severity severity) => severity switch
    {
        Severity.Low    => "low",
        Severity.Medium => "medium",
        Severity.High   => "high",
        _               => "low",
    };
}
