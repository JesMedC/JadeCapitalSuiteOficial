using JadeCapital.Billing.Application.Features.Subscriptions;
using JadeCapital.Billing.Domain.Stripe;
using JadeCapital.Billing.Domain.Subscriptions;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using JadeCapital.Shared.Kernel.Time;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;
// DTO alias: the parsed event from the gateway is the Shared.Kernel DTO
// record. We persist the Billing.Domain aggregate. Without the alias, the
// two `StripeWebhookEvent` types collide on every reference.
using StripeWebhookEventDto = JadeCapital.Shared.Kernel.Stripe.StripeWebhookEvent;
using DomainStripeWebhookEvent = JadeCapital.Billing.Domain.Stripe.StripeWebhookEvent;

namespace JadeCapital.Billing.Application.Stripe;

/// <summary>
/// Command: handle a verified Stripe webhook payload (Wave 6, slices 6a.1 + 6a.2).
///
/// <para>
/// <b>Slice 6a.1 scope</b>: signature verify + log only.
/// </para>
/// <para>
/// <b>Slice 6a.2 scope</b>:
/// <list type="number">
///   <item>Idempotency check via <c>billing.stripe_webhook_events</c>.</item>
///   <item>Append-log the parsed event to <c>billing.stripe_webhook_events</c>.</item>
///   <item>Dispatch on <c>event.type</c> for <c>customer.subscription.*</c>.</item>
///   <item>Optimistic-concurrency retry (jitter, up to 3 attempts) on the
///   EF Core <c>DbUpdateConcurrencyException</c>.</item>
/// </list>
/// </para>
/// </summary>
public sealed record HandleWebhookCommand(
    string PayloadJson,
    string SignatureHeader) : IRequest<Result<WebhookOutcomeDto>>;

/// <summary>
/// Outcome DTO returned by <see cref="HandleWebhookHandler"/>. Shape matches
/// the spec ("processed" or "duplicate" + event id).
/// </summary>
public sealed record WebhookOutcomeDto(string Outcome, string EventId);

