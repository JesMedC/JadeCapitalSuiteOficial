namespace JadeCapital.Shared.Kernel.Stripe;

/// <summary>
/// Stripe Checkout Session DTO (Wave 6, slice 6a.2).
/// <para>
/// Wire shape:
/// <code>
/// {
///   "session_id": "cs_test_abc123",
///   "url": "https://checkout.stripe.com/c/pay/cs_test_abc123",
///   "expires_at": "2026-08-19T15:32:00Z"
/// }
/// </code>
/// </para>
/// <para>
/// Returned by <see cref="IStripeGateway.CreateCheckoutSessionAsync"/>. The
/// FE redirects the user to <see cref="Url"/>; Stripe then drives the
/// subscription creation and fires the <c>customer.subscription.created</c>
/// webhook when payment completes.
/// </para>
/// </summary>
public sealed record StripeCheckoutSessionDto(
    string SessionId,
    string Url,
    DateTimeOffset ExpiresAt);
