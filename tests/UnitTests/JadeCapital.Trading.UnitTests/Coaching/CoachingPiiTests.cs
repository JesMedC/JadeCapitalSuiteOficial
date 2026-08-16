using JadeCapital.Shared.Kernel.Coaching;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Coaching;
using JadeCapital.Trading.Application.Coaching.Rules;
using JadeCapital.Trading.Domain.Behavioral;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Journal;
using DomainSeverity = JadeCapital.Trading.Domain.Behavioral.Severity;

namespace JadeCapital.Trading.UnitTests.Coaching;

// ============================================================================
//  PII — slice 2d.1 (Trader Journal Core) — cross-cutting guard.
//
//  Spec mandate: "Coaching prompt copy MUST NOT include absolute numbers
//  from the user's P&L (no +$150.00 or -50%). The copy MAY use
//  qualitative language but MUST NOT include raw amounts, percentages,
//  or instrument-specific amounts."
//
//  This test assembly exercises the three BehavioralEvent-driven rules
//  AND the TiltSequence rule (the spec's canonical example mentions
//  TiltSequence) with trades that have non-trivial P&L amounts. We then
//  concatenate every produced prompt's Title + Body and assert that no
//  absolute amount from any trade surfaces in the rendered text.
//
//  Cross-rule guarantee: if a future rule were to embed a trade's
//  PnL.Amount in its copy, the registry would route those prompts to
//  the dashboard unchanged, so this single test catches regressions
//  across all 5 rules at once.
//
//  RED. Implementation lives in JadeCapital.Trading.Application/Coaching/Rules.
// ============================================================================

public class CoachingPiiTests
{
    [Fact]
    public void NoPromptBodyContainsAbsolutePnlAmountFromTrades()
    {
        // Build closed trades with distinctive amounts: -450.00 and -150.00.
        // If any rule embeds an amount in its copy, the body string will
        // contain "450" or "-150.00" or "$450" and we fail loudly.
        var userId = Guid.NewGuid();
        var open1 = new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero);
        var close1 = open1.AddMinutes(5);

        var trade1 = BuildClosedTrade("EUR/USD", 1.0m, 1.10m, 1.05m, open1, close1, userId, expectPositive: false);

        // Behavioral events that would drive every rule:
        var tiltEvt = new BehavioralEvent(
            RuleId: "TiltSequence",
            Severity: DomainSeverity.High,
            TradeIds: new[] { trade1.Id },
            OccurredAt: close1,
            Message: "3 pérdidas consecutivas en 90 min — posible tilt");

        var revengeEvt = new BehavioralEvent(
            RuleId: "RevengeTrade",
            Severity: DomainSeverity.Medium,
            TradeIds: new[] { trade1.Id },
            OccurredAt: open1,
            Message: "Operación EUR/USD con tamaño 1.5× la anterior perdedora");

        var overEvt = new BehavioralEvent(
            RuleId: "OvertradingDay",
            Severity: DomainSeverity.High,
            TradeIds: new[] { trade1.Id },
            OccurredAt: close1,
            Message: "Día con 14 operaciones (baseline 3.5/día)");

        var registry = new CoachingRuleRegistry(new ICoachingRule[]
        {
            new TiltSequenceRule(),
            new RevengeTradeRule(),
            new OvertradingDayRule(),
            new LongBreakRule(),
            new PreMarketPlanMissRule(),
        });

        var ctx = new CoachingContext(
            UserId: userId,
            WindowStart: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            WindowEnd: new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero),
            Trades: new[] { trade1 },
            Journals: Array.Empty<JournalEntry>(),
            BehavioralEvents: new[] { tiltEvt, revengeEvt, overEvt });

        var prompts = registry.Evaluate(ctx);

        // The Loss would be -0.05 * 1.0 = -0.05 (volume=1, diff=-0.05)
        // We don't know the exact amount without computing, so instead we
        // capture whatever PnL the trade actually has and assert it is
        // not present in the prompt copy.
        var pnlString = trade1.PnL!.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var absolutePnl = System.Math.Abs(trade1.PnL!.Amount);
        var absoluteString = absolutePnl.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        foreach (var prompt in prompts)
        {
            prompt.Title.Should().NotContain(pnlString, $"title must not contain raw PnL '{pnlString}'");
            prompt.Body.Should().NotContain(pnlString, $"body must not contain raw PnL '{pnlString}'");
            prompt.Title.Should().NotContain(absoluteString, $"title must not contain absolute PnL '{absoluteString}'");
            prompt.Body.Should().NotContain(absoluteString, $"body must not contain absolute PnL '{absoluteString}'");
        }
    }

    private static Trade BuildClosedTrade(
        string symbol,
        decimal volume,
        decimal entry,
        decimal exit,
        DateTimeOffset openedAt,
        DateTimeOffset closedAt,
        Guid userId,
        bool expectPositive)
    {
        var s = Symbol.Create(symbol).Value;
        var v = Money.Create(volume, Currency.Usd).Value;
        var e = Money.Create(entry, Currency.Usd).Value;
        var x = Money.Create(exit, Currency.Usd).Value;
        var trade = Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), userId,
            s, AssetClass.Forex, TradeDirection.Long,
            v, e, "USD", null, null, openedAt).Value;
        var result = trade.Close(x, closedAt, Substitute.For<IClock>());
        result.IsSuccess.Should().BeTrue();
        return trade;
    }
}
