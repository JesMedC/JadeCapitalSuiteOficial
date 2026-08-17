using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using MediatR;

namespace JadeCapital.Billing.Application.Stripe;

/// <summary>
/// Command: handle a verified Stripe webhook payload (Wave 6, slice 6a.1).
///
/// <para>
/// <b>Slice 6a.1 scope</b>: signature verify + log only. Idempotency check,
/// append-log persistence, and subscription dispatch land in slice 6a.2
/// (which adds migration 0023 for <c>billing.stripe_webhook_events</c>).
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
/// <b>Why this is minimal in 6a.1</b>: the spec says "signature verify + log"
/// only — no business logic yet. The 6a.2 slice adds:
/// <list type="bullet">
///   <item>Idempotency check via <c>billing.stripe_webhook_events</c> (event_id unique)</item>
///   <item>Append-log the parsed event to the same table</item>
///   <item>Dispatch on <c>event.type</c> for <c>customer.subscription.*</c></item>
/// </list>
/// </para>
/// </summary>
public sealed class HandleWebhookHandler
    : IRequestHandler<HandleWebhookCommand, Result<WebhookOutcomeDto>>
{
    private readonly IStripeGateway _gateway;

    public HandleWebhookHandler(IStripeGateway gateway)
    {
        _gateway = gateway;
    }

    public async Task<Result<WebhookOutcomeDto>> Handle(HandleWebhookCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.PayloadJson))
            return Result.Failure<WebhookOutcomeDto>(
                Error.Validation("validation.payload_required", "Payload is required."));

        var verifyResult = await _gateway.VerifyWebhookAsync(cmd.PayloadJson, cmd.SignatureHeader, ct);

        if (verifyResult.IsFailure)
        {
            // Signature failures MUST NOT log the payload (PII leak risk).
            // Endpoint layer is responsible for the actual Serilog entry.
            return Result.Failure<WebhookOutcomeDto>(verifyResult.Error);
        }

        return Result.Success(new WebhookOutcomeDto(
            Outcome: "processed",
            EventId: verifyResult.Value.EventId));
    }
}
