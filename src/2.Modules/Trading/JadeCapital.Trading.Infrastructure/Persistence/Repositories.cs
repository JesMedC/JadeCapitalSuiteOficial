using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Accounts;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Instruments;
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
        string? symbolFilter = null,
        Guid? accountIdFilter = null)
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

        if (accountIdFilter is not null && accountIdFilter.Value != Guid.Empty)
            query = query.Where(t => t.AccountId == accountIdFilter.Value);

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
        string? symbolFilter = null,
        Guid? accountIdFilter = null)
    {
        IQueryable<Trade> query = _db.Trades.Where(t => t.UserId == userId);

        if (statusFilter is not null)
            query = query.Where(t => t.Status == statusFilter.Value);

        if (!string.IsNullOrWhiteSpace(symbolFilter))
        {
            var normalized = symbolFilter.Trim().ToUpperInvariant();
            query = query.Where(t => t.Symbol.Value == normalized);
        }

        if (accountIdFilter is not null && accountIdFilter.Value != Guid.Empty)
            query = query.Where(t => t.AccountId == accountIdFilter.Value);

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

    public async Task<IReadOnlyList<Trade>> ListClosedByUserIdAsync(
        Guid userId,
        CancellationToken ct)
    {
        return await _db.Trades
            .Where(t => t.UserId == userId && t.Status == TradeStatus.Closed)
            .OrderBy(t => t.ClosedAt)
            .ToListAsync(ct);
    }

    public Task<int> CountByInstrumentIdAsync(Guid instrumentId, CancellationToken ct)
        => _db.Trades.CountAsync(t => t.InstrumentId == instrumentId, ct);

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

    public Task DeleteAsync(Trade trade, CancellationToken ct)
    {
        _db.Trades.Remove(trade);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Repositorio de Accounts. Sin eventos ni navegaciones: cada Account vive
/// aislada (los trades son FKs via shadow navigation en TradeConfiguration).
/// </summary>
public sealed class AccountRepository : IAccountRepository
{
    private readonly TradingDbContext _db;

    public AccountRepository(TradingDbContext db) { _db = db; }

    public Task<Account?> FindByIdAsync(Guid id, CancellationToken ct)
        => _db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct);

    /// <summary>
    /// Wave 8 slice 8a.1 — canonical <see cref="JadeCapital.Shared.Kernel.Repository.IRepository{T}.GetByIdAsync"/>
    /// contributed by the <c>IRepository&lt;Account&gt;</c> extension.
    /// Forwards to the bespoke <see cref="FindByIdAsync"/> impl so the
    /// audit decorator (which calls <c>GetByIdAsync</c> for the
    /// pre-mutation snapshot via <c>DecoratedRepository&lt;T&gt;</c>) hits
    /// the same SQL path.
    /// </summary>
    public Task<Account?> GetByIdAsync(Guid id, CancellationToken ct)
        => FindByIdAsync(id, ct);

    public async Task<IReadOnlyList<Account>> ListByUserIdAsync(Guid userId, CancellationToken ct)
        => await _db.Accounts
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(Account account, CancellationToken ct)
        => await _db.Accounts.AddAsync(account, ct);

    public async Task UpdateAsync(Account account, CancellationToken ct)
    {
        // Wave 8 slice 8a.1 — UpdateAsync contributed by the
        // IRepository<Account> extension. Mirrors the TradeRepository
        // UpdateAsync pattern: attach the detached entity via the change
        // tracker so EF emits the UPDATE SQL with the post-mutation
        // values (ChangeTracker.OriginalValues is then available to the
        // AccountAuditDecorator for the pre-mutation diff).
        var entry = _db.Entry(account);
        if (entry.State == EntityState.Detached)
        {
            _db.Accounts.Update(account);
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// Wave 8 slice 8a.1 — BREAKING rename from <c>RemoveAsync</c> to
    /// <c>DeleteAsync</c>. The body is identical to the old
    /// <c>RemoveAsync</c> (mark the entity as Deleted in the change
    /// tracker; the caller is responsible for SaveChanges). The Account
    /// table has no soft-delete flag, so this is a hard delete — the
    /// handler is responsible for the pre-check of associated trades
    /// (FK RESTRICT in DB).
    /// </summary>
    public Task DeleteAsync(Account account, CancellationToken ct)
    {
        _db.Accounts.Remove(account);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Repositorio de Instruments. Symbol es VO OwnsOne + value converter;
/// el lookup por symbol normaliza a mayusculas para matchear la columna.
/// </summary>
public sealed class InstrumentRepository : IInstrumentRepository
{
    private readonly TradingDbContext _db;

    public InstrumentRepository(TradingDbContext db) { _db = db; }

    public Task<Instrument?> FindByIdAsync(Guid id, CancellationToken ct)
        => _db.Instruments.FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<Instrument?> FindBySymbolAsync(string symbol, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;
        var normalized = symbol.Trim().ToUpperInvariant();
        return await _db.Instruments
            .FirstOrDefaultAsync(i => i.Symbol.Value == normalized, ct);
    }

    public async Task<IReadOnlyList<Instrument>> ListActiveAsync(CancellationToken ct)
    {
        // OrderBy en client side: Symbol.Value no es traduc por EF (es property
        // derivada via value converter). AsEnumerable() mueve la query a memoria.
        var rows = await _db.Instruments
            .Where(i => i.IsActive)
            .ToListAsync(ct);
        return rows.OrderBy(i => i.Symbol.Value).ToList();
    }

    public async Task<IReadOnlyList<Instrument>> ListAllAsync(CancellationToken ct)
    {
        var rows = await _db.Instruments.ToListAsync(ct);
        return rows.OrderBy(i => i.Symbol.Value).ToList();
    }

    public async Task AddAsync(Instrument instrument, CancellationToken ct)
        => await _db.Instruments.AddAsync(instrument, ct);

    public Task RemoveAsync(Instrument instrument, CancellationToken ct)
    {
        _db.Instruments.Remove(instrument);
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