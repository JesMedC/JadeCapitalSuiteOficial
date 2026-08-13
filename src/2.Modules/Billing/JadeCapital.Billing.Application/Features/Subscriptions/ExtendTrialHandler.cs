using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using MediatR;

namespace JadeCapital.Billing.Application.Features.Subscriptions;

/// <summary>Command: extend a Trial subscription's end date into the future.</summary>
public sealed record ExtendTrialCommand(
    Guid SubscriptionId,
    DateTimeOffset NewTrialEndsAt,
    int ObservedVersion,
    string Actor) : IRequest<Result>;

/// <summary>
/// Trial extension handler. Loads the aggregate, delegates to the
/// <c>ExtendTrial</c> mutator, and persists. Domain rules (must be in Trial
/// status; <c>newTrialEndsAt</c> must be in the future) live in the
/// aggregate; the handler does not duplicate them.
/// </summary>
public sealed class ExtendTrialHandler : IRequestHandler<ExtendTrialCommand, Result>
{
    private readonly ISubscriptionAdminRepository _repo;
    private readonly ISubscriptionAdminUnitOfWork _uow;
    private readonly IClock _clock;

    public ExtendTrialHandler(
        ISubscriptionAdminRepository repo,
        ISubscriptionAdminUnitOfWork uow,
        IClock clock)
    {
        _repo = repo;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result> Handle(ExtendTrialCommand req, CancellationToken ct)
    {
        var subscription = await _repo.LoadForUpdateAsync(req.SubscriptionId, ct);
        if (subscription is null)
            return Result.Failure(Error.NotFound("notfound.subscription.not_found",
                "Subscription was not found."));

        var mutatorResult = subscription.ExtendTrial(req.NewTrialEndsAt, req.ObservedVersion, req.Actor, _clock.UtcNow);
        if (mutatorResult.IsFailure) return mutatorResult;

        return await _uow.SaveChangesAsync(ct);
    }
}
