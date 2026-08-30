using JadeCapital.Trading.Application.Alerts.Rules;

namespace JadeCapital.Trading.UnitTests.Alerts;

// ============================================================================
//  RRAverageBelowRule tests — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Three scenarios:
//   1. Fires when the avg R/R (loser → next winner) of the last 10 trades
//      is below 1.5 (low reward relative to risk).
//   2. Does NOT fire when the avg R/R >= 1.5 (acceptable).
//   3. Does NOT fire when no loser/winner pair exists in the window.
//
//  P&L shaping: see DrawdownExceededRuleTests — entry=1.10, exit=1.20
//  (diff=0.10), volume = |pnl|/0.10.
// ============================================================================

public class RRAverageBelowRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Fires_WhenAverageRewardRiskRatioIsBelowThreshold()
    {
        // Sequence (newest→oldest): +100, -200, +50, -100, +80.
        // Chronological pairs (loser → next winner):
        //   -200 → +50 (RR = 50/200 = 0.25)
        //   -100 → +80 (RR = 80/100 = 0.80)
        // Avg RR = 0.525 < 1.5 → fires.
        var trades = new[]
        {
            BuildTrade(daysAgo: 9, pnl: 100m),
            BuildTrade(daysAgo: 8, pnl: -200m),
            BuildTrade(daysAgo: 6, pnl: 50m),
            BuildTrade(daysAgo: 4, pnl: -100m),
            BuildTrade(daysAgo: 2, pnl: 80m),
        };
        var rule = new RRAverageBelowRule();

        var alerts = rule.Evaluate(AlertContextFactory.Empty() with
        {
            ClosedTrades = trades, WindowEnd = Now,
        });

        alerts.Should().HaveCount(1);
        alerts[0].Severity.Should().Be(JadeCapital.Shared.Kernel.Coaching.Severity.Medium);
        alerts[0].Body.Should().Contain("R/R promedio");
        alerts[0].Body.Should().Contain("1.5");
    }

    [Fact]
    public void DoesNotFire_WhenAverageRewardRiskRatioMeetsThreshold()
    {
        // Sequence (newest→oldest): -100, +300, -100, +400.
        // Chronological pairs:
        //   -100 → +300 (RR = 300/100 = 3.0)
        //   -100 → +400 (RR = 400/100 = 4.0)
        // Avg RR = 3.5 > 1.5 → does not fire.
        var trades = new[]
        {
            BuildTrade(daysAgo: 8, pnl: -100m),
            BuildTrade(daysAgo: 6, pnl: 300m),
            BuildTrade(daysAgo: 4, pnl: -100m),
            BuildTrade(daysAgo: 2, pnl: 400m),
        };
        var rule = new RRAverageBelowRule();

        var alerts = rule.Evaluate(AlertContextFactory.Empty() with
        {
            ClosedTrades = trades, WindowEnd = Now,
        });

        alerts.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFire_WhenNoLoserWinnerPairsExist()
    {
        // All winners — no R/R to compute, no signal.
        var trades = new[]
        {
            BuildTrade(daysAgo: 5, pnl: 100m),
            BuildTrade(daysAgo: 3, pnl: 200m),
        };
        var rule = new RRAverageBelowRule();

        var alerts = rule.Evaluate(AlertContextFactory.Empty() with
        {
            ClosedTrades = trades, WindowEnd = Now,
        });

        alerts.Should().BeEmpty();
    }

    private static Trade BuildTrade(int daysAgo, decimal pnl)
    {
        var s = Symbol.Create("EUR/USD").Value;
        var e = Money.Create(1.10m, Currency.Usd).Value;
        var entry = e.Amount;
        var diff = 0.10m;
        var volume = pnl >= 0 ? pnl / diff : (-pnl) / diff;
        if (volume < 1m) volume = 1m;
        var v = Money.Create(volume, Currency.Usd).Value;
        var x = Money.Create(entry + diff, Currency.Usd).Value;
        var direction = pnl >= 0 ? TradeDirection.Long : TradeDirection.Short;
        var openedAt = Now.AddDays(-daysAgo);
        var closedAt = Now.AddDays(-daysAgo).AddMinutes(5);
        var trade = Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            s, AssetClass.Forex, direction,
            v, e, "USD", null, null, openedAt).Value;
        var result = trade.Close(x, closedAt, Substitute.For<IClock>());
        result.IsSuccess.Should().BeTrue();
        return trade;
    }
}