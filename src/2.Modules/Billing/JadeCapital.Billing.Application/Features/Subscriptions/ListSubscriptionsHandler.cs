using JadeCapital.Billing.Contracts.Subscriptions;
using JadeCapital.Billing.Domain.Subscriptions;
using JadeCapital.Shared.Kernel.Results;
using MediatR;

namespace JadeCapital.Billing.Application.Features.Subscriptions;

/// <summary>Query: list Admin-visible subscriptions filtered by status, paged.</summary>
public sealed record ListSubscriptionsQuery(string Status, int Page, int PageSize)
    : IRequest<Result<PagedSubscriptions>>;

/// <summary>
/// Lists subscriptions for the Admin surface. No actor capture (reads do not
/// mutate state); paged + status-filtered by design (slice 0f.5 design.md
/// "list/search" requirement). Validates pagination bounds and forwards the
/// (lowercased) status name to the repository.
/// </summary>
public sealed class ListSubscriptionsHandler : IRequestHandler<ListSubscriptionsQuery, Result<PagedSubscriptions>>
{
    private readonly ISubscriptionAdminRepository _repo;

    public ListSubscriptionsHandler(ISubscriptionAdminRepository repo, IPlanLookup _ /* unused */)
    {
        _repo = repo;
    }

    public async Task<Result<PagedSubscriptions>> Handle(ListSubscriptionsQuery req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Status))
            return Result.Failure<PagedSubscriptions>(Error.Validation(
                "validation.subscription.status_required", "Status filter is required."));
        if (req.Page < 1)
            return Result.Failure<PagedSubscriptions>(Error.Validation(
                "validation.subscription.page_invalid", "Page must be >= 1."));
        if (req.PageSize < 1 || req.PageSize > 200)
            return Result.Failure<PagedSubscriptions>(Error.Validation(
                "validation.subscription.page_size_invalid", "PageSize must be between 1 and 200."));

        var status = req.Status.Trim().ToLowerInvariant();
        var items = await _repo.ListPagedAsync(status, req.Page, req.PageSize, ct);
        return Result.Success(items);
    }
}
