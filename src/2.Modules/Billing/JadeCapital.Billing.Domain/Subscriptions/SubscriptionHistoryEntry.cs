using JadeCapital.Shared.Kernel.Primitives;

namespace JadeCapital.Billing.Domain.Subscriptions;

/// <summary>
/// Append-only snapshot of a Subscription transition. Carries the prior and
/// resulting plan + status, the actor who initiated the change, the moment it
/// was committed, and the aggregate <c>Version</c> observed by the mutator.
/// </summary>
/// <remarks>
/// Ordering is newest-first via (OccurredAt DESC, Id DESC). The aggregate
/// assigns <see cref="Id"/> via <c>MonotonicGuid.NewId()</c> so the Guid
/// tie-break is deterministic across process restarts within a single
/// domain session. The DB index on <c>(subscription_id, occurred_at DESC,
/// id DESC)</c> reproduces the same ordering server-side per design.md.
/// </remarks>
public sealed class SubscriptionHistoryEntry : Entity<Guid>
{
    public Guid SubscriptionId { get; private set; }

    public SubscriptionAction Action { get; private set; }
    public PlanCode PriorPlanCode { get; private set; } = default!;
    public PlanCode ResultingPlanCode { get; private set; } = default!;
    public SubscriptionStatus PriorStatus { get; private set; }
    public SubscriptionStatus ResultingStatus { get; private set; }
    public string Actor { get; private set; } = default!;
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Snapshot of the aggregate version immediately after the change.</summary>
    public int Version { get; private set; }

    public string? Reason { get; private set; }
    public DateTimeOffset? PriorTrialEndsAt { get; private set; }
    public DateTimeOffset? NewTrialEndsAt { get; private set; }

    private SubscriptionHistoryEntry() { }

    private SubscriptionHistoryEntry(
        Guid id,
        Guid subscriptionId,
        SubscriptionAction action,
        PlanCode priorPlanCode,
        PlanCode resultingPlanCode,
        SubscriptionStatus priorStatus,
        SubscriptionStatus resultingStatus,
        string actor,
        DateTimeOffset occurredAt,
        int version,
        string? reason,
        DateTimeOffset? priorTrialEndsAt,
        DateTimeOffset? newTrialEndsAt) : base(id)
    {
        SubscriptionId = subscriptionId;
        Action = action;
        PriorPlanCode = priorPlanCode;
        ResultingPlanCode = resultingPlanCode;
        PriorStatus = priorStatus;
        ResultingStatus = resultingStatus;
        Actor = actor;
        OccurredAt = occurredAt;
        Version = version;
        Reason = reason;
        PriorTrialEndsAt = priorTrialEndsAt;
        NewTrialEndsAt = newTrialEndsAt;
    }

    /// <summary>
    /// Factory for the mutation write path. Trusts the caller to provide a
    /// positive Guid and non-empty actor.
    /// </summary>
    public static SubscriptionHistoryEntry Create(
        Guid id,
        Guid subscriptionId,
        SubscriptionAction action,
        PlanCode priorPlanCode,
        PlanCode resultingPlanCode,
        SubscriptionStatus priorStatus,
        SubscriptionStatus resultingStatus,
        string actor,
        DateTimeOffset occurredAt,
        int version,
        string? reason = null,
        DateTimeOffset? priorTrialEndsAt = null,
        DateTimeOffset? newTrialEndsAt = null)
    {
        return new SubscriptionHistoryEntry(
            id, subscriptionId, action,
            priorPlanCode, resultingPlanCode,
            priorStatus, resultingStatus,
            actor, occurredAt, version,
            reason, priorTrialEndsAt, newTrialEndsAt);
    }

    /// <summary>
    /// Returns entries ordered (occurred_at DESC, id DESC) — newest first;
    /// the Guid tie-breaker breaks timestamp ties deterministically.
    /// Replicates the DB index documented in design.md and replicated
    /// in 0007_BillingSubscriptions.sql.
    /// </summary>
    public static IReadOnlyList<SubscriptionHistoryEntry> OrderNewestFirst(
        IEnumerable<SubscriptionHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .ToList();
    }
}
