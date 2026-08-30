namespace JadeCapital.Shared.Kernel.MarketData;

/// <summary>
/// Read-only quote provider abstraction. The default implementation is
/// <c>InMemoryQuoteProvider</c> (deterministic seed + clock); Wave 6 swaps
/// the DI registration for a real broker provider without touching consumers.
/// </summary>
public interface IQuoteProvider
{
    /// <summary>
    /// Returns the most recent quote for a single symbol, or <c>null</c> if
    /// the symbol is unknown to the provider. Lookups are case-insensitive.
    /// </summary>
    Task<Quote?> GetQuoteAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent quotes for a batch of symbols. Unknown symbols
    /// are silently dropped (no exception, no entry in the result). Order is
    /// not guaranteed.
    /// </summary>
    Task<IReadOnlyList<Quote>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken ct = default);
}
