using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;

namespace JadeCapital.Billing.Infrastructure.Stripe;

/// <summary>
/// Stub Stripe gateway for dev / CI without a real API key (Wave 6, slice 6a.1).
///
/// <para>
/// Registered by DI when <see cref="StripeOptions.ApiKey"/> is null or empty.
/// Returns synthetic but PREDICTABLE responses so dev workflows still work:
/// <list type="bullet">
///   <item><c>CreateOrGetCustomerAsync</c> → <c>cus_stub_{userId:N}</c></item>
///   <item><c>VerifyWebhookAsync</c> → a synthetic parsed event</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why a stub, not a real client</b>: dev environments frequently do not
/// have a Stripe sandbox key. Forcing the dev to set one would block every
/// local run. The stub returns the same shape as the real gateway, so the
/// rest of the system never knows which one is active.
/// </para>
///
/// <para>
/// <b>Slice 6a.1 surface</b>: only Customer + Webhook verification. Checkout,
/// Portal, Subscription, PaymentMethod, Invoice reads land in slice 6a.2.
/// </para>
/// </summary>
public sealed class StubStripeGateway : IStripeGateway
{
    public Task<Result<StripeCustomerDto>> CreateOrGetCustomerAsync(
        Guid userId, string email, string? displayName, CancellationToken ct = default)
    {
        // Predictable id: cus_stub_{userId:N} where N is the hex format. Two
        // calls with the same userId return the same id (idempotent).
        var stubId = $"cus_stub_{userId:N}";
        var dto = new StripeCustomerDto(
            StripeCustomerId: stubId,
            Email: email,
            DisplayName: displayName,
            CreatedAt: DateTimeOffset.UtcNow);

        return Task.FromResult(Result.Success(dto));
    }

    public Task<Result<StripeWebhookEvent>> VerifyWebhookAsync(
        string payload, string signatureHeader, CancellationToken ct = default)
    {
        // The stub accepts ANY payload + signature. In real Stripe, signature
        // verification is mandatory. The stub is for dev only — webhook
        // testing must use the real gateway with a Stripe CLI fixture.
        var eventId = $"evt_stub_{Guid.NewGuid():N}";
        var dto = new StripeWebhookEvent(
            EventId: eventId,
            Type: "ping",
            PayloadJson: payload,
            OccurredAt: DateTimeOffset.UtcNow);

        return Task.FromResult(Result.Success(dto));
    }
}
