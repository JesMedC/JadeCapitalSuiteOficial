using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Trading.Domain.Enums;

namespace JadeCapital.Trading.Domain.Trades;

public sealed record TradeOpenedDomainEvent(
    Guid TradeId,
    Guid UserId,
    string Symbol,
    AssetClass AssetClass,
    TradeDirection Direction,
    decimal Volume,
    decimal EntryPrice,
    string AccountCurrency,
    DateTimeOffset OccurredOn) : IDomainEvent;

public sealed record TradeClosedDomainEvent(
    Guid TradeId,
    Guid UserId,
    decimal PnL,
    string AccountCurrency,
    DateTimeOffset ClosedAt,
    DateTimeOffset OccurredOn) : IDomainEvent;

public sealed record TradeCancelledDomainEvent(
    Guid TradeId,
    Guid UserId,
    DateTimeOffset CancelledAt,
    DateTimeOffset OccurredOn) : IDomainEvent;
