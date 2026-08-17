using JadeCapital.Trading.Contracts.Strategies;
using JadeCapital.Trading.Domain.Strategies;

namespace JadeCapital.Trading.Application.Abstractions;

// ============================================================================
//  IStrategyRepository — slice 3a (Trader Strategies + Alerts + Planner).
//
//  Cross-user scope: cada Find/List recibe <c>userId</c> explicito y filtra
//  WHERE user_id = @userId. Add/Update reciben el aggregate ya validado;
//  el handler es responsable del ownership y las reglas de negocio
//  (unicidad de name activo via ExistsByNameAsync).
//
//  GetAnalyticsAsync computa aggregates sobre los trades cerrados del
//  usuario para una strategy dada. Se calcula on-read (sin precomputar)
//  porque Wave 3 no requiere materialized views — Wave 4+ introducira
//  cache si algun user pasa de ~10k trades.
// ============================================================================

public interface IStrategyRepository
{
    /// <summary>
    /// Lookup by id sin filtrar por userId. El handler valida el ownership
    /// contra <c>req.UserId</c>; una strategy de otro user mapea a NotFound
    /// (no leak existencia).
    /// </summary>
    Task<Strategy?> GetByIdAsync(Guid strategyId, CancellationToken ct);

    /// <summary>
    /// Lista strategies del user. Si <paramref name="activeOnly"/> es true,
    /// filtra WHERE is_active=true (el caso del FE por default). Si false,
    /// incluye soft-deleted (caso admin).
    /// </summary>
    Task<IReadOnlyList<Strategy>> ListByUserAsync(
        Guid userId, bool activeOnly, CancellationToken ct);

    /// <summary>
    /// Existe una strategy ACTIVA del user con este name (case-insensitive
    /// via lower(name)). El handler lo invoca antes del Create/Update para
    /// enforce la uniqueness invariant (la partial UNIQUE INDEX de la DB
    /// es la red de seguridad contra race conditions).
    /// </summary>
    Task<bool> ExistsByNameAsync(Guid userId, string name, CancellationToken ct);

    /// <summary>
    /// Staggea una strategy nueva para SaveChanges.
    /// </summary>
    Task AddAsync(Strategy strategy, CancellationToken ct);

    /// <summary>
    /// Marca una strategy existente como Modified para SaveChanges. Idempotente
    /// si el aggregate ya esta tracked (la mayoria de las veces porque el
    /// handler lo acabo de cargar via GetByIdAsync).
    /// </summary>
    Task UpdateAsync(Strategy strategy, CancellationToken ct);

    /// <summary>
    /// Computa el aggregate StrategyAnalyticsDto para una strategy del user.
    /// Solo cuenta trades cerrados (Open y Cancelled excluidos).
    /// </summary>
    Task<StrategyAnalyticsDto> GetAnalyticsAsync(
        Guid userId, Guid strategyId, CancellationToken ct);
}