using FluentAssertions;
using JadeCapital.Billing.Application.Stripe;
using JadeCapital.Billing.Contracts.Portal;
using JadeCapital.Billing.Domain.Stripe;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using NSubstitute;

namespace JadeCapital.Billing.UnitTests.Stripe;

/// <summary>
/// Tests for <see cref="GetPaymentMethodsHandler"/> (Wave 6, slice 6b.1).
///
/// <para>
/// <b>Contract</b>:
/// <list type="bullet">
///   <item>Own payment methods → returns list of
///   <see cref="BillingPortalPaymentMethodDto"/>.</item>
///   <item>No Stripe customer mapping → 404
///   <c>notfound.stripe.customer_not_found</c>.</item>
///   <item>Empty list (Stripe returns []) → empty array, NOT 404.</item>
///   <item>Stripe API down → Result.Failure with <c>stripe.*</c> code.</item>
/// </list>
/// </para>
/// </summary>
public class BillingPortalGetPaymentMethodsHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Email = "user@example.com";
    private const string DisplayName = "User Name";
    private const string StripeCustomerId = "cus_123";

    private static StripeCustomer NewStripeCustomer(Guid userId)
        => StripeCustomer.Create(
            Guid.NewGuid(), userId, StripeCustomerId, Email, DisplayName,
            new JadeCapital.Shared.Kernel.Time.SystemClock()).Value;

    [Fact]
    public async Task Handle_Own_PaymentMethods_Returns_List_From_Gateway()
    {
        var customer = NewStripeCustomer(UserId);

        var customerRepo = Substitute.For<IStripeCustomerRepository>();
        customerRepo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(customer);

        var gateway = Substitute.For<IStripeGateway>();
        IReadOnlyList<StripePaymentMethodDto> methods = new[]
        {
            new StripePaymentMethodDto("pm_1", "visa", "4242", DateTimeOffset.UnixEpoch.AddYears(2), true),
            new StripePaymentMethodDto("pm_2", "mastercard", "5555", DateTimeOffset.UnixEpoch.AddYears(3), false),
        };
        gateway.GetPaymentMethodsAsync(StripeCustomerId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(methods));

        var sut = new GetPaymentMethodsHandler(customerRepo, gateway);
        var result = await sut.Handle(new GetPaymentMethodsQuery(UserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].Id.Should().Be("pm_1");
        result.Value[0].Brand.Should().Be("visa");
        result.Value[0].Last4.Should().Be("4242");
        result.Value[0].IsDefault.Should().BeTrue();
        result.Value[1].Brand.Should().Be("mastercard");
        result.Value[1].IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_No_Stripe_Customer_Returns_404_Without_Calling_Gateway()
    {
        var customerRepo = Substitute.For<IStripeCustomerRepository>();
        customerRepo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((StripeCustomer?)null);

        var gateway = Substitute.For<IStripeGateway>();

        var sut = new GetPaymentMethodsHandler(customerRepo, gateway);
        var result = await sut.Handle(new GetPaymentMethodsQuery(UserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.stripe.customer_not_found");
        await gateway.DidNotReceive().GetPaymentMethodsAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Empty_List_Returns_Empty_Array_Not_404()
    {
        // Customer exists but has no payment methods on file (e.g. just
        // signed up, hasn't added a card yet). The endpoint MUST return 200
        // with an empty array, not 404 — Stripe distinguishes "no methods"
        // from "no customer".
        var customer = NewStripeCustomer(UserId);

        var customerRepo = Substitute.For<IStripeCustomerRepository>();
        customerRepo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(customer);

        var gateway = Substitute.For<IStripeGateway>();
        IReadOnlyList<StripePaymentMethodDto> empty = Array.Empty<StripePaymentMethodDto>();
        gateway.GetPaymentMethodsAsync(StripeCustomerId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(empty));

        var sut = new GetPaymentMethodsHandler(customerRepo, gateway);
        var result = await sut.Handle(new GetPaymentMethodsQuery(UserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Stripe_Down_Returns_Stripe_Error()
    {
        var customer = NewStripeCustomer(UserId);

        var customerRepo = Substitute.For<IStripeCustomerRepository>();
        customerRepo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(customer);

        var gateway = Substitute.For<IStripeGateway>();
        gateway.GetPaymentMethodsAsync(StripeCustomerId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<StripePaymentMethodDto>>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.Unavailable("Stripe API down")));

        var sut = new GetPaymentMethodsHandler(customerRepo, gateway);
        var result = await sut.Handle(new GetPaymentMethodsQuery(UserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("stripe.unavailable");
    }

    [Fact]
    public async Task Handle_Empty_UserId_Returns_ValidationError()
    {
        var customerRepo = Substitute.For<IStripeCustomerRepository>();
        var gateway = Substitute.For<IStripeGateway>();

        var sut = new GetPaymentMethodsHandler(customerRepo, gateway);
        var result = await sut.Handle(new GetPaymentMethodsQuery(Guid.Empty), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("validation");
        await customerRepo.DidNotReceive().GetByUserIdAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
