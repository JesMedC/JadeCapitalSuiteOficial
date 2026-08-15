namespace JadeCapital.Billing.Domain.Subscriptions;

/// <summary>
/// Subscription lifecycle states. Authoritative for "cancellable" semantics.
/// </summary>
public enum SubscriptionStatus
{
    /// <summary>Standard paid subscription in force.</summary>
    Active = 0,

    /// <summary>Trial period in force; ends at <c>TrialEndsAt</c>.</summary>
    Trial = 1,

    /// <summary>Cancelled; no further mutations allowed.</summary>
    Cancelled = 2,

    /// <summary>Trial expired without conversion to a paid plan.</summary>
    Expired = 3
}

/// <summary>
/// Catalog of actions that append history entries to a Subscription.
/// Surfaced on <c>SubscriptionHistoryEntry.Action</c>.
/// </summary>
public enum SubscriptionAction
{
    TierChanged = 0,
    Cancelled = 1,
    TrialExtended = 2
}
