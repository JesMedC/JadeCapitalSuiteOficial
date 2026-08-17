using JadeCapital.Trading.Application.Alerts;
using JadeCapital.Trading.Application.Alerts.Rules;

namespace JadeCapital.Trading.UnitTests.Alerts;

// ============================================================================
//  NoTradesInDaysRule tests — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Three scenarios:
//   1. Fires when the most recent ClosedAt is >= 5 days ago AND there's
//      a prior trade in the 30-day lookback window.
//   2. Does NOT fire when the most recent trade is < 5 days ago.
//   3. Does NOT fire when the user has no prior activity (only one trade,
//      older than 5 days — the "very first trade" carve-out).
// ============================================================================

public class NoTradesInDaysRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 14, 0, 0, TimeSpan.Zero);

    private static AlertContext CtxWithClosedTrades(IReadOnlyList<Trade> closed)
        => AlertContextFactory.Empty() with
        {
            ClosedTrades = closed,
            WindowEnd = Now,
        };

    [Fact]
    public void Fires_WhenMostRecentTradeIs5PlusDaysOldAndPriorActivityInLast30Days()
    {
        var trades = new[]
        {
            BuildTrade(openedDaysAgo: 8, closedDaysAgo: 7),  // most recent
            BuildTrade(openedDaysAgo: 15, closedDaysAgo: 14), // prior, within 30 days
        };
        var rule = new NoTradesInDaysRule();

        var alerts = rule.Evaluate(CtxWithClosedTrades(trades));

        alerts.Should().HaveCount(1);
        alerts[0].RuleId.Should().Be("NoTradesInDays");
        alerts[0].Severity.Should().Be(JadeCapital.Shared.Kernel.Coaching.Severity.Low);
        alerts[0].Title.Should().Be("Racha sin operar");
        alerts[0].Body.Should().Contain("5 días sin operar");
    }

    [Fact]
    public void DoesNotFire_WhenMostRecentTradeIsLessThan5DaysOld()
    {
        var trades = new[]
        {
            BuildTrade(openedDaysAgo: 3, closedDaysAgo: 2),
            BuildTrade(openedDaysAgo: 10, closedDaysAgo: 9),
        };
        var rule = new NoTradesInDaysRule();

        var alerts = rule.Evaluate(CtxWithClosedTrades(trades));

        alerts.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFire_WhenNoPriorActivityInLast30Days()
    {
        // Only one trade ever — older than 5 days. The rule carves this
        // out: no prior activity means no alert (the user just has a stale
        // single trade, not a "break" after activity).
        var trades = new[]
        {
            BuildTrade(openedDaysAgo: 50, closedDaysAgo: 48),
        };
        var rule = new NoTradesInDaysRule();

        var alerts = rule.Evaluate(CtxWithClosedTrades(trades));

        alerts.Should().BeEmpty();
    }

    private static Trade BuildTrade(int openedDaysAgo, int closedDaysAgo)
    {
        var s = Symbol.Create("EUR/USD").Value;
        var v = Money.Create(1m, Currency.Usd).Value;
        var e = Money.Create(1.10m, Currency.Usd).Value;
        var x = Money.Create(1.05m, Currency.Usd).Value;
        var openedAt = Now.AddDays(-openedDaysAgo);
        var closedAt = Now.AddDays(-closedDaysAgo);
        var trade = Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            s, AssetClass.Forex, TradeDirection.Long,
            v, e, "USD", null, null, openedAt).Value;
        var result = trade.Close(x, closedAt, Substitute.For<IClock>());
        result.IsSuccess.Should().BeTrue();
        return trade;
    }
}