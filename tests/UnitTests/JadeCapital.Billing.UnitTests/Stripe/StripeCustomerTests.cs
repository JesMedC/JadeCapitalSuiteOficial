using JadeCapital.Billing.Domain.Stripe;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Billing.UnitTests.Stripe;

/// <summary>
/// Domain tests for the StripeCustomer aggregate (Wave 6, slice 6a.1).
///
/// <para>
/// RED-first contract:
/// <list type="bullet">
///   <item>create valid → Result.Success, CreatedAt set to clock.UtcNow</item>
///   <item>stripeCustomerId missing → invalid_stripe_customer_id</item>
///   <item>stripeCustomerId without <c>cus_</c> prefix → invalid_stripe_customer_id</item>
///   <item>userId empty → user_id_required</item>
///   <item>email empty → email_required</item>
///   <item>displayName nullable</item>
/// </list>
/// </para>
/// </summary>
public class StripeCustomerTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; init; } = new(2026, 8, 19, 14, 32, 0, TimeSpan.Zero);
    }

    private static readonly IClock Clock = new FixedClock();
    private static readonly Guid ValidUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string ValidStripeCustomerId = "cus_test_abc123";
    private const string ValidEmail = "user@example.com";

    [Fact]
    public void Create_Valid_Inputs_Returns_Success()
    {
        var id = Guid.NewGuid();
        var result = StripeCustomer.Create(id, ValidUserId, ValidStripeCustomerId, ValidEmail, "John Doe", Clock);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(id);
        result.Value.UserId.Should().Be(ValidUserId);
        result.Value.StripeCustomerId.Should().Be(ValidStripeCustomerId);
        result.Value.Email.Should().Be(ValidEmail);
        result.Value.DisplayName.Should().Be("John Doe");
        result.Value.CreatedAt.Should().Be(Clock.UtcNow);
    }

    [Fact]
    public void Create_DisplayName_Null_Is_Allowed()
    {
        var result = StripeCustomer.Create(Guid.NewGuid(), ValidUserId, ValidStripeCustomerId, ValidEmail, null, Clock);

        result.IsSuccess.Should().BeTrue();
        result.Value.DisplayName.Should().BeNull();
    }

    [Fact]
    public void Create_Empty_Id_Returns_IdRequired()
    {
        var result = StripeCustomer.Create(Guid.Empty, ValidUserId, ValidStripeCustomerId, ValidEmail, null, Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.stripe.customer.id_required");
    }

    [Fact]
    public void Create_Empty_UserId_Returns_UserIdRequired()
    {
        var result = StripeCustomer.Create(Guid.NewGuid(), Guid.Empty, ValidStripeCustomerId, ValidEmail, null, Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.stripe.customer.user_id_required");
    }

    [Fact]
    public void Create_Empty_StripeCustomerId_Returns_StripeCustomerIdRequired()
    {
        var result = StripeCustomer.Create(Guid.NewGuid(), ValidUserId, "", ValidEmail, null, Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.stripe.customer.stripe_customer_id_required");
    }

    [Fact]
    public void Create_StripeCustomerId_Without_CusPrefix_Returns_InvalidStripeCustomerId()
    {
        var result = StripeCustomer.Create(Guid.NewGuid(), ValidUserId, "sub_invalid", ValidEmail, null, Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.stripe.customer.invalid_stripe_customer_id");
    }

    [Fact]
    public void Create_Whitespace_StripeCustomerId_Returns_StripeCustomerIdRequired()
    {
        var result = StripeCustomer.Create(Guid.NewGuid(), ValidUserId, "   ", ValidEmail, null, Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.stripe.customer.stripe_customer_id_required");
    }

    [Fact]
    public void Create_Empty_Email_Returns_EmailRequired()
    {
        var result = StripeCustomer.Create(Guid.NewGuid(), ValidUserId, ValidStripeCustomerId, "", null, Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.stripe.customer.email_required");
    }

    [Fact]
    public void Create_Whitespace_Email_Returns_EmailRequired()
    {
        var result = StripeCustomer.Create(Guid.NewGuid(), ValidUserId, ValidStripeCustomerId, "   ", null, Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.stripe.customer.email_required");
    }

    [Fact]
    public void Create_Pins_CreatedAt_To_Clock_Not_Real_Now()
    {
        // Determinism for tests — CreatedAt MUST come from the clock, not UtcNow directly.
        var before = Clock.UtcNow;
        var result = StripeCustomer.Create(Guid.NewGuid(), ValidUserId, ValidStripeCustomerId, ValidEmail, null, Clock);
        var after = Clock.UtcNow.AddSeconds(1);

        result.IsSuccess.Should().BeTrue();
        result.Value.CreatedAt.Should().BeOnOrAfter(before);
        result.Value.CreatedAt.Should().BeOnOrBefore(after);
    }
}
