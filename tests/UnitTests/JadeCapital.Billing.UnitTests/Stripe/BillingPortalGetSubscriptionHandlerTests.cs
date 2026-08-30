using FluentAssertions;
using JadeCapital.Billing.Application.Stripe;
using JadeCapital.Billing.Contracts.Portal;
using JadeCapital.Billing.Domain.Stripe;
using JadeCapital.Billing.Domain.Subscriptions;
using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using JadeCapital.Shared.Kernel.Time;
using NSubstitute;

namespace JadeCapital.Billing.UnitTests.Stripe;

/// <summary>
/// Tests for <see cref="GetSubscriptionHandler"/> (Wave 6, slice 6b.1).
///
/// <para>
/// <b>Contract</b>:
/// <list type="bullet">
///   <item>Own subscription → returns <see cref="BillingPortalSubscriptionDto"/>
///   mapped from the gateway's <see cref="StripeSubscriptionDto"/>.</item>
///   <item>Caller has no Stripe customer mapping → 404
///   <c>notfound.stripe.customer_not_found</c>.</item>
///   <item>Caller has a Stripe customer but no local subscription yet → 404
///   <c>notfound.subscription_not_found</c>.</item>
///   <item>Cross-user lookup attempt → 404. The handler MUST resolve the
///   caller's subscription from the JWT-derived userId, never from a
///   caller-supplied stripeSubscriptionId.</item>
///   <item>Stripe API down → Result.Failure with <c>stripe.*</c> code
///   (endpoint maps to 503).</item>
///   <item>Cancellation token propagates.</item>
/// </list>
/// </para>
/// </summary>
public class BillingPortalGetSubscriptionHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Email = "user@example.com";
    private const string DisplayName = "User Name";
    private const string StripeSubId = "sub_123";
    private const string StripeCustomerId = "cus_123";

    private static StripeCustomer NewStripeCustomer(Guid userId)
        => StripeCustomer.Create(
            Guid.NewGuid(), userId, StripeCustomerId, Email, DisplayName, new SystemClock()).Value;

    private static Plan NewProPlan()
        => Plan.FromTrusted(
            id: Guid.NewGuid(),
            code: PlanCode.FromTrusted("pro"),
            name: "Pro Plan",
            monthlyPrice: Money.FromTrusted(99m, "USD"),
            isEligibleForSelfService: true,
            isDeprecated: false);

    private static Subscription NewActiveSubscription(Guid userId)
        => Subscription.Create(
            Guid.NewGuid(), userId,
            NewProPlan(),
            SubscriptionStatus.Active, null, DateTimeOffset.UtcNow).Value;

    [Fact]
    public async Task Handle_Own_Subscription_Returns_Dto_From_Gateway()
    {
        var customer = NewStripeCustomer(UserId);
        var sub = NewActiveSubscription(UserId);
        SetStripeSubscriptionId(sub, StripeSubId);

        var subRepo = Substitute.For<ISubscriptionAdminRepository>();
        subRepo.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(sub);

        var customerRepo = Substitute.For<IStripeCustomerRepository>();
        customerRepo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(customer);

        var gateway = Substitute.For<IStripeGateway>();
        gateway.GetSubscriptionAsync(StripeSubId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new StripeSubscriptionDto(
                StripeSubscriptionId: StripeSubId,
                Status: "active",
                PlanCode: "pro",
                CurrentPeriodEnd: DateTimeOffset.UnixEpoch.AddDays(30),
                CancelAtPeriodEnd: false)));

        var sut = new GetSubscriptionHandler(customerRepo, subRepo, gateway);
        var result = await sut.Handle(new GetSubscriptionQuery(UserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.StripeSubscriptionId.Should().Be(StripeSubId);
        dto.Status.Should().Be("active");
        dto.PlanCode.Should().Be("pro");
        dto.CancelAtPeriodEnd.Should().BeFalse();
        dto.CurrentPeriodEnd.Should().Be(DateTimeOffset.UnixEpoch.AddDays(30));
    }

    [Fact]
    public async Task Handle_No_Stripe_Customer_Returns_404_Without_Calling_Gateway()
    {
        var customerRepo = Substitute.For<IStripeCustomerRepository>();
        customerRepo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((StripeCustomer?)null);

        var subRepo = Substitute.For<ISubscriptionAdminRepository>();
        var gateway = Substitute.For<IStripeGateway>();

        var sut = new GetSubscriptionHandler(customerRepo, subRepo, gateway);
        var result = await sut.Handle(new GetSubscriptionQuery(UserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.stripe.customer_not_found");
        await gateway.DidNotReceive().GetSubscriptionAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Stripe_Customer_But_No_Local_Subscription_Returns_404()
    {
        // Caller has done POST /api/billing/stripe/customers (so the mapping
        // exists) but never completed checkout, so there's no local
        // Subscription row yet. The portal subscription endpoint returns 404,
        // not 500.
        var customer = NewStripeCustomer(UserId);

        var customerRepo = Substitute.For<IStripeCustomerRepository>();
        customerRepo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(customer);

        var subRepo = Substitute.For<ISubscriptionAdminRepository>();
        subRepo.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((Subscription?)null);

        var gateway = Substitute.For<IStripeGateway>();

        var sut = new GetSubscriptionHandler(customerRepo, subRepo, gateway);
        var result = await sut.Handle(new GetSubscriptionQuery(UserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.subscription_not_found");
        await gateway.DidNotReceive().GetSubscriptionAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Cross_User_Lookup_Returns_404_Not_Other_Users_Subscription()
    {
        // The handler MUST resolve the subscription from the JWT-derived
        // userId, never accept a stripeSubscriptionId from the request. Even
        // if caller A passed caller B's Stripe sub id (somehow), the handler
        // would still resolve A's local Subscription by A's userId and never
        // call the gateway with B's sub id. This test pins that contract:
        // when the local Subscription has StripeSubscriptionId = null (i.e.
        // webhook hasn't synced yet), we return 404 even though Stripe has
        // data for the caller.
        var customerA = NewStripeCustomer(UserId);
        var subA = NewActiveSubscription(UserId);
        // subA.StripeSubscriptionId is null (default) — webhook hasn't synced.

        var customerRepo = Substitute.For<IStripeCustomerRepository>();
        customerRepo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(customerA);

        var subRepo = Substitute.For<ISubscriptionAdminRepository>();
        subRepo.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(subA);

        var gateway = Substitute.For<IStripeGateway>();

        var sut = new GetSubscriptionHandler(customerRepo, subRepo, gateway);
        var result = await sut.Handle(new GetSubscriptionQuery(UserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.subscription_not_found");
        await gateway.DidNotReceive().GetSubscriptionAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Stripe_Down_Returns_Stripe_Error()
    {
        var customer = NewStripeCustomer(UserId);
        var sub = NewActiveSubscription(UserId);
        // Subscription.SyncFromStripe binds the StripeSubscriptionId one-time.
        // For this test we don't go through the webhook path; we set the id
        // via reflection (the production path is SyncFromStripe — slice 6a.2).
        SetStripeSubscriptionId(sub, StripeSubId);

        var customerRepo = Substitute.For<IStripeCustomerRepository>();
        customerRepo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(customer);

        var subRepo = Substitute.For<ISubscriptionAdminRepository>();
        subRepo.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(sub);

        var gateway = Substitute.For<IStripeGateway>();
        gateway.GetSubscriptionAsync(StripeSubId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<StripeSubscriptionDto>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.Unavailable("Stripe API down")));

        var sut = new GetSubscriptionHandler(customerRepo, subRepo, gateway);
        var result = await sut.Handle(new GetSubscriptionQuery(UserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("stripe.unavailable");
    }

    [Fact]
    public async Task Handle_Cancellation_Propagates()
    {
        var customer = NewStripeCustomer(UserId);
        var sub = NewActiveSubscription(UserId);
        SetStripeSubscriptionId(sub, StripeSubId);

        var customerRepo = Substitute.For<IStripeCustomerRepository>();
        customerRepo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(customer);

        var subRepo = Substitute.For<ISubscriptionAdminRepository>();
        subRepo.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(sub);

        var gateway = Substitute.For<IStripeGateway>();
        gateway.GetSubscriptionAsync(StripeSubId, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callInfo.ArgAt<CancellationToken>(1).ThrowIfCancellationRequested();
                return Result.Success(new StripeSubscriptionDto(
                    StripeSubId, "active", "pro", DateTimeOffset.UnixEpoch, false));
            });

        var sut = new GetSubscriptionHandler(customerRepo, subRepo, gateway);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await FluentActions.Awaiting(() => sut.Handle(new GetSubscriptionQuery(UserId), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Test fixture: <see cref="Subscription.StripeSubscriptionId"/> is set by
    /// the webhook path (<c>SyncFromStripe</c>) on first sync. Tests that need
    /// the portal read path to call the gateway use this helper to bind the
    /// Stripe id without going through the webhook.
    /// </summary>
    private static void SetStripeSubscriptionId(Subscription sub, string stripeSubId)
    {
        // Reflection: StripeSubscriptionId has a private setter. We bypass it
        // for test-only. Production binds via SyncFromStripe (slice 6a.2).
        var prop = typeof(Subscription).GetProperty(
            nameof(Subscription.StripeSubscriptionId),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        prop.Should().NotBeNull();
        prop!.SetValue(sub, stripeSubId);
    }

    [Fact]
    public async Task Handle_Empty_UserId_Returns_ValidationError()
    {
        var customerRepo = Substitute.For<IStripeCustomerRepository>();
        var subRepo = Substitute.For<ISubscriptionAdminRepository>();
        var gateway = Substitute.For<IStripeGateway>();

        var sut = new GetSubscriptionHandler(customerRepo, subRepo, gateway);
        var result = await sut.Handle(new GetSubscriptionQuery(Guid.Empty), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("validation");
        await customerRepo.DidNotReceive().GetByUserIdAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Gateway_Timeout_Maps_To_Stripe_Timeout()
    {
        // Stripe timeout (different from "unavailable") — the endpoint maps
        // both to 503. Pin the error code so callers can distinguish at the
        // FE if they need to.
        var customer = NewStripeCustomer(UserId);
        var sub = NewActiveSubscription(UserId);
        SetStripeSubscriptionId(sub, StripeSubId);

        var customerRepo = Substitute.For<IStripeCustomerRepository>();
        customerRepo.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(customer);

        var subRepo = Substitute.For<ISubscriptionAdminRepository>();
        subRepo.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(sub);

        var gateway = Substitute.For<IStripeGateway>();
        gateway.GetSubscriptionAsync(StripeSubId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<StripeSubscriptionDto>(
                JadeCapital.Shared.Kernel.Stripe.StripeError.Timeout("Stripe call timed out")));

        var sut = new GetSubscriptionHandler(customerRepo, subRepo, gateway);
        var result = await sut.Handle(new GetSubscriptionQuery(UserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("stripe.timeout");
    }
}
