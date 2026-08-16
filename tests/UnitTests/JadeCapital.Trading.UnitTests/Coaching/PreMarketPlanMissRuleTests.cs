using JadeCapital.Shared.Kernel.Coaching;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Coaching;
using JadeCapital.Trading.Application.Coaching.Rules;
using JadeCapital.Trading.Domain.Behavioral;
using DomainSeverity = JadeCapital.Trading.Domain.Behavioral.Severity;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Journal;

namespace JadeCapital.Trading.UnitTests.Coaching;

// ============================================================================
//  PreMarketPlanMissRule — slice 2d.1 (Trader Journal Core).
//
//  Spec-driven test contract:
//   - RuleId:    "PreMarketPlanMiss"
//   - Priority:  400
//   - Severity:  Low
//   - Trigger:   "journal entry today with premarket_plan non-empty AND ≥ 1
//                 trade today that doesn't reference any of the journal's
//                 tags or instruments in premarket_plan"
//   - Body:      "Tu plan de hoy mencionaba X e Y, pero operaste Z. ¿Estabas
//                 siguiendo tu plan?"
//   - Cta:       { route: "/app/journal", label: "Ver plan de hoy" }
//
//  This rule is STANDALONE — it reads the user's journal entry for today
//  plus trades opened today. It DOES NOT consume BehavioralEvent (no
//  behavioral rule captures this kind of plan-vs-execution drift).
//
//  The "today" date is supplied via the JournalEntry.LocalDate; the rule
//  matches that date against the trade's OpenedAt.Date. We pin both to
//  the same calendar date in the fixture.
//
//  RED. Implementation lives in JadeCapital.Trading.Application/Coaching/Rules.
// ============================================================================

public class PreMarketPlanMissRuleTests
{
    private readonly PreMarketPlanMissRule _sut = new();

    private static JournalEntry JournalForToday(
        Guid userId,
        LocalDate today,
        string plan,
        IReadOnlyList<string> tags)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(today.Year, today.Month, today.Day, 12, 0, 0, TimeSpan.Zero));
        return JournalEntry.CreateOrUpdate(
            userId,
            today,
            "UTC",
            null, null, null,
            plan,
            null,
            tags,
            clock).Value;
    }

    private static Trade OpenTradeOnDate(DateOnly date, string symbol, Guid userId)
    {
        // All symbols in the fixture use USD as the quote currency so the
        // Money.Create call below can consistently use Currency.Usd.
        var s = Symbol.Create(symbol).Value;
        var v = Money.Create(1.0m, Currency.Usd).Value;
        var e = Money.Create(1.10m, Currency.Usd).Value;
        var openedAt = new DateTimeOffset(date.Year, date.Month, date.Day, 10, 0, 0, TimeSpan.Zero);
        return Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), userId,
            s, AssetClass.Forex, TradeDirection.Long,
            v, e, "USD", null, null, openedAt).Value;
    }

    [Fact]
    public void EmitsWhenJournalPlanMentionsSymbolsNotPresentInTodaysTrades()
    {
        var userId = Guid.NewGuid();
        var today = new LocalDate(2026, 8, 17);
        var journal = JournalForToday(
            userId,
            today,
            "Esperar ruptura en EUR/USD y GBP/USD antes de operar.",
            new[] { "EUR/USD", "GBP/USD" });
        // Today the user traded XAU/USD (drift from the plan).
        var driftTrade = OpenTradeOnDate(today.ToDateOnly(), "XAU/USD", userId);

        var ctx = new CoachingContext(
            UserId: userId,
            WindowStart: DateTimeOffset.MinValue,
            WindowEnd: DateTimeOffset.MaxValue,
            Trades: new[] { driftTrade },
            Journals: new[] { journal },
            BehavioralEvents: Array.Empty<BehavioralEvent>());

        var prompts = _sut.Evaluate(ctx);

        prompts.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                RuleId = "PreMarketPlanMiss",
                Severity = DomainSeverity.Low,
            }, opts => opts.ExcludingMissingMembers());

        prompts[0].Cta.Route.Should().Be("/app/journal");
        prompts[0].Cta.Label.Should().Be("Ver plan de hoy");
        prompts[0].Body.Should().Contain("plan de hoy");
        prompts[0].Body.Should().Contain("siguiendo tu plan");
    }

    [Fact]
    public void DoesNotEmitWhenTradeIsMentionedInJournalTags()
    {
        var userId = Guid.NewGuid();
        var today = new LocalDate(2026, 8, 17);
        var journal = JournalForToday(
            userId,
            today,
            "Operar EUR/USD con setup claro.",
            new[] { "EUR/USD" });
        var matchingTrade = OpenTradeOnDate(today.ToDateOnly(), "EUR/USD", userId);

        var ctx = new CoachingContext(
            UserId: userId,
            WindowStart: DateTimeOffset.MinValue,
            WindowEnd: DateTimeOffset.MaxValue,
            Trades: new[] { matchingTrade },
            Journals: new[] { journal },
            BehavioralEvents: Array.Empty<BehavioralEvent>());

        var prompts = _sut.Evaluate(ctx);

        prompts.Should().BeEmpty();
    }
}
