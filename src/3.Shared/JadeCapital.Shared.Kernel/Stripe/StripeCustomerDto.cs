namespace JadeCapital.Shared.Kernel.Stripe;

/// <summary>
/// Stripe Customer DTO (Wave 6, slice 6a.1).
/// <para>
/// Wire shape:
/// <code>
/// {
///   "stripe_customer_id": "cus_123",
///   "email": "user@example.com",
///   "display_name": "John Doe",
///   "created_at": "2026-08-19T14:32:00Z"
/// }
/// </code>
/// </para>
/// </summary>
public sealed record StripeCustomerDto(
    string StripeCustomerId,
    string Email,
    string? DisplayName,
    DateTimeOffset CreatedAt);
