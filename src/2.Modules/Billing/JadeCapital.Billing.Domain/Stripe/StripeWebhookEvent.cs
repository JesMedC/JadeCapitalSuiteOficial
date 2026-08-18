using JadeCapital.Billing.Domain.Common;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Billing.Domain.Stripe;

/// <summary>
/// Aggregate root for an append-only record of a received Stripe webhook
/// event (Wave 6, slice 6a.2).
///
/// <para>
/// <b>Invariants</b>:
/// <list type="bullet">
///   <item><see cref="EventId"/> is non-empty (Stripe <c>evt_...</c> id)</item>
///   <item><see cref="EventType"/> is non-empty (free-form string — Stripe
///   adds new event types over time)</item>
///   <item><see cref="PayloadJson"/> is non-empty (raw body, preserved verbatim)</item>
///   <item><see cref="ReceivedAt"/> is the time the request was received</item>
///   <item><see cref="ProcessedAt"/> is set by <see cref="MarkProcessed"/>
///   on a successful dispatch</item>
///   <item><see cref="ProcessingError"/> is set by <see cref="MarkFailed"/>
///   on a failed dispatch</item>
///   <item>The aggregate is append-only: <see cref="EventId"/>,
///   <see cref="EventType"/>, <see cref="PayloadJson"/>, and
///   <see cref="ReceivedAt"/> are NOT publicly settable</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Idempotency</b>: the DB enforces a UNIQUE constraint on
/// <c>event_id</c>. Re-delivery of the same <c>evt_...</c> id returns the
/// existing row (no insert) — the handler's idempotency check is therefore
/// keyed on the event_id, not on a separate "delivery_id".
/// </para>
///
/// <para>
/// <b>Status</b>: derived from <see cref="ProcessedAt"/> +
/// <see cref="ProcessingError"/>. We do NOT add a dedicated status column —
/// the combination of those two fields is the source of truth, and the
/// status property is a convenience for callers + tests.
/// </para>
/// </summary>
public sealed class StripeWebhookEvent : AggregateRoot<Guid>
{
    public string EventId { get; private set; } = default!;
    public string EventType { get; private set; } = default!;
    public string PayloadJson { get; private set; } = default!;
    public string? SignatureHeader { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? ProcessingError { get; private set; }

    /// <summary>
    /// Derived status. Mirrors what a dedicated <c>status</c> column would
    /// expose: <see cref="StripeWebhookEventStatus.Pending"/> before
    /// processing, <see cref="StripeWebhookEventStatus.Processed"/> after a
    /// successful dispatch, <see cref="StripeWebhookEventStatus.Failed"/>
    /// after a failed dispatch.
    /// </summary>
    public StripeWebhookEventStatus Status
    {
        get
        {
            if (ProcessingError is not null) return StripeWebhookEventStatus.Failed;
            if (ProcessedAt is not null) return StripeWebhookEventStatus.Processed;
            return StripeWebhookEventStatus.Pending;
        }
    }

    // EF materialization.
    private StripeWebhookEvent() { }

    private StripeWebhookEvent(
        Guid id,
        string eventId,
        string eventType,
        string payloadJson,
        string? signatureHeader,
        DateTimeOffset receivedAt) : base(id)
    {
        EventId = eventId;
        EventType = eventType;
        PayloadJson = payloadJson;
        SignatureHeader = signatureHeader;
        ReceivedAt = receivedAt;
    }

    /// <summary>
    /// Factory. Validates the event id, type, and payload. Returns
    /// <c>Result.Success</c> with a new aggregate or
    /// <c>Result.Failure</c> with the relevant
    /// <see cref="StripeWebhookEventErrors"/>.
    /// </summary>
    public static Result<StripeWebhookEvent> Record(
        Guid id,
        string eventId,
        string eventType,
        string payloadJson,
        string? signatureHeader,
        DateTimeOffset receivedAt,
        IClock clock)
    {
        if (id == Guid.Empty)
            return Result.Failure<StripeWebhookEvent>(StripeWebhookEventErrors.IdRequired);
        if (string.IsNullOrWhiteSpace(eventId))
            return Result.Failure<StripeWebhookEvent>(StripeWebhookEventErrors.InvalidEventId);
        if (string.IsNullOrWhiteSpace(eventType))
            return Result.Failure<StripeWebhookEvent>(StripeWebhookEventErrors.InvalidEventType);
        if (string.IsNullOrWhiteSpace(payloadJson))
            return Result.Failure<StripeWebhookEvent>(StripeWebhookEventErrors.InvalidPayloadJson);

        return Result.Success(new StripeWebhookEvent(
            id, eventId.Trim(), eventType.Trim(), payloadJson,
            signatureHeader, receivedAt));
    }

    /// <summary>
    /// Marks the event as successfully processed. Sets
    /// <see cref="ProcessedAt"/> to the clock's UtcNow and clears any prior
    /// <see cref="ProcessingError"/>. Idempotent: calling on an already-
    /// processed event updates the timestamp to the latest clock.
    /// </summary>
    public void MarkProcessed(IClock clock)
    {
        ProcessedAt = clock.UtcNow;
        ProcessingError = null;
    }

    /// <summary>
    /// Marks the event as failed. Sets <see cref="ProcessingError"/> and
    /// leaves <see cref="ProcessedAt"/> null. The caller is responsible for
    /// persisting the entity after this call.
    /// </summary>
    public void MarkFailed(string error, IClock clock)
    {
        if (string.IsNullOrWhiteSpace(error))
            error = "Unknown error";
        // Truncate to fit the VARCHAR(2000) column in migration 0023.
        ProcessingError = error.Length > 2000 ? error.Substring(0, 2000) : error;
        // Note: ProcessedAt stays null when failed; Status will be Failed.
        _ = clock;  // Reserved for future clock-based logging.
    }
}

/// <summary>
/// Derived status for a <see cref="StripeWebhookEvent"/>. Mirrors what a
/// dedicated status column would expose. Lives in the same namespace so
/// callers that only need the status enum don't need a separate import.
/// </summary>
public enum StripeWebhookEventStatus
{
    /// <summary>Row created, no dispatch attempted yet (or in flight).</summary>
    Pending = 0,

    /// <summary>Dispatch completed successfully.</summary>
    Processed = 1,

    /// <summary>Dispatch failed; see <c>ProcessingError</c>.</summary>
    Failed = 2
}

/// <summary>
/// Error catalog for the <see cref="StripeWebhookEvent"/> aggregate (Wave 6,
/// slice 6a.2). Codes are namespaced under <c>stripe.webhook_event.</c> for
/// consistent routing. Co-located with the aggregate for read locality.
/// </summary>
public static class StripeWebhookEventErrors
{
    public static readonly Error IdRequired =
        Error.Validation("stripe.webhook_event.id_required", "Webhook event identifier is required.");

    public static readonly Error InvalidEventId =
        Error.Validation("stripe.webhook_event.invalid_event_id", "Stripe event id is required and must be non-empty.");

    public static readonly Error InvalidEventType =
        Error.Validation("stripe.webhook_event.invalid_event_type", "Stripe event type is required and must be non-empty.");

    public static readonly Error InvalidPayloadJson =
        Error.Validation("stripe.webhook_event.invalid_payload_json", "Webhook payload JSON is required and must be non-empty.");
}