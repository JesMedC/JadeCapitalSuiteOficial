using JadeCapital.Shared.Kernel.Coaching;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Coaching;
using JadeCapital.Trading.Domain.Behavioral;

namespace JadeCapital.Trading.UnitTests.Coaching;

// ============================================================================
//  FakeCoachingRule — slice 2d.1 (Trader Journal Core) — test fixture.
//
//  Light-weight rule used by CoachingRuleRegistryTests. Allows tests to
//  verify registry ordering without spinning up the real 5-rule set.
//  Tracks how many times Evaluate was called so we can assert registry
//  fan-out.
// ============================================================================

internal sealed class FakeCoachingRule : ICoachingRule
{
    public FakeCoachingRule(string ruleId, int priority, IReadOnlyList<CoachingPrompt>? prompts = null)
    {
        RuleId = ruleId;
        Priority = priority;
        Prompts = prompts ?? Array.Empty<CoachingPrompt>();
    }

    public string RuleId { get; }
    public int Priority { get; }
    public IReadOnlyList<CoachingPrompt> Prompts { get; }
    public int EvaluateCount { get; private set; }

    public IReadOnlyList<CoachingPrompt> Evaluate(CoachingContext ctx)
    {
        EvaluateCount++;
        return Prompts;
    }
}

internal static class CoachingContextFactory
{
    public static CoachingContext Empty(Guid? userId = null)
        => new(
            UserId: userId ?? Guid.NewGuid(),
            WindowStart: new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero),
            WindowEnd: new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero),
            Trades: Array.Empty<Trade>(),
            Journals: Array.Empty<JournalEntry>(),
            BehavioralEvents: Array.Empty<BehavioralEvent>());

    public static CoachingContext WithEvents(params BehavioralEvent[] events)
    {
        var ctx = Empty();
        return ctx with { BehavioralEvents = events };
    }
}
