using JadeCapital.Shared.Kernel.Coaching;
using JadeCapital.Trading.Application.Coaching;
using KernelSeverity = JadeCapital.Shared.Kernel.Coaching.Severity;

namespace JadeCapital.Trading.UnitTests.Coaching;

// ============================================================================
//  CoachingRuleRegistry — slice 2d.1 (Trader Journal Core).
//
//  Spec-driven test contract:
//   - Holds a sorted (by Priority asc) collection of ICoachingRule.
//   - Evaluate iterates every rule, concatenates prompts, then sorts the
//     aggregate by severity desc + OccurredAt desc (per spec requirement
//     "Severity ordering": high first, low last; within a severity newer
//     prompts come first).
//   - Always returns a non-null, never-null empty list. Missing rules →
//     empty list, not null.
//
//  RED. Implementation lives in JadeCapital.Trading.Application/Coaching.
// ============================================================================

public class CoachingRuleRegistryTests
{
    [Fact]
    public void EvaluateAggregatesPromptsFromAllRules()
    {
        var pA = Sample("RuleA", KernelSeverity.Low, new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero));
        var pB = Sample("RuleB", KernelSeverity.High, new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero));
        var ruleA = new FakeCoachingRule("RuleA", priority: 100, prompts: new[] { pA });
        var ruleB = new FakeCoachingRule("RuleB", priority: 200, prompts: new[] { pB });

        var registry = new CoachingRuleRegistry(new ICoachingRule[] { ruleA, ruleB });

        var prompts = registry.Evaluate(CoachingContextFactory.Empty());

        prompts.Should().HaveCount(2);
        ruleA.EvaluateCount.Should().Be(1);
        ruleB.EvaluateCount.Should().Be(1);
    }

    [Fact]
    public void EvaluateOrdersBySeverityDescendingAndThenOccurredAtDescending()
    {
        // High + newest: should come first.
        // High + older: second.
        // Medium: third.
        // Low: last.
        var highNew = Sample("RuleX", KernelSeverity.High, new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero));
        var highOld = Sample("RuleX", KernelSeverity.High, new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero));
        var medium = Sample("RuleY", KernelSeverity.Medium, new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero));
        var low = Sample("RuleZ", KernelSeverity.Low, new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero));

        var ruleA = new FakeCoachingRule("R-A", priority: 1, prompts: new[] { medium });
        var ruleB = new FakeCoachingRule("R-B", priority: 2, prompts: new[] { low });
        var ruleC = new FakeCoachingRule("R-C", priority: 3, prompts: new[] { highNew, highOld });

        var registry = new CoachingRuleRegistry(new ICoachingRule[] { ruleA, ruleB, ruleC });

        var prompts = registry.Evaluate(CoachingContextFactory.Empty());

        prompts.Should().HaveCount(4);
        prompts[0].OccurredAt.Should().Be(highNew.OccurredAt);
        prompts[1].OccurredAt.Should().Be(highOld.OccurredAt);
        prompts[2].Should().BeEquivalentTo(medium, opts => opts.ExcludingMissingMembers());
        prompts[3].Should().BeEquivalentTo(low, opts => opts.ExcludingMissingMembers());
    }

    private static CoachingPrompt Sample(string ruleId, KernelSeverity severity, DateTimeOffset occurredAt)
        => new(
            RuleId: ruleId,
            Severity: severity,
            Title: $"title:{ruleId}",
            Body: $"body:{ruleId}",
            Cta: new Cta("/app/journal", "x"),
            OccurredAt: occurredAt);
}
