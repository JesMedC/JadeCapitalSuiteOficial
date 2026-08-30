using JadeCapital.Billing.Domain.Common;
using JadeCapital.Billing.Domain.Subscriptions.Events;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;

namespace JadeCapital.Billing.Domain.Subscriptions;

/// <summary>
/// Aggregate root for a single user's subscription lifecycle.
///
/// Optimistic-concurrency boundary: every mutation requires the caller to
/// pass the version it observed. The aggregate compares that observed value
/// against its current <see cref="Version"/> and rejects stale calls with
/// <c>subscription.version_conflict</c>. Slice 0f turns this into a DB-level
/// <c>xmin</c> / explicit <c>version</c> guard via EF Core.
///
/// History invariant: every state transition appends a
/// <see cref="SubscriptionHistoryEntry"/> capturing prior and resulting plan +
/// status, actor, <see cref="OccurredAt"/>, and the post-mutation
/// <see cref="Version"/>. No-op transitions (e.g. same-plan ChangeTier) DO
/// NOT append, as required by spec "no-op rejection".
/// </summary>
public sealed class Subscription : AggregateRoot<Guid>
{
    /// <summary>Initial version assigned at Create.</summary>
    public const int InitialVersion = 1;

    public Guid UserId { get; private set; }
    public PlanCode PlanCode { get; private set; } = default!;
    public SubscriptionStatus Status { get; private set; }
    public DateTimeOffset? TrialEndsAt { get; private set; }
    public SubscriptionPeriod CurrentPeriod { get; private set; } = default!;

    /// <summary>
    /// Wave 6a.2: Stripe subscription id (e.g. <c>sub_...</c>). NULL until
    /// the first webhook syncs it. The DB column is nullable + has a UNIQUE
    /// partial index (one Stripe subscription per local row). The handler
    /// MUST NOT regress a non-null value to null.
    /// </summary>
    public string? StripeSubscriptionId { get; private set; }

    /// <summary>
    /// Optimistic-concurrency token. Bumped atomically on every successful
    /// mutation. The mutator's caller passes the value it observed; mismatch
    /// ⇒ <c>subscription.version_conflict</c>.
    /// </summary>
    public int Version { get; private set; }

    private readonly List<SubscriptionHistoryEntry> _history = new();

    /// <summary>Newest-first projection of all committed history entries.</summary>
    public IReadOnlyList<SubscriptionHistoryEntry> History
        => SubscriptionHistoryEntry.OrderNewestFirst(_history);

    /// <summary>
    /// Returns the most recently appended history entry, or <c>null</c> if no
    /// transitions have been recorded (e.g. fresh aggregate, or a no-op
    /// ChangeTier that didn't append one). Application handlers use this to
    /// persist the entry explicitly via <c>DbContext.Add</c> instead of
    /// relying on EF collection-tracking through the private navigation
    /// (which slice 0e.1 demonstrated marks new entries as Modified).
    /// </summary>
    public SubscriptionHistoryEntry? LastHistoryEntry
        => _history.Count == 0 ? null : _history[^1];

    private Subscription() { }

    private Subscription(
        Guid id,
        Guid userId,
        PlanCode planCode,
        SubscriptionStatus status,
        DateTimeOffset? trialEndsAt,
        SubscriptionPeriod currentPeriod,
        DateTimeOffset createdAt) : base(id)
    {
        UserId = userId;
        PlanCode = planCode;
        Status = status;
        TrialEndsAt = trialEndsAt;
        CurrentPeriod = currentPeriod;
        Version = InitialVersion;
        SetCreatedAt(createdAt);
    }

    /// <summary>
    /// Creates a new subscription. Validates the eligible plan and rejects
    /// already-cancelled starting states.
    /// </summary>
    public static Result<Subscription> Create(
        Guid id,
        Guid userId,
        Plan plan,
        SubscriptionStatus status,
        DateTimeOffset? trialEndsAt,
        DateTimeOffset utcNow)
    {
        if (id == Guid.Empty)
            return Result.Failure<Subscription>(BillingDomainErrors.Subscription.IdRequired);
        if (userId == Guid.Empty)
            return Result.Failure<Subscription>(BillingDomainErrors.Subscription.UserIdRequired);
        if (plan is null)
            return Result.Failure<Subscription>(BillingDomainErrors.Subscription.PlanRequired);

        if (status == SubscriptionStatus.Cancelled)
            return Result.Failure<Subscription>(BillingDomainErrors.Subscription.NotCancellable);

        if (!plan.IsEligibleForSelfService)
            return Result.Failure<Subscription>(BillingDomainErrors.Subscription.PlanNotEligible);

        // Initial period: 30-day window starting at creation. The billing
        // engine that owns renewal cadence lives in 0f+; this is a sensible
        // first-month placeholder so aggregate state stays self-describing.
        var periodResult = SubscriptionPeriod.Create(
            utcNow, utcNow.AddDays(30));
        if (periodResult.IsFailure)
            return Result.Failure<Subscription>(periodResult.Error);

        return Result.Success(new Subscription(
            id, userId, plan.Code, status, trialEndsAt,
            periodResult.Value, utcNow));
    }

