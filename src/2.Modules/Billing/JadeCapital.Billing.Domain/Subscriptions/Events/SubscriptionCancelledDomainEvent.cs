using JadeCapital.Shared.Kernel.Primitives;

namespace JadeCapital.Billing.Domain.Subscriptions.Events;

/// <summary>
/// Raised when a subscription transitions to Cancelled. Reason is required.
/// </summary>
public sealed record SubscriptionCancelledDomainEvent(
    Guid SubscriptionId,
    string Reason,
    string Actor,
    DateTimeOffset OccurredOn) : IDomainEvent;
