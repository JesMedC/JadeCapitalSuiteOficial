using JadeCapital.Shared.Kernel.Alerts;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Alerts;

// ============================================================================
//  AlertRegistry — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Aggregates a set of <see cref="IAlertRule"/> implementations. The
//  constructor sorts by <c>Priority</c> ascending so iteration is stable.
//
//  Behavior:
//   - Evaluate iterates every rule, collects alerts, then sorts the
//     aggregate by Severity DESCENDING + CreatedAt DESCENDING (per spec
//     requirement "Severity ordering": high first, low last; within a
//     severity newer alerts come first).
//   - ALWAYS wraps each rule.Evaluate in try/catch so a single failure
//     does not crash the BackgroundService or skip the rest (per spec
//     scenario "Service survives transient errors"). The exception is
//     logged at Warning level.
//   - Returns a non-null, never-null empty list. Missing rules → empty
//     list, not null.
//
//  Intentional non-responsibilities:
//   - Does NOT persist alerts — that's AlertEvaluationService.
//   - Does NOT dedup — the repository's INSERT ... ON CONFLICT DO NOTHING
//     handles that at the DB layer.
// ============================================================================

public sealed class AlertRegistry
{
    private readonly IReadOnlyList<IAlertRule> _rules;
    private readonly ILogger<AlertRegistry> _logger;

    public AlertRegistry(IEnumerable<IAlertRule> rules, ILogger<AlertRegistry> logger)
    {
        _rules = rules.OrderBy(r => r.Priority).ToList();
        _logger = logger;
    }

    /// <summary>
    /// Iterates every rule (in <c>Priority</c> order), collects alerts,
    /// then sorts the aggregate by Severity desc + CreatedAt desc. A rule
    /// that throws is logged and skipped (per spec "transient errors"
    /// requirement).
    /// </summary>
    public IReadOnlyList<Alert> Evaluate(AlertContext ctx)
    {
        var collected = new List<Alert>();
        foreach (var rule in _rules)
        {
            try
            {
                collected.AddRange(rule.Evaluate(ctx));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Alert rule {RuleId} failed for user {UserId}; continuing with next rule.",
                    rule.RuleId, ctx.UserId);
            }
        }

        return collected
            .OrderByDescending(a => (byte)a.Severity)
            .ThenByDescending(a => a.ExpiresAt ?? DateTimeOffset.MinValue)
            .ToList();
    }
}