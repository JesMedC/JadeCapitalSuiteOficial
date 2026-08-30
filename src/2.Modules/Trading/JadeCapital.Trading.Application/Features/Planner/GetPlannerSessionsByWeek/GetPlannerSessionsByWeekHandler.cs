using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.Planner;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Planner;
using JadeCapital.Trading.Domain.Trades;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Planner.GetPlannerSessionsByWeek;

// ============================================================================
//  GetPlannerSessionsByWeekQuery + Handler — slice 3c.
//
//  GET /api/planner/week?week=YYYY-MM-DD  (Monday)
//
//  Devuelve las sesiones del user en la semana ISO del monday dado, mas
//  un payload `comparison` por sesion (actualTradeCount, actualSymbols,
//  followsPlan) y un aggregate PlannerWeekComparisonDto (planned, completed,
//  skipped, actualTrades, totalPnl) sobre TODO el rango.
//
//  Week derivation: ISO 8601 (Monday start). El handler acepta una fecha
//  cualquiera y la normaliza al lunes de su semana ISO via ISOWeek.GetWeekOfYear
//  + delta days. Si el param no parsea, devuelve 422 invalid_week.
//
//  Comparison logic (por sesion):
//   - actualTradeCount: trades abiertos O cerrados el session_date para el user.
//   - actualClosedTradeCount: subset que estan en status Closed.
//   - actualSymbols: distinct symbols de los trades del user en esa fecha.
//   - followsPlan: status == Completed AND actualClosedTradeCount > 0 AND
//     (session.symbol is null OR actualSymbols.Contains(session.symbol)).
//
//  Cross-user: query siempre filtra WHERE user_id = req.UserId. La comparison
//  usa tambien user_id scope (los trades son del mismo user).
// ============================================================================

public sealed record GetPlannerSessionsByWeekQuery(
    Guid UserId,
    LocalDate WeekStart) : IRequest<Result<PlannerWeekDto>>;

public sealed class GetPlannerSessionsByWeekHandler
    : IRequestHandler<GetPlannerSessionsByWeekQuery, Result<PlannerWeekDto>>
{
    private readonly IPlannerSessionRepository _sessions;
    private readonly ITradeRepository _trades;

    public GetPlannerSessionsByWeekHandler(
        IPlannerSessionRepository sessions,
        ITradeRepository trades)
    {
        _sessions = sessions;
        _trades = trades;
    }

    public async Task<Result<PlannerWeekDto>> Handle(
        GetPlannerSessionsByWeekQuery req,
        CancellationToken ct)
    {
        if (!IsMonday(req.WeekStart))
            return Result.Failure<PlannerWeekDto>(TradingDomainErrors.Planner.InvalidWeek);

        var weekEnd = req.WeekStart.AddDays(6);

        // Range as UTC DateTimeOffset for the trade query (ClosedAt is UTC).
        var fromUtc = req.WeekStart.ToDateOnly().ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = weekEnd.ToDateOnly().ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc);

        var sessionList = await _sessions.ListByUserAndWeekAsync(
            req.UserId, req.WeekStart, weekEnd, ct);

        var tradesInWeek = await _trades.ListByUserIdAndOpenedAtRangeAsync(
            req.UserId, fromUtc, toUtc, ct);

        var weekComparison = await _sessions.GetWeekComparisonAsync(
            req.UserId, req.WeekStart, weekEnd, ct);

        var sessionDtos = sessionList
            .OrderBy(s => s.SessionDate)
            .Select(s =>
            {
                var dto = s.ToDto();
                var comp = ComputeComparison(s, tradesInWeek);
                return new PlannerSessionWithComparisonDto(dto, comp);
            })
            .ToList();

        return Result.Success(new PlannerWeekDto(
            req.WeekStart.Iso8601,
            sessionDtos,
            weekComparison));
    }

    /// <summary>
    /// Compara la sesion contra los trades del user en su session_date.
    /// Mismo set de trades que la query rango-semana; aqui filtramos por fecha.
    /// </summary>
    private static PlannerSessionComparisonDto ComputeComparison(
        PlannerSession session, IReadOnlyList<Trade> tradesInWeek)
    {
        var sessionDate = session.SessionDate.ToDateOnly();

        var tradesOnDate = tradesInWeek
            .Where(t => DateOnly.FromDateTime(t.OpenedAt.UtcDateTime) == sessionDate)
            .ToList();

        var actualTradeCount = tradesOnDate.Count;
        var actualClosedTradeCount = tradesOnDate.Count(t => t.Status == TradeStatus.Closed);
        var actualSymbols = tradesOnDate
            .Select(t => t.Symbol.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var followsPlan = session.Status == PlannerStatus.Completed
                          && actualClosedTradeCount > 0
                          && (session.Symbol is null
                              || actualSymbols.Contains(session.Symbol, StringComparer.OrdinalIgnoreCase));

        return new PlannerSessionComparisonDto(
            session.Id,
            (byte)session.Status,
            actualTradeCount,
            actualClosedTradeCount,
            actualSymbols,
            followsPlan);
    }

    private static bool IsMonday(LocalDate d)
    {
        // DayOfWeek: Monday=1..Sunday=0. LocalDate.Year/Month/Day → DateTime.DayOfWeek.
        var dt = new DateTime(d.Year, d.Month, d.Day);
        return dt.DayOfWeek == DayOfWeek.Monday;
    }
}