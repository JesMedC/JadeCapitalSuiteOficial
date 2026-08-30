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

// ===== Wave 10 slice 10.5 — GDPR Art. 17 lifecycle events =====

/// <summary>
/// Raised when the user invokes DELETE /api/users/me. The user has been
/// anonymized (email -> "deleted-{guid}@anonymized.local", display name ->
/// "Deleted User", password hash cleared) and the cascade orchestrator is
/// now soft-deleting every user-owned aggregate across modules.
/// </summary>
public sealed record UserAnonymizedDomainEvent(
    Guid UserId,
    DateTimeOffset OccurredOn) : IDomainEvent;

/// <summary>
/// Raised when the soft-delete cascade has finished and the 30-day grace
/// period is scheduled. The hard-delete sweep BackgroundService will pick
/// up the user on day 31 via
/// <see cref="JadeCapital.Identity.Infrastructure.BackgroundServices.HardDeleteSweepBackgroundService"/>.
/// </summary>
public sealed record UserScheduledHardDeleteDomainEvent(
    Guid UserId,
    DateTimeOffset ScheduledFor,
    DateTimeOffset OccurredOn) : IDomainEvent;

/// <summary>
/// Raised when the hard-delete sweep has physically purged the user row.
/// The <c>identity.users</c> row is gone; the only remaining trace is one
/// pseudonymized <c>audit.events</c> row (user_id = NULL,
/// entity_id = original_guid).
/// </summary>
public sealed record UserHardDeletedDomainEvent(
    Guid UserId,
    DateTimeOffset OccurredOn) : IDomainEvent;