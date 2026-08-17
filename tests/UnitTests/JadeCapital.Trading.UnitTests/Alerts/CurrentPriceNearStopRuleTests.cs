using JadeCapital.Trading.Application.Alerts.Rules;

namespace JadeCapital.Trading.UnitTests.Alerts;

// ============================================================================
//  CurrentPriceNearStopRule tests — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Two scenarios:
//   1. Fires when there's >= 1 open trade older than 30 min (the Wave 3
//      proxy for "near stop" — we don't have live market data so we use
//      staleness as the proxy, per the rule's docstring).
//   2. Does NOT fire when all open trades are fresh (< 30 min old).
// ============================================================================

public class CurrentPriceNearStopRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Fires_WhenOpenTradeIsStaleOlderThan30Minutes()
    {
        var openTrade = BuildOpenTrade(minutesAgo: 60);
        var ctx = AlertContextFactory.Empty() with
        {
            OpenTrades = new[] { openTrade },
            WindowEnd = Now,
        };

        var alerts = new CurrentPriceNearStopRule().Evaluate(ctx);

        alerts.Should().HaveCount(1);
        alerts[0].Severity.Should().Be(JadeCapital.Shared.Kernel.Coaching.Severity.Low);
        alerts[0].Body.Should().Contain("cerca de zona de entrada");
    }

    [Fact]
    public void DoesNotFire_WhenAllOpenTradesAreFresh()
    {
        var openTrade = BuildOpenTrade(minutesAgo: 10);
        var ctx = AlertContextFactory.Empty() with
        {
            OpenTrades = new[] { openTrade },
            WindowEnd = Now,
        };

        var alerts = new CurrentPriceNearStopRule().Evaluate(ctx);

        alerts.Should().BeEmpty();
    }

    private static Trade BuildOpenTrade(int minutesAgo)
    {
        var s = Symbol.Create("EUR/USD").Value;
        var v = Money.Create(1m, Currency.Usd).Value;
        var e = Money.Create(1.10m, Currency.Usd).Value;
        var openedAt = Now.AddMinutes(-minutesAgo);
        return Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            s, AssetClass.Forex, TradeDirection.Long,
            v, e, "USD", null, null, openedAt).Value;
    }
}