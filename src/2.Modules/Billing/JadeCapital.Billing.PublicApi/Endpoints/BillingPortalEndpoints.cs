using JadeCapital.Billing.Application.Stripe;
using JadeCapital.Billing.Contracts.Portal;
using JadeCapital.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JadeCapital.Billing.PublicApi.Endpoints;

/// <summary>
/// Self-service billing portal read endpoints (Wave 6, slice 6b.1).
///
/// <para>
/// Endpoints in this slice:
/// <list type="bullet">
///   <item>GET <c>/api/billing/portal/subscription</c> — RequireAuthorization.
///   Returns the caller's own <see cref="BillingPortalSubscriptionDto"/>
///   (mapped from Stripe via <c>IStripeGateway.GetSubscriptionAsync</c>).
///   The handler resolves the caller's userId from the JWT, looks up the
///   local Subscription by userId, then calls the gateway with THAT
///   subscription's StripeSubscriptionId. Cross-user lookup returns 404.</item>
///   <item>GET <c>/api/billing/portal/payment-methods</c> — RequireAuthorization.
///   Returns the caller's own list of
///   <see cref="BillingPortalPaymentMethodDto"/>. Cross-user lookup returns 404.</item>
///   <item>GET <c>/api/billing/portal/invoices</c> — RequireAuthorization.
///   Returns the caller's own list of
///   <see cref="BillingPortalInvoiceDto"/>. Cross-user lookup returns 404.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Cross-user isolation</b>: none of these endpoints accept a
/// <c>stripeCustomerId</c> or <c>stripeSubscriptionId</c> in the request.
/// The handler resolves the caller's identity from the JWT only. A user
/// cannot read another user's billing data, even by tampering with the URL
/// or query string.
/// </para>
///
/// <para>
/// <b>Stripe status mapping</b>:
/// <list type="bullet">
///   <item><c>notfound.*</c> → 404 (no Stripe customer mapping, no local subscription, etc.).</item>
///   <item><c>stripe.unavailable</c>, <c>stripe.timeout</c> → 503 Service Unavailable
///   (the Stripe API is down or slow — caller can retry).</item>
///   <item>Other <c>stripe.*</c> → 502 Bad Gateway (Stripe returned an error).</item>
///   <item><c>validation.*</c> → 422 Unprocessable Entity.</item>
/// </list>
/// </para>
/// </summary>
public static class BillingPortalEndpoints
{
    public static IEndpointRouteBuilder MapBillingPortalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/billing/portal")
            .WithTags("Billing.Portal")
            .RequireAuthorization();

        group.MapGet("/subscription", GetSubscriptionAsync)
            .WithName("BillingPortalGetSubscription")
            .WithSummary("Get the caller's current subscription for the billing portal.")
            .Produces<BillingPortalSubscriptionDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)
            .Produces<ProblemDetails>(StatusCodes.Status502BadGateway)
            .RequireRateLimiting("api-billing");

        group.MapGet("/payment-methods", GetPaymentMethodsAsync)
            .WithName("BillingPortalGetPaymentMethods")
            .WithSummary("List the caller's payment methods for the billing portal.")
            .Produces<IReadOnlyList<BillingPortalPaymentMethodDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)
            .Produces<ProblemDetails>(StatusCodes.Status502BadGateway)
            .RequireRateLimiting("api-billing");

        group.MapGet("/invoices", GetInvoicesAsync)
            .WithName("BillingPortalGetInvoices")
            .WithSummary("List the caller's invoices for the billing portal.")
            .Produces<IReadOnlyList<BillingPortalInvoiceDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)
            .Produces<ProblemDetails>(StatusCodes.Status502BadGateway)
            .RequireRateLimiting("api-billing");

        return app;
    }

    private static async Task<IResult> GetSubscriptionAsync(
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        [Microsoft.AspNetCore.Mvc.FromServices] IHttpContextAccessor httpContextAccessor,
        CancellationToken ct)
    {
        var userId = ExtractUserId(httpContextAccessor.HttpContext);
        if (userId is null) return Results.Unauthorized();

        var result = await sender.Send(new GetSubscriptionQuery(userId.Value), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : MapPortalError(result.Error);
    }

    private static async Task<IResult> GetPaymentMethodsAsync(
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        [Microsoft.AspNetCore.Mvc.FromServices] IHttpContextAccessor httpContextAccessor,
        CancellationToken ct)
    {
        var userId = ExtractUserId(httpContextAccessor.HttpContext);
        if (userId is null) return Results.Unauthorized();

        var result = await sender.Send(new GetPaymentMethodsQuery(userId.Value), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : MapPortalError(result.Error);
    }

    private static async Task<IResult> GetInvoicesAsync(
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        [Microsoft.AspNetCore.Mvc.FromServices] IHttpContextAccessor httpContextAccessor,
        CancellationToken ct)
    {
        var userId = ExtractUserId(httpContextAccessor.HttpContext);
        if (userId is null) return Results.Unauthorized();

        var result = await sender.Send(new GetInvoicesQuery(userId.Value), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : MapPortalError(result.Error);
    }

    private static Guid? ExtractUserId(HttpContext? http)
    {
        var userIdClaim = http?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? http?.User?.FindFirst("sub")?.Value;
        return Guid.TryParse(userIdClaim, out var parsed) ? parsed : (Guid?)null;
    }

    /// <summary>
    /// Maps a handler <see cref="Error"/> to an HTTP response. Distinct from
    /// the 6a.2 mapping in <c>BillingStripeEndpoints</c>: we differentiate
    /// <c>stripe.unavailable</c> / <c>stripe.timeout</c> (503 Service
    /// Unavailable — caller can retry) from other <c>stripe.*</c> errors
    /// (502 Bad Gateway — Stripe returned an error response). This matches
    /// the spec's "Stripe down → 503" requirement.
    /// </summary>
    private static IResult MapPortalError(Error error)
    {
        if (error.Code.StartsWith("notfound.", StringComparison.Ordinal))
            return Results.Problem(
                type: $"https://jadecapital/errors/{error.Code}",
                title: "Not found",
                detail: error.Message,
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["code"] = error.Code });

        // Stripe unavailable / timeout → 503 (the upstream is temporarily
        // unreachable; callers can retry).
        if (error.Code.StartsWith("stripe.unavailable", StringComparison.Ordinal) ||
            error.Code.StartsWith("stripe.timeout", StringComparison.Ordinal))
            return Results.Problem(
                type: $"https://jadecapital/errors/{error.Code}",
                title: "Stripe temporarily unavailable",
                detail: error.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?> { ["code"] = error.Code });

        // Other Stripe errors → 502 Bad Gateway (upstream returned an error).
        if (error.Code.StartsWith("stripe.", StringComparison.Ordinal))
            return Results.Problem(
                type: $"https://jadecapital/errors/{error.Code}",
                title: "Stripe upstream failure",
                detail: error.Message,
                statusCode: StatusCodes.Status502BadGateway,
                extensions: new Dictionary<string, object?> { ["code"] = error.Code });

        // Validation errors → 422.
        return Results.Problem(
            type: $"https://jadecapital/errors/{error.Code}",
            title: "Request failed",
            detail: error.Message,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}
