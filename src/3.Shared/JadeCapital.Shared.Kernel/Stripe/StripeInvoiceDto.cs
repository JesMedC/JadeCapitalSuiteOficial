namespace JadeCapital.Shared.Kernel.Stripe;

/// <summary>
/// Stripe Invoice DTO (Wave 6, slice 6a.2).
/// <para>
/// Wire shape:
/// <code>
/// {
///   "id": "in_123",
///   "number": "INV-001",
///   "amount_cents": 1999,
///   "currency": "usd",
///   "issued_at": "2026-08-19T00:00:00Z",
///   "paid_at": "2026-08-19T01:00:00Z",
///   "status": "paid",
///   "pdf_url": "https://stripe.com/in.pdf"
/// }
/// </code>
/// </para>
/// <para>
/// <b>AmountCents</b> uses <c>long</c> (not <c>decimal</c>) to mirror
/// Stripe's own API — Stripe stores money as integer cents. Negative
/// values are not used by Stripe but the type allows them (defensive).
/// </para>
/// <para>
/// <b>PaidAt</b> is nullable for unpaid invoices (status <c>open</c>,
/// <c>draft</c>, etc.).
/// </para>
/// </summary>
public sealed record StripeInvoiceDto(
    string Id,
    string Number,
    long AmountCents,
    string Currency,
    DateTimeOffset IssuedAt,
    DateTimeOffset? PaidAt,
    string Status,
    string PdfUrl);
