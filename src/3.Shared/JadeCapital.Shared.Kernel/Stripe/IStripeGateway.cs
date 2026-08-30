using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Shared.Kernel.Stripe;

/// <summary>
/// Stripe gateway abstraction (Wave 6, slices 6a.1 + 6a.2).
///
/// <para>
/// <b>Why Shared.Kernel</b>: mirrors the <c>IQuoteProvider</c> (Wave 4b) and
/// <c>IAIProvider</c> (Wave 5b) precedent. The wire shapes
/// (<see cref="StripeCustomerDto"/>, <see cref="StripeWebhookEvent"/>, etc.)
/// are cross-module stable — Billing owns the default impl (<c>StripeGateway</c>
/// in Billing.Infrastructure) but Identity or Trading could later consume
/// the same abstraction.
/// </para>
///
/// <para>
/// <b>Failure semantics</b>: callers MUST NOT wrap any method in try/catch
/// for transient provider failures. Every method returns <c>Result&lt;T&gt;</c>
/// with a <see cref="StripeError"/> carrying a code of one of:
/// <c>stripe.authentication_error</c>, <c>stripe.api_error</c>,
/// <c>stripe.rate_limit_error</c>, <c>stripe.invalid_request</c>,
/// <c>stripe.internal_error</c>, <c>stripe.unavailable</c>,
/// <c>stripe.signature_invalid</c>, <c>stripe.signature_missing</c>,
/// <c>stripe.timeout</c>.
/// </para>
///
/// <para>
/// <b>Cancellation</b>: every method accepts a <see cref="CancellationToken"/>
/// as the LAST parameter with a default value. If the caller's token fires,
/// the method MUST throw <see cref="OperationCanceledException"/> per the
/// standard .NET contract — cancellation is NEVER reported as a Result
/// failure.
/// </para>
///
/// <para>
/// <b>Stub fallback</b>: when <c>StripeOptions.ApiKey</c> is null or empty,
/// DI MUST register <c>StubStripeGateway</c> instead of the real
/// <c>StripeGateway</c>. The stub returns synthetic but predictable responses
/// so dev / CI without a Stripe key still works.
/// </para>
///
/// <para>
/// <b>Slice 6a.1 surface</b>: Customer + Webhook verification.
/// <b>Slice 6a.2 surface</b>: + Checkout + Portal + Subscription +
/// PaymentMethods + Invoices (5 new methods, total 7).
/// </para>
/// </summary>
public interface IStripeGateway
{
    /// <summary>
    /// Creates a Stripe Customer for the given user; idempotent — returns the
    /// existing mapping if <paramref name="userId"/> already has a row.
    /// </summary>
    Task<Result<StripeCustomerDto>> CreateOrGetCustomerAsync(
        Guid userId, string email, string? displayName, CancellationToken ct = default);

    /// <summary>
    /// Verifies a Stripe webhook signature and returns the parsed event.
    /// On invalid signature returns <c>Result.Failure(StripeError("stripe.signature_invalid", ...))</c>.
    /// On missing signature header returns
    /// <c>Result.Failure(StripeError("stripe.signature_missing", ...))</c>.
    /// </summary>
    Task<Result<StripeWebhookEvent>> VerifyWebhookAsync(
        string payload, string signatureHeader, CancellationToken ct = default);

    /// <summary>
    /// Creates a Stripe Checkout session for a subscription. Returns the URL
    /// the FE should redirect the user to. Stripe drives payment + the
    /// subsequent <c>customer.subscription.created</c> webhook.
    /// </summary>
    Task<Result<StripeCheckoutSessionDto>> CreateCheckoutSessionAsync(
        Guid userId, string priceId, string successUrl, string cancelUrl,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a Stripe Customer Portal session for the user. Returns the
    /// URL the FE should redirect the user to. Stripe hosts the actual
    /// self-service UI (cancel subscription, update card, view invoices).
    /// </summary>
    Task<Result<StripePortalSessionDto>> CreatePortalSessionAsync(
        Guid userId, string returnUrl, CancellationToken ct = default);

    /// <summary>
    /// Reads subscription state from Stripe (single source of truth for
    /// current state). Used by the webhook handler to apply updates and by
    /// the billing portal read API (slice 6b.1).
    /// </summary>
    Task<Result<StripeSubscriptionDto>> GetSubscriptionAsync(
        string stripeSubscriptionId, CancellationToken ct = default);

    /// <summary>
    /// Lists payment methods for a Stripe Customer. Newest-first; the
    /// <c>is_default</c> flag on the DTO is the source of truth for which
    /// card is charged.
    /// </summary>
    Task<Result<IReadOnlyList<StripePaymentMethodDto>>> GetPaymentMethodsAsync(
        string stripeCustomerId, CancellationToken ct = default);

    /// <summary>
    /// Lists invoices for a Stripe Customer (newest first, capped at 100 by
    /// Stripe's default page size).
    /// </summary>
    Task<Result<IReadOnlyList<StripeInvoiceDto>>> GetInvoicesAsync(
        string stripeCustomerId, CancellationToken ct = default);
}
