namespace JadeCapital.Shared.Kernel.MarketData;

/// <summary>
/// Cross-module wire shape for a single market quote. Lives in Shared.Kernel
/// because Trading (alert rules, scanner), SignalR broadcast (Wave 4c), and
/// the API proxy all consume it. Wave 6 will swap the provider implementation
/// without changing this shape.
/// </summary>
public sealed record Quote(
    string Symbol,
    decimal Bid,
    decimal Ask,
    decimal Spread,
    decimal Volume24h,
    DateTimeOffset Timestamp,
    QuoteSource Source);
