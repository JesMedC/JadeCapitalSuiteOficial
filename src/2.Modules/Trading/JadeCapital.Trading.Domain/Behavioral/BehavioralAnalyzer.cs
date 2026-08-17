using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.PreTradeChecklists;
using JadeCapital.Trading.Domain.Trades;

namespace JadeCapital.Trading.Domain.Behavioral;

// ============================================================================
//  BehavioralAnalyzer — slice 2b.1 (Trader Journal Core).
//
//  Pure function: (trades, checklists, windowStart, windowEnd) → result.
//  No persistence, no I/O, no MediatR. Same input → same output, byte-for-byte.
//
//  Five rules from the spec:
//   1. RevengeTradeRule        — next trade ≥1.5× losing trade volume,
//                                same symbol, ≤30 min after the loss close.
//   2. OvertradingDayRule      — day with ≥12 closed trades when the
//                                user's baseline (median of prior days
//                                in window) is ≤5.
//   3. TiltSequenceRule        — 3 consecutive closed losses within 90 min.
//   4. OverconfidenceAfterWin  — 2 consecutive wins + next trade ≥2× last
//                                winner volume, same symbol.
//   5. EmotionalityAggregation — bucket closed trades (linked to a
//                                PreTradeChecklist) by emotionality 1-2 / 3 / 4-5.
//
//  Thresholds are exposed as `public const` so the tests (and Wave 3 trending)
//  can reference them without duplicating magic numbers.
// ============================================================================

public static class BehavioralAnalyzer
{
    // ============== Thresholds (spec) ==============

    public const decimal RevengeRatioThreshold = 1.5m;
    public const int RevengeCooldownMinutes = 30;

    public const int OvertradingMinTrades = 12;
    public const int OvertradingMaxBaseline = 5;

    public const int TiltWindowMinutes = 90;
    public const int TiltMinConsecutiveLosses = 3;

    public const decimal OverconfidenceRatio = 2.0m;
    public const int OverconfidenceStreak = 2;

    private static readonly TimeSpan RevengeCooldown = TimeSpan.FromMinutes(RevengeCooldownMinutes);
    private static readonly TimeSpan TiltWindow = TimeSpan.FromMinutes(TiltWindowMinutes);

    /// <summary>
    /// Evaluates all 5 rules over the user's closed trades + linked
    /// checklists in [windowStart, windowEnd). The window is enforced
    /// by the caller (handler); this analyzer filters further to
    /// <c>ClosedAt &gt;= windowStart &amp;&amp; ClosedAt &lt; windowEnd</c>
    /// so that test fixtures can pass a superset of trades without
    /// polluting results.
    ///
    /// Cancelled trades are excluded (no PnL). Open trades are excluded
    /// (no ClosedAt). Only Closed trades drive every rule.
    /// </summary>
    public static BehavioralAnalyticsResult Analyze(
        IReadOnlyList<Trade> closedTrades,
        IReadOnlyList<PreTradeChecklist> checklists,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        var inWindow = closedTrades
            .Where(t => t.Status == TradeStatus.Closed
                        && t.ClosedAt is { } closedAt
                        && closedAt >= windowStart
                        && closedAt < windowEnd)
            .OrderBy(t => t.ClosedAt!.Value)
            .ToList();

        var events = new List<BehavioralEvent>();
        events.AddRange(RevengeTradeRule(inWindow));
        events.AddRange(OvertradingDayRule(inWindow));
        events.AddRange(TiltSequenceRule(inWindow));
        events.AddRange(OverconfidenceAfterWinRule(inWindow));

        var aggregations = EmotionalityAggregationRule(inWindow, checklists);

        return new BehavioralAnalyticsResult(events, aggregations);
    }

