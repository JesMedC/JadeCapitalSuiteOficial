using JadeCapital.Trading.Application.Features.MfeMae.GetTradeMfeMae;

namespace JadeCapital.Trading.UnitTests.Application.MfeMae;

// ============================================================================
//  MfeMaeAggregator — slice 2c (Trader Journal Core / MFE/MAE Charts).
//
//  Three unit tests proving the bucket-assignment invariant:
//   1. Empty history returns all-zero buckets.
//   2. A single winner long is attributed to MfeLongWinners + MaeLongWinners
//      (NOT to any Loser bucket — this was the bug the smoke surfaced).
//   3. A loser short is attributed to MfeShortLosers + MaeShortLosers.
//   4. Mixed winners/losers in both directions hit the right 4-of-8 buckets.
//
//  The aggregator is a pure function: Trade list → bucket dictionary.
//  No mocks, no DI: just build trades via the public Trade.Open/Trade.Close
//  API and assert.
// ============================================================================

public class MfeMaeAggregatorTests
{
    private static readonly DateTimeOffset OpenedAt =
        new(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ClosedAt =
        new(2026, 8, 17, 14, 0, 0, TimeSpan.Zero);

    private static Trade BuildClosedLong(decimal volume, decimal entry, decimal exit)
    {
        var symbol = Symbol.Create("EUR/USD").Value;
        var trade = Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            symbol, AssetClass.Forex, TradeDirection.Long,
            Money.Create(volume, Currency.Usd).Value,
            Money.Create(entry, Currency.Usd).Value,
            "USD", null, null, OpenedAt).Value;
        trade.Close(
            Money.Create(exit, Currency.Usd).Value,
            ClosedAt,
            Substitute.For<IClock>());
        return trade;
    }

    private static Trade BuildClosedShort(decimal volume, decimal entry, decimal exit)
    {
        var symbol = Symbol.Create("EUR/USD").Value;
        var trade = Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            symbol, AssetClass.Forex, TradeDirection.Short,
            Money.Create(volume, Currency.Usd).Value,
            Money.Create(entry, Currency.Usd).Value,
            "USD", null, null, OpenedAt).Value;
        trade.Close(
            Money.Create(exit, Currency.Usd).Value,
            ClosedAt,
            Substitute.For<IClock>());
        return trade;
    }

    [Fact]
    public void EmptyHistory_AllBucketsAreZero()
    {
        var agg = MfeMaeAggregator.Aggregate(new List<Trade>());

        agg.Count.Should().Be(8);
        foreach (var (_, stat) in agg)
        {
            stat.Count.Should().Be(0);
            stat.TotalMagnitude.Should().Be(0m);
            stat.AvgMagnitude.Should().Be(0m);
        }
    }

    [Fact]
    public void WinnerLong_BucketedAsMfeLongWinnersAndMaeLongWinners()
    {
        // 1000 units, entry 1.10 → exit 1.20. PnL = +100 (winner long).
        // Wave 2 approximation: MFE = +100, MAE = 0.
        var winner = BuildClosedLong(volume: 1000m, entry: 1.10m, exit: 1.20m);

        var agg = MfeMaeAggregator.Aggregate(new List<Trade> { winner });

        agg[MfeMaeBucket.MfeLongWinners].Should().Be(new MfeMaeBucketStat(1, 100m));
        agg[MfeMaeBucket.MaeLongWinners].Should().Be(new MfeMaeBucketStat(1, 0m));
        // The four Long losers/Shorts stay at zero — the winner must NOT
        // leak into any Loser bucket (regression: previous bug attributed
        // the winner to MfeLongLosers because of an off-by-one in the
        // bucket-index formula).
        agg[MfeMaeBucket.MfeLongLosers].Count.Should().Be(0);
        agg[MfeMaeBucket.MaeLongLosers].Count.Should().Be(0);
        agg[MfeMaeBucket.MfeShortWinners].Count.Should().Be(0);
        agg[MfeMaeBucket.MfeShortLosers].Count.Should().Be(0);
        agg[MfeMaeBucket.MaeShortWinners].Count.Should().Be(0);
        agg[MfeMaeBucket.MaeShortLosers].Count.Should().Be(0);
    }

    [Fact]
    public void LoserShort_BucketedAsMfeShortLosersAndMaeShortLosers()
    {
        // Short: loss when price rises. 1000 × (-0.005) = -5 PnL.
        // Wave 2: MFE = 0, MAE = -|−5| = -5.
        var loser = BuildClosedShort(volume: 1000m, entry: 1.10m, exit: 1.105m);

        var agg = MfeMaeAggregator.Aggregate(new List<Trade> { loser });

        agg[MfeMaeBucket.MfeShortLosers].Should().Be(new MfeMaeBucketStat(1, 0m));
        agg[MfeMaeBucket.MaeShortLosers].Should().Be(new MfeMaeBucketStat(1, 5m));
        // The other 6 buckets stay empty.
        foreach (var bucket in Enum.GetValues<MfeMaeBucket>())
        {
            if (bucket == MfeMaeBucket.MfeShortLosers || bucket == MfeMaeBucket.MaeShortLosers) continue;
            agg[bucket].Count.Should().Be(0);
        }
    }

    [Fact]
    public void MixedHistory_AllFourPopulatedBucketsGetTheirShare()
    {
        var winnerLong  = BuildClosedLong (volume: 1000m, entry: 1.10m, exit: 1.20m); // PnL +100
        var loserLong   = BuildClosedLong (volume: 1000m, entry: 1.10m, exit: 1.00m); // PnL -100
        var winnerShort = BuildClosedShort(volume: 1000m, entry: 1.10m, exit: 1.05m); // PnL +50 (price dropped)
        var loserShort  = BuildClosedShort(volume: 1000m, entry: 1.10m, exit: 1.20m); // PnL -100 (price rose)

        var agg = MfeMaeAggregator.Aggregate(
            new List<Trade> { winnerLong, loserLong, winnerShort, loserShort });

        // MFE carries realized magnitude for winners, 0 for losers.
        agg[MfeMaeBucket.MfeLongWinners].Should().Be(new MfeMaeBucketStat(1, 100m));
        agg[MfeMaeBucket.MfeLongLosers].Should().Be(new MfeMaeBucketStat(1, 0m));
        agg[MfeMaeBucket.MfeShortWinners].Should().Be(new MfeMaeBucketStat(1, 50m));
        agg[MfeMaeBucket.MfeShortLosers].Should().Be(new MfeMaeBucketStat(1, 0m));

        // MAE carries realized magnitude for losers, 0 for winners.
        agg[MfeMaeBucket.MaeLongWinners].Should().Be(new MfeMaeBucketStat(1, 0m));
        agg[MfeMaeBucket.MaeLongLosers].Should().Be(new MfeMaeBucketStat(1, 100m));
        agg[MfeMaeBucket.MaeShortWinners].Should().Be(new MfeMaeBucketStat(1, 0m));
        agg[MfeMaeBucket.MaeShortLosers].Should().Be(new MfeMaeBucketStat(1, 100m));
    }
}