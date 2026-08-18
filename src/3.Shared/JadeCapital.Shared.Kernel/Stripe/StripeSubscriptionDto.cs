namespace JadeCapital.Shared.Kernel.Stripe;

/// <summary>
/// Stripe Subscription DTO (Wave 6, slice 6a.2).
/// <para>
/// Wire shape:
/// <code>
/// {
///   "stripe_subscription_id": "sub_123",
///   "status": "active",
///   "plan_code": "pro",
///   "current_period_end": "2026-09-19T00:00:00Z",
///   "cancel_at_period_end": false
/// }
/// </code>
/// </para>
/// <para>
/// <b>Status</b> is a free-form string on purpose. Stripe's documented
/// values include <c>active</c>, <c>past_due</c>, <c>canceled</c>,
/// <c>trialing</c>, <c>unpaid</c>, <c>incomplete</c>,
/// <c>incomplete_expired</c>, <c>paused</c>. The mapping to our internal
/// <c>SubscriptionStatus</c> enum lives in
/// <c>SubscriptionWebhookSync</c> (Billing.Domain).
/// </para>
/// </summary>
public sealed record StripeSubscriptionDto(
    string StripeSubscriptionId,
    string Status,
    string PlanCode,
    DateTimeOffset CurrentPeriodEnd,
    bool CancelAtPeriodEnd);
