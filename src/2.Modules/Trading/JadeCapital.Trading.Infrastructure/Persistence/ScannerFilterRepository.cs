using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Scanner;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Persistence;

internal sealed class ScannerFilterRepository : IScannerFilterRepository
{
    private readonly TradingDbContext _db;
    public ScannerFilterRepository(TradingDbContext db) { _db = db; }

    public Task<ScannerFilter?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.ScannerFilters.FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<ScannerFilter?> GetByUserAndNameAsync(Guid userId, string name, CancellationToken ct)
        => _db.ScannerFilters.FirstOrDefaultAsync(f => f.UserId == userId && f.Name == name, ct);

    public async Task<IReadOnlyList<ScannerFilter>> ListByUserAsync(Guid userId, bool activeOnly, CancellationToken ct)
    {
        var q = _db.ScannerFilters.Where(f => f.UserId == userId);
        if (activeOnly) q = q.Where(f => f.IsActive);
        return await q.OrderBy(f => f.Name).ToListAsync(ct);
    }

    public async Task AddAsync(ScannerFilter filter, CancellationToken ct)
        => await _db.ScannerFilters.AddAsync(filter, ct);

    public Task UpdateAsync(ScannerFilter filter, CancellationToken ct)
    { _db.ScannerFilters.Update(filter); return Task.CompletedTask; }
}
