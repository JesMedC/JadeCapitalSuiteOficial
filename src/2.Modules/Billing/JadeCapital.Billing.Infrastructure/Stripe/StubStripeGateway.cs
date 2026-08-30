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

    public Task<Result<StripeCheckoutSessionDto>> CreateCheckoutSessionAsync(
        Guid userId, string priceId, string successUrl, string cancelUrl, CancellationToken ct = default)
    {
        // Predictable synthetic session id; the URL points to a stub host so
        // devs know it's not a real Stripe URL. expiresAt is "1 hour from now"
        // matching Stripe's real Checkout session window.
        var sessionId = $"cs_stub_{Guid.NewGuid():N}";
        var dto = new StripeCheckoutSessionDto(
            SessionId: sessionId,
            Url: $"https://stub.example.com/checkout/{sessionId}",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1));

        return Task.FromResult(Result.Success(dto));
    }

    public Task<Result<StripePortalSessionDto>> CreatePortalSessionAsync(
        Guid userId, string returnUrl, CancellationToken ct = default)
    {
        var sessionId = $"ps_stub_{Guid.NewGuid():N}";
        var dto = new StripePortalSessionDto(
            SessionId: sessionId,
            Url: $"https://stub.example.com/portal/{sessionId}",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1));

        return Task.FromResult(Result.Success(dto));
    }

    public Task<Result<StripeSubscriptionDto>> GetSubscriptionAsync(
        string stripeSubscriptionId, CancellationToken ct = default)
    {
        // Return a stable "active" synthetic subscription for any input.
        var dto = new StripeSubscriptionDto(
            StripeSubscriptionId: stripeSubscriptionId,
            Status: "active",
            PlanCode: "pro",
            CurrentPeriodEnd: DateTimeOffset.UtcNow.AddDays(30),
            CancelAtPeriodEnd: false);

        return Task.FromResult(Result.Success(dto));
    }

    public Task<Result<IReadOnlyList<StripePaymentMethodDto>>> GetPaymentMethodsAsync(
        string stripeCustomerId, CancellationToken ct = default)
    {
        // Return one predictable stub card so the FE renders something.
        var stubCard = new StripePaymentMethodDto(
            Id: $"pm_stub_{Guid.NewGuid():N}",
            Brand: "visa",
            Last4: "4242",
            ExpiresAt: DateTimeOffset.UtcNow.AddYears(3),
            IsDefault: true);

        IReadOnlyList<StripePaymentMethodDto> dtos = new[] { stubCard };
        return Task.FromResult(Result.Success(dtos));
    }

    public Task<Result<IReadOnlyList<StripeInvoiceDto>>> GetInvoicesAsync(
        string stripeCustomerId, CancellationToken ct = default)
    {
        // Three stub invoices, newest-first. Tests that need a specific count
        // can substitute a custom stub.
        var now = DateTimeOffset.UtcNow;
        var dtos = new List<StripeInvoiceDto>
        {
            new("in_stub_1", "STUB-001", 1999, "usd", now.AddDays(-1),  now.AddDays(-1).AddHours(1),  "paid",   "https://stub.example.com/inv/1.pdf"),
            new("in_stub_2", "STUB-002", 1999, "usd", now.AddDays(-31), now.AddDays(-31).AddHours(1), "paid",   "https://stub.example.com/inv/2.pdf"),
            new("in_stub_3", "STUB-003", 1999, "usd", now.AddDays(-61), now.AddDays(-61).AddHours(1), "paid",   "https://stub.example.com/inv/3.pdf"),
        };
        IReadOnlyList<StripeInvoiceDto> readOnly = dtos;
        return Task.FromResult(Result.Success(readOnly));
    }
}
