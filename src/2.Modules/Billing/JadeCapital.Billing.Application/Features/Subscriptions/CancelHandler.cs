using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using MediatR;

namespace JadeCapital.Billing.Application.Features.Subscriptions;

/// <summary>Command: cancel a subscription with a required reason.</summary>
public sealed record CancelCommand(
    Guid SubscriptionId,
    string Reason,
    int ObservedVersion,
    string Actor) : IRequest<Result>;

/// <summary>
/// Cancellation handler. Loads the aggregate (with its history), delegates
/// to the aggregate's <c>Cancel</c> mutator, then stages the new history
/// entry via <see cref="ISubscriptionAdminUnitOfWork.AddHistoryEntry"/> and
/// commits. The UoW abstraction dodges the slice 0e collection-tracking bug
/// where EF Core marks entries appended through the aggregate's private
/// backing field as <c>Modified</c> instead of <c>Added</c>.
/// </summary>
public sealed class CancelHandler : IRequestHandler<CancelCommand, Result>
{
    private readonly ISubscriptionAdminRepository _repo;
    private readonly ISubscriptionAdminUnitOfWork _uow;
    private readonly IClock _clock;

    public CancelHandler(
        ISubscriptionAdminRepository repo,
        ISubscriptionAdminUnitOfWork uow,
        IClock clock)
    {
        _repo = repo;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result> Handle(CancelCommand req, CancellationToken ct)
    {
        var subscription = await _repo.LoadForUpdateAsync(req.SubscriptionId, ct);
        if (subscription is null)
            return Result.Failure(Error.NotFound("notfound.subscription.not_found",
                "Subscription was not found."));

        var mutatorResult = subscription.Cancel(req.Reason, req.ObservedVersion, req.Actor, _clock.UtcNow);
        if (mutatorResult.IsFailure) return mutatorResult;

        var newEntry = subscription.LastHistoryEntry;
        if (newEntry is not null && mutatorResult.IsSuccess)
            _uow.AddHistoryEntry(newEntry);

        return await _uow.SaveChangesAsync(ct);
    }
}