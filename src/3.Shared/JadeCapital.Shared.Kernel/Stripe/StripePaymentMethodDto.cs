namespace JadeCapital.Shared.Kernel.Stripe;

/// <summary>
/// Stripe Payment Method DTO (Wave 6, slice 6a.2).
/// <para>
/// Wire shape:
/// <code>
/// {
///   "id": "pm_123",
///   "brand": "visa",
///   "last4": "4242",
///   "expires_at": "2028-08-19T00:00:00Z",
///   "is_default": true
/// }
/// </code>
/// </para>
/// <para>
/// <b>Brand</b> is a free-form string (<c>visa</c>, <c>mastercard</c>,
/// <c>amex</c>, etc.) — Stripe adds new brands over time. The DTO does
/// not enforce a fixed enum.
/// </para>
/// <para>
/// <b>ExpiresAt</b> is nullable because not every Payment Method has an
/// expiry (e.g. SEPA debit, bank transfer methods).
/// </para>
/// </summary>
public sealed record StripePaymentMethodDto(
    string Id,
    string Brand,
    string Last4,
    DateTimeOffset? ExpiresAt,
    bool IsDefault);
