using JadeCapital.Trading.Application.Alerts;
using AlertWire = JadeCapital.Shared.Kernel.Alerts.Alert;

namespace JadeCapital.Trading.UnitTests.Alerts;

// ============================================================================
//  FakeAlertRule + AlertContextFactory — slice 3b test harness.
//
//  Mirrors the FakeCoachingRule / CoachingContextFactory pattern from
//  slice 2d (Trader Journal Core). Lets the registry tests drive a
//  deterministic set of rules without depending on the 5 real rules.
// ============================================================================

internal sealed class FakeAlertRule : IAlertRule
{
    private readonly IReadOnlyList<AlertWire> _alerts;
    private readonly bool _throwOnEvaluate;

    public FakeAlertRule(
        string ruleId,
        int priority,
        IReadOnlyList<AlertWire> alerts,
        bool throwOnEvaluate = false)
    {
        RuleId = ruleId;
        Priority = priority;
        _alerts = alerts;
        _throwOnEvaluate = throwOnEvaluate;
    }

    public string RuleId { get; }
    public int Priority { get; }
    public int EvaluateCount { get; private set; }

    public IReadOnlyList<AlertWire> Evaluate(AlertContext ctx)
    {
        EvaluateCount++;
        if (_throwOnEvaluate)
            throw new InvalidOperationException("FakeAlertRule simulated failure.");
        return _alerts;
    }
}

internal static class AlertContextFactory
{
    public static AlertContext Empty()
        => new(
            UserId: Guid.NewGuid(),
            WindowStart: DateTimeOffset.MinValue,
            WindowEnd: new DateTimeOffset(2026, 8, 18, 14, 0, 0, TimeSpan.Zero),
            ClosedTrades: Array.Empty<Trade>(),
            OpenTrades: Array.Empty<Trade>(),
            RecentJournals: Array.Empty<JournalEntry>(),
            BehavioralAnalytics: null);
}