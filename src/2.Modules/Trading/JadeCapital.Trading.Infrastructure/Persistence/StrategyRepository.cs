using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Contracts.Strategies;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Strategies;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Persistence;

// ============================================================================
//  StrategyRepository — slice 3a (Trader Strategies + Alerts + Planner).
//
//  Implementacion EF Core de IStrategyRepository. Cross-user scope: cada
//  Find/List recibe userId explicito y filtra WHERE user_id = @userId.
//
//  GetAnalyticsAsync computa aggregates on-read sobre los trades cerrados
//  del user con strategy_id = X. Solo Closed (Open y Cancelled excluidos).
//  Si Wave 4+ algun user pasa de ~10k trades linkeados a una strategy,
//  introducimos cursor pagination o materialized views.
// ============================================================================

public sealed class StrategyRepository : IStrategyRepository
{
    private readonly TradingDbContext _db;

    public StrategyRepository(TradingDbContext db) { _db = db; }

    public Task<Strategy?> GetByIdAsync(Guid strategyId, CancellationToken ct)
        => _db.Strategies.FirstOrDefaultAsync(s => s.Id == strategyId, ct);

    public async Task<IReadOnlyList<Strategy>> ListByUserAsync(
        Guid userId, bool activeOnly, CancellationToken ct)
    {
        IQueryable<Strategy> query = _db.Strategies.Where(s => s.UserId == userId);

        if (activeOnly)
            query = query.Where(s => s.IsActive);

        return await query
            .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsByNameAsync(Guid userId, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var normalized = name.Trim().ToLowerInvariant();

        // EF no soporta lower() directamente — materializamos la lista
        // activa del user y comparamos en memoria. Set chico (esperado:
        // < 50 strategies activas por user); el partial UNIQUE INDEX en
        // la DB es la red de seguridad contra races.
        var activeRows = await _db.Strategies
            .Where(s => s.UserId == userId && s.IsActive)
            .Select(s => s.Name)
            .ToListAsync(ct);

        return activeRows.Any(n => string.Equals(n, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public async Task AddAsync(Strategy strategy, CancellationToken ct)
        => await _db.Strategies.AddAsync(strategy, ct);

    public async Task UpdateAsync(Strategy strategy, CancellationToken ct)
    {
        var entry = _db.Entry(strategy);
        if (entry.State == EntityState.Detached)
        {
            _db.Strategies.Update(strategy);
        }
        await Task.CompletedTask;
    }

    public async Task<StrategyAnalyticsDto> GetAnalyticsAsync(
        Guid userId, Guid strategyId, CancellationToken ct)
    {
        // Strategy debe pertenecer al user. Si no, devolvemos empty
        // (el handler ya valido ownership, asi que esto es defense in depth).
        var strategy = await _db.Strategies
            .FirstOrDefaultAsync(s => s.Id == strategyId && s.UserId == userId, ct);
        if (strategy is null)
        {
            return new StrategyAnalyticsDto(
                strategyId, string.Empty,
                0, 0, 0, 0m, 0m, 0m, 0m, null, null, null);
        }

        var closedTrades = await _db.Trades
            .Where(t => t.UserId == userId
                && t.StrategyId == strategyId
                && t.Status == TradeStatus.Closed
                && t.PnL != null)
            .ToListAsync(ct);

        if (closedTrades.Count == 0)
        {
            return new StrategyAnalyticsDto(
                strategyId, strategy.Name,
                TradeCount: 0,
                WinCount: 0, LossCount: 0,
                WinRate: 0m, TotalPnl: 0m, Expectancy: 0m,
                ProfitFactor: 0m, AvgMfe: null, AvgMae: null,
                LastTradeAt: null);
        }

        // Aggregate in-memory. Set chico esperado (< 200 trades por strategy).
        var winners = closedTrades.Where(t => t.PnL!.Amount > 0).ToList();
        var losers = closedTrades.Where(t => t.PnL!.Amount <= 0).ToList();
        var winSum = winners.Sum(t => t.PnL!.Amount);
        var lossSumAbs = losers.Sum(t => Math.Abs(t.PnL!.Amount));
        var totalPnl = closedTrades.Sum(t => t.PnL!.Amount);

        var profitFactor = lossSumAbs > 0
            ? Math.Round(winSum / lossSumAbs, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var avgMfe = closedTrades.Where(t => t.MfeAmount.HasValue).Any()
            ? Math.Round(closedTrades.Where(t => t.MfeAmount.HasValue).Average(t => t.MfeAmount!.Value), 2, MidpointRounding.AwayFromZero)
            : (decimal?)null;

        var avgMae = closedTrades.Where(t => t.MaeAmount.HasValue).Any()
            ? Math.Round(closedTrades.Where(t => t.MaeAmount.HasValue).Average(t => t.MaeAmount!.Value), 2, MidpointRounding.AwayFromZero)
            : (decimal?)null;

        return new StrategyAnalyticsDto(
            strategyId, strategy.Name,
            TradeCount: closedTrades.Count,
            WinCount: winners.Count,
            LossCount: losers.Count,
            WinRate: Math.Round((decimal)winners.Count / closedTrades.Count, 4, MidpointRounding.AwayFromZero),
            TotalPnl: Math.Round(totalPnl, 2, MidpointRounding.AwayFromZero),
            Expectancy: Math.Round(totalPnl / closedTrades.Count, 2, MidpointRounding.AwayFromZero),
            ProfitFactor: profitFactor,
            AvgMfe: avgMfe,
            AvgMae: avgMae,
            LastTradeAt: closedTrades.Max(t => t.ClosedAt));
    }
}