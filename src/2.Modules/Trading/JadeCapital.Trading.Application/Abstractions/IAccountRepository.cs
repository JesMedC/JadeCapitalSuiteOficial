using JadeCapital.Shared.Kernel.Repository;
using JadeCapital.Trading.Domain.Accounts;

namespace JadeCapital.Trading.Application.Abstractions;

/// <summary>
/// Contrato de persistencia para el aggregate Account.
/// Implementacion EF Core en Infrastructure (TradingDbContext).
/// </summary>
/// <remarks>
/// Wave 8 slice 8a.1 — BREAKING rename from <c>RemoveAsync</c> to
/// <c>DeleteAsync</c> to align with the canonical
/// <c>IRepository&lt;T&gt;.DeleteAsync(T, ct)</c> surface from
/// <c>Shared.Kernel/Repository/IRepository.cs</c>. No <c>[Obsolete]</c>,
/// no overload, no deprecation period. The single handler call site
/// (<c>DeleteAccountHandler</c>) is updated atomically in the same slice.
///
/// The interface also extends <see cref="IRepository{T}"/> gaining the
/// canonical generic CRUD surface (<c>GetByIdAsync</c> + <c>AddAsync</c> +
/// <c>UpdateAsync</c> + <c>DeleteAsync</c>). The bespoke
/// <c>FindByIdAsync(Guid, ct)</c> + <c>ListByUserIdAsync(Guid, ct)</c>
/// methods stay on the interface for backwards compatibility with the
/// existing handler call sites (mirrors the Wave 7 7b.1 ITradeRepository
/// precedent — bespoke reads stay, generic CRUD is gained).
/// </remarks>
public interface IAccountRepository : IRepository<Account>
{
    /// <summary>
    /// Single-fetch by primary key. Bespoke signature — kept on the
    /// interface for backwards compatibility with the existing handlers
    /// that take a userId-scoped lookup path (the canonical
    /// <see cref="IRepository{T}.GetByIdAsync"/> is also exposed by the
    /// base).
    /// </summary>
    Task<Account?> FindByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Lista todas las cuentas del usuario. Ordenadas por CreatedAt descendente
    /// para que la cuenta mas reciente aparezca arriba en el dashboard.
    /// </summary>
    Task<IReadOnlyList<Account>> ListByUserIdAsync(Guid userId, CancellationToken ct);
}