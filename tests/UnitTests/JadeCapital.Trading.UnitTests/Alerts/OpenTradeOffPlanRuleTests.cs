using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Alerts;
using JadeCapital.Trading.Application.Alerts.Rules;

namespace JadeCapital.Trading.UnitTests.Alerts;

// ============================================================================
//  OpenTradeOffPlanRule tests — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Three scenarios:
//   1. Fires when an open trade today has a Symbol NOT mentioned in the
//      user's journal today (premarket_plan + tags).
//   2. Does NOT fire when every open trade's symbol IS in the plan.
//   3. Does NOT fire when there's no journal entry today or the plan is empty.
// ============================================================================

public class OpenTradeOffPlanRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Fires_WhenOpenTradeSymbolIsNotInTodaysPlan()
    {
        // Trade is on GBP/USD; the journal mentions only EUR/USD.
        var openGbp = BuildOpenTrade(symbol: "GBP/USD", minutesAgo: 30);
        var journal = BuildJournal(
            tags: new[] { "EUR/USD" },
            plan: "Watching EUR/USD for breakout today",
            moodPost: 3); // Neutral mood so the journal isn't empty
        var ctx = AlertContextFactory.Empty() with
        {
            OpenTrades = new[] { openGbp },
            RecentJournals = new[] { journal },
            WindowEnd = Now,
        };

        var alerts = new OpenTradeOffPlanRule().Evaluate(ctx);

        alerts.Should().HaveCount(1);
        alerts[0].Severity.Should().Be(JadeCapital.Shared.Kernel.Coaching.Severity.Medium);
        alerts[0].Body.Should().Contain("GBP/USD");
        alerts[0].Body.Should().Contain("plan");
    }

    [Fact]
    public void DoesNotFire_WhenAllOpenTradeSymbolsAreInPlan()
    {
        var openEur = BuildOpenTrade(symbol: "EUR/USD", minutesAgo: 30);
        var journal = BuildJournal(
            tags: new[] { "EUR/USD" },
            plan: "Watching EUR/USD for breakout",
            moodPost: 3);
        var ctx = AlertContextFactory.Empty() with
        {
            OpenTrades = new[] { openEur },
            RecentJournals = new[] { journal },
            WindowEnd = Now,
        };

        var alerts = new OpenTradeOffPlanRule().Evaluate(ctx);

        alerts.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFire_WhenJournalPlanIsEmpty()
    {
        // Open trade today + journal today with empty plan → no signal.
        var openEur = BuildOpenTrade(symbol: "EUR/USD", minutesAgo: 30);
        var journal = BuildJournal(
            tags: Array.Empty<string>(),
            plan: "",
            moodPost: 3);
        var ctx = AlertContextFactory.Empty() with
        {
            OpenTrades = new[] { openEur },
            RecentJournals = new[] { journal },
            WindowEnd = Now,
        };

        var alerts = new OpenTradeOffPlanRule().Evaluate(ctx);

        alerts.Should().BeEmpty();
    }

    private static Trade BuildOpenTrade(string symbol, int minutesAgo)
    {
        var s = Symbol.Create(symbol).Value;
        var v = Money.Create(1m, Currency.Usd).Value;
        var e = Money.Create(1.10m, Currency.Usd).Value;
        var openedAt = Now.AddMinutes(-minutesAgo);
        return Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            s, AssetClass.Forex, TradeDirection.Long,
            v, e, "USD", null, null, openedAt).Value;
    }

    private static JournalEntry BuildJournal(IReadOnlyList<string> tags, string plan, int? moodPost)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var result = JournalEntry.CreateOrUpdate(
            userId: Guid.NewGuid(),
            localDate: LocalDate.From(DateOnly.FromDateTime(Now.UtcDateTime)),
            timezone: "UTC",
            moodPre: null,
            moodDuring: null,
            moodPost: moodPost.HasValue
                ? JadeCapital.Trading.Domain.Journal.Mood.Create((byte)moodPost.Value).Value
                : null,
            premarketPlan: plan,
            postmarketReflection: null,
            tags: tags,
            clock: clock);
        result.IsSuccess.Should().BeTrue("journal build must succeed");
        return result.Value;
    }
}