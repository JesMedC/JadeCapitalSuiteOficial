using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Persistence;

/// <summary>
/// EF Core-backed implementation of <see cref="IImportJobRepository"/>.
/// Single-DBContext design; cross-user isolation is the handler's responsibility.
/// </summary>
public sealed class ImportJobRepository : IImportJobRepository
{
    private readonly TradingDbContext _db;
    public ImportJobRepository(TradingDbContext db) { _db = db; }

    public Task<ImportJob?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.ImportJobs.FirstOrDefaultAsync(j => j.Id == id, ct);

    public Task<ImportJob?> FindActiveBySha256Async(Guid userId, string sha256, CancellationToken ct)
        => _db.ImportJobs
            .Where(j => j.UserId == userId
                && j.FileSha256 == sha256
                && (j.Status == ImportJobStatus.Pending
                    || j.Status == ImportJobStatus.InProgress
                    || j.Status == ImportJobStatus.Completed))
            .OrderByDescending(j => j.StartedAt)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(ImportJob job, CancellationToken ct)
        => await _db.ImportJobs.AddAsync(job, ct);

    public Task UpdateAsync(ImportJob job, CancellationToken ct)
    {
        var entry = _db.Entry(job);
        if (entry.State == EntityState.Detached)
            _db.ImportJobs.Update(job);
        return Task.CompletedTask;
    }
}