using JadeCapital.Shared.Kernel.Primitives;

namespace JadeCapital.Billing.Domain.Subscriptions.Events;

/// <summary>
/// Raised when an Admin changes the subscription tier. Carries prior and
/// resulting plan codes; the resulting period and version live on the
/// aggregate itself (Infrastructure projects them for the read model).
/// </summary>
public sealed record SubscriptionTierChangedDomainEvent(
    Guid SubscriptionId,
    PlanCode PriorPlanCode,
    PlanCode ResultingPlanCode,
    string Actor,
    DateTimeOffset OccurredOn) : IDomainEvent;
