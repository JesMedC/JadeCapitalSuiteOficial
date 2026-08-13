using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using MediatR;

namespace JadeCapital.Billing.Application.Features.Subscriptions;

/// <summary>Command: change a subscription's tier to <see cref="NewPlanCode"/>.</summary>
public sealed record ChangeTierCommand(
    Guid SubscriptionId,
    string NewPlanCode,
    int ObservedVersion,
    string Actor) : IRequest<Result>;

/// <summary>
/// Tier change handler. Loads the subscription, looks up the target plan,
/// delegates to the aggregate's optimistic-concurrency mutator, and persists.
/// The aggregate (slice 0e) owns eligibility + no-op + history invariants;
/// this handler is the orchestration boundary that injects the actor + clock.
/// </summary>
public sealed class ChangeTierHandler : IRequestHandler<ChangeTierCommand, Result>
{
    private readonly ISubscriptionAdminRepository _repo;
    private readonly IPlanLookup _plans;
    private readonly ISubscriptionAdminUnitOfWork _uow;
    private readonly IClock _clock;

    public ChangeTierHandler(
        ISubscriptionAdminRepository repo,
        IPlanLookup plans,
        ISubscriptionAdminUnitOfWork uow,
        IClock clock)
    {
        _repo = repo;
        _plans = plans;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result> Handle(ChangeTierCommand req, CancellationToken ct)
    {
        var subscription = await _repo.LoadForUpdateAsync(req.SubscriptionId, ct);
        if (subscription is null)
            return Result.Failure(Error.NotFound("notfound.subscription.not_found",
                "Subscription was not found."));

        var plan = await _plans.FindByCodeAsync(req.NewPlanCode, ct);
        if (plan is null)
            return Result.Failure(Error.Validation("validation.subscription.plan_not_found",
                "Target plan was not found."));

        var mutatorResult = subscription.ChangeTier(plan, req.ObservedVersion, req.Actor, _clock.UtcNow);
        if (mutatorResult.IsFailure) return mutatorResult;

        return await _uow.SaveChangesAsync(ct);
    }
}