    // ============================================================
    //  Rule 1: Revenge trading after a loss
    //  Severity: Medium. Emits ONE event per qualifying pair.
    // ============================================================
    private static IEnumerable<BehavioralEvent> RevengeTradeRule(IReadOnlyList<Trade> inWindow)
    {
        for (var i = 0; i < inWindow.Count - 1; i++)
        {
            var loser = inWindow[i];
            if (loser.PnL is null || loser.PnL.Amount >= 0) continue;
            if (loser.ClosedAt is null) continue;

            var next = inWindow[i + 1];
            if (next.PnL is null) continue;
            if (next.ClosedAt is null) continue;

            // Volume ratio + same symbol + cooldown.
            var ratio = loser.Volume.Amount > 0
                ? next.Volume.Amount / loser.Volume.Amount
                : 0m;

            if (ratio < RevengeRatioThreshold) continue;
            if (!string.Equals(next.Symbol.Value, loser.Symbol.Value, StringComparison.OrdinalIgnoreCase)) continue;

            var delta = next.OpenedAt - loser.ClosedAt.Value;
            if (delta < TimeSpan.Zero || delta > RevengeCooldown) continue;

            var message =
                $"Operación {next.Symbol.Value} con tamaño {ratio:0.0}× la anterior perdedora — posible revenge trading";

            yield return new BehavioralEvent(
                RuleId: "RevengeTrade",
                Severity: Severity.Medium,
                TradeIds: new[] { loser.Id, next.Id },
                OccurredAt: next.OpenedAt,
                Message: message);
        }
    }

    // ============================================================
    //  Rule 2: Overtrading day
    //  Severity: High when N/median > 2, Medium otherwise.
    //  Baseline = median of per-day trade counts in the window, EXCLUDING
    //  the candidate day. We exclude the candidate so a single bad day
    //  doesn't bias its own baseline.
    // ============================================================
    private static IEnumerable<BehavioralEvent> OvertradingDayRule(IReadOnlyList<Trade> inWindow)
    {
        // Group by local UTC date of ClosedAt. Wave 3 may switch to user
        // timezone (header) — for Wave 2 the handler resolves UTC and the
        // bucket math is the same modulo calendar boundary.
        var perDay = inWindow
            .Where(t => t.ClosedAt is not null)
            .GroupBy(t => DateOnly.FromDateTime(t.ClosedAt!.Value.UtcDateTime))
            .ToDictionary(g => g.Key, g => g.Count());

        if (perDay.Count == 0) yield break;

        foreach (var (day, count) in perDay)
        {
            if (count < OvertradingMinTrades) continue;

            // Baseline excludes this candidate day.
            var baselineValues = perDay.Where(kv => kv.Key != day).Select(kv => (double)kv.Value).ToList();
            if (baselineValues.Count == 0) continue; // can't establish baseline
            baselineValues.Sort();
            var median = baselineValues.Count % 2 == 1
                ? baselineValues[baselineValues.Count / 2]
                : (baselineValues[baselineValues.Count / 2 - 1] + baselineValues[baselineValues.Count / 2]) / 2.0;

            if (median > OvertradingMaxBaseline) continue;

            var ratio = median > 0 ? count / median : count;
            var severity = ratio > 2.0 ? Severity.High : Severity.Medium;

            var message =
                $"Día con {count} operaciones (baseline {median:0.#}/día) — posible sobreoperativa";

            var tradesThatDay = inWindow
                .Where(t => t.ClosedAt is not null
                            && DateOnly.FromDateTime(t.ClosedAt.Value.UtcDateTime) == day)
                .Select(t => t.Id)
                .ToList();

            // OccurredAt = first close of the day.
            var firstClose = inWindow
                .Where(t => t.ClosedAt is not null
                            && DateOnly.FromDateTime(t.ClosedAt.Value.UtcDateTime) == day)
                .Min(t => t.ClosedAt!.Value);

            yield return new BehavioralEvent(
                RuleId: "OvertradingDay",
                Severity: severity,
                TradeIds: tradesThatDay,
                OccurredAt: firstClose,
                Message: message);
        }
    }

    // ============================================================
    //  Rule 3: Tilt sequence (3 losses in 90 min)
    //  Severity: High.
    // ============================================================
    private static IEnumerable<BehavioralEvent> TiltSequenceRule(IReadOnlyList<Trade> inWindow)
    {
        // Sliding window over the trade list (already in ClosedAt order).
        var start = 0;
        while (start < inWindow.Count)
        {
            var first = inWindow[start];
            if (first.PnL is null || first.PnL.Amount >= 0 || first.ClosedAt is null)
            {
                start++;
                continue;
            }

            // Count consecutive losses from `start` whose ClosedAt falls
            // within TiltWindow of `first.ClosedAt`.
            var losses = new List<Trade> { first };
            for (var j = start + 1; j < inWindow.Count; j++)
            {
                var t = inWindow[j];
                if (t.PnL is null || t.PnL.Amount >= 0 || t.ClosedAt is null) break;
                if (t.ClosedAt.Value - first.ClosedAt.Value > TiltWindow) break;
                losses.Add(t);
                if (losses.Count >= TiltMinConsecutiveLosses) break;
            }

            if (losses.Count >= TiltMinConsecutiveLosses)
            {
                yield return new BehavioralEvent(
                    RuleId: "TiltSequence",
                    Severity: Severity.High,
                    TradeIds: losses.Select(t => t.Id).ToList(),
                    OccurredAt: losses[^1].ClosedAt!.Value,
                    Message: $"{losses.Count} pérdidas consecutivas en {TiltWindowMinutes} min — posible tilt");

                // Advance past the matched window to avoid re-emitting overlapping events.
                start += losses.Count;
                continue;
            }

            start++;
        }
    }

