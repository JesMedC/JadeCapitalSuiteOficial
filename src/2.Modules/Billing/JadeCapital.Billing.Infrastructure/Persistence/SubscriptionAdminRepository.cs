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
}
