using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
// Alias to disambiguate from JadeCapital.Shared.Kernel.Stripe.StripeError
// (our DTO error wrapper). Within this file, StripeErrors = Stripe.StripeError.
using StripeErrors = Stripe.StripeError;

namespace JadeCapital.Billing.Infrastructure.Stripe;

/// <summary>
/// Real Stripe SDK gateway (Wave 6, slice 6a.1).
///
/// <para>
/// Mirrors the <c>OllamaHttpClient</c> precedent (Wave 5, slice 5b.1):
/// never throws on transient provider failures — every method returns
/// <c>Result&lt;T&gt;</c> with a <see cref="StripeError"/>. Stripe SDK
/// exceptions (<see cref="StripeException"/>, <see cref="HttpRequestException"/>,
/// <see cref="TaskCanceledException"/>) are mapped to well-known codes:
/// <c>stripe.authentication_error</c>, <c>stripe.api_error</c>,
/// <c>stripe.rate_limit_error</c>, <c>stripe.invalid_request</c>,
/// <c>stripe.unavailable</c>, <c>stripe.timeout</c>,
/// <c>stripe.internal_error</c>.
/// </para>
///
/// <para>
/// <b>Testing seam</b>: the gateway depends on
/// <see cref="IStripeClient"/> (Stripe's own abstraction over the HTTP
/// transport). In tests we inject a fake <see cref="IStripeClient"/> that
/// returns synthetic responses — no real network calls, no real API keys.
/// </para>
///
/// <para>
/// <b>Stub fallback</b>: when <see cref="StripeOptions.ApiKey"/> is null or
/// empty, DI MUST register <see cref="StubStripeGateway"/> instead. This
/// gateway is never constructed without a valid key in production.
/// </para>
/// </summary>
public sealed class StripeGateway : IStripeGateway
{
    private readonly IStripeClient _client;
    private readonly StripeOptions _options;
    private readonly ILogger<StripeGateway> _logger;

    public StripeGateway(
        IStripeClient client,
        IOptions<StripeOptions> options,
        ILogger<StripeGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;

        // Stripe.NET 47.0.0 has StripeConfiguration.ApiVersion as a read-only
        // property (it pins the LIBRARY default, not a per-deployment override).
        // Per-request overrides go through RequestOptions.ApiVersion, but for
        // Wave 6a.1 we trust the library default (which the spec pins to
        // "2025-08-13" via the user's Wave 6 decision #1). The options field
        // is preserved so 6a.2 can override per-request if needed.
    }

    // ============================================
    // Customers
    // ============================================

