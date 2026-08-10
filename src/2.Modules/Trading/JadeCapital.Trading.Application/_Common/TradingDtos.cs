using JadeCapital.Trading.Domain.Enums;

namespace JadeCapital.Trading.Application._Common;

/// <summary>
/// DTOs compartidos por los features de Trading (Commands, Queries).
/// Viven en Application porque son contratos de capa de aplicacion — la
/// capa API los mapea a response shapes segun corresponda.
/// </summary>

public sealed record TradeDto(
    Guid Id,
    Guid UserId,
    string Symbol,
    AssetClass AssetClass,
    TradeDirection Direction,
    TradeStatus Status,
    decimal Volume,
    string VolumeCurrency,
    decimal EntryPrice,
    string EntryPriceCurrency,
    decimal? ExitPrice,
    string? ExitPriceCurrency,
    decimal? Pnl,
    string? PnlCurrency,
    string AccountCurrency,
    string? Strategy,
    string? Notes,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PagedTradesDto(
    IReadOnlyList<TradeDto> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record DashboardSummaryDto(
    int TotalCount,
    int OpenCount,
    int ClosedCount,
    int WinsCount,
    int LossesCount,
    decimal WinRate,
    decimal TotalPnL,
    decimal BestTrade,
    decimal WorstTrade,
    decimal AvgTrade,
    string Currency);

public sealed record CalendarDayDto(DateOnly Date, decimal Pnl, int TradeCount);

public sealed record CalendarDto(int Year, int Month, IReadOnlyList<CalendarDayDto> Days);