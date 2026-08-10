using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Trading.Application.Abstractions;

/// <summary>
/// Unit of Work scoped a la BD de Trading.
/// Replicado aqui (en lugar de importarlo de Shared.Kernel/Shared.Infrastructure)
/// para mantener Application libre de dependencias de Infrastructure.
/// </summary>
public interface IUnitOfWork
{
    Task<Result<int>> SaveChangesAsync(CancellationToken ct = default);
}
