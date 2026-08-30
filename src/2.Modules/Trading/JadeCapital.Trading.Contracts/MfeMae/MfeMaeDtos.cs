namespace JadeCapital.Trading.Contracts.MfeMae;

// ============================================================================
//  MFE/MAE wire contract — slice 2c (Trader Journal Core / MFE/MAE Charts).
//
//  Mirror of the analysis result projected by
//  JadeCapital.Trading.Application.Features.MfeMae.GetTradeMfeMae.
//
//  Wire shape (matches the JSON in
//  openspec/changes/2026-08-17-trader-journal-core/specs/mfe-mae-charts/spec.md):
//
//  {
//    tradeId: GUID,
//    direction: 'Long'|'Short',
//    isWinner: bool,
//    mfeAmount: decimal | null,
//    maeAmount: decimal | null,
//    currency: string,
//    aggregate: {
//      mfe_long_winners:  { count, totalMagnitude, avgMagnitude },
//      mfe_long_losers:   {...},
//      mfe_short_winners: {...},
//      mfe_short_losers:  {...},
//      mae_long_winners:  {...},
//      mae_long_losers:   {...},
//      mae_short_winners: {...},
//      mae_short_losers:  {...}
//    }
//  }
//
//  System.Text.Json serializes the enum (TradeDirection) as its string
//  name on the API host (default policy). `currency` is always present —
//  even when MFE/MAE are null (open trade), the wire carries the trade's
//  account currency so the FE can render the column header without
//  branching.
// ============================================================================

public sealed record TradeMfeMaeDto(
    Guid TradeId,
    string Direction,
    bool IsWinner,
    decimal? MfeAmount,
    decimal? MaeAmount,
    string Currency,
    MfeMaeAggregateDto Aggregate);

public sealed record MfeMaeAggregateDto(
    MfeMaeBucketDto MfeLongWinners,
    MfeMaeBucketDto MfeLongLosers,
    MfeMaeBucketDto MfeShortWinners,
    MfeMaeBucketDto MfeShortLosers,
    MfeMaeBucketDto MaeLongWinners,
    MfeMaeBucketDto MaeLongLosers,
    MfeMaeBucketDto MaeShortWinners,
    MfeMaeBucketDto MaeShortLosers);

/// <summary>
/// Bucket statistics. <c>Count</c> is the number of closed trades in the
/// user's history that fall into this bucket. <c>TotalMagnitude</c> is the
/// sum of <c>|MFE|</c> or <c>|MAE|</c> for that bucket (always non-negative).
/// <c>AvgMagnitude</c> is <c>TotalMagnitude / Count</c>, or 0 when Count is 0.
/// </summary>
public sealed record MfeMaeBucketDto(
    int Count,
    decimal TotalMagnitude,
    decimal AvgMagnitude);