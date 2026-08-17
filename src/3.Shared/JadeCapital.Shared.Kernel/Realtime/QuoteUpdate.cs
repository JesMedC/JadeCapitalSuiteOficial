using System.Text.Json.Serialization;
using JadeCapital.Shared.Kernel.MarketData;

namespace JadeCapital.Shared.Kernel.Realtime;

// ============================================================================
//  QuoteUpdate — slice 4c (Realtime) wire shape.
//
//  DTO broadcast over SignalR to subscribed clients. Differs from
//  JadeCapital.Shared.Kernel.MarketData.Quote intentionally:
//   - exposes `Last` (mid price) so the FE doesn't need to compute it
//   - exposes `Ts` (server-side UTC timestamp of the broadcast tick)
//   - drops `Spread` and `Volume24h` (the realtime stream cares about price
//     movement, not book depth)
//
//  Lives under JadeCapital.Shared.Kernel.Realtime so the realtime namespace
//  is independent of MarketData internals — keeps the realtime surface
//  self-contained, and a future proto/Avro/flatbuffer migration can swap
//  this shape without touching the MarketData layer.
// ============================================================================

public sealed record QuoteUpdate(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("bid")] decimal Bid,
    [property: JsonPropertyName("ask")] decimal Ask,
    [property: JsonPropertyName("last")] decimal Last,
    [property: JsonPropertyName("ts")] DateTimeOffset Ts,
    [property: JsonPropertyName("source")] QuoteSource Source)
{
    /// <summary>
    /// Project a domain <see cref="Quote"/> to its broadcast DTO. The `Last`
    /// field is computed as the mid-price (avg of bid+ask), rounded to 8
    /// decimals to match the Quote precision contract.
    /// </summary>
    public static QuoteUpdate FromQuote(Quote q) =>
        new(
            Symbol: q.Symbol,
            Bid: q.Bid,
            Ask: q.Ask,
            Last: Math.Round(0.5m * (q.Bid + q.Ask), 8),
            Ts: q.Timestamp,
            Source: q.Source);
}