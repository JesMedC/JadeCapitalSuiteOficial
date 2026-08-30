namespace JadeCapital.Shared.Kernel.Stripe;

/// <summary>
/// Parsed Stripe webhook event DTO (Wave 6, slice 6a.1).
/// <para>
/// Wire shape (the raw payload is preserved verbatim — Stripe sends
/// arbitrary shapes and we MUST NOT re-serialize, only re-parse when needed):
/// <code>
/// {
///   "event_id": "evt_123",
///   "type": "customer.subscription.created",
///   "payload_json": "{ \"id\": \"evt_123\", \"type\": \"customer.subscription.created\", ... }",
///   "occurred_at": "2026-08-19T14:32:00Z"
/// }
/// </code>
/// </para>
/// <para>
/// <b>Idempotency</b>: <see cref="EventId"/> is the Stripe <c>evt_...</c> id —
/// unique across all Stripe events ever. The webhook handler persists this
/// in <c>billing.stripe_webhook_events</c> (slice 6a.2) and uses it to
/// dedupe re-deliveries.
/// </para>
/// <para>
/// <b>Type</b> is a free-form string on purpose — Stripe adds new event types
/// over time. The handler dispatches on <c>type</c> in
/// <c>HandleWebhookHandler</c>.
/// </para>
/// </summary>
public sealed record StripeWebhookEvent(
    string EventId,
    string Type,
    string PayloadJson,
    DateTimeOffset OccurredAt);
