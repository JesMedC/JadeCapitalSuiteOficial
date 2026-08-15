using JadeCapital.Billing.PublicApi.Services;
using JadeCapital.Shared.Kernel.Results;
using MediatR;

namespace JadeCapital.Billing.PublicApi.Endpoints;

/// <summary>
/// Public-facing Billing endpoints. Wave-1.3 of
/// <c>jade-trader-os-core-portals</c>. AllowAnonymous: the marketing pricing
/// pages must fetch the catalog before the visitor authenticates.
///
/// Endpoints:
/// <list type="bullet">
///   <item>GET <c>/api/billing/plans</c> — list eligible, non-deprecated plans
///   ordered by ascending monthly price. Source of truth: <c>billing.plans</c>
///   (Admin-editable, seeded in migration 0008 with starter/pro/elite).</item>
/// </list>
///
/// The Admin write path lives in <c>JadeCapital.Admin.Api</c> (deny-by-default
/// via <c>AdminOnly</c> policy + <c>RequireAdminPolicyHandler</c>). Public
/// surface here deliberately exposes ONLY the catalog — no price history,
/// owner projections, or subscription state can leak to anonymous callers.
/// </summary>
public static class BillingPublicEndpoints
{
    public static IEndpointRouteBuilder MapBillingPublicEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/billing")
            .WithTags("Billing.Public")
            .AllowAnonymous();

        group.MapGet("/plans", ListPlansAsync)
            .WithName("PublicListPlans")
            .WithSummary("Public list of plans eligible for self-service (price + currency + name).")
            .Produces<IReadOnlyList<JadeCapital.Billing.PublicApi.Contracts.PlanInfo>>(StatusCodes.Status200OK)
            .RequireRateLimiting("api-general");

        return app;
    }

    private static async Task<IResult> ListPlansAsync(
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetPublicPlansQuery(), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static IResult ProblemFromResult(Error error)
        => Results.Problem(
            type: $"https://jadecapital/errors/{error.Code}",
            title: "Request failed",
            detail: error.Message,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
