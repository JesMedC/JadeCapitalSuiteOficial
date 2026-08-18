using JadeCapital.Billing.Contracts.Subscriptions;
using JadeCapital.Billing.Domain.Subscriptions;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Billing.Application.Features.Subscriptions;

/// <summary>
/// Repository abstraction the Admin surface uses. Reads list + detail; loads
/// subscriptions with their history for the mutator write path. Implementation
/// lives in JadeCapital.Billing.Infrastructure.Persistence.
/// </summary>
/// <remarks>
/// SKIPPED from Wave 8 audit coverage (2026-08-19-wave8-audit-coverage-extended) — no mutation methods on this interface; the Subscription aggregate is already audited by Wave 6's <c>SubscriptionAuditDecorator</c>.
/// </remarks>
public interface ISubscriptionAdminRepository
{
    /// <summary>Returns a paged slice filtered by the lowercase status name
    /// (<c>active</c>, <c>trial</c>, <c>cancelled</c>, <c>expired</c>).</summary>
    Task<PagedSubscriptions> ListPagedAsync(string status, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Loads a subscription with its history for mutator write paths.
    /// Returns null if not found; the handler converts that into NotFound.</summary>
    Task<Subscription?> LoadForUpdateAsync(Guid subscriptionId, CancellationToken ct = default);

    /// <summary>
    /// Wave 6a.2: looks up a subscription by its Stripe subscription id
    /// (e.g. <c>sub_...</c>). Used by the webhook handler to resolve a
    /// Stripe event to the local subscription. Returns null if no mapping
    /// exists (e.g. webhook arrived before <c>/api/billing/stripe/customers</c>
    /// was called, or admin created the subscription without a Stripe mapping).
    /// </summary>
    Task<Subscription?> FindByStripeSubscriptionIdAsync(
        string stripeSubscriptionId, CancellationToken ct = default);

    /// <summary>
    /// Wave 6b.1: looks up a subscription by the owning user's id. Used by
    /// the billing portal read API (slice 6b.1) to resolve the caller's
    /// local subscription from the JWT-derived userId without exposing any
    /// caller-supplied id. Returns null if the user has no local subscription
    /// (e.g. they completed Stripe customer setup but never finished
    /// checkout, or admin created the subscription without going through the
    /// standard flow).
    /// </summary>
    Task<Subscription?> FindByUserIdAsync(
        Guid userId, CancellationToken ct = default);
}

/// <summary>Unit-of-work boundary for the Admin write paths. Persists the
/// aggregate's mutations (status, version, history) atomically.</summary>
public interface ISubscriptionAdminUnitOfWork
{
    /// <summary>
    /// Stages a new history entry for insertion. Bypasses the EF Core
    /// collection-tracking bug where entries appended through the aggregate's
    /// private backing field are detected as <c>Modified</c> instead of
    /// <c>Added</c>. The caller (handler) passes the entry returned by
    /// <c>Subscription.LastHistoryEntry</c> after a successful mutation.
    /// </summary>
    void AddHistoryEntry(SubscriptionHistoryEntry entry);

    Task<Result> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Plan lookup by its lowercase <see cref="PlanCode"/> value.</summary>
public interface IPlanLookup
{
    Task<Plan?> FindByCodeAsync(string planCode, CancellationToken ct = default);

    /// <summary>
    /// Lists all plans eligible for self-service sign-up
    /// (<c>is_eligible_for_self_service = true AND is_deprecated = false</c>).
    /// Used by the public catalog endpoint (slice Wave-1.3).
    /// </summary>
    Task<IReadOnlyList<Plan>> ListEligibleForSelfServiceAsync(CancellationToken ct = default);
}

/// <summary>Identity.Contracts projection lookup. Implementation lives in
/// Identity.Infrastructure and exposes only <c>Email</c> + <c>DisplayName</c>
/// via <see cref="JadeCapital.Identity.Contracts.Projections.IUserOwnerProjection"/>.</summary>
public interface IOwnerProjectionLookup
{
    Task<JadeCapital.Identity.Contracts.Projections.IUserOwnerProjection?> FindByUserIdAsync(
        Guid userId, CancellationToken ct = default);
}
