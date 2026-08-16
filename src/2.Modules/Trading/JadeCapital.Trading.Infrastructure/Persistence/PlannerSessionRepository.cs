using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Contracts.Planner;
using JadeCapital.Trading.Domain.Planner;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Persistence;

// ============================================================================
//  PlannerSessionRepository — slice 3c.
//
//  EF Core implementation. Cross-user safety: cada query acepta userId
//  explicito y filtra por WHERE user_id. GetByIdAsync NO filtra por user
//  (el handler valida ownership y mapea una sesion ajena a NotFound para
//  no leak existencia).
//
//  GetWeekComparisonAsync hace JOIN con trading.trades (solo count + sum de
//  PnL, sin payload completo). La query esta optimizada con indices:
//    - ix_planner_user_date (user_id, session_date)
//    - ix_trades_user_opened_at (existente desde Wave 0).
// ============================================================================

internal sealed class PlannerSessionRepository : IPlannerSessionRepository
{
    private readonly TradingDbContext _db;

    public PlannerSessionRepository(TradingDbContext db)
    {
        _db = db;
    }

    public Task<PlannerSession?> GetByIdAsync(Guid sessionId, CancellationToken ct)
    {
        return _db.PlannerSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
    }

    public async Task<IReadOnlyList<PlannerSession>> ListByUserAndWeekAsync(
        Guid userId, LocalDate weekStart, LocalDate weekEnd, CancellationToken ct)
    {
        return await _db.PlannerSessions
            .Where(s => s.UserId == userId
                     && s.SessionDate >= weekStart
                     && s.SessionDate <= weekEnd)
            .OrderBy(s => s.SessionDate)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsForDateAsync(
        Guid userId, LocalDate sessionDate, CancellationToken ct)
    {
        return await _db.PlannerSessions
            .AnyAsync(s => s.UserId == userId && s.SessionDate == sessionDate, ct);
    }

    public async Task<PlannerWeekComparisonDto> GetWeekComparisonAsync(
        Guid userId, LocalDate weekStart, LocalDate weekEnd, CancellationToken ct)
    {
        // Sessions aggregate (count por status).
        var sessions = await _db.PlannerSessions
            .Where(s => s.UserId == userId
                     && s.SessionDate >= weekStart
                     && s.SessionDate <= weekEnd)
            .Select(s => s.Status)
            .ToListAsync(ct);

        var planned = sessions.Count(s => (byte)s == 1);
        var completed = sessions.Count(s => (byte)s == 2);
        var skipped = sessions.Count(s => (byte)s == 3);
        var cancelled = sessions.Count(s => (byte)s == 4);

        // Trades closed in the week — sum PnL + count.
        var fromUtc = weekStart.ToDateOnly().ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = weekEnd.ToDateOnly().ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc);

        var tradeStats = await _db.Trades
            .Where(t => t.UserId == userId
                     && t.ClosedAt != null
                     && t.ClosedAt >= fromUtc
                     && t.ClosedAt <= toUtc)
            .GroupBy(t => 1)
            .Select(g => new
            {
                Count = g.Count(),
                TotalPnl = g.Sum(t => t.PnL == null ? 0m : t.PnL.Amount)
            })
            .FirstOrDefaultAsync(ct);

        var actualTrades = tradeStats?.Count ?? 0;
        var totalPnl = tradeStats?.TotalPnl ?? 0m;

        return new PlannerWeekComparisonDto(planned, completed, skipped, cancelled, actualTrades, totalPnl);
    }

    public async Task AddAsync(PlannerSession session, CancellationToken ct)
    {
        await _db.PlannerSessions.AddAsync(session, ct);
    }

    public Task UpdateAsync(PlannerSession session, CancellationToken ct)
    {
        _db.PlannerSessions.Update(session);
        return Task.CompletedTask;
    }
}
