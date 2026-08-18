namespace JadeCapital.Shared.Kernel.Stripe;

/// <summary>
/// Stripe Customer Portal Session DTO (Wave 6, slice 6a.2).
/// <para>
/// Wire shape:
/// <code>
/// {
///   "session_id": "ps_test_abc123",
///   "url": "https://billing.stripe.com/p/session/ps_test_abc123",
///   "expires_at": "2026-08-19T15:32:00Z"
/// }
/// </code>
/// </para>
/// <para>
/// Returned by <see cref="IStripeGateway.CreatePortalSessionAsync"/>. The
/// FE redirects the user to <see cref="Url"/>; Stripe hosts the actual
/// self-service UI (cancel subscription, update card, view invoices).
/// </para>
/// </summary>
public sealed record StripePortalSessionDto(
    string SessionId,
    string Url,
    DateTimeOffset ExpiresAt);
