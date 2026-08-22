using JadeCapital.Billing.Infrastructure.Stripe;
using JadeCapital.Shared.Kernel.Stripe;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stripe;

namespace JadeCapital.Billing.UnitTests.Stripe;

/// <summary>
/// Tests for <see cref="StubStripeGateway"/> (Wave 6, slice 6a.1).
/// The stub returns synthetic but PREDICTABLE responses — dev / CI uses this
/// when no Stripe key is configured.
/// </summary>
public class StubStripeGatewayTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly StubStripeGateway Sut = new();

    [Fact]
    public async Task CreateOrGet_Returns_Predictable_Stub_Id()
    {
        var result = await Sut.CreateOrGetCustomerAsync(UserId, "x@y.com", "John", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.StripeCustomerId.Should().Be($"cus_stub_{UserId:N}");
        result.Value.Email.Should().Be("x@y.com");
        result.Value.DisplayName.Should().Be("John");
    }

    [Fact]
    public async Task CreateOrGet_Is_Idempotent_Across_Calls()
    {
        var first = await Sut.CreateOrGetCustomerAsync(UserId, "x@y.com", null, CancellationToken.None);
        var second = await Sut.CreateOrGetCustomerAsync(UserId, "different@y.com", "diff", CancellationToken.None);

        first.Value.StripeCustomerId.Should().Be(second.Value.StripeCustomerId,
            "stub returns the same id regardless of email (idempotent)");
    }

    [Fact]
    public async Task VerifyWebhook_Returns_Synthetic_Event_For_Any_Payload()
    {
        var result = await Sut.VerifyWebhookAsync(
            "{\"foo\":\"bar\"}", "t=123,v1=abc", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.EventId.Should().StartWith("evt_stub_");
        result.Value.Type.Should().Be("ping");
        result.Value.PayloadJson.Should().Be("{\"foo\":\"bar\"}");
    }

    [Fact]
    public async Task Checkout_Returns_Named_Id_Url_And_Future_Expiry()
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(59);

        var result = await Sut.CreateCheckoutSessionAsync(
            UserId, "price_pro", "https://app/success", "https://app/cancel");

        result.Value.SessionId.Should().StartWith("cs_stub_");
        result.Value.Url.Should().Be($"https://stub.example.com/checkout/{result.Value.SessionId}");
        result.Value.ExpiresAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task Portal_Returns_Named_Id_Url_And_Future_Expiry()
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(59);

        var result = await Sut.CreatePortalSessionAsync(UserId, "https://app/billing");

        result.Value.SessionId.Should().StartWith("ps_stub_");
        result.Value.Url.Should().Be($"https://stub.example.com/portal/{result.Value.SessionId}");
        result.Value.ExpiresAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task Subscription_Returns_Active_Pro_Shape()
    {
        var result = await Sut.GetSubscriptionAsync("sub_stub_contract");

        result.Value.StripeSubscriptionId.Should().Be("sub_stub_contract");
        result.Value.Status.Should().Be("active");
        result.Value.PlanCode.Should().Be("pro");
        result.Value.CancelAtPeriodEnd.Should().BeFalse();
    }

    [Fact]
    public async Task PaymentMethods_Return_Default_Visa_4242_Card()
    {
        var result = await Sut.GetPaymentMethodsAsync("cus_stub_contract");

        result.Value.Should().ContainSingle();
        result.Value[0].Id.Should().StartWith("pm_stub_");
        result.Value[0].Brand.Should().Be("visa");
        result.Value[0].Last4.Should().Be("4242");
        result.Value[0].IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Invoices_Return_Three_Newest_First_Paid_Usd_Shapes()
    {
        var result = await Sut.GetInvoicesAsync("cus_stub_contract");

        result.Value.Should().HaveCount(3);
        result.Value.Should().OnlyContain(invoice =>
            invoice.Id.StartsWith("in_stub_", StringComparison.Ordinal)
            && invoice.Currency == "usd"
            && invoice.Status == "paid"
            && invoice.PaidAt.HasValue);
        result.Value.Select(invoice => invoice.IssuedAt)
            .Should().BeInDescendingOrder();
    }
}