    // ============================================
    // Mutations
    // ============================================

    /// <summary>
    /// Changes the subscription's tier to <paramref name="newPlan"/>. Rejects
    /// ineligible plans, stale <paramref name="observedVersion"/>, and emits
    /// no history on a no-op (same-plan) transition.
    /// </summary>
    public Result ChangeTier(
        Plan newPlan,
        int observedVersion,
        string actor,
        DateTimeOffset utcNow)
    {
        if (newPlan is null)
            return Result.Failure(BillingDomainErrors.Subscription.PlanRequired);

        var versionCheck = EnsureVersionMatch(observedVersion);
        if (versionCheck.IsFailure) return versionCheck;

        if (!newPlan.IsEligibleForSelfService)
            return Result.Failure(BillingDomainErrors.Subscription.PlanNotEligible);

        // No-op: same plan. No history entry, no version bump.
        if (newPlan.Code == PlanCode)
            return Result.Success();

        var priorPlan = PlanCode;
        var priorStatus = Status;
        PlanCode = newPlan.Code;
        Version++;
        Touch();

        AppendHistory(
            SubscriptionAction.TierChanged,
            priorPlan, newPlan.Code,
            priorStatus, Status,
            actor, utcNow);
        RaiseDomainEvent(new SubscriptionTierChangedDomainEvent(
            Id, priorPlan, newPlan.Code, actor, utcNow));

        return Result.Success();
    }

    /// <summary>
    /// Cancels the subscription from a cancellable (Active / Trial) state.
    /// Requires a non-empty reason. Already-Cancelled returns Conflict and
    /// does NOT append history (no-op rejection rule).
    /// </summary>
    public Result Cancel(
        string reason,
        int observedVersion,
        string actor,
        DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(BillingDomainErrors.Subscription.CancellationReasonRequired);

        var versionCheck = EnsureVersionMatch(observedVersion);
        if (versionCheck.IsFailure) return versionCheck;

        if (Status == SubscriptionStatus.Cancelled)
            return Result.Failure(BillingDomainErrors.Subscription.NotCancellable);

        var priorPlan = PlanCode;
        var priorStatus = Status;
        Status = SubscriptionStatus.Cancelled;
        Version++;
        Touch();

        AppendHistory(
            SubscriptionAction.Cancelled,
            priorPlan, priorPlan,
            priorStatus, Status,
            actor, utcNow,
            reason: reason);
        RaiseDomainEvent(new SubscriptionCancelledDomainEvent(
            Id, reason, actor, utcNow));

        return Result.Success();
    }

    /// <summary>
    /// Extends the trial end to <paramref name="newTrialEnd"/> (must be in the
    /// future). Refuses non-trial subscriptions and past-or-equal end dates.
    /// </summary>
    public Result ExtendTrial(
        DateTimeOffset newTrialEnd,
        int observedVersion,
        string actor,
        DateTimeOffset utcNow)
    {
        var versionCheck = EnsureVersionMatch(observedVersion);
        if (versionCheck.IsFailure) return versionCheck;

        if (Status != SubscriptionStatus.Trial)
            return Result.Failure(BillingDomainErrors.Subscription.NotInTrial);

        if (newTrialEnd <= utcNow)
            return Result.Failure(BillingDomainErrors.Subscription.TrialEndExpired);

        var priorPlan = PlanCode;
        var priorStatus = Status;
        var priorEnd = TrialEndsAt;
        TrialEndsAt = newTrialEnd;
        Version++;
        Touch();

        AppendHistory(
            SubscriptionAction.TrialExtended,
            priorPlan, priorPlan,
            priorStatus, Status,
            actor, utcNow,
            priorTrialEndsAt: priorEnd,
            newTrialEndsAt: newTrialEnd);
        RaiseDomainEvent(new SubscriptionTrialExtendedDomainEvent(
            Id, priorEnd ?? newTrialEnd, newTrialEnd, actor, utcNow));

        return Result.Success();
    }

