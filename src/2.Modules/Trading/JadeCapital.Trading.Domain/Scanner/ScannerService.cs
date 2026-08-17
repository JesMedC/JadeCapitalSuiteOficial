using JadeCapital.Trading.Domain.Instruments;
using JadeCapital.Trading.Domain.Scanner;
using JadeCapital.Trading.Domain.Trades;

namespace JadeCapital.Trading.Domain.Scanner;

// ============================================================================
//  ScannerService — pure domain logic. No persistence, no I/O.
//  Computes a ranked list of ScanResult from the user's instrument universe
//  + their closed trades history. Filters by historical R-R today; spread/volume
//  filters are declared on ScannerFilter but not yet wired to live market data.
// ============================================================================

public static class ScannerService
{
    public static IReadOnlyList<ScanResult> Run(
        IReadOnlyList<Instrument> instruments,
        IReadOnlyList<Trade> closedTrades,
        ScannerFilter filter,
        int limit = 20)
    {
        if (instruments.Count == 0 || closedTrades.Count == 0) return Array.Empty<ScanResult>();

        // Bucket closed trades by symbol → aggregate R-R + total PnL.
        var bySymbol = closedTrades
            .Where(t => t.PnL is not null)
            .GroupBy(t => t.Symbol.Value)
            .ToDictionary(g => g.Key, g =>
            {
                var totalPnl = g.Sum(t => t.PnL!.Amount);
                var totalVolume = g.Sum(t => t.Volume.Amount);
                var rr = totalVolume > 0 ? totalPnl / totalVolume : 0m;
                return (Rr: rr, TotalTrades: g.Count(), TotalPnl: totalPnl);
            });

        var results = new List<ScanResult>();
        foreach (var inst in instruments)
        {
            if (!bySymbol.TryGetValue(inst.Symbol.Value, out var stats)) continue;
            if (stats.TotalTrades == 0) continue;
            if (filter.MinRiskReward.HasValue && stats.Rr < filter.MinRiskReward.Value) continue;

            var matched = new List<string> { $"historical_rr={stats.Rr:F2}" };
            if (filter.MinRiskReward.HasValue) matched.Add($"min_rr>={filter.MinRiskReward.Value}");
            if (filter.MinVolume.HasValue) matched.Add($"min_vol>={filter.MinVolume.Value}");
            if (filter.MinSpread.HasValue) matched.Add($"min_spread>={filter.MinSpread.Value} (Wave 4b)");
            if (filter.MaxSpread.HasValue) matched.Add($"max_spread<={filter.MaxSpread.Value} (Wave 4b)");

            results.Add(new ScanResult(
                inst.Symbol.Value,
                (byte)inst.AssetClasses,
                stats.Rr,
                stats.TotalTrades,
                stats.TotalPnl,
                matched));
        }

        return results
            .OrderByDescending(r => r.HistoricalRiskReward)
            .Take(Math.Max(1, limit))
            .ToList();
    }
}
