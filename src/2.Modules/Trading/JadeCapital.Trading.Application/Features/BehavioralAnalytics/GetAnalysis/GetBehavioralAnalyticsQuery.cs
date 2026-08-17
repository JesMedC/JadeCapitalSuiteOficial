using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.Behavioral;
using JadeCapital.Trading.Domain.Behavioral;
using MediatR;

namespace JadeCapital.Trading.Application.Features.BehavioralAnalytics.GetAnalysis;

// ============================================================================
//  GetBehavioralAnalyticsQuery — slice 2b.1 (Trader Journal Core).
//
//  GET /api/trades/behavioral?period=7d|30d|90d|all
//
//  Loads the user's closed trades in the window + linked pre-trade
//  checklists, then runs <see cref="BehavioralAnalyzer.Analyze"/> (pure
//  function) and projects the domain result into the wire DTO.
//
//  Window semantics:
//   - 7d / 30d / 90d → windowStart = now - N days, windowEnd = now.
///   - all            → windowStart = DateTimeOffset.MinValue (no lower bound),
//                       windowEnd = now (we cap the upper bound so we don't
//                       project future-closed trades).
//
//  Cross-user scope is enforced at the repository layer
//  (ListClosedByUserIdAsync / ListByUserIdAsync filter by userId).
// ============================================================================

public sealed record GetBehavioralAnalyticsQuery(
    Guid UserId,
    BehavioralPeriod Period) : IRequest<Result<BehavioralAnalysisDto>>;

/// <summary>
/// Loads trades + checklists for the window, runs the analyzer, projects
/// the result to the wire DTO. The window math lives here (not in the
/// analyzer) so the analyzer remains a pure function over an explicit
/// window — that makes the analyzer trivially unit-testable with
/// deterministic fixtures.
/// </summary>
public sealed class GetBehavioralAnalyticsHandler
    : IRequestHandler<GetBehavioralAnalyticsQuery, Result<BehavioralAnalysisDto>>
{
    private readonly ITradeRepository _trades;
    private readonly IPreTradeChecklistRepository _checklists;
    private readonly IClock _clock;

    public GetBehavioralAnalyticsHandler(
        ITradeRepository trades,
        IPreTradeChecklistRepository checklists,
        IClock clock)
    {
        _trades = trades;
        _checklists = checklists;
        _clock = clock;
    }

    public async Task<Result<BehavioralAnalysisDto>> Handle(
        GetBehavioralAnalyticsQuery req,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var (windowStart, windowEnd) = ResolveWindow(req.Period, now);

        // For period=all we bypass the opened_at range query and load
        // closed trades for the whole user history. For N-day windows we
        // still go through the closed-trades-only repo because the
        // analyzer filters by ClosedAt — opened_at filtering would miss
        // trades that opened before the window but closed inside it.
        var closedTrades = await _trades.ListClosedByUserIdAsync(req.UserId, ct);
        var checklists = await _checklists.ListByUserIdAsync(req.UserId, ct);

        var analytics = BehavioralAnalyzer.Analyze(
            closedTrades, checklists, windowStart, windowEnd);

        var dto = analytics.ToDto(req.Period, windowStart, windowEnd);
        return Result.Success(dto);
    }

    private static (DateTimeOffset Start, DateTimeOffset End) ResolveWindow(
        BehavioralPeriod period, DateTimeOffset now)
    {
        var end = now;
        var start = period switch
        {
            BehavioralPeriod.All   => DateTimeOffset.MinValue,
            BehavioralPeriod.Days7  => now.AddDays(-7),
            BehavioralPeriod.Days30 => now.AddDays(-30),
            BehavioralPeriod.Days90 => now.AddDays(-90),
            _                       => now.AddDays(-30), // defensive default → 30d
        };
        return (start, end);
    }
}
