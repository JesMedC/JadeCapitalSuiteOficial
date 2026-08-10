using JadeCapital.Shared.Kernel.Primitives;

namespace JadeCapital.Trading.Domain.Accounts;

public sealed record AccountOpenedDomainEvent(
    Guid AccountId,
    Guid UserId,
    string Name,
    string Currency,
    decimal InitialBalance,
    DateTimeOffset OpenedAt) : IDomainEvent
{
    public DateTimeOffset OccurredOn => OpenedAt;
}

public sealed record AccountUpdatedDomainEvent(
    Guid AccountId,
    Guid UserId,
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
