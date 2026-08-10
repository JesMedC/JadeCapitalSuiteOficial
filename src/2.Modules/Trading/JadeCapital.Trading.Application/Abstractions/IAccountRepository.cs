using JadeCapital.Trading.Domain.Accounts;

namespace JadeCapital.Trading.Application.Abstractions;

/// <summary>
/// Contrato de persistencia para el aggregate Account.
/// Implementacion EF Core en Infrastructure (TradingDbContext).
/// </summary>
public interface IAccountRepository
{
    Task<Account?> FindByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Lista todas las cuentas del usuario. Ordenadas por CreatedAt descendente
    /// para que la cuenta mas reciente aparezca arriba en el dashboard.
    /// </summary>
    Task<IReadOnlyList<Account>> ListByUserIdAsync(Guid userId, CancellationToken ct);

    Task AddAsync(Account account, CancellationToken ct);

    /// <summary>
    /// Borra fisicamente una cuenta. El handler es responsable de validar
    /// previamente que no tenga trades asociados (la FK en DB es RESTRICT
    /// y dispararia un DbUpdateException sin pre-check).
    /// </summary>
    Task RemoveAsync(Account account, CancellationToken ct);
}