    /// <summary>
    /// Returns true iff the subscription may be cancelled from its current
    /// status (Active or Trial).
    /// </summary>
    public bool IsCancellable()
        => Status is SubscriptionStatus.Active or SubscriptionStatus.Trial;

    /// <summary>
    /// Wave 6a.2: applies a Stripe webhook event to this subscription. Sets
    /// the Stripe subscription id (one-time, never nulled), updates the
    /// status if it changed, bumps the version, and appends a
    /// <see cref="SubscriptionAction.WebhookSynced"/> history entry. No-op
    /// (no history, no version bump) when the resulting status equals the
    /// current status.
    /// <para>
    /// The caller is the <c>HandleWebhookHandler</c> after it has verified
    /// the signature and appended the webhook event to
    /// <c>billing.stripe_webhook_events</c>. The actor is hard-coded to
    /// <c>"stripe-webhook"</c> for auditability.
    /// </para>
    /// <para>
    /// <b>Why no <c>observedVersion</c> check here</b>: the handler reads
    /// the subscription with its current version, captures it, and calls
    /// <see cref="SyncFromStripe"/>. If the admin mutated the row in
    /// between, the handler retries up to 3 times with jitter by refetching
    /// (see <c>HandleWebhookHandler.SyncSubscriptionAsync</c>). The aggregate
    /// itself stays a pure state-transition boundary; retry is a handler
    /// concern.
    /// </para>
    /// </summary>
    public Result SyncFromStripe(
        StripeSubscriptionDto stripeSub,
        SubscriptionStatus mappedStatus,
        DateTimeOffset utcNow)
    {
        if (stripeSub is null)
            return Result.Failure(
                Error.Validation("validation.subscription.stripe_dto_required",
                    "Stripe subscription DTO is required."));

        // 1. Set StripeSubscriptionId (one-time). If a different stripe id
        // is already set, reject — this is a safety net for accidental
        // cross-tenant remapping.
        if (string.IsNullOrEmpty(StripeSubscriptionId))
        {
            StripeSubscriptionId = stripeSub.StripeSubscriptionId;
        }
        else if (!string.Equals(StripeSubscriptionId, stripeSub.StripeSubscriptionId, StringComparison.Ordinal))
        {
            return Result.Failure(
                Error.Conflict("subscription.stripe_id_mismatch",
                    "Subscription is already mapped to a different Stripe id."));
        }

        // 2. Status transition. No-op when status is unchanged (consistent
        // with ChangeTier's no-op rejection rule).
        if (Status == mappedStatus)
            return Result.Success();

        var priorPlan = PlanCode;
        var priorStatus = Status;
        Status = mappedStatus;
        Version++;
        Touch();

        AppendHistory(
            SubscriptionAction.WebhookSynced,
            priorPlan, priorPlan,
            priorStatus, Status,
            actor: "stripe-webhook",
            utcNow: utcNow,
            reason: $"stripe_event:{stripeSub.Status}");

        return Result.Success();
    }

    private void AppendHistory(
        SubscriptionAction action,
        PlanCode priorPlan,
        PlanCode resultingPlan,
        SubscriptionStatus priorStatus,
        SubscriptionStatus resultingStatus,
        string actor,
        DateTimeOffset utcNow,
        string? reason = null,
        DateTimeOffset? priorTrialEndsAt = null,
        DateTimeOffset? newTrialEndsAt = null)
    {
        var entry = SubscriptionHistoryEntry.Create(
            Common.MonotonicGuid.NewId(),
            Id,
            action,
            priorPlan, resultingPlan,
            priorStatus, resultingStatus,
            actor, utcNow,
            Version,
            reason,
            priorTrialEndsAt,
            newTrialEndsAt);

        _history.Add(entry);
    }

    /// <summary>
    /// Shared rule: rejects any mutation whose observed version does not
    /// match the aggregate's current version. Returns success when no
    /// conflict; the caller is responsible for bumping <see cref="Version"/>
    /// and <see cref="Touch"/>ing the entity once its state change is
    /// decided.
    /// </summary>
    private Result EnsureVersionMatch(int observedVersion)
        => observedVersion == Version
            ? Result.Success()
            : Result.Failure(BillingDomainErrors.Subscription.VersionConflict);
}
