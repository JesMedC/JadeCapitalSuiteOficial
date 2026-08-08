using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Application.Abstractions;

/// <summary>
/// Unit of Work (definido en Shared.Kernel, replicado aqui para que Application
/// no dependa de Shared.Infrastructure — ambos comparten la misma forma).
/// </summary>
public interface IUnitOfWork
{
    Task<Result<int>> SaveChangesAsync(CancellationToken ct = default);
}