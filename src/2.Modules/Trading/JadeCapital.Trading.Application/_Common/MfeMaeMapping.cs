using JadeCapital.Trading.Application.Features.MfeMae.GetTradeMfeMae;
using JadeCapital.Trading.Contracts.MfeMae;
using JadeCapital.Trading.Domain.Trades;

namespace JadeCapital.Trading.Application._Common;

// ============================================================================
//  MfeMaeMapping — slice 2c (Trader Journal Core / MFE/MAE Charts).
//
//  Wire projection for the per-trade MFE/MAE endpoint. Stays in Application
//  (not Domain) because it depends on the Contracts DTO shape (wire layer).
//
//  Direction serializes as the string name ("Long" | "Short") — matches
//  the spec and the rest of the API. Currency is always present (even
//  when MFE/MAE are null on an open trade) so the FE can render the
//  table column header without branching on null currency.
// ============================================================================

public static class MfeMaeMapping
{
    public static TradeMfeMaeDto ToDto(
        Trade trade,
        IReadOnlyDictionary<MfeMaeBucket, MfeMaeBucketStat> aggregate)
    {
        ArgumentNullException.ThrowIfNull(trade);
        ArgumentNullException.ThrowIfNull(aggregate);

        var isWinner = trade.PnL is { } pnl && pnl.Amount > 0m;
        var direction = trade.Direction switch
        {
            Domain.Enums.TradeDirection.Long  => "Long",
            Domain.Enums.TradeDirection.Short => "Short",
            _                                  => "Unknown",
        };

        var aggDto = new MfeMaeAggregateDto(
            MfeLongWinners:  BucketToDto(aggregate[MfeMaeBucket.MfeLongWinners]),
            MfeLongLosers:   BucketToDto(aggregate[MfeMaeBucket.MfeLongLosers]),
            MfeShortWinners: BucketToDto(aggregate[MfeMaeBucket.MfeShortWinners]),
            MfeShortLosers:  BucketToDto(aggregate[MfeMaeBucket.MfeShortLosers]),
            MaeLongWinners:  BucketToDto(aggregate[MfeMaeBucket.MaeLongWinners]),
            MaeLongLosers:   BucketToDto(aggregate[MfeMaeBucket.MaeLongLosers]),
            MaeShortWinners: BucketToDto(aggregate[MfeMaeBucket.MaeShortWinners]),
            MaeShortLosers:  BucketToDto(aggregate[MfeMaeBucket.MaeShortLosers]));

        return new TradeMfeMaeDto(
            TradeId: trade.Id,
            Direction: direction,
            IsWinner: isWinner,
            MfeAmount: trade.MfeAmount,
            MaeAmount: trade.MaeAmount,
            Currency: trade.AccountCurrency,
            Aggregate: aggDto);
    }

    private static MfeMaeBucketDto BucketToDto(MfeMaeBucketStat stat)
        => new(Count: stat.Count, TotalMagnitude: stat.TotalMagnitude, AvgMagnitude: stat.AvgMagnitude);
}