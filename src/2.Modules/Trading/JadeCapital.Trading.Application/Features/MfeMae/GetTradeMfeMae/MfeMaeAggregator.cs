using JadeCapital.Trading.Domain.Trades;

namespace JadeCapital.Trading.Application.Features.MfeMae.GetTradeMfeMae;

// ============================================================================
//  MfeMaeAggregator — slice 2c (Trader Journal Core / MFE/MAE Charts).
//
//  Pure function over the user's closed trades. Computes 8 buckets per the
//  spec: direction (Long|Short) × outcome (Winners|Losers) × {MFE, MAE}.
//
//  Each bucket reports:
//    - Count                 : number of closed trades falling into the bucket
//    - TotalMagnitude        : Σ |MFE| or |MAE| for the bucket (always ≥ 0)
//    - AvgMagnitude          : TotalMagnitude / Count, or 0 when Count == 0
//
//  Cancelled trades are excluded by the repo (ListClosedByUserIdAsync only
//  returns Status=Closed). Open trades are excluded too (no PnL). Trades
//  with MfeAmount == null OR MaeAmount == null (defensive: should not
//  happen post-Close in Wave 2 but defensive nonetheless) are skipped.
//
//  Empty history returns all-zero buckets — the spec scenario
//  "Empty history returns empty histograms" mandates Count=0 for every
//  bucket when the user has no closed trades.
// ============================================================================

public static class MfeMaeAggregator
{
    public static IReadOnlyDictionary<MfeMaeBucket, MfeMaeBucketStat> Aggregate(
        IReadOnlyList<Trade> closedTrades)
    {
        // Seed every bucket with zeros so the wire contract always has the
        // 8 keys regardless of history size.
        var buckets = Enum.GetValues<MfeMaeBucket>()
            .ToDictionary(b => b, _ => new MfeMaeBucketStat(0, 0m));

        foreach (var t in closedTrades)
        {
            // Defensive: should never happen post-Wave-2 Close, but if a
            // data migration left MFE/MAE null on a closed trade we skip
            // rather than crash the aggregate.
            if (t.MfeAmount is null || t.MaeAmount is null) continue;
            if (t.PnL is null) continue;

            var isWinner = t.PnL.Amount > 0m;
            var direction = t.Direction == Domain.Enums.TradeDirection.Long
                ? DirectionBucket.Long
                : DirectionBucket.Short;

            // MFE bucket: for winners, MFE carries the realized magnitude.
            // For losers, MFE is the approximation 0 — we still attribute
            // the trade to the right MFE bucket using the sign of PnL so
            // the histogram reflects the user's distribution of "would-have
            // been" MFE vs realized MAE.
            var mfeKey = BucketKey(direction, isWinner, Metric.Mfe);
            var maeKey = BucketKey(direction, isWinner, Metric.Mae);

            var mfeStat = buckets[mfeKey];
            buckets[mfeKey] = mfeStat.Add(t.MfeAmount.Value);

            var maeStat = buckets[maeKey];
            buckets[maeKey] = maeStat.Add(t.MaeAmount.Value);
        }

        return buckets;
    }

    private static MfeMaeBucket BucketKey(
        DirectionBucket direction, bool isWinner, Metric metric)
    {
        // 8 combinations: MFE|MAE × Long|Short × Win|Loss.
        // The metric dimension dominates so MFE buckets (0..3) and MAE
        // buckets (4..7) form two contiguous blocks of 4. Enum order:
        //   MfeLongWinners=0  MfeLongLosers=1  MfeShortWinners=2  MfeShortLosers=3
        //   MaeLongWinners=4  MaeLongLosers=5  MaeShortWinners=6  MaeShortLosers=7
        var metIdx = metric == Metric.Mfe ? 0 : 1;
        var dirIdx = direction == DirectionBucket.Long ? 0 : 1;
        var outIdx = isWinner ? 0 : 1;
        var flat = (metIdx * 4) + (dirIdx * 2) + outIdx;
        return (MfeMaeBucket)flat;
    }

    private enum DirectionBucket { Long, Short }
    private enum Metric { Mfe, Mae }
}

/// <summary>
/// Stable identity for the 8 buckets. Ordered: direction (Long|Short) ×
/// outcome (Winners|Losers) × metric (MFE|MAE). The wire contract
/// references these by name in <see cref="Contracts.MfeMae.MfeMaeAggregateDto"/>.
/// </summary>
public enum MfeMaeBucket
{
    MfeLongWinners = 0,
    MfeLongLosers = 1,
    MfeShortWinners = 2,
    MfeShortLosers = 3,
    MaeLongWinners = 4,
    MaeLongLosers = 5,
    MaeShortWinners = 6,
    MaeShortLosers = 7,
}

/// <summary>
/// Internal mutable accumulator. <see cref="Add"/> returns a NEW instance
/// so the aggregator is still pure (no shared mutable state between
/// iterations).
/// </summary>
public sealed record MfeMaeBucketStat(int Count, decimal TotalMagnitude)
{
    public MfeMaeBucketStat Add(decimal amount) =>
        new(Count + 1, TotalMagnitude + Math.Abs(amount));

    /// <summary>
    /// Returns the average magnitude, or 0 when no trades fall into the
    /// bucket (avoids divide-by-zero and keeps the wire contract uniform).
    /// </summary>
    public decimal AvgMagnitude =>
        Count == 0 ? 0m : TotalMagnitude / Count;
}