using JadeCapital.Shared.Kernel.Coaching;

namespace JadeCapital.Trading.Application.Coaching;

// ============================================================================
//  CoachingRuleRegistry — slice 2d.1 (Trader Journal Core).
//
//  Aggregates a set of <see cref="ICoachingRule"/> implementations. The
//  constructor sorts by <c>Priority</c> ascending so iteration is stable
//  (low-numbered rules fire first when an external driver chooses to
//  short-circuit). For the Wave 2 prompt output we sort the AGGREGATE
//  again by severity desc + OccurredAt desc — that's what the trader
//  dashboard renders.
//
//  The registry is intentionally framework-free (no MediatR, no
//  persistence) so it can be unit-tested with a hand-rolled
//  FakeCoachingRule and so adding a sixth rule in Wave 3 only touches
//  one DI line, not the registry.
// ============================================================================

public sealed class CoachingRuleRegistry
{
    private readonly IReadOnlyList<ICoachingRule> _rules;

    public CoachingRuleRegistry(IEnumerable<ICoachingRule> rules)
    {
        _rules = rules
            .OrderBy(r => r.Priority)
            .ToList();
    }

    /// <summary>
    /// Iterates every rule, collects its prompts, then sorts the
    /// aggregate by <c>Severity</c> descending (high → medium → low)
    /// and ties broken by <c>OccurredAt</c> descending (newest first
    /// within a severity — spec requirement).
    /// </summary>
    public IReadOnlyList<CoachingPrompt> Evaluate(CoachingContext ctx)
    {
        var collected = new List<CoachingPrompt>();
        foreach (var rule in _rules)
        {
            collected.AddRange(rule.Evaluate(ctx));
        }

        return collected
            .OrderByDescending(p => (byte)p.Severity)
            .ThenByDescending(p => p.OccurredAt)
            .ToList();
    }
}
