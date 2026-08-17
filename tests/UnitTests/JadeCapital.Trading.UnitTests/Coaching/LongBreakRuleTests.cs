using JadeCapital.Shared.Kernel.Coaching;
using JadeCapital.Trading.Application.Coaching;
using JadeCapital.Trading.Application.Coaching.Rules;
using JadeCapital.Trading.Domain.Behavioral;
using DomainSeverity = JadeCapital.Trading.Domain.Behavioral.Severity;
using JadeCapital.Trading.Domain.Enums;

namespace JadeCapital.Trading.UnitTests.Coaching;

// ============================================================================
//  LongBreakRule — slice 2d.1 (Trader Journal Core).
//
//  Spec-driven test contract:
//   - RuleId:    "LongBreak"
//   - Priority:  300
//   - Severity:  Low
//   - Trigger:   "user hasn't opened a trade in 5+ calendar days AND had ≥ 1
//                 trade in the previous 30 days"
//   - Body:      "5 días sin operar — ¿descanso intencional o falta de
//                 disciplina?"
//   - Cta:       { route: "/app/journal", label: "Reflexionar" }
//
//  This rule is STANDALONE — it does NOT read BehavioralEvents (the
//  BehavioralAnalyzer has no notion of inactivity). It looks directly at
//  the user's trades in the window. Window math is contextual: "today"
//  comes from Trading.Application handler (we set it via ClosedAt < today
//  in the test fixture).
//
//  RED. Implementation lives in JadeCapital.Trading.Application/Coaching/Rules.
// ============================================================================

public class LongBreakRuleTests
{
    private readonly LongBreakRule _sut = new();

    private static Trade OpenTradeAt(DateTimeOffset openedAt, Guid userId)
    {
        var s = Symbol.Create("EUR/USD").Value;
        var v = Money.Create(1.0m, Currency.Usd).Value;
        var e = Money.Create(1.10m, Currency.Usd).Value;
        return Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), userId,
            s, AssetClass.Forex, TradeDirection.Long,
            v, e, "USD", null, null, openedAt).Value;
    }

    [Fact]
    public void EmitsWhenLastTradeIsFiveOrMoreCalendarDaysAgo()
    {
        var userId = Guid.NewGuid();
        var today = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        // Last trade 6 days ago, prior trade 20 days ago (still inside
        // the "previous 30 days" guard).
        var lastTrade = OpenTradeAt(new DateTimeOffset(2026, 8, 11, 9, 0, 0, TimeSpan.Zero), userId);
        var earlierTrade = OpenTradeAt(new DateTimeOffset(2026, 7, 28, 9, 0, 0, TimeSpan.Zero), userId);

        var ctx = new CoachingContext(
            UserId: userId,
            WindowStart: today.AddDays(-60),
            WindowEnd: today,
            Trades: new[] { lastTrade, earlierTrade },
            Journals: Array.Empty<JournalEntry>(),
            BehavioralEvents: Array.Empty<BehavioralEvent>());

        var prompts = _sut.Evaluate(ctx);

        prompts.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                RuleId = "LongBreak",
                Severity = DomainSeverity.Low,
            }, opts => opts.ExcludingMissingMembers());

        prompts[0].Cta.Route.Should().Be("/app/journal");
        prompts[0].Cta.Label.Should().Be("Reflexionar");
        prompts[0].Body.Should().Contain("5 días sin operar");
        prompts[0].Body.Should().Contain("disciplina");
    }

    [Fact]
    public void DoesNotEmitWhenLastTradeWasYesterday()
    {
        var userId = Guid.NewGuid();
        var today = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        var yesterday = OpenTradeAt(today.AddDays(-1), userId);

        var ctx = new CoachingContext(
            UserId: userId,
            WindowStart: today.AddDays(-60),
            WindowEnd: today,
            Trades: new[] { yesterday },
            Journals: Array.Empty<JournalEntry>(),
            BehavioralEvents: Array.Empty<BehavioralEvent>());

        var prompts = _sut.Evaluate(ctx);

        prompts.Should().BeEmpty();
    }
}
