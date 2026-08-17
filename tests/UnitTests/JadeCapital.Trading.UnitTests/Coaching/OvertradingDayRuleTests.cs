using JadeCapital.Shared.Kernel.Coaching;
using JadeCapital.Trading.Application.Coaching.Rules;
using JadeCapital.Trading.Domain.Behavioral;
using DomainSeverity = JadeCapital.Trading.Domain.Behavioral.Severity;

namespace JadeCapital.Trading.UnitTests.Coaching;

// ============================================================================
//  OvertradingDayRule — slice 2d.1 (Trader Journal Core).
//
//  Spec-driven test contract:
//   - RuleId:    "OvertradingDay"
//   - Priority:  200
//   - Severity:  Medium
//   - Body:      "Operaste N veces hoy — tu promedio es M. ¿Estás sobre-operando?"
//   - Cta:       { route: "/app/patterns", label: "Ver patrones" }
//
//  RED. Implementation lives in JadeCapital.Trading.Application/Coaching/Rules.
// ============================================================================

public class OvertradingDayRuleTests
{
    private readonly OvertradingDayRule _sut = new();

    [Fact]
    public void EmitsOnePromptPerOvertradingDayEvent()
    {
        var occurredAt = new DateTimeOffset(2026, 8, 14, 16, 0, 0, TimeSpan.Zero);
        var e1 = new BehavioralEvent(
            RuleId: "OvertradingDay",
            Severity: DomainSeverity.High,
            TradeIds: new[] { Guid.NewGuid(), Guid.NewGuid() },
            OccurredAt: occurredAt,
            Message: "Día con 14 operaciones");
        var ctx = CoachingContextFactory.WithEvents(e1);

        var prompts = _sut.Evaluate(ctx);

        prompts.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                RuleId = "OvertradingDay",
                Severity = DomainSeverity.Medium,
            }, opts => opts.ExcludingMissingMembers());

        prompts[0].Cta.Route.Should().Be("/app/patterns");
        prompts[0].Cta.Label.Should().Be("Ver patrones");
        prompts[0].Body.Should().Contain("¿Estás sobre-operando?");
        prompts[0].OccurredAt.Should().Be(occurredAt);
    }

    [Fact]
    public void DoesNotEmitWhenNoBehavioralEvents()
    {
        var ctx = CoachingContextFactory.Empty();

        var prompts = _sut.Evaluate(ctx);

        prompts.Should().BeEmpty();
    }
}
