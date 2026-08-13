using JadeCapital.Billing.Contracts.Subscriptions;
using JadeCapital.Billing.Domain.Subscriptions;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Billing.Application.Features.Subscriptions;

/// <summary>
/// Repository abstraction the Admin surface uses. Reads list + detail; loads
/// subscriptions with their history for the mutator write path. Implementation
/// lives in JadeCapital.Billing.Infrastructure.Persistence.
/// </summary>
public interface ISubscriptionAdminRepository
{
    /// <summary>Returns a paged slice filtered by the lowercase status name
    /// (<c>active</c>, <c>trial</c>, <c>cancelled</c>, <c>expired</c>).</summary>
    Task<PagedSubscriptions> ListPagedAsync(string status, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Loads a subscription with its history for mutator write paths.
    /// Returns null if not found; the handler converts that into NotFound.</summary>
    Task<Subscription?> LoadForUpdateAsync(Guid subscriptionId, CancellationToken ct = default);
}

/// <summary>Unit-of-work boundary for the Admin write paths. Persists the
/// aggregate's mutations (status, version, history) atomically.</summary>
public interface ISubscriptionAdminUnitOfWork
{
    Task<Result> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Plan lookup by its lowercase <see cref="PlanCode"/> value.</summary>
public interface IPlanLookup
{
    Task<Plan?> FindByCodeAsync(string planCode, CancellationToken ct = default);
}

/// <summary>Identity.Contracts projection lookup. Implementation lives in
/// Identity.Infrastructure and exposes only <c>Email</c> + <c>DisplayName</c>
/// via <see cref="JadeCapital.Identity.Contracts.Projections.IUserOwnerProjection"/>.</summary>
public interface IOwnerProjectionLookup
{
    Task<JadeCapital.Identity.Contracts.Projections.IUserOwnerProjection?> FindByUserIdAsync(
        Guid userId, CancellationToken ct = default);
}
