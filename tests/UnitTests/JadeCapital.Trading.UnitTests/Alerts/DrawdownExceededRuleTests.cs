using JadeCapital.Trading.Application.Alerts.Rules;

namespace JadeCapital.Trading.UnitTests.Alerts;

// ============================================================================
//  DrawdownExceededRule tests — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Three scenarios:
//   1. Fires when peak-to-trough drawdown > 5% (after a profit peak).
//   2. Does NOT fire when drawdown <= 5% (within threshold).
//   3. Does NOT fire when peak <= 0 (no profits accumulated yet).
//
//  P&L shaping: Trade.Close computes (exit - entry) * volume. To get
//  arbitrary PnL values without exiting at unrealistic prices, we use
//  entry=1.10 + exit=1.20 (diff=0.10) and scale the volume so that
//  PnL = 0.10 * volume = desired pnl. The volume cap is set to 1 if the
//  desired PnL is < 0.10 (entry-exit diff gives negative pnl otherwise).
// ============================================================================

public class DrawdownExceededRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Fires_WhenPeakToTroughDrawdownExceedsThreshold()
    {
        // Curve: +100, +50 (peak 150), -200 (trough -50). DD = 200/150 = 133%.
        var trades = new[]
        {
            BuildTrade(daysAgo: 10, pnl: 100m),
            BuildTrade(daysAgo: 8, pnl: 50m),
            BuildTrade(daysAgo: 5, pnl: -200m),
        };
        var rule = new DrawdownExceededRule();

        var alerts = rule.Evaluate(AlertContextFactory.Empty() with
        {
            ClosedTrades = trades, WindowEnd = Now,
        });

        alerts.Should().HaveCount(1);
        alerts[0].Severity.Should().Be(JadeCapital.Shared.Kernel.Coaching.Severity.High);
        alerts[0].Body.Should().Contain("DD actual > 5%");
    }

    [Fact]
    public void DoesNotFire_WhenDrawdownIsWithinThreshold()
    {
        // Curve: +1000 (peak 1000), -20 (trough 980). DD = 20/1000 = 2% < 5%.
        var trades = new[]
        {
            BuildTrade(daysAgo: 10, pnl: 1000m),
            BuildTrade(daysAgo: 5, pnl: -20m),
        };
        var rule = new DrawdownExceededRule();

        var alerts = rule.Evaluate(AlertContextFactory.Empty() with
        {
            ClosedTrades = trades, WindowEnd = Now,
        });

        alerts.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFire_WhenPeakIsNotPositive()
    {
        // All losses — no peak > 0, no drawdown to measure.
        var trades = new[]
        {
            BuildTrade(daysAgo: 10, pnl: -50m),
            BuildTrade(daysAgo: 5, pnl: -30m),
        };
        var rule = new DrawdownExceededRule();

        var alerts = rule.Evaluate(AlertContextFactory.Empty() with
        {
            ClosedTrades = trades, WindowEnd = Now,
        });

        alerts.Should().BeEmpty();
    }

    /// <summary>
    /// Builds a closed trade with the desired <paramref name="pnl"/> using
    /// entry=1.10 + exit=1.20 (diff=0.10) and volume = pnl / 0.10 (scaled).
    /// For very small pnl we clamp volume to 1 and exit to match.
    /// </summary>
    private static Trade BuildTrade(int daysAgo, decimal pnl)
    {
        var s = Symbol.Create("EUR/USD").Value;
        var e = Money.Create(1.10m, Currency.Usd).Value;
        var entry = e.Amount;
        var diff = 0.10m;
        var volume = pnl >= 0 ? pnl / diff : (-pnl) / diff;
        if (volume < 1m) volume = 1m;
        var v = Money.Create(volume, Currency.Usd).Value;
        // Exit = entry + diff (always positive since diff > 0).
        var x = Money.Create(entry + diff, Currency.Usd).Value;
        // For losers we flip the direction so Close computes (entry - exit) * volume = pnl.
        var direction = pnl >= 0 ? TradeDirection.Long : TradeDirection.Short;
        var openedAt = Now.AddDays(-daysAgo);
        var closedAt = Now.AddDays(-daysAgo).AddMinutes(5);
        var trade = Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            s, AssetClass.Forex, direction,
            v, e, "USD", null, null, openedAt).Value;
        var result = trade.Close(x, closedAt, Substitute.For<IClock>());
        result.IsSuccess.Should().BeTrue($"trade close must succeed; error: {result.Error.Code}");
        return trade;
    }
}