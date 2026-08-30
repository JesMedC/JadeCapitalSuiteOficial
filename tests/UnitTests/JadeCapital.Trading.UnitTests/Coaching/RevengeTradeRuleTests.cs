using JadeCapital.Shared.Kernel.Coaching;
using JadeCapital.Trading.Application.Coaching.Rules;
using JadeCapital.Trading.Domain.Behavioral;
using DomainSeverity = JadeCapital.Trading.Domain.Behavioral.Severity;

namespace JadeCapital.Trading.UnitTests.Coaching;

// ============================================================================
//  RevengeTradeRule — slice 2d.1 (Trader Journal Core).
//
//  Spec-driven test contract (from coaching-prompts/spec.md):
//   - RuleId:        "RevengeTrade"
//   - Priority:      100
//   - Severity:      High
//   - Body:          "Detectamos revenge trading — ¿estabas emocionalmente
//                     afectado por la pérdida anterior? Te recomendamos un
//                     break de 30 min."
//   - Cta:           { route: "/app/journal", label: "Escribir en el diario" }
//
//  RED. Implementation lives in JadeCapital.Trading.Application/Coaching/Rules.
// ============================================================================

public class RevengeTradeRuleTests
{
    private readonly RevengeTradeRule _sut = new();

    [Fact]
    public void EmitsOnePromptPerRevengeTradeEvent()
    {
        var userId = Guid.NewGuid();
        var e1 = new BehavioralEvent(
            RuleId: "RevengeTrade",
            Severity: DomainSeverity.Medium,
            TradeIds: new[] { Guid.NewGuid(), Guid.NewGuid() },
            OccurredAt: new DateTimeOffset(2026, 8, 10, 10, 10, 0, TimeSpan.Zero),
            Message: "Operación EUR/USD con tamaño 1.5× la anterior perdedora");
        var e2 = new BehavioralEvent(
            RuleId: "RevengeTrade",
            Severity: DomainSeverity.Medium,
            TradeIds: new[] { Guid.NewGuid(), Guid.NewGuid() },
            OccurredAt: new DateTimeOffset(2026, 8, 11, 14, 30, 0, TimeSpan.Zero),
            Message: "Operación GBP/USD con tamaño 2.0× la anterior perdedora");

        var ctx = CoachingContextFactory.WithEvents(e1, e2);

        var prompts = _sut.Evaluate(ctx);

        prompts.Should().HaveCount(2);
        prompts[0].RuleId.Should().Be("RevengeTrade");
        prompts[0].Severity.Should().Be(JadeCapital.Shared.Kernel.Coaching.Severity.High);
        prompts[0].OccurredAt.Should().Be(e1.OccurredAt);
        prompts[1].OccurredAt.Should().Be(e2.OccurredAt);

        prompts.Should().AllSatisfy(p =>
        {
            p.Cta.Route.Should().Be("/app/journal");
            p.Cta.Label.Should().Be("Escribir en el diario");
            p.Body.Should().Contain("revenge trading");
            p.Body.Should().Contain("break de 30 min");
        });
    }

    [Fact]
    public void DoesNotEmitWhenNoBehavioralEvents()
    {
        var ctx = CoachingContextFactory.Empty();

        var prompts = _sut.Evaluate(ctx);

        prompts.Should().BeEmpty();
    }
}
