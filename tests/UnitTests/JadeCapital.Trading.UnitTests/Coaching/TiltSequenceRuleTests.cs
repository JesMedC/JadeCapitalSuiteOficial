using JadeCapital.Shared.Kernel.Coaching;
using JadeCapital.Trading.Application.Coaching.Rules;
using JadeCapital.Trading.Domain.Behavioral;
using DomainSeverity = JadeCapital.Trading.Domain.Behavioral.Severity;

namespace JadeCapital.Trading.UnitTests.Coaching;

// ============================================================================
//  TiltSequenceRule — slice 2d.1 (Trader Journal Core).
//
//  Spec-driven test contract:
//   - RuleId:    "TiltSequence"
//   - Priority:  50   (highest urgency — fires first)
//   - Severity:  High
//   - Body:      "3 pérdidas consecutivas en 90 min — posible tilt. Cierra el
//                 terminal y volvé mañana con cabeza fresca."
//   - Cta:       { route: "/app/trades", label: "Revisar operaciones" }
//
//  RED. Implementation lives in JadeCapital.Trading.Application/Coaching/Rules.
// ============================================================================

public class TiltSequenceRuleTests
{
    private readonly TiltSequenceRule _sut = new();

    [Fact]
    public void EmitsPromptForTiltSequenceEvent()
    {
        var occurredAt = new DateTimeOffset(2026, 8, 15, 12, 30, 0, TimeSpan.Zero);
        var e = new BehavioralEvent(
            RuleId: "TiltSequence",
            Severity: DomainSeverity.High,
            TradeIds: new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() },
            OccurredAt: occurredAt,
            Message: "3 pérdidas consecutivas en 90 min — posible tilt");

        var ctx = CoachingContextFactory.WithEvents(e);

        var prompts = _sut.Evaluate(ctx);

        prompts.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                RuleId = "TiltSequence",
                Severity = DomainSeverity.High,
            }, opts => opts.ExcludingMissingMembers());

        prompts[0].Cta.Route.Should().Be("/app/trades");
        prompts[0].Cta.Label.Should().Be("Revisar operaciones");
        prompts[0].Body.Should().Contain("3 pérdidas consecutivas");
        prompts[0].Body.Should().Contain("tilt");
    }

    [Fact]
    public void DoesNotEmitWhenNoBehavioralEvents()
    {
        var ctx = CoachingContextFactory.Empty();

        var prompts = _sut.Evaluate(ctx);

        prompts.Should().BeEmpty();
    }
}
