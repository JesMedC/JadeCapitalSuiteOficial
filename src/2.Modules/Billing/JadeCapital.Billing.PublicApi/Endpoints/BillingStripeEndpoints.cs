using JadeCapital.Billing.Application.Stripe;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace JadeCapital.Billing.PublicApi.Endpoints;

/// <summary>
/// Stripe-backed Billing endpoints (Wave 6, slice 6a.1).
///
/// <para>
/// Endpoints in this slice:
/// <list type="bullet">
///   <item>POST <c>/api/billing/stripe/customers</c> — RequireAuthorization.
///   Creates or fetches the Stripe Customer mapping for the caller.
///   Returns <see cref="StripeCustomerDto"/>.</item>
///   <item>POST <c>/api/billing/stripe/webhooks</c> — AllowAnonymous (Stripe
///   signs the request; no user session). Reads the RAW body via
///   <c>Request.EnableBuffering</c> + <c>Body.CopyToAsync</c> BEFORE binding
///   (JSON binding would corrupt the payload). Returns 200 on success,
///   401 on signature failure.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Out of scope for 6a.1</b> (lands in 6a.2): checkout session creation,
/// portal session creation, subscription webhook dispatch.
/// </para>
/// </summary>
public static class BillingStripeEndpoints
{
    public static IEndpointRouteBuilder MapBillingStripeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/billing/stripe")
            .WithTags("Billing.Stripe");

        group.MapPost("/customers", CreateOrGetCustomerAsync)
            .WithName("StripeCreateOrGetCustomer")
            .WithSummary("Create or fetch the Stripe Customer mapping for the authenticated user.")
            .Produces<StripeCustomerDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status502BadGateway)
            .RequireAuthorization()
            .RequireRateLimiting("api-billing");

        group.MapPost("/webhooks", HandleWebhookAsync)
            .WithName("StripeWebhook")
            .WithSummary("Receive Stripe webhook events. Signature-verified via Stripe-Signature header.")
            .Produces<WebhookOutcomeDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .AllowAnonymous()
            .RequireRateLimiting("api-general");

        return app;
    }

    private static async Task<IResult> CreateOrGetCustomerAsync(
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        [Microsoft.AspNetCore.Mvc.FromServices] IHttpContextAccessor httpContextAccessor,
        CancellationToken ct)
    {
        // Extract the authenticated user's id + email from the JWT.
        var http = httpContextAccessor.HttpContext;
        var userIdClaim = http?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var emailClaim = http?.User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? http?.User?.FindFirst("email")?.Value;

        if (string.IsNullOrWhiteSpace(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(emailClaim))
            return Results.Unauthorized();

        var displayName = http?.User?.FindFirst("name")?.Value;

        var result = await sender.Send(
            new CreateOrGetCustomerCommand(userId, emailClaim, displayName), ct);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : MapStripeError(result.Error);
    }

    private static async Task<IResult> HandleWebhookAsync(
        HttpRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        [Microsoft.AspNetCore.Mvc.FromServices] Microsoft.Extensions.Logging.ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Stripe.Webhook");

        // RAW body read — JSON binding would corrupt the payload. Mirrors
        // the Stripe docs example for ASP.NET Core webhook handlers.
        req.EnableBuffering();
        using var ms = new MemoryStream();
        await req.Body.CopyToAsync(ms, ct);
        var payload = Encoding.UTF8.GetString(ms.ToArray());
        var signature = req.Headers["Stripe-Signature"].ToString();

        var result = await sender.Send(new HandleWebhookCommand(payload, signature), ct);

        if (result.IsFailure)
        {
            // Map Stripe signature errors to 401 (auth-style failure).
            if (result.Error.Code.StartsWith("stripe.signature_", StringComparison.Ordinal))
            {
                logger.LogWarning("Stripe webhook signature failed: {Code}", result.Error.Code);
                return Results.Json(
                    new { code = result.Error.Code, message = result.Error.Message },
                    statusCode: StatusCodes.Status401Unauthorized);
            }
            logger.LogError("Stripe webhook handler failed: {Code} {Message}", result.Error.Code, result.Error.Message);
            return Results.Json(
                new { code = result.Error.Code, message = result.Error.Message },
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(result.Value);
    }

    private static IResult MapStripeError(Error error)
    {
        // Stripe-specific upstream errors → 502 Bad Gateway (defense: the
        // upstream is the actual failing party, not us).
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
