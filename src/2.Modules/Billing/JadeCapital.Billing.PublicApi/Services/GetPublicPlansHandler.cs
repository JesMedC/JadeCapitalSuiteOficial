using JadeCapital.Billing.Application.Features.Subscriptions;
using JadeCapital.Billing.PublicApi.Contracts;
using JadeCapital.Shared.Kernel.Results;
using MediatR;

namespace JadeCapital.Billing.PublicApi.Services;

/// <summary>
/// MediatR query: list the public-visible plan catalog (eligible and not
/// deprecated). Wave-1.3 of <c>jade-trader-os-core-portals</c>. Used by the
/// pricing/landing pages so the frontend no longer hardcodes price points.
/// </summary>
public sealed record GetPublicPlansQuery : IRequest<Result<IReadOnlyList<PlanInfo>>>;

/// <summary>
/// Handler. Delegates to <see cref="IPlanLookup.ListEligibleForSelfServiceAsync"/>
/// (which already filters <c>IsEligibleForSelfService AND !IsDeprecated</c> at
/// the EF Core query level — no in-memory post-filtering) and projects the
/// domain aggregate onto the public <see cref="PlanInfo"/> DTO. Sorted by
/// ascending monthly price so the pricing UI renders low → high without
/// re-sorting on the client.
/// </summary>
public sealed class GetPublicPlansHandler
    : IRequestHandler<GetPublicPlansQuery, Result<IReadOnlyList<PlanInfo>>>
{
    private readonly IPlanLookup _plans;

    public GetPublicPlansHandler(IPlanLookup plans) => _plans = plans;

    public async Task<Result<IReadOnlyList<PlanInfo>>> Handle(
        GetPublicPlansQuery request, CancellationToken ct)
    {
        var plans = await _plans.ListEligibleForSelfServiceAsync(ct);

        var items = plans
            .Select(p => new PlanInfo(
                Code: p.Code.Value,
                Name: p.Name,
                MonthlyPrice: p.MonthlyPrice.Amount,
                Currency: p.MonthlyPrice.CurrencyCode,
                IsEligibleForSelfService: p.IsEligibleForSelfService))
            .ToList();

        return Result.Success<IReadOnlyList<PlanInfo>>(items);
    }
}
