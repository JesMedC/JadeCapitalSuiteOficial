using JadeCapital.Billing.Contracts.Subscriptions;
using JadeCapital.Billing.Domain.Subscriptions;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using MediatR;

namespace JadeCapital.Billing.Application.Features.Subscriptions;

/// <summary>Query: subscription detail + owner projection + complete history (newest-first).</summary>
public sealed record GetSubscriptionDetailQuery(Guid SubscriptionId)
    : IRequest<Result<SubscriptionDetail>>;

/// <summary>
/// Loads a subscription, looks up the plan + owner projection, and projects
/// the aggregate onto the wire DTO. The owner projection lives behind
/// <see cref="JadeCapital.Identity.Contracts.Projections.IUserOwnerProjection"/>
/// (Identity.Contracts) — Admin only sees <c>Email</c> + <c>DisplayName</c>.
/// </summary>
public sealed class GetSubscriptionDetailHandler : IRequestHandler<GetSubscriptionDetailQuery, Result<SubscriptionDetail>>
{
    private readonly ISubscriptionAdminRepository _repo;
    private readonly IPlanLookup _plans;
    private readonly IOwnerProjectionLookup _owners;
    private readonly IClock _clock;

    public GetSubscriptionDetailHandler(
        ISubscriptionAdminRepository repo,
        IPlanLookup plans,
        IOwnerProjectionLookup owners,
        IClock clock)
    {
        _repo = repo;
        _plans = plans;
        _owners = owners;
        _clock = clock;
    }

    public async Task<Result<SubscriptionDetail>> Handle(GetSubscriptionDetailQuery req, CancellationToken ct)
    {
        var subscription = await _repo.LoadForUpdateAsync(req.SubscriptionId, ct);
        if (subscription is null)
            return Result.Failure<SubscriptionDetail>(Error.NotFound(
                "notfound.subscription.not_found", "Subscription was not found."));

        var plan = await _plans.FindByCodeAsync(subscription.PlanCode.Value, ct);
        var owner = await _owners.FindByUserIdAsync(subscription.UserId, ct);

        var detail = new SubscriptionDetail(
            SubscriptionId: subscription.Id,
            UserId: subscription.UserId,
            PlanCode: subscription.PlanCode.Value,
            PlanName: plan?.Name ?? subscription.PlanCode.Value,
            Status: subscription.Status.ToString(),
            TrialEndsAt: subscription.TrialEndsAt,
            CreatedAt: subscription.CreatedAt,
            UpdatedAt: subscription.UpdatedAt,
            Version: subscription.Version,
            Owner: owner ?? new MinimalOwnerProjection(string.Empty, string.Empty),
            History: subscription.History
                .Select(h => new SubscriptionHistoryItem(
                    h.Id, h.Action.ToString(),
                    h.PriorPlanCode.Value, h.ResultingPlanCode.Value,
                    h.PriorStatus.ToString(), h.ResultingStatus.ToString(),
                    h.Actor, h.OccurredAt, h.Version, h.Reason,
                    h.PriorTrialEndsAt, h.NewTrialEndsAt))
                .ToList());

        return Result.Success(detail);
    }

    /// <summary>Empty projection when an owner lookup misses; preserves the
    /// read-only contract without forcing the Admin API to leak user existence.</summary>
    private sealed record MinimalOwnerProjection(string Email, string DisplayName)
        : JadeCapital.Identity.Contracts.Projections.IUserOwnerProjection;
}

/// <summary>Identity.Contracts projection lookup. Implementation lives in
/// Identity.Infrastructure and exposes only <c>Email</c> + <c>DisplayName</c>.</summary>
public interface IOwnerProjectionLookup
{
    Task<JadeCapital.Identity.Contracts.Projections.IUserOwnerProjection?> FindByUserIdAsync(
        Guid userId, CancellationToken ct = default);
}
