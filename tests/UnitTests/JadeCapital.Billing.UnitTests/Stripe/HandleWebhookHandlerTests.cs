using JadeCapital.Billing.Application.Stripe;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;

namespace JadeCapital.Billing.UnitTests.Stripe;

/// <summary>
/// Tests for <see cref="HandleWebhookHandler"/> (Wave 6, slice 6a.1).
///
/// <para>
/// Contract (slice 6a.1 — signature verify + log only, no business logic):
/// <list type="bullet">
///   <item>Valid signature → returns success with parsed event</item>
///   <item>Missing signature → Result.Failure with signature_missing code</item>
///   <item>Malformed signature → Result.Failure with signature_invalid code</item>
///   <item>Gateway error → propagates as Result.Failure</item>
///   <item>Cancellation propagates</item>
/// </list>
/// Full idempotency + business dispatch lands in slice 6a.2 with the
/// <c>billing.stripe_webhook_events</c> migration.
/// </para>
/// </summary>
public class HandleWebhookHandlerTests
{
    private const string Payload = "{\"id\":\"evt_123\",\"type\":\"ping\"}";
    private const string Signature = "t=1234567890,v1=abcdef0123456789";

    [Fact]
    public async Task Handle_Valid_Signature_Returns_Parsed_Event()
    {
        var gateway = Substitute.For<IStripeGateway>();
        var parsed = new StripeWebhookEvent("evt_123", "ping", Payload, DateTimeOffset.UnixEpoch);
        gateway.VerifyWebhookAsync(Payload, Signature, Arg.Any<CancellationToken>())
            .Returns(Result.Success(parsed));

        var sut = new HandleWebhookHandler(gateway);
        var result = await sut.Handle(
            new HandleWebhookCommand(Payload, Signature),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.EventId.Should().Be("evt_123");
        result.Value.Outcome.Should().Be("processed");
    }

    [Fact]
    public async Task Handle_Missing_Signature_Returns_SignatureMissing()
    {
        var gateway = Substitute.For<IStripeGateway>();
        gateway.VerifyWebhookAsync(Payload, "", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<StripeWebhookEvent>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.SignatureMissing("missing header")));

        var sut = new HandleWebhookHandler(gateway);
        var result = await sut.Handle(
            new HandleWebhookCommand(Payload, ""),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("stripe.signature_missing");
    }

    [Fact]
    public async Task Handle_Malformed_Signature_Returns_SignatureInvalid()
    {
        var gateway = Substitute.For<IStripeGateway>();
        gateway.VerifyWebhookAsync(Payload, "bad", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<StripeWebhookEvent>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.SignatureInvalid("bad signature")));

        var sut = new HandleWebhookHandler(gateway);
        var result = await sut.Handle(
            new HandleWebhookCommand(Payload, "bad"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("stripe.signature_invalid");
    }

    [Fact]
    public async Task Handle_Gateway_Auth_Error_Propagates()
    {
        var gateway = Substitute.For<IStripeGateway>();
        gateway.VerifyWebhookAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<StripeWebhookEvent>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.Authentication("no webhook secret")));

        var sut = new HandleWebhookHandler(gateway);
        var result = await sut.Handle(
            new HandleWebhookCommand(Payload, Signature),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("stripe.authentication_error");
    }

    [Fact]
    public async Task Handle_Cancellation_Propagates()
    {
        var gateway = Substitute.For<IStripeGateway>();
        gateway.VerifyWebhookAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callInfo.ArgAt<CancellationToken>(2).ThrowIfCancellationRequested();
                return Result.Success(new StripeWebhookEvent("evt_x", "ping", Payload, DateTimeOffset.UnixEpoch));
            });

        var sut = new HandleWebhookHandler(gateway);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await FluentActions.Awaiting(() =>
                sut.Handle(new HandleWebhookCommand(Payload, Signature), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }
}
