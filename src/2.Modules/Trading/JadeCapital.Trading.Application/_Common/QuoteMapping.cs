using JadeCapital.Shared.Kernel.MarketData;
using JadeCapital.Trading.Contracts.MarketData;

namespace JadeCapital.Trading.Application._Common;

/// <summary>
/// Mapping helpers between the <c>Quote</c> record (Shared.Kernel.MarketData) and
/// <c>QuoteDto</c> (Trading.Contracts.MarketData). The DTO uses <c>byte</c> for
/// <c>Source</c> to match the JSON wire shape; the domain enum encodes the same
/// numeric values via <c>QuoteSource : byte</c>.
/// </summary>
public static class QuoteMappingExtensions
{
    public static QuoteDto ToDto(this Quote q)
        => new(q.Symbol, q.Bid, q.Ask, q.Spread, q.Volume24h, q.Timestamp, (byte)q.Source);
}
