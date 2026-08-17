using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Shared.Kernel.Stripe;

/// <summary>
/// Stripe gateway abstraction (Wave 6, slice 6a.1 — partial interface).
///
/// <para>
/// <b>Why Shared.Kernel</b>: mirrors the <c>IQuoteProvider</c> (Wave 4b) and
/// <c>IAIProvider</c> (Wave 5b) precedent. The wire shapes
/// (<see cref="StripeCustomerDto"/>, <see cref="StripeWebhookEvent"/>) are
/// cross-module stable — Billing owns the default impl (<c>StripeGateway</c>
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
/// <b>Slice 6a.1 surface</b>: only Customer + Webhook verification. Checkout,
/// Portal, Subscription, PaymentMethod, Invoice reads land in slice 6a.2.
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
}
