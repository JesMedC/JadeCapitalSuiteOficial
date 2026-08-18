namespace JadeCapital.Billing.Contracts.Portal;

/// <summary>
/// API-surface DTO for the billing portal subscription view (Wave 6, slice 6b.1).
///
/// <para>
/// This is the FE-facing wire shape returned by
/// <c>GET /api/billing/portal/subscription</c>. It is a strict subset of the
/// upstream <c>JadeCapital.Shared.Kernel.Stripe.StripeSubscriptionDto</c>:
/// only the fields the FE renders (status, plan, period, cancel flag) are
/// exposed. Internal-only fields (raw status enums, version tokens, Stripe
/// metadata) stay in the upstream wire DTO.
/// </para>
///
/// <para>
/// <b>JSON contract</b>: serialized via <c>System.Text.Json</c> with
/// <c>JsonNamingPolicy.SnakeCaseLower</c> so the FE receives
/// <c>stripe_subscription_id</c>, <c>status</c>, <c>plan_code</c>,
/// <c>current_period_end</c>, <c>cancel_at_period_end</c>.
/// </para>
/// </summary>
public sealed record BillingPortalSubscriptionDto(
    string StripeSubscriptionId,
    string Status,
    string PlanCode,
    DateTimeOffset CurrentPeriodEnd,
    bool CancelAtPeriodEnd);

/// <summary>
/// API-surface DTO for a payment method on the billing portal (Wave 6,
/// slice 6b.1).
///
/// <para>
/// Returned as a list by <c>GET /api/billing/portal/payment-methods</c>. The
/// Stripe id is opaque to the FE — only the human-readable fields
/// (brand + last 4 + expiry + is_default) are exposed.
/// </para>
/// </summary>
public sealed record BillingPortalPaymentMethodDto(
    string Id,
    string Brand,
    string Last4,
    DateTimeOffset? ExpiresAt,
    bool IsDefault);

/// <summary>
/// API-surface DTO for an invoice on the billing portal (Wave 6, slice 6b.1).
///
/// <para>
/// Returned as a list by <c>GET /api/billing/portal/invoices</c>. The
/// <c>AmountCents</c> uses <c>long</c> to mirror Stripe's own shape (integer
/// cents, never decimal). <c>PaidAt</c> is nullable for unpaid invoices
/// (status <c>open</c>, <c>draft</c>, etc.).
/// </para>
/// </summary>
public sealed record BillingPortalInvoiceDto(
    string Id,
    string Number,
    long AmountCents,
    string Currency,
    DateTimeOffset IssuedAt,
    DateTimeOffset? PaidAt,
    string Status,
    string PdfUrl);
