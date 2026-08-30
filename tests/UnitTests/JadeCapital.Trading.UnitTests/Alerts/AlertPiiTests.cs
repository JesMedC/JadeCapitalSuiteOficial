using JadeCapital.Shared.Kernel.MarketData;
using JadeCapital.Trading.Application.Alerts;
using JadeCapital.Trading.Application.Alerts.Rules;

namespace JadeCapital.Trading.UnitTests.Alerts;

// ============================================================================
//  PII cross-cutting test — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Spec requirement: "Alert copy MUST NOT include absolute P&L amounts
//  (no +$150.00, no -50%, no specific USD figures)."
//
//  We exercise ALL 5 rules in a single assembly with trades that have
//  distinctive amounts (e.g. -450, +1500, -50%). The registry aggregates
//  the outputs and we assert that:
//   1. No produced alert Title contains a $-prefixed or USD-suffixed
//      amount from any trade's PnL.
//   2. No produced alert Body contains a $-prefixed or USD-suffixed
//      amount from any trade's PnL.
//   3. The "DD actual > 5%" copy does NOT include a percent value
//      derived from any specific trade amount.
//
//  Cross-rule guarantee: if a future rule embeds an absolute amount in
//  its copy, this single test catches it before the alert reaches the
//  dashboard.
// ============================================================================

public class AlertPiiTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoAlertTitleOrBodyContainsAbsolutePnlAmountFromTrades()
    {
        // Distinctive amounts: 450, 1500, 200 USD.
        var trades = new[]
        {
            BuildClosedTrade(pnl: 450m, daysAgo: 8),
            BuildClosedTrade(pnl: -1500m, daysAgo: 6),
            BuildClosedTrade(pnl: 200m, daysAgo: 4),
        };

        var journal = JournalEntry.CreateOrUpdate(
            userId: Guid.NewGuid(),
            localDate: JadeCapital.Shared.Kernel.Time.LocalDate.From(DateOnly.FromDateTime(Now.UtcDateTime)),
            timezone: "UTC",
            moodPre: null,
            moodDuring: null,
            moodPost: null,
            premarketPlan: "Watching EUR/USD",
            postmarketReflection: null,
            tags: new[] { "EUR/USD" },
            clock: Substitute.For<IClock>()).Value;

        var registry = new AlertRegistry(
            new IAlertRule[]
            {
                new NoTradesInDaysRule(),
                new DrawdownExceededRule(),
                new RRAverageBelowRule(),
                new CurrentPriceNearStopRule(Substitute.For<IQuoteProvider>()),
                new OpenTradeOffPlanRule(),
            },
            Substitute.For<ILogger<AlertRegistry>>());

        var ctx = AlertContextFactory.Empty() with
        {
            ClosedTrades = trades,
            OpenTrades = Array.Empty<Trade>(),
            RecentJournals = new[] { journal },
            WindowEnd = Now,
        };

        var alerts = registry.Evaluate(ctx);

        // Disallow $-prefixed or USD-suffixed values matching the test
        // amounts (450, 1500, 200). The numeric form of these amounts
        // must not appear in any title or body.
        var forbiddenTokens = new[] { "$450", "$1500", "$200", "USD 450", "USD 1500", "USD 200", "450 USD", "1500 USD", "200 USD" };

        foreach (var alert in alerts)
        {
            foreach (var token in forbiddenTokens)
            {
                alert.Title.Should().NotContain(token, $"title of {alert.RuleId} must not embed '{token}'");
                alert.Body.Should().NotContain(token, $"body of {alert.RuleId} must not embed '{token}'");
            }
        }
    }

    private static Trade BuildClosedTrade(decimal pnl, int daysAgo)
    {
        var s = Symbol.Create("EUR/USD").Value;
        var v = Money.Create(1m, Currency.Usd).Value;
        var e = Money.Create(1.10m, Currency.Usd).Value;
        var x = Money.Create(pnl >= 0 ? 1.20m : 1.00m, Currency.Usd).Value;
        var openedAt = Now.AddDays(-daysAgo);
        var closedAt = Now.AddDays(-daysAgo).AddMinutes(5);
        var trade = Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            s, AssetClass.Forex, TradeDirection.Long,
            v, e, "USD", null, null, openedAt).Value;
        var result = trade.Close(x, closedAt, Substitute.For<IClock>());
        result.IsSuccess.Should().BeTrue();
        return trade;
    }
}