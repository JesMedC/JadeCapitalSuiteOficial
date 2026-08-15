using JadeCapital.Billing.Domain.Subscriptions;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Billing.Application.Subscriptions;

/// <summary>
/// Application-layer abstraction over the Subscription aggregate's mutators
/// (tier change, cancellation, trial extension). Centralises the three
/// transitions' shared contract — observed-version optimistic concurrency,
/// actor capture, and utcNow stamping — so MediatR handlers (slice 0f) and
/// future ops tooling can depend on a single surface instead of the
/// aggregate directly.
///
/// Implementation calls into the aggregate; the aggregate owns the
/// state-transition rules. This interface is the deduped entry point
/// required by slice 0e.5.
/// </summary>
public interface ISubscriptionMutator
{
    /// <summary>
    /// Tier change. Rejects ineligible plans and stale versions. No-op when
    /// the new plan equals the current one (does NOT append history).
    /// </summary>
    Result ChangeTier(
        Subscription subscription,
        Plan newPlan,
        int observedVersion,
        string actor,
        DateTimeOffset utcNow);

    /// <summary>
    /// Cancellation. Reason is required. Fails Conflict on already-cancelled.
    /// </summary>
    Result Cancel(
        Subscription subscription,
        string reason,
        int observedVersion,
        string actor,
        DateTimeOffset utcNow);

    /// <summary>
    /// Trial extension. New end must be in the future. Fails Conflict when
    /// the subscription is not in Trial status.
    /// </summary>
    Result ExtendTrial(
        Subscription subscription,
        DateTimeOffset newTrialEndsAt,
        int observedVersion,
        string actor,
        DateTimeOffset utcNow);
}

/// <summary>
/// Default adapter that forwards to the aggregate's own mutators. Lives
/// behind <see cref="ISubscriptionMutator"/> so handlers depend on the
/// abstraction, not on the aggregate's API surface.
/// </summary>
public sealed class SubscriptionMutator : ISubscriptionMutator
{
    public Result ChangeTier(
        Subscription subscription,
        Plan newPlan,
        int observedVersion,
        string actor,
        DateTimeOffset utcNow)
        => subscription.ChangeTier(newPlan, observedVersion, actor, utcNow);

    public Result Cancel(
        Subscription subscription,
        string reason,
        int observedVersion,
        string actor,
        DateTimeOffset utcNow)
        => subscription.Cancel(reason, observedVersion, actor, utcNow);

    public Result ExtendTrial(
        Subscription subscription,
        DateTimeOffset newTrialEndsAt,
        int observedVersion,
        string actor,
        DateTimeOffset utcNow)
        => subscription.ExtendTrial(newTrialEndsAt, observedVersion, actor, utcNow);
}
