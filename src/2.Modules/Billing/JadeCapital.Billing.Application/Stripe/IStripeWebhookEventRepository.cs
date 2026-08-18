using JadeCapital.Billing.Domain.Stripe;

namespace JadeCapital.Billing.Application.Stripe;

/// <summary>
/// Repository abstraction for <see cref="StripeWebhookEvent"/> (Wave 6,
/// slice 6a.2). Append-only log of received Stripe webhook events.
///
/// <para>
/// Lives in Billing.Application; the EF implementation lives in
/// <c>JadeCapital.Billing.Infrastructure.Persistence.StripeWebhookEventRepository</c>.
/// </para>
/// </summary>
public interface IStripeWebhookEventRepository
{
    /// <summary>
    /// Looks up an event by Stripe's <c>event_id</c> (e.g. <c>evt_...</c>).
    /// Returns null if no row exists yet. Used by the handler's
    /// idempotency check before inserting.
    /// </summary>
    Task<StripeWebhookEvent?> FindByEventIdAsync(string eventId, CancellationToken ct = default);

    /// <summary>
    /// Adds a new event to the DbContext. Caller flushes via SaveChanges.
    /// </summary>
    Task AddAsync(StripeWebhookEvent webhookEvent, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing event in the DbContext (e.g. to mark
    /// <c>ProcessedAt</c> or <c>ProcessingError</c> after dispatch).
    /// </summary>
    Task UpdateAsync(StripeWebhookEvent webhookEvent, CancellationToken ct = default);
}
