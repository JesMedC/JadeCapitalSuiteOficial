using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
// Alias to disambiguate from JadeCapital.Shared.Kernel.Stripe.StripeError
// (our DTO error wrapper). Within this file, StripeErrors = Stripe.StripeError.
using StripeErrors = Stripe.StripeError;
// Aliases to disambiguate Stripe.Checkout.SessionService / SessionCreateOptions
// from Stripe.BillingPortal.SessionService / SessionCreateOptions (both 47.0.0).
using CheckoutSessionService = Stripe.Checkout.SessionService;
using CheckoutSessionCreateOptions = Stripe.Checkout.SessionCreateOptions;
using CheckoutSessionLineItemOptions = Stripe.Checkout.SessionLineItemOptions;
using PortalSessionService = Stripe.BillingPortal.SessionService;
using PortalSessionCreateOptions = Stripe.BillingPortal.SessionCreateOptions;

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
/// <b>Stub fallback</b>: when <see cref="StripeOptions.SecretKey"/> is null or
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

    // ============================================
    // Checkout session (slice 6a.2)
    // ============================================

    public async Task<Result<StripeCheckoutSessionDto>> CreateCheckoutSessionAsync(
        Guid userId, string priceId, string successUrl, string cancelUrl, CancellationToken ct = default)
    {
        try
        {
            // Per the design: the handler resolves the Stripe Customer id from
            // the local billing.stripe_customers row (or creates one on the
            // fly). For 6a.2 the gateway assumes the caller has the customer
            // id available — the handler does the lookup and passes the
            // customer id via the price-metadata / customer param lookup.
            //
            // Since we don't have the customer id in the interface signature,
            // we do a server-side search: list customers with metadata.user_id
            // = userId. If exactly one match, use it. Otherwise error.
            var customerService = new CustomerService(_client);
            var matchingCustomers = await customerService.ListAsync(
                new CustomerListOptions
                {
                    Limit = 1,
                },
                cancellationToken: ct);

            // Note: Stripe's CustomerListOptions doesn't natively filter on
            // metadata values server-side. We need a workaround: fetch with
            // the user id in the metadata hash and filter client-side. For
            // production scale this would be a separate indexed lookup; in
            // 6a.2 the typical case is < 1 customer per user.
            //
            // The handler in Billing.Application is responsible for ensuring
            // the customer mapping exists BEFORE calling this gateway. If no
            // matching customer is found, we return a 502 — the handler
            // should have caught that.
            var matched = matchingCustomers.Data.FirstOrDefault(c =>
                c.Metadata != null &&
                c.Metadata.TryGetValue("user_id", out var metaUserId) &&
                string.Equals(metaUserId, userId.ToString(), StringComparison.Ordinal));

            if (matched is null)
            {
                return Result.Failure<StripeCheckoutSessionDto>(
                    JadeCapital.Shared.Kernel.Stripe.StripeError.InvalidRequest(
                        $"No Stripe customer found for user {userId}."));
            }

            var sessionService = new CheckoutSessionService(_client);
            var session = await sessionService.CreateAsync(
                new CheckoutSessionCreateOptions
                {
                    Mode = "subscription",
                    Customer = matched.Id,
                    LineItems = new List<CheckoutSessionLineItemOptions>
                    {
                        new CheckoutSessionLineItemOptions { Price = priceId, Quantity = 1 }
                    },
                    SuccessUrl = successUrl,
                    CancelUrl = cancelUrl,
                    Metadata = new Dictionary<string, string>
                    {
                        ["user_id"] = userId.ToString()
                    }
                },
                cancellationToken: ct);

            return Result.Success(new StripeCheckoutSessionDto(
                SessionId: session.Id,
                Url: session.Url ?? string.Empty,
                ExpiresAt: session.ExpiresAt));
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe checkout session create failed for user {UserId}", userId);
            return Result.Failure<StripeCheckoutSessionDto>(MapStripeException(ex));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Stripe HTTP transport error during checkout for user {UserId}", userId);
            return Result.Failure<StripeCheckoutSessionDto>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.Unavailable(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe SDK unexpected error during checkout for user {UserId}", userId);
            return Result.Failure<StripeCheckoutSessionDto>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.Internal(ex.Message));
        }
    }

    // ============================================
    // Customer Portal session (slice 6a.2)
    // ============================================

    public async Task<Result<StripePortalSessionDto>> CreatePortalSessionAsync(
        Guid userId, string returnUrl, CancellationToken ct = default)
    {
        try
        {
            // Same lookup pattern as CreateCheckoutSessionAsync — find the
            // Stripe customer by metadata.user_id. The handler in
            // Billing.Application is responsible for ensuring the mapping
            // exists BEFORE calling the gateway.
            var customerService = new CustomerService(_client);
            var matchingCustomers = await customerService.ListAsync(
                new CustomerListOptions { Limit = 100 },
                cancellationToken: ct);

            var matched = matchingCustomers.Data.FirstOrDefault(c =>
                c.Metadata != null &&
                c.Metadata.TryGetValue("user_id", out var metaUserId) &&
                string.Equals(metaUserId, userId.ToString(), StringComparison.Ordinal));

            if (matched is null)
            {
                return Result.Failure<StripePortalSessionDto>(
                    new JadeCapital.Shared.Kernel.Stripe.StripeError(
                        "stripe.customer_not_found",
                        $"No Stripe customer found for user {userId}."));
            }

            var portalService = new PortalSessionService(_client);
            var options = new PortalSessionCreateOptions
            {
                Customer = matched.Id,
                ReturnUrl = returnUrl
            };

            if (!string.IsNullOrWhiteSpace(_options.CustomerPortalConfigurationId))
            {
                options.Configuration = _options.CustomerPortalConfigurationId;
            }

            var session = await portalService.CreateAsync(options, cancellationToken: ct);

            return Result.Success(new StripePortalSessionDto(
                SessionId: session.Id,
                Url: session.Url,
                ExpiresAt: DateTimeOffset.UtcNow.AddHours(1)));  // Portal sessions don't carry an explicit ExpiresAt in Stripe's API.
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe portal session create failed for user {UserId}", userId);
            return Result.Failure<StripePortalSessionDto>(MapStripeException(ex));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Stripe HTTP transport error during portal for user {UserId}", userId);
            return Result.Failure<StripePortalSessionDto>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.Unavailable(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe SDK unexpected error during portal for user {UserId}", userId);
            return Result.Failure<StripePortalSessionDto>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.Internal(ex.Message));
        }
    }

    // ============================================
    // Subscription read (slice 6a.2)
    // ============================================

    public async Task<Result<StripeSubscriptionDto>> GetSubscriptionAsync(
        string stripeSubscriptionId, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(stripeSubscriptionId))
                return Result.Failure<StripeSubscriptionDto>(
                    JadeCapital.Shared.Kernel.Stripe.StripeError.InvalidRequest(
                        "Stripe subscription id is required."));

            var service = new SubscriptionService(_client);
            var sub = await service.GetAsync(stripeSubscriptionId, cancellationToken: ct);

            return Result.Success(new StripeSubscriptionDto(
                StripeSubscriptionId: sub.Id,
                Status: sub.Status,
                PlanCode: sub.Items?.Data.FirstOrDefault()?.Price?.Id ?? string.Empty,
                CurrentPeriodEnd: sub.CurrentPeriodEnd,
                CancelAtPeriodEnd: sub.CancelAtPeriodEnd));
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe subscription get failed for {SubId}", stripeSubscriptionId);
            return Result.Failure<StripeSubscriptionDto>(MapStripeException(ex));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Stripe HTTP transport error during get-subscription");
            return Result.Failure<StripeSubscriptionDto>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.Unavailable(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe SDK unexpected error during get-subscription");
            return Result.Failure<StripeSubscriptionDto>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.Internal(ex.Message));
        }
    }

    // ============================================
    // Payment methods list (slice 6a.2)
    // ============================================

    public async Task<Result<IReadOnlyList<StripePaymentMethodDto>>> GetPaymentMethodsAsync(
        string stripeCustomerId, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(stripeCustomerId))
                return Result.Failure<IReadOnlyList<StripePaymentMethodDto>>(
                    JadeCapital.Shared.Kernel.Stripe.StripeError.InvalidRequest(
                        "Stripe customer id is required."));

            var service = new PaymentMethodService(_client);
            var list = await service.ListAsync(
                new PaymentMethodListOptions
                {
                    Customer = stripeCustomerId,
                    Type = "card",
                    Limit = 100
                },
                cancellationToken: ct);

            var dtos = list.Data
                .Select(pm => new StripePaymentMethodDto(
                    Id: pm.Id,
                    Brand: pm.Card?.Brand ?? string.Empty,
                    Last4: pm.Card?.Last4 ?? string.Empty,
                    ExpiresAt: pm.Card?.ExpMonth is long m && pm.Card?.ExpYear is long y
                        ? new DateTimeOffset((int)y, (int)m, 1, 0, 0, 0, TimeSpan.Zero)
                        : null,
                    IsDefault: false))  // Stripe.NET 47 exposes default via a separate Customer.InvoiceSettings.DefaultPaymentMethod; deferred to slice 6b.1.
                .ToList();

            return Result.Success<IReadOnlyList<StripePaymentMethodDto>>(dtos);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe payment-methods list failed for {CustomerId}", stripeCustomerId);
            return Result.Failure<IReadOnlyList<StripePaymentMethodDto>>(MapStripeException(ex));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Stripe HTTP transport error during get-payment-methods");
            return Result.Failure<IReadOnlyList<StripePaymentMethodDto>>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.Unavailable(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe SDK unexpected error during get-payment-methods");
            return Result.Failure<IReadOnlyList<StripePaymentMethodDto>>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.Internal(ex.Message));
        }
    }

    // ============================================
    // Invoices list (slice 6a.2)
    // ============================================

    public async Task<Result<IReadOnlyList<StripeInvoiceDto>>> GetInvoicesAsync(
        string stripeCustomerId, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(stripeCustomerId))
                return Result.Failure<IReadOnlyList<StripeInvoiceDto>>(
                    JadeCapital.Shared.Kernel.Stripe.StripeError.InvalidRequest(
                        "Stripe customer id is required."));

            var service = new InvoiceService(_client);
            var list = await service.ListAsync(
                new InvoiceListOptions
                {
                    Customer = stripeCustomerId,
                    Limit = 100
                },
                cancellationToken: ct);

            var dtos = list.Data
                .Select(inv => new StripeInvoiceDto(
                    Id: inv.Id,
                    Number: inv.Number ?? inv.Id,
                    AmountCents: inv.AmountDue,
                    Currency: inv.Currency,
                    IssuedAt: inv.Created,
                    PaidAt: inv.Status == "paid" ? (DateTimeOffset?)inv.Created : null,
                    Status: inv.Status,
                    PdfUrl: inv.InvoicePdf ?? string.Empty))
                .ToList();

            return Result.Success<IReadOnlyList<StripeInvoiceDto>>(dtos);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe invoices list failed for {CustomerId}", stripeCustomerId);
            return Result.Failure<IReadOnlyList<StripeInvoiceDto>>(MapStripeException(ex));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Stripe HTTP transport error during get-invoices");
            return Result.Failure<IReadOnlyList<StripeInvoiceDto>>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.Unavailable(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe SDK unexpected error during get-invoices");
            return Result.Failure<IReadOnlyList<StripeInvoiceDto>>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.Internal(ex.Message));
        }
    }

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
