using JadeCapital.Shared.Kernel.MarketData;
using JadeCapital.Shared.Kernel.Primitives;

namespace JadeCapital.Trading.Domain.MarketData;

/// <summary>
/// Persistence snapshot of the latest <see cref="Quote"/> per symbol.
/// Stored in <c>trading.quotes_cache</c>. The repo maps to/from
/// <see cref="Quote"/>; the entity stays minimal so the cache row mirrors
/// the table columns one-to-one.
/// </summary>
public sealed class QuoteCacheEntry : Entity<string>
{
    public string Symbol { get; private set; } = default!;
    public decimal Bid { get; private set; }
    public decimal Ask { get; private set; }
    public decimal Spread { get; private set; }
    public decimal Volume24h { get; private set; }
    public QuoteSource Source { get; private set; }
    public DateTimeOffset CachedAt { get; private set; }

    private QuoteCacheEntry() { }

    public static QuoteCacheEntry FromQuote(Quote quote, DateTimeOffset cachedAt)
        => new(quote, cachedAt);

    private QuoteCacheEntry(Quote quote, DateTimeOffset cachedAt) : base(quote.Symbol)
    {
        Symbol = quote.Symbol;
        Bid = quote.Bid;
        Ask = quote.Ask;
        Spread = quote.Spread;
        Volume24h = quote.Volume24h;
        Source = quote.Source;
        CachedAt = cachedAt;
    }

    public Quote ToQuote() => new(Symbol, Bid, Ask, Spread, Volume24h, CachedAt, Source);

    public void UpdateFrom(Quote quote, DateTimeOffset cachedAt)
    {
        Bid = quote.Bid;
        Ask = quote.Ask;
        Spread = quote.Spread;
        Volume24h = quote.Volume24h;
        Source = quote.Source;
        CachedAt = cachedAt;
    }
}
