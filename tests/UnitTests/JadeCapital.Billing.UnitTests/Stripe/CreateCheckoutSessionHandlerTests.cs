using FluentAssertions;
using JadeCapital.Billing.Application.Stripe;
using JadeCapital.Billing.Domain.Stripe;
using JadeCapital.Billing.Domain.Subscriptions;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using JadeCapital.Shared.Kernel.Time;
using NSubstitute;

namespace JadeCapital.Billing.UnitTests.Stripe;

/// <summary>
/// Tests for <see cref="CreateCheckoutSessionHandler"/> (Wave 6, slice 6a.2).
///
/// <para>
/// <b>Contract</b>:
/// <list type="bullet">
///   <item>Valid user + priceId → calls gateway, returns URL DTO.</item>
///   <item>User has no Stripe customer mapping → handler ensures one first
///   (idempotent create-or-get), then calls gateway.</item>
///   <item>Stripe API error → Result.Failure with StripeError.</item>
///   <item>Empty priceId → Result.Failure with validation error.</item>
///   <item>Cancellation token propagates.</item>
/// </list>
/// </para>
/// </summary>
public class CreateCheckoutSessionHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Email = "user@example.com";
    private const string DisplayName = "User Name";
    private const string PriceId = "price_pro_monthly";
    private const string SuccessUrl = "https://app.example.com/billing/success";
    private const string CancelUrl = "https://app.example.com/billing/cancel";

    private static StripeCustomer NewStripeCustomer(Guid userId)
    {
        var r = StripeCustomer.Create(
            Guid.NewGuid(), userId, "cus_existing_abc", Email, DisplayName, new SystemClock());
        r.IsSuccess.Should().BeTrue();
        return r.Value;
    }

    [Fact]
    public async Task Handle_Existing_Customer_Calls_Gateway_Directly()
    {
        var existingCustomer = NewStripeCustomer(UserId);
        var repo = Substitute.For<IStripeCustomerRepository>();
        var gateway = Substitute.For<IStripeGateway>();
        repo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(existingCustomer);

        var expectedDto = new StripeCheckoutSessionDto(
            "cs_1", "https://checkout.stripe.com/c/cs_1", DateTimeOffset.UnixEpoch.AddHours(1));
        gateway.CreateCheckoutSessionAsync(
            UserId, PriceId, SuccessUrl, CancelUrl, Arg.Any<CancellationToken>())
            .Returns(Result.Success(expectedDto));

        var sut = new CreateCheckoutSessionHandler(repo, gateway, new SystemClock());
        var result = await sut.Handle(
            new CreateCheckoutSessionCommand(UserId, PriceId, SuccessUrl, CancelUrl),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Url.Should().Be("https://checkout.stripe.com/c/cs_1");
        await gateway.Received(1).CreateCheckoutSessionAsync(
            UserId, PriceId, SuccessUrl, CancelUrl, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Missing_Customer_Creates_One_First_Then_ChecksOut()
    {
        // The handler MUST ensure a Stripe customer mapping exists before
        // calling the gateway. If absent, it calls CreateOrGetCustomerAsync
        // to materialise one.
        var repo = Substitute.For<IStripeCustomerRepository>();
        var gateway = Substitute.For<IStripeGateway>();

        // First call: no mapping. Second call (after ensure): mapping present.
        var created = NewStripeCustomer(UserId);
        repo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((StripeCustomer?)null, created);

        gateway.CreateOrGetCustomerAsync(UserId, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new StripeCustomerDto(
                "cus_new", Email, DisplayName, DateTimeOffset.UtcNow)));
        gateway.CreateCheckoutSessionAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new StripeCheckoutSessionDto(
                "cs_1", "https://checkout.stripe.com/c/cs_1", DateTimeOffset.UnixEpoch.AddHours(1))));

        var sut = new CreateCheckoutSessionHandler(repo, gateway, new SystemClock());
        var result = await sut.Handle(
            new CreateCheckoutSessionCommand(UserId, PriceId, SuccessUrl, CancelUrl, Email, DisplayName),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await gateway.Received(1).CreateOrGetCustomerAsync(
            UserId, Email, DisplayName, Arg.Any<CancellationToken>());
        await gateway.Received(1).CreateCheckoutSessionAsync(
            UserId, PriceId, SuccessUrl, CancelUrl, Arg.Any<CancellationToken>());
        await repo.Received(1).AddAsync(Arg.Any<StripeCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Gateway_Failure_Returns_StripeError()
    {
        var existing = NewStripeCustomer(UserId);
        var repo = Substitute.For<IStripeCustomerRepository>();
        var gateway = Substitute.For<IStripeGateway>();
        repo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(existing);
        gateway.CreateCheckoutSessionAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<StripeCheckoutSessionDto>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.Api("Stripe down")));

        var sut = new CreateCheckoutSessionHandler(repo, gateway, new SystemClock());
        var result = await sut.Handle(
            new CreateCheckoutSessionCommand(UserId, PriceId, SuccessUrl, CancelUrl),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("stripe.api_error");
    }

    [Fact]
    public async Task Handle_Empty_PriceId_Returns_ValidationError()
    {
        var existing = NewStripeCustomer(UserId);
        var repo = Substitute.For<IStripeCustomerRepository>();
        var gateway = Substitute.For<IStripeGateway>();
        repo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(existing);

        var sut = new CreateCheckoutSessionHandler(repo, gateway, new SystemClock());
        var result = await sut.Handle(
            new CreateCheckoutSessionCommand(UserId, "", SuccessUrl, CancelUrl),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("price_id_required");
        await gateway.DidNotReceive().CreateCheckoutSessionAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Empty_SuccessUrl_Returns_ValidationError()
    {
        var existing = NewStripeCustomer(UserId);
        var repo = Substitute.For<IStripeCustomerRepository>();
        var gateway = Substitute.For<IStripeGateway>();
        repo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(existing);

        var sut = new CreateCheckoutSessionHandler(repo, gateway, new SystemClock());
        var result = await sut.Handle(
            new CreateCheckoutSessionCommand(UserId, PriceId, "", CancelUrl),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("success_url_required");
    }

    [Fact]
    public async Task Handle_Cancellation_Propagates()
    {
        var existing = NewStripeCustomer(UserId);
        var repo = Substitute.For<IStripeCustomerRepository>();
        var gateway = Substitute.For<IStripeGateway>();
        repo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(existing);
        gateway.CreateCheckoutSessionAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callInfo.ArgAt<CancellationToken>(4).ThrowIfCancellationRequested();
                return Result.Success(new StripeCheckoutSessionDto("cs_1", "u", DateTimeOffset.UnixEpoch));
            });

        var sut = new CreateCheckoutSessionHandler(repo, gateway, new SystemClock());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await FluentActions.Awaiting(() =>
                sut.Handle(new CreateCheckoutSessionCommand(UserId, PriceId, SuccessUrl, CancelUrl), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }
}
