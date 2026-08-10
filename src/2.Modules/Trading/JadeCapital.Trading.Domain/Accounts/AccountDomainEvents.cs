using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Trading.Domain.Enums;

namespace JadeCapital.Trading.Domain.Accounts;

public sealed record AccountOpenedDomainEvent(
    Guid AccountId,
    Guid UserId,
    string Name,
    MarketType MarketType,
    string Currency,
    decimal InitialBalance,
    DateTimeOffset OpenedAt) : IDomainEvent
{
    public DateTimeOffset OccurredOn => OpenedAt;
}

public sealed record AccountUpdatedDomainEvent(
    Guid AccountId,
    Guid UserId,
    MarketType MarketType,
    DateTimeOffset UpdatedAt) : IDomainEvent
{
    public DateTimeOffset OccurredOn => UpdatedAt;
}

public sealed record AccountDeactivatedDomainEvent(
    Guid AccountId,
    Guid UserId,
    DateTimeOffset DeactivatedAt) : IDomainEvent
{
    public DateTimeOffset OccurredOn => DeactivatedAt;
}

public sealed record AccountReactivatedDomainEvent(
    Guid AccountId,
    Guid UserId,
    DateTimeOffset ReactivatedAt) : IDomainEvent
{
    public DateTimeOffset OccurredOn => ReactivatedAt;
}
