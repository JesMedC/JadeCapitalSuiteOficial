using JadeCapital.Trading.Domain.Behavioral;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Trading.UnitTests.Behavioral;

// ============================================================================
//  BehavioralAnalyzer — slice 2b.1 (Trader Journal Core).
//
//  10 unit tests covering the 5 detection rules in the spec:
//   - RevengeTradeRule (2 tests)
//   - OvertradingDayRule (2 tests)
//   - TiltSequenceRule (2 tests)
//   - OverconfidenceAfterWinRule (2 tests)
//   - EmotionalityAggregationRule (2 tests)
//
//  Strict TDD: these tests reference BehavioralAnalyzer (and the
//  Severity / BehavioralEvent / EmotionalityBucket / BehavioralAggregations /
//  BehavioralAnalyticsResult types) before they exist in the domain assembly.
//  RED phase. GREEN happens when the domain layer is implemented.
// ============================================================================

public class BehavioralAnalyzerTests
{
    private static readonly DateTimeOffset WindowStart =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd =
        new(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);

    // ============================================================
    //  Helpers — build closed trades + checklists with deterministic
    //  timestamps. Volume is in USD (Money). PnL is computed by the
    //  Trade.Close aggregate, so we drive it through the public API.
    // ============================================================

    private static Trade OpenAndCloseLong(
        string symbol,
        decimal volume,
        decimal entry,
        decimal exit,
        DateTimeOffset openedAt,
        DateTimeOffset closedAt,
        Guid userId)
    {
        var s = Symbol.Create(symbol).Value;
        var v = Money.Create(volume, Currency.Usd).Value;
        var e = Money.Create(entry, Currency.Usd).Value;
        var trade = Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), userId,
            s, AssetClass.Forex, TradeDirection.Long,
            v, e, "USD", null, null, openedAt).Value;
        trade.Close(
            Money.Create(exit, Currency.Usd).Value,
            closedAt,
            Substitute.For<IClock>());
        return trade;
    }

    private static PreTradeChecklist ChecklistFor(
        Trade trade,
        Emotionality emotionality,
        Guid userId)
    {
        var submission = new PreTradeChecklistSubmission(
            emotionality, SetupQuality.Good, 2.5m, 2.0m, 3);
        return PreTradeChecklist.Create(
            trade.Id, userId, submission, trade.OpenedAt).Value;
    }

    // ========================================================
    //  RevengeTradeRule
    // ========================================================

    [Fact]
    public void RevengeTradeRule_NextTrade1AndAHalfXVolumeInSameSymbolWithin30Min_EmitsMediumEvent()
    {
        var userId = Guid.NewGuid();
        // Loss at 1.0 volume on EUR/USD, then 1.5x (1.5) volume trade on EUR/USD 10 min later.
        var t0 = new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero);
        var t1 = new DateTimeOffset(2026, 8, 10, 10, 10, 0, TimeSpan.Zero);

        var loser = OpenAndCloseLong("EUR/USD", 1.0m, 1.10m, 1.00m, t0, t0.AddMinutes(5), userId);
        var revenge = OpenAndCloseLong("EUR/USD", 1.5m, 1.10m, 1.12m, t1, t1.AddMinutes(5), userId);

        var result = BehavioralAnalyzer.Analyze(
            new List<Trade> { loser, revenge },
            new List<PreTradeChecklist>(),
            WindowStart, WindowEnd);

        result.Events.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                RuleId = "RevengeTrade",
                Severity = Severity.Medium,
            }, opts => opts.ExcludingMissingMembers());
        result.Events[0].TradeIds.Should().BeEquivalentTo(new[] { loser.Id, revenge.Id });
    }

    [Fact]
    public void RevengeTradeRule_NextTradeBelowRatio_DoesNotEmit()
    {
        var userId = Guid.NewGuid();
        // Loss at 1.0, next at 1.4 (below 1.5x ratio) → no event.
        var t0 = new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero);
        var t1 = new DateTimeOffset(2026, 8, 10, 10, 10, 0, TimeSpan.Zero);

        var loser = OpenAndCloseLong("EUR/USD", 1.0m, 1.10m, 1.00m, t0, t0.AddMinutes(5), userId);
        var next = OpenAndCloseLong("EUR/USD", 1.4m, 1.10m, 1.12m, t1, t1.AddMinutes(5), userId);

        var result = BehavioralAnalyzer.Analyze(
            new List<Trade> { loser, next },
            new List<PreTradeChecklist>(),
            WindowStart, WindowEnd);

        result.Events.Where(e => e.RuleId == "RevengeTrade").Should().BeEmpty();
    }

    // ========================================================
    //  OvertradingDayRule
    // ========================================================

    [Fact]
    public void OvertradingDayRule_DayWith12TradesAndBaselineUnder5_EmitsEvent()
    {
        var userId = Guid.NewGuid();
        // Build 30 days of "previous" baseline: 4 trades/day avg (=> median 4, below 5).
        // Then build 1 heavy day with 12 trades.
        var heavyDay = new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);
        var trades = new List<Trade>();

        // 30 baseline days, 4 trades/day on EUR/USD (each losing — closed at lower price).
        for (var d = 0; d < 30; d++)
        {
            var day = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero).AddDays(d);
            for (var i = 0; i < 4; i++)
            {
                var open = day.AddHours(i);
                trades.Add(OpenAndCloseLong("EUR/USD", 1.0m, 1.10m, 1.05m, open, open.AddMinutes(10), userId));
            }
        }

        // 12 trades on heavy day.
        for (var i = 0; i < 12; i++)
        {
            var open = heavyDay.AddMinutes(i * 5);
            trades.Add(OpenAndCloseLong("EUR/USD", 1.0m, 1.10m, 1.05m, open, open.AddMinutes(3), userId));
        }

        var result = BehavioralAnalyzer.Analyze(
            trades,
            new List<PreTradeChecklist>(),
            WindowStart, WindowEnd);

        result.Events.Where(e => e.RuleId == "OvertradingDay").Should().HaveCount(1);
        result.Events.First(e => e.RuleId == "OvertradingDay").Severity.Should().Be(Severity.High);
    }

    [Fact]
    public void OvertradingDayRule_DayWithHighBaseline_DoesNotEmit()
    {
        var userId = Guid.NewGuid();
        // 30 baseline days with 8 trades/day (median 8 > 5). Then a 12-trade day.
        var heavyDay = new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);
        var trades = new List<Trade>();

        for (var d = 0; d < 30; d++)
        {
            var day = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero).AddDays(d);
            for (var i = 0; i < 8; i++)
            {
                var open = day.AddHours(i);
                trades.Add(OpenAndCloseLong("EUR/USD", 1.0m, 1.10m, 1.05m, open, open.AddMinutes(10), userId));
            }
        }

        for (var i = 0; i < 12; i++)
        {
            var open = heavyDay.AddMinutes(i * 5);
            trades.Add(OpenAndCloseLong("EUR/USD", 1.0m, 1.10m, 1.05m, open, open.AddMinutes(3), userId));
        }

        var result = BehavioralAnalyzer.Analyze(
            trades,
            new List<PreTradeChecklist>(),
            WindowStart, WindowEnd);

        result.Events.Where(e => e.RuleId == "OvertradingDay").Should().BeEmpty();
    }

    // ========================================================
    //  TiltSequenceRule
    // ========================================================

    [Fact]
    public void TiltSequenceRule_ThreeLossesIn90Min_EmitsHighEvent()
    {
        var userId = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);
        var l1 = OpenAndCloseLong("EUR/USD", 1.0m, 1.10m, 1.00m, t0, t0.AddMinutes(5), userId);
        var l2 = OpenAndCloseLong("EUR/USD", 1.0m, 1.10m, 1.00m, t0.AddMinutes(20), t0.AddMinutes(25), userId);
        var l3 = OpenAndCloseLong("EUR/USD", 1.0m, 1.10m, 1.00m, t0.AddMinutes(60), t0.AddMinutes(65), userId);

        var result = BehavioralAnalyzer.Analyze(
            new List<Trade> { l1, l2, l3 },
            new List<PreTradeChecklist>(),
            WindowStart, WindowEnd);

        result.Events.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                RuleId = "TiltSequence",
                Severity = Severity.High,
            }, opts => opts.ExcludingMissingMembers());
    }

    [Fact]
    public void TiltSequenceRule_TwoLossesOnly_DoesNotEmit()
    {
        var userId = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);
        var l1 = OpenAndCloseLong("EUR/USD", 1.0m, 1.10m, 1.00m, t0, t0.AddMinutes(5), userId);
        var l2 = OpenAndCloseLong("EUR/USD", 1.0m, 1.10m, 1.00m, t0.AddMinutes(20), t0.AddMinutes(25), userId);

        var result = BehavioralAnalyzer.Analyze(
            new List<Trade> { l1, l2 },
            new List<PreTradeChecklist>(),
            WindowStart, WindowEnd);

        result.Events.Where(e => e.RuleId == "TiltSequence").Should().BeEmpty();
    }

    // ========================================================
    //  OverconfidenceAfterWinRule
    // ========================================================

    [Fact]
    public void OverconfidenceAfterWinRule_TwoWinsPlus2XSizeSameSymbol_EmitsMediumEvent()
    {
        var userId = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);
        var w1 = OpenAndCloseLong("EUR/USD", 1.0m, 1.10m, 1.12m, t0, t0.AddMinutes(5), userId);
        var w2 = OpenAndCloseLong("EUR/USD", 1.0m, 1.10m, 1.12m, t0.AddMinutes(20), t0.AddMinutes(25), userId);
        var big = OpenAndCloseLong("EUR/USD", 2.0m, 1.10m, 1.12m, t0.AddMinutes(40), t0.AddMinutes(45), userId);

        var result = BehavioralAnalyzer.Analyze(
            new List<Trade> { w1, w2, big },
            new List<PreTradeChecklist>(),
            WindowStart, WindowEnd);

        result.Events.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                RuleId = "OverconfidenceAfterWin",
                Severity = Severity.Medium,
            }, opts => opts.ExcludingMissingMembers());
    }

    [Fact]
    public void OverconfidenceAfterWinRule_NextTradeOnDifferentSymbol_DoesNotEmit()
    {
        var userId = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);
        var w1 = OpenAndCloseLong("EUR/USD", 1.0m, 1.10m, 1.12m, t0, t0.AddMinutes(5), userId);
        // No second win on EUR/USD — break the streak. Then a 2x on a different symbol.
        var breather = OpenAndCloseLong("GBP/USD", 1.0m, 1.10m, 1.05m, t0.AddMinutes(20), t0.AddMinutes(25), userId);
        var big = OpenAndCloseLong("GBP/USD", 2.0m, 1.10m, 1.12m, t0.AddMinutes(40), t0.AddMinutes(45), userId);

        var result = BehavioralAnalyzer.Analyze(
            new List<Trade> { w1, breather, big },
            new List<PreTradeChecklist>(),
            WindowStart, WindowEnd);

        result.Events.Where(e => e.RuleId == "OverconfidenceAfterWin").Should().BeEmpty();
    }

    // ========================================================
    //  EmotionalityAggregationRule
    // ========================================================

    [Fact]
    public void EmotionalityAggregationRule_ThreeBucketsPopulated_AggregatesCountWinRateAndPnl()
    {
        var userId = Guid.NewGuid();
        // 3 trades: 1 low (fearful) loser, 1 mid (neutral) winner, 1 high (euphoric) loser.
        var t0 = new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero);
        var low = OpenAndCloseLong("EUR/USD", 1.0m, 1.10m, 1.00m, t0, t0.AddMinutes(5), userId); // -100
        var mid = OpenAndCloseLong("EUR/USD", 1.0m, 1.10m, 1.20m, t0.AddMinutes(20), t0.AddMinutes(25), userId); // +1000
        var high = OpenAndCloseLong("EUR/USD", 1.0m, 1.10m, 1.00m, t0.AddMinutes(40), t0.AddMinutes(45), userId); // -100

        var checklists = new List<PreTradeChecklist>
        {
            ChecklistFor(low, Emotionality.Fearful, userId),
            ChecklistFor(mid, Emotionality.Neutral, userId),
            ChecklistFor(high, Emotionality.Euphoric, userId),
        };

        var result = BehavioralAnalyzer.Analyze(
            new List<Trade> { low, mid, high },
            checklists,
            WindowStart, WindowEnd);

        var byBuck = result.Aggregations.ByEmotionality;
        byBuck.Should().ContainKeys("low_1_2", "mid_3", "high_4_5");
        byBuck["low_1_2"].Count.Should().Be(1);
        byBuck["low_1_2"].WinRate.Should().Be(0m);
        byBuck["low_1_2"].TotalPnl.Should().BeApproximately(-0.10m, 0.001m);
        byBuck["mid_3"].Count.Should().Be(1);
        byBuck["mid_3"].WinRate.Should().Be(1m);
        byBuck["mid_3"].TotalPnl.Should().BeApproximately(0.10m, 0.001m);
        byBuck["high_4_5"].Count.Should().Be(1);
        byBuck["high_4_5"].WinRate.Should().Be(0m);
        byBuck["high_4_5"].TotalPnl.Should().BeApproximately(-0.10m, 0.001m);
    }

    [Fact]
    public void EmotionalityAggregationRule_NoChecklistsReturned_ReturnsZeroBuckets()
    {
        var userId = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero);
        var trade = OpenAndCloseLong("EUR/USD", 1.0m, 1.10m, 1.20m, t0, t0.AddMinutes(5), userId);

        var result = BehavioralAnalyzer.Analyze(
            new List<Trade> { trade },
            new List<PreTradeChecklist>(),
            WindowStart, WindowEnd);

        var byBuck = result.Aggregations.ByEmotionality;
        byBuck.Should().HaveCount(3);
        byBuck.Values.Should().AllSatisfy(b =>
        {
            b.Count.Should().Be(0);
            b.WinRate.Should().Be(0m);
            b.TotalPnl.Should().Be(0m);
        });
    }
}
