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
/// <c>ExtendTrial</c> mutator, then stages the new history entry via
/// <see cref="ISubscriptionAdminUnitOfWork.AddHistoryEntry"/> and commits.
/// The UoW abstraction dodges the slice 0e collection-tracking bug where
/// EF Core marks entries appended through the aggregate's private backing
/// field as <c>Modified</c> instead of <c>Added</c>.
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

        var newEntry = subscription.LastHistoryEntry;
        if (newEntry is not null && mutatorResult.IsSuccess)
            _uow.AddHistoryEntry(newEntry);

        return await _uow.SaveChangesAsync(ct);
    }
}