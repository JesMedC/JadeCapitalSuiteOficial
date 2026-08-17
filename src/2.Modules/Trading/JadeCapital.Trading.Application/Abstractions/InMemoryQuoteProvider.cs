using JadeCapital.Shared.Kernel.MarketData;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Trading.Application.Abstractions;

/// <summary>
/// Deterministic in-memory quote provider for Wave 4b. Returns quotes for a
/// fixed set of well-known symbols (major FX pairs + BTCUSD); returns null
/// for anything else. Reproducible: the same <c>(symbol, IClock.UtcNow.Ticks)</c>
/// always yields the same <see cref="Quote"/>.
/// </summary>
public sealed class InMemoryQuoteProvider : IQuoteProvider
{
    private static readonly HashSet<string> KnownSymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        "EURUSD", "GBPUSD", "USDJPY", "USDCAD", "AUDUSD", "USDCHF", "NZDUSD",
        "EURJPY", "GBPJPY", "EURGBP", "EURCAD", "AUDCAD", "AUDJPY",
        "BTCUSD", "ETHUSD",
    };

    private readonly IClock _clock;

    public InMemoryQuoteProvider(IClock clock)
    {
        _clock = clock;
    }

    public Task<Quote?> GetQuoteAsync(string symbol, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return Task.FromResult<Quote?>(null);

        var normalized = symbol.Trim().ToUpperInvariant();
        if (!KnownSymbols.Contains(normalized))
            return Task.FromResult<Quote?>(null);

        return Task.FromResult<Quote?>(BuildQuote(normalized));
    }

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken ct = default)
    {
        var results = new List<Quote>();
        foreach (var raw in symbols)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var normalized = raw.Trim().ToUpperInvariant();
            if (!KnownSymbols.Contains(normalized)) continue;
            results.Add(BuildQuote(normalized));
        }
        return await Task.FromResult<IReadOnlyList<Quote>>(results);
    }

    private Quote BuildQuote(string symbol)
    {
        var seed = Math.Abs(symbol.GetHashCode());
        var ticks = _clock.UtcNow.Ticks;

        // Mid price 1.0000..2.0000 from seed (forex-ish range).
        var midBase = 1.0m + (seed % 1001) / 1000m;

        // Time-driven walk: ±0.0010 from ticks, modulated per millisecond.
        var walkTicks = (ticks / TimeSpan.TicksPerMillisecond) % 21;
        var walk = (decimal)walkTicks - 10m;
        var mid = midBase + walk * 0.0001m;

        // Spread 1..5 pips of mid (in 1/10000).
        var spreadBps = (seed % 5) + 1;
        var halfSpread = Math.Round(mid * spreadBps / 20000m, 8);

        var bid = Math.Round(mid - halfSpread, 8);
        var ask = Math.Round(mid + halfSpread, 8);
        var spread = ask - bid;

        // Volume 50,000..500,000 blended with the current tick.
        var volumeTick = (int)((ticks / TimeSpan.TicksPerSecond) % 1000);
        var volume = 50_000m + (decimal)((seed + volumeTick) % 450_000);

        return new Quote(symbol, bid, ask, spread, volume, _clock.UtcNow, QuoteSource.Stub);
    }
}
