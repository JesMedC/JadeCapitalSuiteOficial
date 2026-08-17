using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Contracts.Planner;
using JadeCapital.Trading.Domain.Planner;

namespace JadeCapital.Trading.Application.Abstractions;

// ============================================================================
//  IPlannerSessionRepository — slice 3c (Trader Strategies + Alerts + Planner).
//
//  Cross-user scope: cada metodo recibe userId explicito y filtra
//  WHERE user_id = @userId. No hay leak: una sesion de otro user es null
//  en GetByIdAsync y vacia en ListByUserAndWeekAsync.
//
//  GetWeekComparisonAsync computa aggregates on-read: count por status +
//  count + sum(PnL) de los trades cerrados del user en el rango. Esto vive
//  en el repo porque la query cruza dos tablas (planner_sessions + trades).
//  El "comparison" per-session (actualTradeCount, actualSymbols, followsPlan)
//  se computa en el handler con el mismo patron pero por sesion individual.
// ============================================================================

public interface IPlannerSessionRepository
{
    /// <summary>
    /// Lookup por id. NO filtra por userId — el handler valida el ownership
    /// contra req.UserId y mapea una sesion ajena a NotFound (no leak existencia).
    /// </summary>
    Task<PlannerSession?> GetByIdAsync(Guid sessionId, CancellationToken ct);

    /// <summary>
    /// Lista las sesiones del user en el rango [weekStart, weekEnd] (inclusive).
    /// weekStart debe ser un lunes (ISO week). Ordenadas por session_date asc.
    /// </summary>
    Task<IReadOnlyList<PlannerSession>> ListByUserAndWeekAsync(
        Guid userId, LocalDate weekStart, LocalDate weekEnd, CancellationToken ct);

    /// <summary>
    /// True si ya existe una sesion del user en la fecha dada. El handler lo
    /// invoca antes del AddAsync para enforce la uniqueness invariant
    /// (la UNIQUE INDEX de la DB es la red de seguridad contra races).
    /// </summary>
    Task<bool> ExistsForDateAsync(
        Guid userId, LocalDate sessionDate, CancellationToken ct);

    /// <summary>
    /// Computa el aggregate PlannerWeekComparisonDto sobre las sesiones del
    /// user en la semana + los trades cerrados en el mismo rango. Incluye
    /// actualTrades y totalPnl que vienen del JOIN con trading.trades.
    /// </summary>
    Task<PlannerWeekComparisonDto> GetWeekComparisonAsync(
        Guid userId, LocalDate weekStart, LocalDate weekEnd, CancellationToken ct);

    /// <summary>
    /// Stagea una sesion nueva para SaveChanges.
    /// </summary>
    Task AddAsync(PlannerSession session, CancellationToken ct);

    /// <summary>
    /// Marca una sesion existente como Modified para SaveChanges. Idempotente
    /// si el aggregate ya esta tracked.
    /// </summary>
    Task UpdateAsync(PlannerSession session, CancellationToken ct);
}