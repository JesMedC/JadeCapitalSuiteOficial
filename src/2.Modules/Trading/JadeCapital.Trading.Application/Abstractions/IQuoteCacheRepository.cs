using JadeCapital.Shared.Kernel.MarketData;

namespace JadeCapital.Trading.Application.Abstractions;

/// <summary>
/// Read/write access to the <c>trading.quotes_cache</c> table. Wave 4b handlers
/// use this as the fast-path before falling back to <see cref="IQuoteProvider"/>;
/// Wave 4c's <c>QuoteBroadcastService</c> writes through this on every tick.
/// </summary>
public interface IQuoteCacheRepository
{
    Task<Quote?> GetAsync(string symbol, CancellationToken ct);

    Task<IReadOnlyList<Quote>> GetManyAsync(IEnumerable<string> symbols, CancellationToken ct);

    Task UpsertAsync(Quote quote, CancellationToken ct);
}
