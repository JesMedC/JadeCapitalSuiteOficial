using FluentAssertions;
using JadeCapital.Billing.Application.Stripe;
using JadeCapital.Billing.Contracts.Portal;
using JadeCapital.Billing.Domain.Stripe;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using NSubstitute;

namespace JadeCapital.Billing.UnitTests.Stripe;

/// <summary>
/// Tests for <see cref="GetInvoicesHandler"/> (Wave 6, slice 6b.1).
///
/// <para>
/// <b>Contract</b>:
/// <list type="bullet">
///   <item>Own invoices → returns list of
///   <see cref="BillingPortalInvoiceDto"/>.</item>
///   <item>No Stripe customer mapping → 404
///   <c>notfound.stripe.customer_not_found</c>.</item>
///   <item>Empty list (Stripe returns []) → empty array, NOT 404.</item>
///   <item>Stripe API down → Result.Failure with <c>stripe.*</c> code.</item>
/// </list>
/// </para>
/// </summary>
public class BillingPortalGetInvoicesHandlerTests
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
    public async Task Handle_Own_Invoices_Returns_List_From_Gateway()
    {
        var customer = NewStripeCustomer(UserId);

        var customerRepo = Substitute.For<IStripeCustomerRepository>();
        customerRepo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(customer);

        var gateway = Substitute.For<IStripeGateway>();
        IReadOnlyList<StripeInvoiceDto> invoices = new[]
        {
            new StripeInvoiceDto("in_1", "INV-001", 1999, "usd",
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), "paid",
                "https://stripe.com/inv/1.pdf"),
            new StripeInvoiceDto("in_2", "INV-002", 1999, "usd",
                DateTimeOffset.UnixEpoch.AddDays(-30), null, "open",
                "https://stripe.com/inv/2.pdf"),
        };
        gateway.GetInvoicesAsync(StripeCustomerId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(invoices));

        var sut = new GetInvoicesHandler(customerRepo, gateway);
        var result = await sut.Handle(new GetInvoicesQuery(UserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].Id.Should().Be("in_1");
        result.Value[0].Number.Should().Be("INV-001");
        result.Value[0].AmountCents.Should().Be(1999);
        result.Value[0].Currency.Should().Be("usd");
        result.Value[0].Status.Should().Be("paid");
        result.Value[0].PdfUrl.Should().Be("https://stripe.com/inv/1.pdf");
        result.Value[1].PaidAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_No_Stripe_Customer_Returns_404_Without_Calling_Gateway()
    {
        var customerRepo = Substitute.For<IStripeCustomerRepository>();
        customerRepo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((StripeCustomer?)null);

        var gateway = Substitute.For<IStripeGateway>();

        var sut = new GetInvoicesHandler(customerRepo, gateway);
        var result = await sut.Handle(new GetInvoicesQuery(UserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.stripe.customer_not_found");
        await gateway.DidNotReceive().GetInvoicesAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Empty_List_Returns_Empty_Array_Not_404()
    {
        // Customer exists but has no invoices (e.g. never subscribed).
        // The endpoint MUST return 200 with an empty array, not 404.
        var customer = NewStripeCustomer(UserId);

        var customerRepo = Substitute.For<IStripeCustomerRepository>();
        customerRepo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(customer);

        var gateway = Substitute.For<IStripeGateway>();
        IReadOnlyList<StripeInvoiceDto> empty = Array.Empty<StripeInvoiceDto>();
        gateway.GetInvoicesAsync(StripeCustomerId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(empty));

        var sut = new GetInvoicesHandler(customerRepo, gateway);
        var result = await sut.Handle(new GetInvoicesQuery(UserId), CancellationToken.None);

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
        gateway.GetInvoicesAsync(StripeCustomerId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<StripeInvoiceDto>>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.Unavailable("Stripe API down")));

        var sut = new GetInvoicesHandler(customerRepo, gateway);
        var result = await sut.Handle(new GetInvoicesQuery(UserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("stripe.unavailable");
    }

    [Fact]
    public async Task Handle_Empty_UserId_Returns_ValidationError()
    {
        var customerRepo = Substitute.For<IStripeCustomerRepository>();
        var gateway = Substitute.For<IStripeGateway>();

        var sut = new GetInvoicesHandler(customerRepo, gateway);
        var result = await sut.Handle(new GetInvoicesQuery(Guid.Empty), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("validation");
        await customerRepo.DidNotReceive().GetByUserIdAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
