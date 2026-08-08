using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.Primitives;

namespace JadeCapital.Identity.Domain.Users;

public sealed record UserRegisteredDomainEvent(
    Guid UserId,
    string Email,
    UserRole Role,
    DateTimeOffset OccurredOn) : IDomainEvent;

public sealed record UserPasswordChangedDomainEvent(
    Guid UserId,
    DateTimeOffset OccurredOn) : IDomainEvent;

public sealed record UserRoleChangedDomainEvent(
    Guid UserId,
    UserRole PreviousRole,
    UserRole NewRole,
    DateTimeOffset OccurredOn) : IDomainEvent;

public sealed record UserSuspendedDomainEvent(
    Guid UserId,
    string Reason,
    DateTimeOffset OccurredOn) : IDomainEvent;

public sealed record UserReactivatedDomainEvent(
    Guid UserId,
    DateTimeOffset OccurredOn) : IDomainEvent;

public sealed record UserCancelledDomainEvent(
    Guid UserId,
    DateTimeOffset OccurredOn) : IDomainEvent;

public sealed record UserLockedOutDomainEvent(
    Guid UserId,
    DateTimeOffset LockedUntil,
    DateTimeOffset OccurredOn) : IDomainEvent;