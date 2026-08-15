using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Trades;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Queries;

/// <summary>
/// LINQ-to-EF implementation de IMetricsQueryStore.
///
/// Listado:
///   - Filtra por user_id + opened_at (cuando from != null).
///   - OrderBy OpenedAt ASC para que el calculator construya la equity curve
///     en orden cronologico sin re-sort en memoria.
///   - Proyecta las columns PnL (Amount + Currency) que el calculator usa.
///
/// Conteos:
///   - CountByStatusAsync en una sola query con CASE WHEN; evita 3 round-trips
///     cuando el handler necesita Open/Closed/Total.
///   - Cancelled cuenta para Total pero no para las metricas closed-only.
public sealed class MetricsQueryStore : IMetricsQueryStore
{
    private readonly Persistence.TradingDbContext _db;

    public MetricsQueryStore(Persistence.TradingDbContext db) { _db = db; }

    public async Task<IReadOnlyList<Trade>> ListAsync(
        Guid userId,
        DateTimeOffset? from,
        CancellationToken ct)
    {
        IQueryable<Trade> query = _db.Trades
            .AsNoTracking()
            .Where(t => t.UserId == userId);

        if (from is not null)
            query = query.Where(t => t.OpenedAt >= from.Value);

        return await query
            .OrderBy(t => t.OpenedAt)
            .ToListAsync(ct);
    }

    public async Task<(int Open, int Closed, int Total)> CountAsync(
        Guid userId,
        DateTimeOffset? from,
        CancellationToken ct)
    {
        IQueryable<Trade> query = _db.Trades
            .AsNoTracking()
            .Where(t => t.UserId == userId);

        if (from is not null)
            query = query.Where(t => t.OpenedAt >= from.Value);

        // Una sola query con SUM(CASE WHEN status=X THEN 1 ELSE 0 END) — 3 conteos
        // en un solo round-trip. Postgres traduce bien este patron.
        var row = await query
            .GroupBy(t => 1)
            .Select(g => new
            {
                Open = g.Sum(t => t.Status == TradeStatus.Open ? 1 : 0),
                Closed = g.Sum(t => t.Status == TradeStatus.Closed ? 1 : 0),
                Total = g.Count(),
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return (0, 0, 0);
        return (row.Open, row.Closed, row.Total);
    }
}
