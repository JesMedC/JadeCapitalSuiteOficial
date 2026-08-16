using JadeCapital.Trading.Application.Alerts;
using AlertWire = JadeCapital.Shared.Kernel.Alerts.Alert;
using Severity = JadeCapital.Shared.Kernel.Coaching.Severity;
using Cta = JadeCapital.Shared.Kernel.Coaching.Cta;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.UnitTests.Alerts;

// ============================================================================
//  AlertRegistry tests — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Two scenarios:
//   1. Aggregates alerts from every rule, then orders by Severity desc.
//   2. A rule that throws does NOT crash the registry; subsequent rules
//      still run (per spec scenario "Service survives transient errors").
//
//  Uses a hand-rolled FakeAlertRule so we can drive the rule outputs
//  deterministically without spinning up the real 5 rules.
// ============================================================================

public class AlertRegistryTests
{
    [Fact]
    public void Evaluate_AggregatesFromAllRules_AndOrdersBySeverityDescending()
    {
        var lowRule = new FakeAlertRule(
            ruleId: "R-Low", priority: 100,
            alerts: new AlertWire[]
            {
                new("R-Low", Severity.Low, "Low title", "low body",
                    new Cta("/x", "x"), null),
            });
        var mediumRule = new FakeAlertRule(
            ruleId: "R-Med", priority: 200,
            alerts: new AlertWire[]
            {
                new("R-Med", Severity.Medium, "Med title", "med body",
                    new Cta("/x", "x"), null),
            });
        var highRule = new FakeAlertRule(
            ruleId: "R-Hi", priority: 300,
            alerts: new AlertWire[]
            {
                new("R-Hi", Severity.High, "High title", "high body",
                    new Cta("/x", "x"), null),
            });

        var registry = new AlertRegistry(
            new IAlertRule[] { mediumRule, lowRule, highRule },
            Substitute.For<ILogger<AlertRegistry>>());

        var result = registry.Evaluate(AlertContextFactory.Empty());

        result.Should().HaveCount(3);
        result[0].RuleId.Should().Be("R-Hi");
        result[1].RuleId.Should().Be("R-Med");
        result[2].RuleId.Should().Be("R-Low");

        lowRule.EvaluateCount.Should().Be(1);
        mediumRule.EvaluateCount.Should().Be(1);
        highRule.EvaluateCount.Should().Be(1);
    }

    [Fact]
    public void Evaluate_RuleThrows_OthersStillRunAndExceptionIsLogged()
    {
        var thrower = new FakeAlertRule(
            ruleId: "R-Throw", priority: 100,
            alerts: Array.Empty<AlertWire>(),
            throwOnEvaluate: true);
        var survivor = new FakeAlertRule(
            ruleId: "R-Ok", priority: 200,
            alerts: new AlertWire[]
            {
                new("R-Ok", Severity.Medium, "t", "b",
                    new Cta("/x", "x"), null),
            });

        var logger = Substitute.For<ILogger<AlertRegistry>>();
        var registry = new AlertRegistry(
            new IAlertRule[] { thrower, survivor },
            logger);

        var result = registry.Evaluate(AlertContextFactory.Empty());

        result.Should().HaveCount(1);
        result[0].RuleId.Should().Be("R-Ok");
        thrower.EvaluateCount.Should().Be(1);
        survivor.EvaluateCount.Should().Be(1);

        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object?>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object?, Exception?, string>>());
    }
}