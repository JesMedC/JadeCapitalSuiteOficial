namespace JadeCapital.Trading.Contracts.Scanner;

public sealed record ScannerFilterDto(
    Guid Id,
    string Name,
    decimal? MinSpread,
    decimal? MaxSpread,
    decimal? MinVolume,
    decimal? MinRiskReward,
    byte VolatilityWindow,
    string? ActiveHours,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpsertScannerFilterRequest(
    string Name,
    decimal? MinSpread,
    decimal? MaxSpread,
    decimal? MinVolume,
    decimal? MinRiskReward,
    byte VolatilityWindow,
    string? ActiveHours);

public sealed record ScanResultDto(
    string Symbol,
    byte AssetClass,
    decimal HistoricalRiskReward,
    int TotalTrades,
    decimal TotalPnl,
    IReadOnlyList<string> MatchedCriteria);

public sealed record RunScannerRequest(Guid FilterId, int? Limit);
