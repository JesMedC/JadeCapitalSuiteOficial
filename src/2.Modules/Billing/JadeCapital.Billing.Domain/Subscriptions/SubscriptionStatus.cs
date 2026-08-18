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
    Expired = 3,

    /// <summary>
    /// Wave 6a.2: payment failed but not yet cancelled. Stripe's
    /// <c>past_due</c> and <c>unpaid</c> statuses both map here. The
    /// trader's subscription is no longer paying; the admin must intervene
    /// to retry the charge, update the card, or cancel.
    /// </summary>
    PastDue = 4
}

/// <summary>
/// Catalog of actions that append history entries to a Subscription.
/// Surfaced on <c>SubscriptionHistoryEntry.Action</c>.
/// </summary>
public enum SubscriptionAction
{
    TierChanged = 0,
    Cancelled = 1,
    TrialExtended = 2,

    /// <summary>
    /// Wave 6a.2: the subscription was synced from a Stripe webhook
    /// (customer.subscription.created|updated|deleted). Carries a stripe
    /// subscription id; the resulting status is recorded in the history
    /// entry's <c>ResultingStatus</c>.
    /// </summary>
    WebhookSynced = 3
}
