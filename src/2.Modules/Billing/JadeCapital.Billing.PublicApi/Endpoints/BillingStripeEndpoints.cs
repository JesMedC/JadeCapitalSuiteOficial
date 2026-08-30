using JadeCapital.Billing.Application.Stripe;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace JadeCapital.Billing.PublicApi.Endpoints;

/// <summary>
    /// Stripe-backed Billing endpoints (Wave 6, slices 6a.1 + 6a.2).
    ///
    /// <para>
    /// Endpoints in this slice (6a.1 + 6a.2):
    /// <list type="bullet">
    ///   <item>POST <c>/api/billing/stripe/customers</c> — RequireAuthorization.
    ///   Creates or fetches the Stripe Customer mapping for the caller.
    ///   Returns <see cref="StripeCustomerDto"/>.</item>
    ///   <item>POST <c>/api/billing/stripe/checkout</c> — RequireAuthorization.
    ///   Creates a Stripe Checkout session for the caller and returns the URL
    ///   the FE should redirect to. The handler ensures a Stripe Customer
    ///   mapping exists before calling the gateway.</item>
    ///   <item>POST <c>/api/billing/stripe/portal</c> — RequireAuthorization.
    ///   Creates a Stripe Customer Portal session for the caller and returns
    ///   the URL the FE should redirect to. Returns 404 if no Stripe Customer
    ///   mapping exists (user must complete a Checkout first).</item>
    ///   <item>POST <c>/api/billing/stripe/webhooks</c> — AllowAnonymous (Stripe
    ///   signs the request; no user session). Reads the RAW body via
    ///   <c>Request.EnableBuffering</c> + <c>Body.CopyToAsync</c> BEFORE binding
    ///   (JSON binding would corrupt the payload). Returns 200 on success,
    ///   401 on signature failure.</item>
    /// </list>
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

            // Wave 6a.2 — Checkout session creation.
            group.MapPost("/checkout", CreateCheckoutSessionAsync)
                .WithName("StripeCreateCheckoutSession")
                .WithSummary("Create a Stripe Checkout session for a subscription.")
                .Produces<StripeCheckoutSessionDto>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status401Unauthorized)
                .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
                .Produces<ProblemDetails>(StatusCodes.Status502BadGateway)
                .RequireAuthorization()
                .RequireRateLimiting("api-billing");

            // Wave 6a.2 — Customer Portal session creation.
            group.MapPost("/portal", CreatePortalSessionAsync)
                .WithName("StripeCreatePortalSession")
                .WithSummary("Create a Stripe Customer Portal session for the authenticated user.")
                .Produces<StripePortalSessionDto>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status401Unauthorized)
                .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
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
            var claims = ExtractClaims(httpContextAccessor.HttpContext);
            if (claims.UserId is null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(claims.Email)) return Results.Unauthorized();

            var result = await sender.Send(
                new CreateOrGetCustomerCommand(claims.UserId.Value, claims.Email, claims.DisplayName), ct);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : MapStripeError(result.Error);
        }

        private static async Task<IResult> CreateCheckoutSessionAsync(
            [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
            [Microsoft.AspNetCore.Mvc.FromServices] IHttpContextAccessor httpContextAccessor,
            [Microsoft.AspNetCore.Mvc.FromBody] CheckoutRequest req,
            CancellationToken ct)
        {
            var claims = ExtractClaims(httpContextAccessor.HttpContext);
            if (claims.UserId is null) return Results.Unauthorized();

            var cmd = new CreateCheckoutSessionCommand(
                UserId: claims.UserId.Value,
                PriceId: req?.PriceId ?? string.Empty,
                SuccessUrl: req?.SuccessUrl ?? string.Empty,
                CancelUrl: req?.CancelUrl ?? string.Empty,
                Email: claims.Email,
                DisplayName: claims.DisplayName);

            var result = await sender.Send(cmd, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : MapStripeError(result.Error);
        }

        private static async Task<IResult> CreatePortalSessionAsync(
            [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
            [Microsoft.AspNetCore.Mvc.FromServices] IHttpContextAccessor httpContextAccessor,
            [Microsoft.AspNetCore.Mvc.FromBody] PortalRequest req,
            CancellationToken ct)
        {
            var claims = ExtractClaims(httpContextAccessor.HttpContext);
            if (claims.UserId is null) return Results.Unauthorized();

            var cmd = new CreatePortalSessionCommand(
                UserId: claims.UserId.Value,
                ReturnUrl: req?.ReturnUrl ?? string.Empty);

            var result = await sender.Send(cmd, ct);
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

        private sealed record CheckoutRequest(string PriceId, string SuccessUrl, string CancelUrl);
        private sealed record PortalRequest(string ReturnUrl);

        private readonly record struct AuthClaims(Guid? UserId, string? Email, string? DisplayName);

        private static AuthClaims ExtractClaims(HttpContext? http)
        {
            var userIdClaim = http?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var emailClaim = http?.User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                ?? http?.User?.FindFirst("email")?.Value;
            var displayName = http?.User?.FindFirst("name")?.Value;

            Guid? userId = Guid.TryParse(userIdClaim, out var parsed) ? parsed : (Guid?)null;
            return new AuthClaims(userId, emailClaim, displayName);
        }

        private static IResult MapStripeError(Error error)
        {
            // 404 NotFound special-case (e.g. stripe.customer_not_found).
            if (error.Code.StartsWith("notfound.", StringComparison.Ordinal))
                return Results.Problem(
                    type: $"https://jadecapital/errors/{error.Code}",
                    title: "Not found",
                    detail: error.Message,
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?> { ["code"] = error.Code });

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
