using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Application.Coaching;
using JadeCapital.Trading.Application.Coaching.Rules;
using JadeCapital.Trading.Contracts.Coaching;
using JadeCapital.Trading.Domain.Behavioral;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Coaching.GetPrompts;

// ============================================================================
//  GetCoachingPromptsQuery — slice 2d.1 (Trader Journal Core).
//
//  GET /api/coaching/prompts?period=7d|30d|90d|all
//
//  Loads the user's trades + journal entries in the window, runs the
//  in-memory <see cref="BehavioralAnalyzer"/> to detect behavioral
//  events, then runs the <see cref="CoachingRuleRegistry"/> over the
//  result. Cross-user scope enforced at the repository layer (each
//  List*Async filters by userId).
//
//  Window semantics match <see cref="GetBehavioralAnalyticsQuery"/>:
//   - 7d / 30d / 90d → windowStart = now - N days, windowEnd = now.
//   - all            → windowStart = DateTimeOffset.MinValue,
//                       windowEnd = now.
//
//  Empty history → 200 OK with `{ period, prompts: [] }`. Missing rules
//  in the registry → still a 200 with the prompts the registered ones
//  emitted (the rule set is the source of truth for "what is available").
// ============================================================================

public sealed record GetCoachingPromptsQuery(
    Guid UserId,
    CoachingPeriod Period) : IRequest<Result<CoachingPromptsDto>>;

/// <summary>
/// One-shot handler: load (trades, journals, behavioral events), run
/// registry, project to wire DTO.
/// </summary>
public sealed class GetCoachingPromptsHandler
    : IRequestHandler<GetCoachingPromptsQuery, Result<CoachingPromptsDto>>
{
    private readonly ITradeRepository _trades;
    private readonly IJournalEntryRepository _journals;
    private readonly IPreTradeChecklistRepository _checklists;
    private readonly IClock _clock;

    public GetCoachingPromptsHandler(
        ITradeRepository trades,
        IJournalEntryRepository journals,
        IPreTradeChecklistRepository checklists,
        IClock clock)
    {
        _trades = trades;
        _journals = journals;
        _checklists = checklists;
        _clock = clock;
    }

    public async Task<Result<CoachingPromptsDto>> Handle(
        GetCoachingPromptsQuery req,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var (windowStart, windowEnd) = ResolveWindow(req.Period, now);

        // Load all of the user's closed trades; the analyzer + rules
        // filter by ClosedAt in memory (consistent with the behavioral
        // analytics handler in slice 2b).
        var closedTrades = await _trades.ListClosedByUserIdAsync(req.UserId, ct);

        // Pull journal entries for the window. The repository's range
        // method takes LocalDate bounds; for the broad windows the
        // caller typically passes "today" as a single-day query from
        // PreMarketPlanMissRule via the rule's own logic. We pass the
        // window's calendar boundaries in UTC for simplicity (the
        // rule filters further by trading calendar date inside
        // PreMarketPlanMissRule).
        var journals = await LoadJournalsForWindow(req.UserId, windowStart, windowEnd, req.Period, ct);

        var checklists = await _checklists.ListByUserIdAsync(req.UserId, ct);

        var analyticsResult = BehavioralAnalyzer.Analyze(
            closedTrades,
            checklists,
            windowStart,
            windowEnd);

        var context = new CoachingContext(
            UserId: req.UserId,
            WindowStart: windowStart,
            WindowEnd: windowEnd,
            Trades: closedTrades,
            Journals: journals,
            BehavioralEvents: analyticsResult.Events);

        // The registry is constructed inline because there is no DI
        // graph for ICoachingRule (Wave 2 keeps the 5 rules concretely
        // enumerated to keep the rule list discoverable from a single
        // spot — easier than wiring 5 AddSingleton lines in the host).
        var registry = BuildRegistry();
        var prompts = registry.Evaluate(context);

        var dto = CoachingMapping.ToDto(prompts, req.Period);
        return Result.Success(dto);
    }

    private static CoachingRuleRegistry BuildRegistry()
        => new(new ICoachingRule[]
        {
            new TiltSequenceRule(),
            new RevengeTradeRule(),
            new OvertradingDayRule(),
            new LongBreakRule(),
            new PreMarketPlanMissRule(),
        });

    private static (DateTimeOffset Start, DateTimeOffset End) ResolveWindow(
        CoachingPeriod period, DateTimeOffset now)
    {
        var end = now;
        var start = period switch
        {
            CoachingPeriod.All   => DateTimeOffset.MinValue,
            CoachingPeriod.Days7  => now.AddDays(-7),
            CoachingPeriod.Days30 => now.AddDays(-30),
            CoachingPeriod.Days90 => now.AddDays(-90),
            _                    => now.AddDays(-30),
        };
        return (start, end);
    }

    /// <summary>
    /// Loads the journals for the window. For multi-day windows we
    /// request the full historical range; the PreMarketPlanMissRule
    /// does its own date matching against the trades' calendar dates.
    /// </summary>
    private async Task<IReadOnlyList<JadeCapital.Trading.Domain.Journal.JournalEntry>> LoadJournalsForWindow(
        Guid userId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CoachingPeriod period,
        CancellationToken ct)
    {
        // For All/period we want the full journal history; for N-day
        // windows we cap at today so PreMarketPlanMissRule can find
        // today's entry. We use a 60-day backward cap for the narrow
        // windows because the window might be 7d but the user could
        // legitimately have a plan that's 3 days old.
        var upper = windowEnd.UtcDateTime;
        var lowerDaysBack = period switch
        {
            CoachingPeriod.Days7  => 30,
            CoachingPeriod.Days30 => 60,
            CoachingPeriod.Days90 => 120,
            CoachingPeriod.All    => 365 * 5,
            _                     => 60,
        };
        var lower = upper.AddDays(-lowerDaysBack);
        var from = JadeCapital.Shared.Kernel.Time.LocalDate.From(DateOnly.FromDateTime(lower));
        var to = JadeCapital.Shared.Kernel.Time.LocalDate.From(DateOnly.FromDateTime(upper));
        return await _journals.ListByRangeAsync(userId, from, to, ct);
    }
}
