using JadeCapital.Shared.Kernel.Repository;
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
//
//  Wave 7 slice 7b.1 — extends IRepository<Strategy> for the audit decorator
//  pipeline. DeleteAsync is a defensive STUB — the canonical Strategy
//  mutation surface is Strategy.Update + Strategy.Deactivate(clock), NOT
//  a hard delete. See StrategyAuditDecorator for the audit emission.
// ============================================================================

public interface IStrategyRepository : IRepository<Strategy>
{
    // GetByIdAsync, AddAsync, UpdateAsync are inherited from IRepository<Strategy>
    // (Wave 7 slice 7b.1 extension — same precedent as IUserRepository in 7a.1).

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
    /// Computa el aggregate StrategyAnalyticsDto para una strategy del user.
    /// Solo cuenta trades cerrados (Open y Cancelled excluidos).
    /// </summary>
    Task<StrategyAnalyticsDto> GetAnalyticsAsync(
        Guid userId, Guid strategyId, CancellationToken ct);

    /// <summary>
    /// NOT SUPPORTED. Strategy deletion is not a valid operation — use
    /// <c>Strategy.Deactivate(clock)</c> followed by <c>UpdateAsync</c> to
    /// flip <c>IsActive</c> from <c>true</c> to <c>false</c>. The decorator
    /// emits <see cref="Audit.AuditAction.Failed"/> before re-throwing.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// Always thrown. The concrete impl is a defensive stub.
    /// </exception>
    new Task DeleteAsync(Strategy strategy, CancellationToken ct);
}