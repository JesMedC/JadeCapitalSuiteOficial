using JadeCapital.Billing.Application.Features.Subscriptions;
using JadeCapital.Billing.Contracts.Subscriptions;
using JadeCapital.Billing.Domain.Subscriptions;
using JadeCapital.Billing.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Billing.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ISubscriptionAdminRepository"/>.
/// Slice 0f. Status-filtered, paged list; load-with-history for mutator
/// write paths. The handler layer is responsible for optimistic-concurrency
/// validation; the repository is a pure data-access boundary.
/// </summary>
public sealed class SubscriptionAdminRepository : ISubscriptionAdminRepository
{
    private readonly BillingDbContext _db;

    public SubscriptionAdminRepository(BillingDbContext db) { _db = db; }

    public async Task<PagedSubscriptions> ListPagedAsync(string status, int page, int pageSize, CancellationToken ct = default)
    {
        var items = await _db.Subscriptions
            .AsNoTracking()
            .Where(s => s.Status == ParseStatus(status))
            .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SubscriptionListItem(
                s.Id,
                s.UserId,
                s.PlanCode.Value,
                s.Status.ToString(),
                s.UpdatedAt ?? s.CreatedAt,
                s.Version))
            .ToListAsync(ct);

        var total = await _db.Subscriptions
            .Where(s => s.Status == ParseStatus(status))
            .CountAsync(ct);

        return new PagedSubscriptions(items, page, pageSize, total);
    }

    public Task<Subscription?> LoadForUpdateAsync(Guid subscriptionId, CancellationToken ct = default)
        => _db.Subscriptions
            .Include("_history")
            .FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);

    /// <summary>
    /// Wave 6a.2: looks up a subscription by its Stripe subscription id
    /// (<c>sub_...</c>). The webhook handler uses this to resolve a
    /// <c>customer.subscription.*</c> event to the local subscription
    /// without an admin lookup. The DB has a UNIQUE partial index on
    /// <c>stripe_subscription_id</c> (created by migration 0023), so the
    /// query is a fast equality lookup with at most one row.
    /// </summary>
    public Task<Subscription?> FindByStripeSubscriptionIdAsync(
        string stripeSubscriptionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stripeSubscriptionId))
            return Task.FromResult<Subscription?>(null);

        return _db.Subscriptions
            .Include("_history")
            .FirstOrDefaultAsync(
                s => s.StripeSubscriptionId == stripeSubscriptionId,
                ct);
    }

    /// <summary>
    /// Wave 6b.1: looks up a subscription by the owning user's id. The
    /// billing portal read API uses this to resolve the caller's local
    /// subscription from the JWT-derived userId. The DB has a UNIQUE index
    /// on <c>user_id</c> (mirrored in <c>SubscriptionConfiguration</c>), so
    /// the query is a fast equality lookup with at most one row.
    /// </summary>
    public Task<Subscription?> FindByUserIdAsync(
        Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            return Task.FromResult<Subscription?>(null);

        return _db.Subscriptions
            .Include("_history")
            .FirstOrDefaultAsync(s => s.UserId == userId, ct);
    }

    private static SubscriptionStatus ParseStatus(string status)
        => Enum.TryParse<SubscriptionStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : SubscriptionStatus.Active;
}

/// <summary>EF Core implementation of <see cref="ISubscriptionAdminUnitOfWork"/>.</summary>
public sealed class BillingAdminUnitOfWork : ISubscriptionAdminUnitOfWork
{
    private readonly BillingDbContext _db;
    public BillingAdminUnitOfWork(BillingDbContext db) { _db = db; }

    public void AddHistoryEntry(SubscriptionHistoryEntry entry) =>
        _db.SubscriptionHistory.Add(entry);

    public async Task<Result> SaveChangesAsync(CancellationToken ct = default)
    {
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>Plan lookup by lowercase <see cref="Domain.Subscriptions.PlanCode.Value"/>.</summary>
public sealed class PlanLookup : IPlanLookup
{
    private readonly BillingDbContext _db;
    public PlanLookup(BillingDbContext db) { _db = db; }

    public Task<Plan?> FindByCodeAsync(string planCode, CancellationToken ct = default)
        => _db.Plans
            .FirstOrDefaultAsync(p => p.Code.Value == planCode.ToLowerInvariant(), ct);

    public async Task<IReadOnlyList<Plan>> ListEligibleForSelfServiceAsync(CancellationToken ct = default)
        => await _db.Plans
            .AsNoTracking()
            .Where(p => p.IsEligibleForSelfService && !p.IsDeprecated)
            .OrderBy(p => p.MonthlyPrice.Amount)
            .ToListAsync(ct);
}
