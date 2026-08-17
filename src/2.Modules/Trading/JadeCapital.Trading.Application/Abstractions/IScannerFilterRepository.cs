using JadeCapital.Trading.Domain.Scanner;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Trading.Application.Abstractions;

public interface IScannerFilterRepository
{
    Task<ScannerFilter?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<ScannerFilter?> GetByUserAndNameAsync(Guid userId, string name, CancellationToken ct);
    Task<IReadOnlyList<ScannerFilter>> ListByUserAsync(Guid userId, bool activeOnly, CancellationToken ct);
    Task AddAsync(ScannerFilter filter, CancellationToken ct);
    Task UpdateAsync(ScannerFilter filter, CancellationToken ct);
}
