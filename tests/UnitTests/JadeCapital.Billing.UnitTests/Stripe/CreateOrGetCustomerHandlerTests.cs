using JadeCapital.Billing.Application.Stripe;
using JadeCapital.Billing.Domain.Stripe;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Billing.UnitTests.Stripe;

/// <summary>
/// Tests for <see cref="CreateOrGetCustomerHandler"/> (Wave 6, slice 6a.1).
///
/// <para>
/// Contract:
/// <list type="bullet">
///   <item>Existing user → returns existing StripeCustomer from repo</item>
///   <item>New user → calls gateway, persists, returns dto</item>
///   <item>Stripe error → returns Result.Failure with StripeError</item>
///   <item>Idempotent re-call → returns same id without second gateway call</item>
/// </list>
/// </para>
/// </summary>
public class CreateOrGetCustomerHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Email = "user@example.com";
    private const string DisplayName = "User Name";

    private static StripeCustomer NewStripeCustomer(Guid userId, string stripeCustomerId = "cus_test_abc")
    {
        var r = StripeCustomer.Create(Guid.NewGuid(), userId, stripeCustomerId, Email, DisplayName, new SystemClock());
        r.IsSuccess.Should().BeTrue();
        return r.Value;
    }

    [Fact]
    public async Task Handle_Existing_User_Returns_Existing_Mapping()
    {
        var existingCustomer = NewStripeCustomer(UserId);
        var repo = Substitute.For<IStripeCustomerRepository>();
        var gateway = Substitute.For<IStripeGateway>();
        repo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(existingCustomer);

        var sut = new CreateOrGetCustomerHandler(repo, gateway, new SystemClock());
        var result = await sut.Handle(
            new CreateOrGetCustomerCommand(UserId, Email, DisplayName),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.StripeCustomerId.Should().Be(existingCustomer.StripeCustomerId);
        // Gateway MUST NOT be called when the user already has a mapping (idempotency).
        await gateway.DidNotReceive().CreateOrGetCustomerAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_New_User_Calls_Gateway_And_Persists()
    {
        var repo = Substitute.For<IStripeCustomerRepository>();
        var gateway = Substitute.For<IStripeGateway>();
        repo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((StripeCustomer?)null);
        gateway.CreateOrGetCustomerAsync(UserId, Email, DisplayName, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new StripeCustomerDto(
                "cus_new", Email, DisplayName, DateTimeOffset.UtcNow)));

        var sut = new CreateOrGetCustomerHandler(repo, gateway, new SystemClock());
        var result = await sut.Handle(
            new CreateOrGetCustomerCommand(UserId, Email, DisplayName),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.StripeCustomerId.Should().Be("cus_new");
        await gateway.Received(1).CreateOrGetCustomerAsync(
            UserId, Email, DisplayName, Arg.Any<CancellationToken>());
        await repo.Received(1).AddAsync(Arg.Any<StripeCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Gateway_Failure_Returns_StripeError()
    {
        var repo = Substitute.For<IStripeCustomerRepository>();
        var gateway = Substitute.For<IStripeGateway>();
        repo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((StripeCustomer?)null);
        gateway.CreateOrGetCustomerAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<StripeCustomerDto>(JadeCapital.Shared.Kernel.Stripe.StripeError.Authentication("Invalid key")));

        var sut = new CreateOrGetCustomerHandler(repo, gateway, new SystemClock());
        var result = await sut.Handle(
            new CreateOrGetCustomerCommand(UserId, Email, DisplayName),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("stripe.authentication_error");
        // MUST NOT persist on failure.
        await repo.DidNotReceive().AddAsync(Arg.Any<StripeCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Idempotent_ReCall_Does_Not_Double_Call_Gateway()
    {
        // First call creates. Second call sees the mapping and returns without calling gateway.
        var firstCustomer = NewStripeCustomer(UserId);
        var repo = Substitute.For<IStripeCustomerRepository>();
        var gateway = Substitute.For<IStripeGateway>();
        repo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(firstCustomer);

        var sut = new CreateOrGetCustomerHandler(repo, gateway, new SystemClock());

        var first = await sut.Handle(new CreateOrGetCustomerCommand(UserId, Email, DisplayName), CancellationToken.None);
        var second = await sut.Handle(new CreateOrGetCustomerCommand(UserId, Email, DisplayName), CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        first.Value.StripeCustomerId.Should().Be(second.Value.StripeCustomerId);
        await gateway.DidNotReceive().CreateOrGetCustomerAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Cancellation_Propagates()
    {
        var repo = Substitute.For<IStripeCustomerRepository>();
        var gateway = Substitute.For<IStripeGateway>();
        repo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((StripeCustomer?)null);
        gateway.CreateOrGetCustomerAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callInfo.ArgAt<CancellationToken>(3).ThrowIfCancellationRequested();
                return Result.Success(new StripeCustomerDto("cus_x", Email, DisplayName, DateTimeOffset.UtcNow));
            });

        var sut = new CreateOrGetCustomerHandler(repo, gateway, new SystemClock());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await FluentActions.Awaiting(() =>
                sut.Handle(new CreateOrGetCustomerCommand(UserId, Email, DisplayName), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }
}
