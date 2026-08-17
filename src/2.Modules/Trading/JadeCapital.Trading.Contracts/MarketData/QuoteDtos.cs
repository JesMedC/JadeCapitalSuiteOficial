namespace JadeCapital.Trading.Contracts.MarketData;

/// <summary>
/// Wire shape for /api/quotes endpoints. Mirrors the backend
/// <c>Quote</c> record (slice 4b, Wave 4).
/// </summary>
public sealed record QuoteDto(
    string Symbol,
    decimal Bid,
    decimal Ask,
    decimal Spread,
    decimal Volume24h,
    DateTimeOffset Timestamp,
    byte Source);
