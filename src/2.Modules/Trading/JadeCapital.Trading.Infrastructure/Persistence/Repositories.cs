using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Trades;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Persistence;

public sealed class TradeRepository : ITradeRepository
{
    private readonly TradingDbContext _db;

    public TradeRepository(TradingDbContext db) { _db = db; }

    public Task<Trade?> FindByIdAsync(Guid id, CancellationToken ct)
        => _db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<Trade>> ListByUserIdAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken ct,
        TradeStatus? statusFilter = null,
        string? symbolFilter = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        IQueryable<Trade> query = _db.Trades.Where(t => t.UserId == userId);

        if (statusFilter is not null)
            query = query.Where(t => t.Status == statusFilter.Value);

        if (!string.IsNullOrWhiteSpace(symbolFilter))
        {
            var normalized = symbolFilter.Trim().ToUpperInvariant();
            query = query.Where(t => t.Symbol.Value == normalized);
        }

        // OwnsOne carga los value objects en la misma query — no requiere Include().
        return await query
            .OrderByDescending(t => t.OpenedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public Task<int> CountByUserIdAsync(
        Guid userId,
        CancellationToken ct,
        TradeStatus? statusFilter = null,
        string? symbolFilter = null)
    {
        IQueryable<Trade> query = _db.Trades.Where(t => t.UserId == userId);

        if (statusFilter is not null)
            query = query.Where(t => t.Status == statusFilter.Value);

        if (!string.IsNullOrWhiteSpace(symbolFilter))
        {
            var normalized = symbolFilter.Trim().ToUpperInvariant();
            query = query.Where(t => t.Symbol.Value == normalized);
        }

        return query.CountAsync(ct);
    }

    public async Task<IReadOnlyList<Trade>> ListByUserIdAndOpenedAtRangeAsync(
        Guid userId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        return await _db.Trades
            .Where(t => t.UserId == userId
                && t.OpenedAt >= from
                && t.OpenedAt < to)
            .OrderBy(t => t.OpenedAt)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Trade trade, CancellationToken ct)
        => await _db.Trades.AddAsync(trade, ct);

    public async Task UpdateAsync(Trade trade, CancellationToken ct)
    {
        var entry = _db.Entry(trade);
        if (entry.State == EntityState.Detached)
        {
            _db.Trades.Update(trade);
        }
        await Task.CompletedTask;
    }

    public Task RemoveAsync(Trade trade, CancellationToken ct)
    {
        _db.Trades.Remove(trade);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Unit of Work scoped a la BD de Trading.
/// </summary>
public sealed class TradingUnitOfWork : Application.Abstractions.IUnitOfWork
{
    private readonly TradingDbContext _db;
    public TradingUnitOfWork(TradingDbContext db) { _db = db; }

    public async Task<Shared.Kernel.Results.Result<int>> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            var rows = await _db.SaveChangesAsync(ct);
            return Shared.Kernel.Results.Result<int>.Success(rows);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Shared.Kernel.Results.Result<int>.Failure(
                Shared.Kernel.Results.Error.Conflict("db.concurrency", "Concurrency conflict."));
        }
        catch (DbUpdateException ex)
        {
            return Shared.Kernel.Results.Result<int>.Failure(
                Shared.Kernel.Results.Error.Failure("db.update.failed", ex.Message));
        }
    }
}