using FluentAssertions;
using JadeCapital.Billing.Application.Stripe;
using JadeCapital.Billing.Domain.Stripe;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using JadeCapital.Shared.Kernel.Time;
using NSubstitute;

namespace JadeCapital.Billing.UnitTests.Stripe;

/// <summary>
/// Tests for <see cref="CreatePortalSessionHandler"/> (Wave 6, slice 6a.2).
///
/// <para>
/// <b>Contract</b>:
/// <list type="bullet">
///   <item>User with Stripe customer mapping → returns Portal URL.</item>
///   <item>User with NO Stripe customer mapping → Result.Failure
///   <c>stripe.customer_not_found</c> (NOT a gateway call).</item>
///   <item>Stripe API error → Result.Failure with StripeError.</item>
///   <item>Empty returnUrl → Result.Failure with validation error.</item>
///   <item>Cancellation token propagates.</item>
/// </list>
/// </para>
/// </summary>
public class CreatePortalSessionHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string ReturnUrl = "https://app.example.com/billing";
    private const string Email = "user@example.com";
    private const string DisplayName = "User Name";

    private static StripeCustomer NewStripeCustomer(Guid userId)
    {
        var r = StripeCustomer.Create(
            Guid.NewGuid(), userId, "cus_existing_abc", Email, DisplayName, new SystemClock());
        r.IsSuccess.Should().BeTrue();
        return r.Value;
    }

    [Fact]
    public async Task Handle_Existing_Customer_Returns_Portal_Url()
    {
        var existing = NewStripeCustomer(UserId);
        var repo = Substitute.For<IStripeCustomerRepository>();
        var gateway = Substitute.For<IStripeGateway>();
        repo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(existing);
        gateway.CreatePortalSessionAsync(UserId, ReturnUrl, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new StripePortalSessionDto(
                "ps_1", "https://billing.stripe.com/p/ps_1", DateTimeOffset.UnixEpoch.AddHours(1))));

        var sut = new CreatePortalSessionHandler(repo, gateway, new SystemClock());
        var result = await sut.Handle(
            new CreatePortalSessionCommand(UserId, ReturnUrl),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Url.Should().Be("https://billing.stripe.com/p/ps_1");
        await gateway.Received(1).CreatePortalSessionAsync(
            UserId, ReturnUrl, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Missing_Customer_Returns_NotFound_Without_Calling_Gateway()
    {
        var repo = Substitute.For<IStripeCustomerRepository>();
        var gateway = Substitute.For<IStripeGateway>();
        repo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((StripeCustomer?)null);

        var sut = new CreatePortalSessionHandler(repo, gateway, new SystemClock());
        var result = await sut.Handle(
            new CreatePortalSessionCommand(UserId, ReturnUrl),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.stripe.customer_not_found");
        // MUST NOT call gateway when the customer mapping is absent — the
        // portal session is meaningless without a Stripe customer.
        await gateway.DidNotReceive().CreatePortalSessionAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Gateway_Failure_Returns_StripeError()
    {
        var existing = NewStripeCustomer(UserId);
        var repo = Substitute.For<IStripeCustomerRepository>();
        var gateway = Substitute.For<IStripeGateway>();
        repo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(existing);
        gateway.CreatePortalSessionAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<StripePortalSessionDto>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.Unavailable("Stripe down")));

        var sut = new CreatePortalSessionHandler(repo, gateway, new SystemClock());
        var result = await sut.Handle(
            new CreatePortalSessionCommand(UserId, ReturnUrl),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("stripe.unavailable");
    }

    [Fact]
    public async Task Handle_Empty_ReturnUrl_Returns_ValidationError()
    {
        var existing = NewStripeCustomer(UserId);
        var repo = Substitute.For<IStripeCustomerRepository>();
        var gateway = Substitute.For<IStripeGateway>();
        repo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(existing);

        var sut = new CreatePortalSessionHandler(repo, gateway, new SystemClock());
        var result = await sut.Handle(
            new CreatePortalSessionCommand(UserId, ""),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("return_url_required");
        await gateway.DidNotReceive().CreatePortalSessionAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Cancellation_Propagates()
    {
        var existing = NewStripeCustomer(UserId);
        var repo = Substitute.For<IStripeCustomerRepository>();
        var gateway = Substitute.For<IStripeGateway>();
        repo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(existing);
        gateway.CreatePortalSessionAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callInfo.ArgAt<CancellationToken>(2).ThrowIfCancellationRequested();
                return Result.Success(new StripePortalSessionDto("ps_1", "u", DateTimeOffset.UnixEpoch));
            });

        var sut = new CreatePortalSessionHandler(repo, gateway, new SystemClock());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await FluentActions.Awaiting(() =>
                sut.Handle(new CreatePortalSessionCommand(UserId, ReturnUrl), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }
}
