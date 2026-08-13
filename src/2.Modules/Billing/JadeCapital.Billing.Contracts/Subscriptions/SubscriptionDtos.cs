namespace JadeCapital.Billing.Contracts.Subscriptions;

/// <summary>
/// Public DTOs exposed by the Billing module's Admin surface. Slice 0f.
/// These DTOs are the wire shape — Admin.Api endpoints project the domain
/// aggregate onto them. Decimal-only for money: no float/double ever.
/// </summary>
public sealed record SubscriptionListItem(
    Guid SubscriptionId,
    Guid UserId,
    string PlanCode,
    string Status,
    DateTimeOffset UpdatedAt,
    int Version);

/// <summary>Paged projection of <see cref="SubscriptionListItem"/> for the Admin list/search endpoint.</summary>
public sealed record PagedSubscriptions(
    IReadOnlyList<SubscriptionListItem> Items,
    int Page,
    int PageSize,
    int Total);

/// <summary>Detail DTO carrying subscription state, plan metadata, owner projection, and history.</summary>
public sealed record SubscriptionDetail(
    Guid SubscriptionId,
    Guid UserId,
    string PlanCode,
    string PlanName,
    string Status,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version,
    JadeCapital.Identity.Contracts.Projections.IUserOwnerProjection Owner,
    IReadOnlyList<SubscriptionHistoryItem> History);

/// <summary>History entry shape exposed to Admin. Newest-first ordering is
/// guaranteed by the aggregate's <c>OrderNewestFirst</c> projection.</summary>
public sealed record SubscriptionHistoryItem(
    Guid Id,
    string Action,
    string PriorPlanCode,
    string ResultingPlanCode,
    string PriorStatus,
    string ResultingStatus,
    string Actor,
    DateTimeOffset OccurredAt,
    int Version,
    string? Reason,
    DateTimeOffset? PriorTrialEndsAt,
    DateTimeOffset? NewTrialEndsAt);
