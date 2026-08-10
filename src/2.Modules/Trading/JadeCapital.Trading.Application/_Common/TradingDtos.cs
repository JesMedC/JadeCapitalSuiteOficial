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
    Guid AccountId,
    string AccountName,
    Guid InstrumentId,
    InstrumentSummaryDto Instrument,
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

/// <summary>
/// Subset del Instrument que viaja embebido en TradeDto. Evita N+1 al
/// proyectar listas y mantiene la respuesta API simple (sin requerir
/// round-trip extra al cliente).
/// </summary>
public sealed record InstrumentSummaryDto(
    string Symbol,
    AssetClass AssetClass,
    decimal ContractSize,
    int DecimalPlaces,
    decimal PipValue,
    decimal PayoutPercent);

/// <summary>
/// Projection completa de un Instrument para los endpoints CRUD
/// (/api/instruments). Incluye identificador, estado y timestamps.
/// </summary>
public sealed record InstrumentDto(
    Guid Id,
    string Symbol,
    AssetClass AssetClass,
    decimal ContractSize,
    int DecimalPlaces,
    decimal PipValue,
    decimal PayoutPercent,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Projection completa de una Account para los endpoints CRUD
/// (/api/accounts). Incluye identificador, owner, configuracion del broker
/// y timestamps.
/// </summary>
public sealed record AccountDto(
    Guid Id,
    Guid UserId,
    string Name,
    string Broker,
    string Currency,
    decimal InitialBalance,
    decimal Leverage,
    decimal PayoutPercent,
    bool IsActive,
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
