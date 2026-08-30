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
        result.Value.PayloadJson.Should().Be("{\"foo\":\"bar\"}");
    }
}
