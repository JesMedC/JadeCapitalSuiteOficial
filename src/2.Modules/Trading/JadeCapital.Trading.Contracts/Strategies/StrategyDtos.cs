namespace JadeCapital.Trading.Contracts.Strategies;

// ============================================================================
//  Strategy wire contracts — slice 3a (Trader Strategies + Alerts + Planner).
//
//  Mirror of the projections from the application layer
//  (JadeCapital.Trading.Application.Features.Strategies.*).
//
//  System.Text.Json serializes the Timeframe enum as its underlying byte on
//  the wire (default policy of the API host). The FE converts the byte to a
//  Timeframe label (M1..MN) via the TIMEFRAME_LABELS map.
//
//  Money / currency fields: aggregates use decimal-as-string to avoid
//  JS Number precision drift on the FE; the FE parses with Decimal.js
//  or BigNumber (kept out of scope for slice 3a — the FE just formats
//  the raw string for display).
// ============================================================================

public sealed record StrategyDto(
    Guid Id,
    Guid UserId,
    string Name,
    string? Description,
    string? Symbol,
    byte? Timeframe,
    string? Rules,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpsertStrategyRequest(
    string Name,
    string? Description,
    string? Symbol,
    byte? Timeframe,
    string? Rules);

/// <summary>
/// Aggregate metrics for a single strategy over its closed trades
/// (slice 3a analytics). Empty strategy returns tradeCount=0 with all
/// numeric fields at zero or null per the spec scenario "Empty strategy".
/// </summary>
public sealed record StrategyAnalyticsDto(
    Guid StrategyId,
    string Name,
    int TradeCount,
    int WinCount,
    int LossCount,
    decimal WinRate,
    decimal TotalPnl,
    decimal Expectancy,
    decimal ProfitFactor,
    decimal? AvgMfe,
    decimal? AvgMae,
    DateTimeOffset? LastTradeAt);

/// <summary>
/// Body for <c>PUT /api/trades/{tradeId}/strategy</c>: set the trade's
/// strategy_id to the given value (or null to untag).
/// </summary>
public sealed record SetTradeStrategyRequest(Guid? StrategyId);