/// <summary>
/// Handler for <see cref="HandleWebhookCommand"/>.
///
/// <para>
/// <b>Flow</b> (slice 6a.2):
/// <list type="number">
///   <item>Verify the signature (catches all signature errors — returns them as Result.Failure).</item>
///   <item>Look up the event by <c>event_id</c>. If a row exists with
///   <c>ProcessedAt != null</c>, return <c>duplicate</c>.</item>
///   <item>Otherwise, append a new <see cref="DomainStripeWebhookEvent"/> row (or
///   update the existing pending row).</item>
///   <item>Dispatch on <c>event.type</c>:
///   <list type="bullet">
///     <item><c>customer.subscription.created</c> /
///     <c>customer.subscription.updated</c> → look up local subscription by
///     Stripe id, apply <see cref="SubscriptionWebhookSync.ApplyToSubscription"/>.</item>
///     <item><c>customer.subscription.deleted</c> → look up local subscription
///     and call <see cref="Subscription.Cancel"/>.</item>
///     <item>Anything else → mark processed, no mutation.</item>
///   </list></item>
///   <item>Optimistic-concurrency retry: if SaveChanges throws the EF Core
///   <c>DbUpdateConcurrencyException</c> (detected via type-name reflection
///   to keep <c>Billing.Application</c> free of an EF Core reference),
///   refetch the subscription + retry up to 3 times with 50-200ms jitter.
///   After 3 failures, mark the webhook as Failed.</item>
///   <item>On dispatch success, mark the webhook row as Processed.</item>
/// </list>
/// </para>
/// </summary>
public sealed class HandleWebhookHandler
    : IRequestHandler<HandleWebhookCommand, Result<WebhookOutcomeDto>>
{
    private readonly IStripeGateway _gateway;
    private readonly IStripeWebhookEventRepository _webhookRepo;
    private readonly ISubscriptionAdminRepository _subRepo;
    private readonly ISubscriptionAdminUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<HandleWebhookHandler> _logger;

    private const int MaxOptimisticConcurrencyRetries = 3;
    private const string StripeWebhookActor = "stripe-webhook";
    private const string EfCoreConcurrencyExceptionTypeName =
        "Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException";

    public HandleWebhookHandler(
        IStripeGateway gateway,
        IStripeWebhookEventRepository webhookRepo,
        ISubscriptionAdminRepository subRepo,
        ISubscriptionAdminUnitOfWork uow,
        IClock clock,
        ILogger<HandleWebhookHandler>? logger = null)
    {
        _gateway = gateway;
        _webhookRepo = webhookRepo;
        _subRepo = subRepo;
        _uow = uow;
        _clock = clock;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<HandleWebhookHandler>.Instance;
    }

    public async Task<Result<WebhookOutcomeDto>> Handle(
        HandleWebhookCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.PayloadJson))
            return Result.Failure<WebhookOutcomeDto>(
                Error.Validation("validation.payload_required", "Payload is required."));

        var verifyResult = await _gateway.VerifyWebhookAsync(
            cmd.PayloadJson, cmd.SignatureHeader, ct);

        if (verifyResult.IsFailure)
        {
            // Signature failures MUST NOT log the payload (PII leak risk).
            // Endpoint layer is responsible for the actual Serilog entry.
            return Result.Failure<WebhookOutcomeDto>(verifyResult.Error);
        }

        var evt = verifyResult.Value;

        // 2. Idempotency check.
        var existing = await _webhookRepo.FindByEventIdAsync(evt.EventId, ct);
        if (existing?.ProcessedAt is not null)
        {
            // Already processed. Return duplicate, no mutation.
            return Result.Success(new WebhookOutcomeDto(
                Outcome: "duplicate",
                EventId: evt.EventId));
        }

        // 3. Parse Stripe subscription id from the payload (best-effort;
        //    for non-subscription events this returns null and we log-only).
        var stripeSubId = TryExtractStripeSubscriptionId(evt.PayloadJson);

        // 4. Append-log (create-or-update the row).
        var rowResult = existing is null
            ? BuildWebhookRow(evt)
            : Result.Success(existing);
        if (rowResult.IsFailure)
        {
            _logger.LogWarning(
                "Stripe webhook event row construction failed for {EventId}: {Code}",
                evt.EventId, rowResult.Error.Code);
            return Result.Failure<WebhookOutcomeDto>(rowResult.Error);
        }

        var row = rowResult.Value;
        if (existing is null)
        {
            await _webhookRepo.AddAsync(row, ct);
        }

        // 5. Dispatch.
        if (stripeSubId is null || !IsSubscriptionEvent(evt.Type))
        {
            // Unknown / unsupported event — log + mark processed.
            row.MarkProcessed(_clock);
            await SafeSaveAsync(ct);
            return Result.Success(new WebhookOutcomeDto(
                Outcome: "processed",
                EventId: evt.EventId));
        }

        // 6. Apply subscription change with optimistic-concurrency retry.
        var dispatchResult = await DispatchSubscriptionEventAsync(
            row, stripeSubId, evt, ct);

        return dispatchResult.IsSuccess
            ? Result.Success(new WebhookOutcomeDto(
                Outcome: "processed",
                EventId: evt.EventId))
            : Result.Failure<WebhookOutcomeDto>(dispatchResult.Error);
    }

    /// <summary>
    /// Applies the subscription event to the local subscription with
    /// optimistic-concurrency retry. Returns Success on a clean dispatch
    /// (processed or failed-but-acknowledged), or Failure when the local
    /// state cannot be reasoned about (e.g. transient DB blip).
    /// </summary>
    private async Task<Result> DispatchSubscriptionEventAsync(
        DomainStripeWebhookEvent row,
        string stripeSubId,
        StripeWebhookEventDto evt,
        CancellationToken ct)
    {
        var subscription = await _subRepo.FindByStripeSubscriptionIdAsync(stripeSubId, ct);
        if (subscription is null)
        {
            // Stripe fired before the local subscription existed. The handler
            // marks the webhook as processed (so re-delivery is a duplicate)
            // and returns. Admin must re-process via out-of-band job in Wave 7.
            _logger.LogWarning(
                "Stripe webhook {EventId} references subscription {SubId} with no local mapping; marking processed",
                evt.EventId, stripeSubId);
            row.MarkProcessed(_clock);
            await SafeSaveAsync(ct);
            return Result.Success();
        }

        // Retry loop: read, mutate, save; on concurrency conflict,
        // refetch and retry with jitter.
        for (var attempt = 0; attempt < MaxOptimisticConcurrencyRetries; attempt++)
        {
            try
            {
                ApplySubscriptionMutation(subscription, evt);
                PersistSubscriptionChanges(subscription, row);

                await _uow.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Stripe webhook {EventId} dispatched on attempt {Attempt} (sub={SubId})",
                    evt.EventId, attempt + 1, stripeSubId);
                return Result.Success();
            }
            catch (Exception ex) when (IsEfCoreConcurrencyException(ex))
            {
                // Stale version — refetch the aggregate and retry.
                _logger.LogWarning(
                    "Optimistic-concurrency conflict on Stripe webhook {EventId} (attempt {Attempt}); refetching",
                    evt.EventId, attempt + 1);

                subscription = await _subRepo.FindByStripeSubscriptionIdAsync(stripeSubId, ct);
                if (subscription is null)
                {
                    row.MarkFailed("version_conflict_subscription_vanished", _clock);
                    await SafeSaveAsync(ct);
                    return Result.Success();
                }

                // Jitter: 50-200ms backoff.
                var jitterMs = Random.Shared.Next(50, 200);
                await Task.Delay(TimeSpan.FromMilliseconds(jitterMs), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Transient failure (DB blip, etc.). Mark webhook as Failed
                // and return a non-translated Failure so the caller logs.
                _logger.LogError(ex,
                    "Stripe webhook {EventId} dispatch failed on attempt {Attempt}",
                    evt.EventId, attempt + 1);

                row.MarkFailed(ex.Message, _clock);
                await SafeSaveAsync(ct);

                return Result.Failure(
                    Error.Validation(
                        "stripe.webhook_dispatch_failed",
                        "Webhook dispatch failed; webhook row marked for ops review."));
            }
        }

        // All retries exhausted. Mark webhook as Failed with a version_conflict marker.
        _logger.LogError(
            "Stripe webhook {EventId} exhausted optimistic-concurrency retries ({Max}); marking Failed",
            evt.EventId, MaxOptimisticConcurrencyRetries);

        row.MarkFailed("version_conflict_after_retries", _clock);
        await SafeSaveAsync(ct);

        return Result.Success();  // acknowledge to Stripe (re-delivery safe)
    }

    private void ApplySubscriptionMutation(Subscription subscription, StripeWebhookEventDto evt)
    {
        if (evt.Type == "customer.subscription.deleted")
        {
            subscription.Cancel(
                reason: "stripe-subscription-deleted",
                observedVersion: subscription.Version,
                actor: StripeWebhookActor,
                utcNow: _clock.UtcNow);
            return;
        }

        // created + updated: translate Stripe status → SubscriptionStatus via
        // the SyncFromStripe path. The Stripe sub id is the one we resolved
        // from the payload earlier (or already on the subscription for
        // "updated" events).
        var stripeSubId = subscription.StripeSubscriptionId
            ?? TryExtractStripeSubscriptionId(evt.PayloadJson)
            ?? throw new InvalidOperationException(
                "ApplySubscriptionMutation could not resolve the Stripe subscription id.");

        var stripeSub = new StripeSubscriptionDto(
            StripeSubscriptionId: stripeSubId,
            Status: TryExtractStripeStatus(evt.PayloadJson) ?? "active",
            PlanCode: TryExtractStripePlanCode(evt.PayloadJson) ?? string.Empty,
            CurrentPeriodEnd: TryExtractStripeCurrentPeriodEnd(evt.PayloadJson)
                ?? _clock.UtcNow.AddDays(30),
            CancelAtPeriodEnd: TryExtractStripeCancelAtPeriodEnd(evt.PayloadJson));

        var result = SubscriptionWebhookSync.ApplyToSubscription(
            subscription, stripeSub, _clock);

        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"SubscriptionWebhookSync.ApplyToSubscription failed: {result.Error.Code} {result.Error.Message}");
        }
    }

    private void PersistSubscriptionChanges(
        Subscription subscription,
        DomainStripeWebhookEvent row)
    {
        // The aggregate appends a history entry on a successful mutation;
        // explicit-stage it via the UoW to avoid the EF collection-tracking
        // bug where private-nav additions get detected as Modified.
        var lastEntry = subscription.LastHistoryEntry;
        if (lastEntry is not null)
        {
            _uow.AddHistoryEntry(lastEntry);
        }

        row.MarkProcessed(_clock);
    }

    /// <summary>
    /// Builds a new <see cref="DomainStripeWebhookEvent"/> aggregate from the
    /// parsed payload. The signature header is null when the test fixture
    /// doesn't supply one (the stub gateway ignores it).
    /// </summary>
    private Result<DomainStripeWebhookEvent> BuildWebhookRow(StripeWebhookEventDto parsed)
    {
        return DomainStripeWebhookEvent.Record(
            id: Guid.NewGuid(),
            eventId: parsed.EventId,
            eventType: parsed.Type,
            payloadJson: parsed.PayloadJson,
            signatureHeader: null,  // raw header NOT retained in DB to avoid PII storage
            receivedAt: _clock.UtcNow,
            clock: _clock);
    }

    /// <summary>
    /// Swallows DB save failures when marking the webhook row (we don't
    /// want a secondary failure to mask the real dispatch error).
    /// </summary>
    private async Task SafeSaveAsync(CancellationToken ct)
    {
        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to persist webhook event row state");
        }
    }

    /// <summary>
    /// Reflection-based detection of the EF Core concurrency exception. We
    /// deliberately avoid referencing <c>Microsoft.EntityFrameworkCore</c> from
    /// <c>Billing.Application</c> (the Application layer is infrastructure-
    /// agnostic). The full type name is stable across EF Core versions.
    /// </summary>
    private static bool IsEfCoreConcurrencyException(Exception ex)
        => ex.GetType().FullName == EfCoreConcurrencyExceptionTypeName;

    private static bool IsSubscriptionEvent(string type) =>
        type is "customer.subscription.created"
              or "customer.subscription.updated"
              or "customer.subscription.deleted";

    /// <summary>
    /// Best-effort extraction of <c>data.object.id</c> from the raw Stripe
    /// payload. Returns null for non-subscription events. The webhook
    /// dispatcher treats a null return as "log + processed".
    /// </summary>
    private static string? TryExtractStripeSubscriptionId(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("object", out var obj) &&
                obj.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.String)
            {
                return id.GetString();
            }
        }
        catch (JsonException)
        {
            // Malformed payload — treat as non-subscription event.
        }
        return null;
    }

    private static string? TryExtractStripeStatus(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("object", out var obj) &&
                obj.TryGetProperty("status", out var status) &&
                status.ValueKind == JsonValueKind.String)
            {
                return status.GetString();
            }
        }
        catch (JsonException) { /* swallow */ }
        return null;
    }

    private static string? TryExtractStripePlanCode(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("object", out var obj) &&
                obj.TryGetProperty("items", out var items) &&
                items.TryGetProperty("data", out var itemsArr) &&
                itemsArr.ValueKind == JsonValueKind.Array &&
                itemsArr.GetArrayLength() > 0)
            {
                var first = itemsArr[0];
                if (first.TryGetProperty("price", out var price) &&
                    price.TryGetProperty("id", out var id) &&
                    id.ValueKind == JsonValueKind.String)
                {
                    return id.GetString();
                }
            }
        }
        catch (JsonException) { /* swallow */ }
        return null;
    }

    private static DateTimeOffset? TryExtractStripeCurrentPeriodEnd(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("object", out var obj) &&
                obj.TryGetProperty("current_period_end", out var cpe) &&
                cpe.ValueKind == JsonValueKind.Number &&
                cpe.TryGetInt64(out var unix))
            {
                return DateTimeOffset.FromUnixTimeSeconds(unix);
            }
        }
        catch (JsonException) { /* swallow */ }
        return null;
    }

    private static bool TryExtractStripeCancelAtPeriodEnd(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("object", out var obj) &&
                obj.TryGetProperty("cancel_at_period_end", out var cape) &&
                cape.ValueKind == JsonValueKind.True)
            {
                return true;
            }
        }
        catch (JsonException) { /* swallow */ }
        return false;
    }
}