    // ============================================================
    //  Rule 4: Overconfidence after wins (2 consecutive wins + 2× next trade)
    //  Severity: Medium.
    // ============================================================
    private static IEnumerable<BehavioralEvent> OverconfidenceAfterWinRule(IReadOnlyList<Trade> inWindow)
    {
        // We need at least streak+1 trades (2 winners + next).
        for (var i = 0; i + OverconfidenceStreak < inWindow.Count; i++)
        {
            // Verify streak of N consecutive wins, same symbol.
            var streak = new List<Trade>();
            var ok = true;
            var symbol = string.Empty;
            for (var k = 0; k < OverconfidenceStreak; k++)
            {
                var t = inWindow[i + k];
                if (t.PnL is null || t.PnL.Amount <= 0) { ok = false; break; }
                if (k == 0) symbol = t.Symbol.Value;
                else if (!string.Equals(t.Symbol.Value, symbol, StringComparison.OrdinalIgnoreCase)) { ok = false; break; }
                streak.Add(t);
            }
            if (!ok) continue;

            var lastWinner = streak[^1];
            var next = inWindow[i + OverconfidenceStreak];
            if (next.PnL is null) continue;

            if (!string.Equals(next.Symbol.Value, symbol, StringComparison.OrdinalIgnoreCase)) continue;
            var ratio = lastWinner.Volume.Amount > 0
                ? next.Volume.Amount / lastWinner.Volume.Amount
                : 0m;
            if (ratio < OverconfidenceRatio) continue;

            var message =
                $"Operación en {next.Symbol.Value} con tamaño {ratio:0.0}× tras {OverconfidenceStreak} ganancias — posible exceso de confianza";

            yield return new BehavioralEvent(
                RuleId: "OverconfidenceAfterWin",
                Severity: Severity.Medium,
                TradeIds: new[] { lastWinner.Id, next.Id },
                OccurredAt: next.OpenedAt,
                Message: message);

            // Skip past `next` to avoid re-using the last winner of this match.
            i += OverconfidenceStreak;
        }
    }

    // ============================================================
    //  Rule 5: Emotionality aggregation
    //  Buckets: low_1_2 (Fearful/Anxious), mid_3 (Neutral), high_4_5 (Confident/Euphoric).
    //  Returns zero buckets when there are no checklists linked in the window.
    // ============================================================
    private static BehavioralAggregations EmotionalityAggregationRule(
        IReadOnlyList<Trade> inWindow,
        IReadOnlyList<PreTradeChecklist> checklists)
    {
        var checklistByTradeId = checklists
            .Where(c => inWindow.Any(t => t.Id == c.TradeId))
            .ToDictionary(c => c.TradeId, c => c.Submission.Emotionality);

        var buckets = new Dictionary<string, List<Trade>>
        {
            ["low_1_2"] = new(),
            ["mid_3"] = new(),
            ["high_4_5"] = new(),
        };

        foreach (var trade in inWindow)
        {
            if (!checklistByTradeId.TryGetValue(trade.Id, out var e)) continue;
            var b = BucketFor(e);
            buckets[b].Add(trade);
        }

        var result = buckets.ToDictionary(
            kv => kv.Key,
            kv =>
            {
                var list = kv.Value;
                var count = list.Count;
                if (count == 0)
                    return new EmotionalityBucket(0, 0m, 0m);
                var wins = list.Count(t => t.PnL is { Amount: > 0 });
                var totalPnl = list.Sum(t => t.PnL?.Amount ?? 0m);
                return new EmotionalityBucket(count, (decimal)wins / count, totalPnl);
            });

        return new BehavioralAggregations(result);
    }

    private static string BucketFor(Emotionality e) => (byte)e switch
    {
        <= 2 => "low_1_2",
        3 => "mid_3",
        >= 4 => "high_4_5",
    };
}
