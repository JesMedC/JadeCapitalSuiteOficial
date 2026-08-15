using JadeCapital.Shared.Kernel.Primitives;

namespace JadeCapital.Billing.Domain.Subscriptions.Events;

/// <summary>
/// Raised when the trial end is extended to a new future date on an active
/// trial subscription.
/// </summary>
public sealed record SubscriptionTrialExtendedDomainEvent(
    Guid SubscriptionId,
    DateTimeOffset PriorTrialEndsAt,
    DateTimeOffset NewTrialEndsAt,
    string Actor,
    DateTimeOffset OccurredOn) : IDomainEvent;
