using JadeCapital.Shared.Kernel.MarketData;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.MarketData;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Persistence;

internal sealed class QuoteCacheRepository : IQuoteCacheRepository
{
    private readonly TradingDbContext _db;
    private readonly IClock _clock;

    public QuoteCacheRepository(TradingDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Quote?> GetAsync(string symbol, CancellationToken ct)
    {
        var entry = await _db.QuoteCacheEntries.FirstOrDefaultAsync(q => q.Symbol == symbol, ct);
        return entry?.ToQuote();
    }

    public async Task<IReadOnlyList<Quote>> GetManyAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        var list = symbols as IReadOnlyCollection<string> ?? symbols.ToList();
        if (list.Count == 0) return Array.Empty<Quote>();

        var rows = await _db.QuoteCacheEntries
            .Where(q => list.Contains(q.Symbol))
            .ToListAsync(ct);
        return rows.Select(r => r.ToQuote()).ToList();
    }

    public async Task UpsertAsync(Quote quote, CancellationToken ct)
    {
        var existing = await _db.QuoteCacheEntries.FirstOrDefaultAsync(q => q.Symbol == quote.Symbol, ct);
        if (existing is null)
        {
            await _db.QuoteCacheEntries.AddAsync(QuoteCacheEntry.FromQuote(quote, _clock.UtcNow), ct);
        }
        else
        {
            existing.UpdateFrom(quote, _clock.UtcNow);
        }
        await _db.SaveChangesAsync(ct);
    }
}
