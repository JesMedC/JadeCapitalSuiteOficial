using JadeCapital.Admin.Api.Authorization;
using JadeCapital.Billing.Application.Features.Subscriptions;
using JadeCapital.Billing.Contracts.Subscriptions;
using JadeCapital.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JadeCapital.Admin.Api.Endpoints;

/// <summary>
/// Admin-only subscription endpoints. Slice 0f of
/// <c>jade-trader-os-core-portals</c>.
///
/// Endpoints:
/// <list type="bullet">
///   <item>GET  <c>/api/admin/subscriptions?status=&amp;page=&amp;pageSize=</c>
///   — paged list/search, scoped to one status (no "all" in slice 0f).</item>
///   <item>GET  <c>/api/admin/subscriptions/{id}</c>
///   — detail + plan + owner projection + complete history (newest-first).</item>
///   <item>POST <c>/api/admin/subscriptions/{id}/change-tier</c>
///   — change plan (requires version + actor + target plan code).</item>
///   <item>POST <c>/api/admin/subscriptions/{id}/cancel</c>
///   — cancel with a required reason.</item>
///   <item>POST <c>/api/admin/subscriptions/{id}/extend-trial</c>
///   — extend a Trial subscription's end date.</item>
/// </list>
///
/// Every endpoint carries <c>RequireAuthorization("AdminOnly")</c>. The
/// policy is registered with the explicit
/// <see cref="RequireAdminPolicyHandler"/> so authorization fails BEFORE
/// the MediatR dispatch — no subscription lookup or mutation side effect can
/// leak information about existence, owner, plan, or history to non-Admins.
/// </summary>
public static class AdminSubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapAdminSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/subscriptions")
            .WithTags("Admin.Subscriptions")
            .RequireAuthorization("AdminOnly");

        group.MapGet("/", ListAsync)
            .WithName("AdminListSubscriptions")
            .WithSummary("Paged list of subscriptions filtered by status.")
            .Produces<PagedSubscriptions>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireRateLimiting("api-general");

        group.MapGet("/{id:guid}", DetailAsync)
            .WithName("AdminGetSubscription")
            .WithSummary("Subscription detail + plan + owner projection + complete history.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRateLimiting("api-general");

        group.MapPost("/{id:guid}/change-tier", ChangeTierAsync)
            .WithName("AdminChangeTier")
            .WithSummary("Change a subscription's tier (requires observed version).")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting("auth-strict");

        group.MapPost("/{id:guid}/cancel", CancelAsync)
            .WithName("AdminCancelSubscription")
            .WithSummary("Cancel a subscription (reason required, observed version required).")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting("auth-strict");

        group.MapPost("/{id:guid}/extend-trial", ExtendTrialAsync)
            .WithName("AdminExtendTrial")
            .WithSummary("Extend a Trial subscription's end date.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting("auth-strict");

        return app;
    }

    private static async Task<IResult> ListAsync(
        [Microsoft.AspNetCore.Mvc.FromQuery] string status,
        [Microsoft.AspNetCore.Mvc.FromQuery] int? page,
        [Microsoft.AspNetCore.Mvc.FromQuery] int? pageSize,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(
            new ListSubscriptionsQuery(status ?? string.Empty, page ?? 1, pageSize ?? 20), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> DetailAsync(
        [Microsoft.AspNetCore.Mvc.FromRoute] Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetSubscriptionDetailQuery(id), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> ChangeTierAsync(
        [Microsoft.AspNetCore.Mvc.FromRoute] Guid id,
        [Microsoft.AspNetCore.Mvc.FromBody] ChangeTierRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        var actor = ResolveActor(http);
        var result = await sender.Send(
            new ChangeTierCommand(id, req.NewPlanCode, req.ObservedVersion, actor), ct);
        return result.IsSuccess
            ? Results.NoContent()
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> CancelAsync(
        [Microsoft.AspNetCore.Mvc.FromRoute] Guid id,
        [Microsoft.AspNetCore.Mvc.FromBody] CancelRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        var actor = ResolveActor(http);
        var result = await sender.Send(
            new CancelCommand(id, req.Reason, req.ObservedVersion, actor), ct);
        return result.IsSuccess
            ? Results.NoContent()
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> ExtendTrialAsync(
        [Microsoft.AspNetCore.Mvc.FromRoute] Guid id,
        [Microsoft.AspNetCore.Mvc.FromBody] ExtendTrialRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        var actor = ResolveActor(http);
        var result = await sender.Send(
            new ExtendTrialCommand(id, req.NewTrialEndsAt, req.ObservedVersion, actor), ct);
        return result.IsSuccess
            ? Results.NoContent()
            : ProblemFromResult(result.Error);
    }

    /// <summary>Actor capture from the JWT's NameIdentifier (sub) claim.</summary>
    private static string ResolveActor(HttpContext http)
        => http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
           ?? "admin@unknown";

    private static IResult ProblemFromResult(Error error)
    {
        var status = error.Code switch
        {
            var c when c.StartsWith("validation", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status400BadRequest,
            var c when c.StartsWith("notfound", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status404NotFound,
            var c when c.StartsWith("conflict", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status409Conflict,
            var c when c.StartsWith("unauthorized", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status401Unauthorized,
            var c when c.StartsWith("forbidden", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        return Results.Problem(
            type: $"https://jadecapital/errors/{error.Code}",
            title: "Request failed",
            detail: error.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}

public sealed record ChangeTierRequest(string NewPlanCode, int ObservedVersion);
public sealed record CancelRequest(string Reason, int ObservedVersion);
public sealed record ExtendTrialRequest(DateTimeOffset NewTrialEndsAt, int ObservedVersion);
