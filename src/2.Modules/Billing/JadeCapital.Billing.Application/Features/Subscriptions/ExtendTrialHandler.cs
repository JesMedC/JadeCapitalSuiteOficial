using JadeCapital.Billing.Infrastructure.Persistence;
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
/// <c>ExtendTrial</c> mutator, persists the state change AND the new
/// history entry (the latter explicitly via <c>DbContext.Add</c> to dodge
/// the slice 0e collection-tracking bug).
/// </summary>
public sealed class ExtendTrialHandler : IRequestHandler<ExtendTrialCommand, Result>
{
    private readonly ISubscriptionAdminRepository _repo;
    private readonly ISubscriptionAdminUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly BillingDbContext _db;

    public ExtendTrialHandler(
        ISubscriptionAdminRepository repo,
        ISubscriptionAdminUnitOfWork uow,
        IClock clock,
        BillingDbContext db)
    {
        _repo = repo;
        _uow = uow;
        _clock = clock;
        _db = db;
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
            _db.SubscriptionHistory.Add(newEntry);

        return await _uow.SaveChangesAsync(ct);
    }
}