    public async Task<Result<StripeCustomerDto>> CreateOrGetCustomerAsync(
        Guid userId, string email, string? displayName, CancellationToken ct = default)
    {
        try
        {
            var service = new CustomerService(_client);

            // Idempotency: first check if a Customer with this email already exists.
            // Stripe's `customers.list(email=...)` is a server-side filter — no
            // pagination needed for the limit=1 case.
            var existing = await service.ListAsync(
                new CustomerListOptions { Email = email, Limit = 1 },
                cancellationToken: ct);

            if (existing.Data.Count > 0)
            {
                var c = existing.Data[0];
                return Result.Success(new StripeCustomerDto(
                    StripeCustomerId: c.Id,
                    Email: c.Email ?? email,
                    DisplayName: c.Name,
                    CreatedAt: c.Created));
            }

            var created = await service.CreateAsync(
                new CustomerCreateOptions
                {
                    Email = email,
                    Name = displayName,
                    Metadata = new Dictionary<string, string>
                    {
                        ["user_id"] = userId.ToString()
                    }
                },
                cancellationToken: ct);

            return Result.Success(new StripeCustomerDto(
                StripeCustomerId: created.Id,
                Email: created.Email ?? email,
                DisplayName: created.Name,
                CreatedAt: created.Created));
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe customer create failed for user {UserId}", userId);
            return Result.Failure<StripeCustomerDto>(MapStripeException(ex));
        }
        catch (OperationCanceledException)
        {
            throw; // Cancellation contract: caller-initiated, propagate.
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Stripe HTTP transport error for user {UserId}", userId);
            return Result.Failure<StripeCustomerDto>(JadeCapital.Shared.Kernel.Stripe.StripeError.Unavailable(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe SDK unexpected error for user {UserId}", userId);
            return Result.Failure<StripeCustomerDto>(JadeCapital.Shared.Kernel.Stripe.StripeError.Internal(ex.Message));
        }
    }

    // ============================================
    // Webhook signature verification
    // ============================================

    public Task<Result<StripeWebhookEvent>> VerifyWebhookAsync(
        string payload, string signatureHeader, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
            {
                _logger.LogWarning("Stripe webhook verification attempted without WebhookSecret configured");
                return Task.FromResult(Result.Failure<StripeWebhookEvent>(
                    JadeCapital.Shared.Kernel.Stripe.StripeError.SignatureMissing("Webhook secret not configured.")));
            }

            if (string.IsNullOrWhiteSpace(signatureHeader))
            {
                return Task.FromResult(Result.Failure<StripeWebhookEvent>(
                    JadeCapital.Shared.Kernel.Stripe.StripeError.SignatureMissing("Stripe-Signature header is required.")));
            }

            // EventUtility.ConstructEvent is synchronous and CPU-bound (HMAC).
            // Per Stripe docs, it throws StripeException on bad signatures
            // and ArgumentException on malformed headers.
            var stripeEvent = EventUtility.ConstructEvent(
                payload, signatureHeader, _options.WebhookSecret);

            var webhookEvent = new StripeWebhookEvent(
                EventId: stripeEvent.Id,
                Type: stripeEvent.Type,
                PayloadJson: payload,
                OccurredAt: stripeEvent.Created);

            return Task.FromResult(Result.Success(webhookEvent));
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature verification failed: {Message}", ex.Message);
            return Task.FromResult(Result.Failure<StripeWebhookEvent>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.SignatureInvalid(ex.StripeError?.Message ?? ex.Message)));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Stripe webhook signature header malformed: {Message}", ex.Message);
            return Task.FromResult(Result.Failure<StripeWebhookEvent>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.SignatureInvalid(ex.Message)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe webhook verification unexpected error");
            return Task.FromResult(Result.Failure<StripeWebhookEvent>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.Internal(ex.Message)));
        }
    }

    // ============================================
    // Reads (land in slice 6a.2)
    // ============================================
    // (Checkout / Portal / Subscription / PaymentMethod / Invoice methods
    // arrive in slice 6a.2 — the interface is intentionally narrow in 6a.1.)

    /// <summary>
    /// Maps a Stripe SDK exception to the appropriate <see cref="JadeCapital.Shared.Kernel.Stripe.StripeError"/>.
    /// Stripe's <see cref="StripeErrors.Code"/> is a short string (see
    /// https://stripe.com/docs/error-codes) — we match on the documented codes
    /// AND the HTTP status to map onto our categories.
    /// </summary>
    internal static JadeCapital.Shared.Kernel.Stripe.StripeError MapStripeException(StripeException ex)
    {
        var code = ex.StripeError?.Code;
        var message = ex.StripeError?.Message ?? ex.Message;

        // HTTP-status-driven mapping (most reliable).
        if (ex.HttpStatusCode == System.Net.HttpStatusCode.Unauthorized)
            return JadeCapital.Shared.Kernel.Stripe.StripeError.Authentication(message);
        if (ex.HttpStatusCode == System.Net.HttpStatusCode.TooManyRequests)
            return JadeCapital.Shared.Kernel.Stripe.StripeError.RateLimit(message);
        if (ex.HttpStatusCode == System.Net.HttpStatusCode.BadRequest)
            return JadeCapital.Shared.Kernel.Stripe.StripeError.InvalidRequest(message);
        if (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            return JadeCapital.Shared.Kernel.Stripe.StripeError.InvalidRequest(message);

        // Code-driven mapping (covers transport-level Stripe errors that may
        // not carry a status).
        if (code is "invalid_api_key" or "authentication_required")
            return JadeCapital.Shared.Kernel.Stripe.StripeError.Authentication(message);
        if (code is "rate_limit" or "rate_limited")
            return JadeCapital.Shared.Kernel.Stripe.StripeError.RateLimit(message);
        if (code is "api_connection_error" or "api_error")
            return JadeCapital.Shared.Kernel.Stripe.StripeError.Unavailable(message);
        if (code is "invalid_request" or "invalid_request_error")
            return JadeCapital.Shared.Kernel.Stripe.StripeError.InvalidRequest(message);

        return JadeCapital.Shared.Kernel.Stripe.StripeError.Api(message);
    }
}
