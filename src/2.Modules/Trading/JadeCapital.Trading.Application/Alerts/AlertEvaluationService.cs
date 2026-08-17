using JadeCapital.Shared.Kernel.Alerts;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Alerts;
using JadeCapital.Trading.Domain.Behavioral;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Journal;
using JadeCapital.Trading.Domain.PreTradeChecklists;
using JadeCapital.Trading.Domain.Trades;
using Microsoft.Extensions.Logging;
using SeverityEnum = JadeCapital.Trading.Domain.Behavioral.Severity;

namespace JadeCapital.Trading.Application.Alerts;

// ============================================================================
//  AlertEvaluationService — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Per-user alert evaluation. Loads the context for one user, runs the
//  registry, persists the emitted alerts (dedup happens at the DB layer
//  via the partial UNIQUE INDEX on (user_id, rule_id, UTC-date)).
//
//  Used by:
//   - AlertEvaluationBackgroundService (5-minute tick, all active users)
//   - POST /api/alerts/_internal/run-now (DEV-ONLY single-user trigger)
//
//  Failure isolation: a per-user failure is logged and skipped — the
//  BackgroundService keeps running for the rest of the active users.
// ============================================================================

public sealed class AlertEvaluationService
{
    private readonly AlertRegistry _registry;
    private readonly IAlertRepository _alerts;
    private readonly ITradeRepository _trades;
    private readonly IJournalEntryRepository _journals;
    private readonly IPreTradeChecklistRepository _checklists;
    private readonly IClock _clock;
    private readonly ILogger<AlertEvaluationService> _logger;

    public AlertEvaluationService(
        AlertRegistry registry,
        IAlertRepository alerts,
        ITradeRepository trades,
        IJournalEntryRepository journals,
        IPreTradeChecklistRepository checklists,
        IClock clock,
        ILogger<AlertEvaluationService> logger)
    {
        _registry = registry;
        _alerts = alerts;
        _trades = trades;
        _journals = journals;
        _checklists = checklists;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Iterates every active user and evaluates their alerts. Total
    /// inserted alerts across all users. The caller (BackgroundService)
    /// MUST log per-user failures; this method is the per-user variant
    /// and is already failure-isolated.
    /// </summary>
    public async Task<int> EvaluateAllActiveUsersAsync(
        JadeCapital.Identity.Contracts.Projections.IActiveUserIdsReader users,
        CancellationToken ct)
    {
        var userIds = await users.ListActiveUserIdsAsync(ct);
        int total = 0;
        foreach (var userId in userIds)
        {
            if (ct.IsCancellationRequested) break;
            total += await EvaluateForUserAsync(userId, ct);
        }
        return total;
    }

    /// <summary>
    /// Evaluates one user. Returns the number of alerts newly persisted
    /// (post-dedup; the registry may emit more than the DB accepts).
    /// </summary>
    public async Task<int> EvaluateForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        // 30-day window for closed trades + behavioral analytics; 7-day for journals.
        var closedWindowStart = now.AddDays(-30);
        var journalWindowStart = now.AddDays(-7);

// Load context. The repository reads may throw — we wrap the whole
            // method in a try/catch so the BackgroundService survives per-user
            // failures (per spec scenario "Service survives transient errors").
            //
            // Important: we MUST serialize the reads because the scoped
            // TradingDbContext is not thread-safe — Task.WhenAll on parallel
            // repo calls against the same DbContext throws "A second operation
            // was started on this context". Sequential await is correct here.
            try
            {
                var closedTrades = await _trades.ListClosedByUserIdAsync(userId, ct);
                var openTrades = await _trades.ListByUserIdAsync(userId, 1, 100, ct, statusFilter: TradeStatus.Open);
                var journals = await _journals.ListByRangeAsync(userId,
                    JadeCapital.Shared.Kernel.Time.LocalDate.From(DateOnly.FromDateTime(journalWindowStart.UtcDateTime)),
                    JadeCapital.Shared.Kernel.Time.LocalDate.From(DateOnly.FromDateTime(now.UtcDateTime)),
                    ct);
                var checklists = await _checklists.ListByUserIdAsync(userId, ct);

            BehavioralAnalyticsResult? analytics = null;
            if (closedTrades.Count > 0 || checklists.Count > 0)
            {
                analytics = BehavioralAnalyzer.Analyze(
                    closedTrades,
                    checklists,
                    closedWindowStart,
                    now);
            }

            var ctx = new AlertContext(
                UserId: userId,
                WindowStart: closedWindowStart,
                WindowEnd: now,
                ClosedTrades: closedTrades,
                OpenTrades: openTrades,
                RecentJournals: journals,
                BehavioralAnalytics: analytics);

            var emitted = _registry.Evaluate(ctx);

            int inserted = 0;
            foreach (var wire in emitted)
            {
                var createdResult = Domain.Alerts.Alert.Create(
                    userId: userId,
                    ruleId: wire.RuleId,
                    severity: ToDomainSeverity(wire.Severity),
                    title: wire.Title,
                    body: wire.Body,
                    ctaRoute: wire.Cta.Route,
                    ctaLabel: wire.Cta.Label,
                    expiresAt: wire.ExpiresAt,
                    clock: _clock);
                if (createdResult.IsFailure)
                {
                    _logger.LogWarning(
                        "Alert rule {RuleId} produced an invalid alert for user {UserId}: {Error}",
                        wire.RuleId, userId, createdResult.Error.Code);
                    continue;
                }
                var ok = await _alerts.AddAsync(createdResult.Value, ct);
                if (ok) inserted++;
            }
            return inserted;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Alert evaluation failed for user {UserId}; continuing with next user.",
                userId);
            return 0;
        }
    }

    private static SeverityEnum ToDomainSeverity(JadeCapital.Shared.Kernel.Coaching.Severity s)
        => (SeverityEnum)(byte)s;
}