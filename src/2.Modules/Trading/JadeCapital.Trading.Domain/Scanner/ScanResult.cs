using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Domain.Instruments;

namespace JadeCapital.Trading.Domain.Scanner;

// ============================================================================
//  ScanResult — read-only value object returned by ScannerService.Run.
//  TODO Wave 4b: integrate IQuoteProvider.GetQuoteAsync for live spread/volume
//  filters; today these columns are declared on the filter but not yet wired.
// ============================================================================

public sealed record ScanResult(
    string Symbol,
    byte AssetClass,
    decimal HistoricalRiskReward,
    int TotalTrades,
    decimal TotalPnl,
    IReadOnlyList<string> MatchedCriteria);
