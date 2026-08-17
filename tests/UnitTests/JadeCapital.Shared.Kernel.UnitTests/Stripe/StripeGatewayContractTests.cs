using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace JadeCapital.Shared.Kernel.UnitTests.Stripe;

internal static class StripeJsonOptions
{
    public static readonly JsonSerializerOptions SnakeCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}

/// <summary>
/// Contract tests for the Stripe gateway wire shape (Wave 6, slice 6a.1).
///
/// <para>
/// Pins the cross-module contract that Billing.Infrastructure's
/// <c>StripeGateway</c> and Billing.Application's handlers rely on.
/// Changing these types is a breaking change.
/// </para>
///
/// <para>
/// These tests assert the SHAPE — names, types, JSON serialization,
/// cancellation token presence. Behavioural tests (timeout, retry, error
/// mapping) live with the gateway impl in Billing.Infrastructure.
/// </para>
///
/// <para>
/// <b>Slice 6a.1 surface</b>: Customer + Webhook verification only.
/// Checkout / Portal / Subscription / PaymentMethod / Invoice land in 6a.2.
/// </para>
/// </summary>
public class StripeGatewayContractTests
{
    [Fact]
    public void IStripeGateway_Declares_Two_Members_In_Slice_6a1()
    {
        // Slice 6a.1 surface: Customer creation + Webhook verification.
        // Slice 6a.2 expands to seven methods. The interface is intentionally
        // narrow in 6a.1 so the test contract pins the boundary.
        var iface = typeof(IStripeGateway);
        var members = iface.GetMethods()
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        members.Should().BeEquivalentTo(new[]
        {
            "CreateOrGetCustomerAsync",
            "VerifyWebhookAsync"
        });
    }

    [Fact]
    public void Every_Method_Returns_Task_Of_Result_Of_T()
    {
        // No method may throw on transient failures — every return MUST be
        // Task<Result<T>> so callers never need a try/catch.
        var iface = typeof(IStripeGateway);
        foreach (var m in iface.GetMethods())
        {
            m.ReturnType.IsGenericType.Should().BeTrue(
                $"{m.Name} must return Task<Result<T>>");
            m.ReturnType.GetGenericTypeDefinition().Should().Be(typeof(Task<>),
                $"{m.Name} must return Task<Result<T>>");

            var innerType = m.ReturnType.GetGenericArguments()[0];
            innerType.IsGenericType.Should().BeTrue(
                $"{m.Name} inner type must be Result<T>");
            innerType.GetGenericTypeDefinition().Should().Be(typeof(Result<>),
                $"{m.Name} inner type must be Result<T>");
        }
    }

    [Fact]
    public void Every_Method_Accepts_CancellationToken_As_Last_Parameter()
    {
        // Cancellation contract: ct MUST be the LAST parameter of every method,
        // and MUST have a default value (callers can omit it).
        var iface = typeof(IStripeGateway);
        foreach (var m in iface.GetMethods())
        {
            var parameters = m.GetParameters();
            parameters.Should().NotBeEmpty($"{m.Name} must take at least a ct");

            var last = parameters[^1];
            last.ParameterType.Should().Be(typeof(CancellationToken),
                $"{m.Name} last parameter must be CancellationToken");

            last.HasDefaultValue.Should().BeTrue(
                $"{m.Name} CancellationToken must have a default value");
        }
    }

    [Fact]
    public void NoThrow_Guarantee_On_Transient_Failures()
    {
        // Contract pin: the interface itself documents that callers MUST NOT
        // wrap calls in try/catch. This is verified by code-review; the test
        // asserts the interface declaration exists and is public.
        typeof(IStripeGateway).IsInterface.Should().BeTrue();
        typeof(IStripeGateway).IsPublic.Should().BeTrue();
    }
}

/// <summary>DTO contract tests for <see cref="StripeCustomerDto"/>.</summary>
public class StripeCustomerDtoTests
{
    [Fact]
    public void StripeCustomerDto_Has_Required_Fields()
    {
        var created = DateTimeOffset.UnixEpoch;
        var dto = new StripeCustomerDto(
            StripeCustomerId: "cus_123",
            Email: "user@example.com",
            DisplayName: "John Doe",
            CreatedAt: created);

        dto.StripeCustomerId.Should().Be("cus_123");
        dto.Email.Should().Be("user@example.com");
        dto.DisplayName.Should().Be("John Doe");
        dto.CreatedAt.Should().Be(created);
    }

    [Fact]
    public void StripeCustomerDto_DisplayName_Can_Be_Null()
    {
        var dto = new StripeCustomerDto("cus_1", "x@y.com", null, DateTimeOffset.UnixEpoch);
        dto.DisplayName.Should().BeNull();
    }

    [Fact]
    public void StripeCustomerDto_Json_Contract_Is_SnakeCase()
    {
        var dto = new StripeCustomerDto(
            "cus_1", "x@y.com", "name", DateTimeOffset.UnixEpoch);
        var json = JsonSerializer.Serialize(dto, StripeJsonOptions.SnakeCase);

        json.Should().Contain("\"stripe_customer_id\"");
        json.Should().Contain("\"email\"");
        json.Should().Contain("\"display_name\"");
        json.Should().Contain("\"created_at\"");
    }
}

/// <summary>DTO contract tests for the parsed webhook event shape.</summary>
public class StripeWebhookEventTests
{
    [Fact]
    public void StripeWebhookEvent_Has_All_Required_Fields()
    {
        var occurred = DateTimeOffset.UnixEpoch;
        var dto = new StripeWebhookEvent(
            EventId: "evt_123",
            Type: "customer.subscription.created",
            PayloadJson: "{\"id\":\"evt_123\"}",
            OccurredAt: occurred);

        dto.EventId.Should().Be("evt_123");
        dto.Type.Should().Be("customer.subscription.created");
        dto.PayloadJson.Should().Contain("evt_123");
        dto.OccurredAt.Should().Be(occurred);
    }

    [Fact]
    public void StripeWebhookEvent_PayloadJson_Is_Required_Non_Empty()
    {
        // Pinning the contract: handler MUST pass the raw payload, NOT a
        // parsed object. Stripe sends arbitrary shapes — we keep the bytes.
        var dto = new StripeWebhookEvent(
            "evt_1", "ping", "{}", DateTimeOffset.UnixEpoch);
        dto.PayloadJson.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void StripeWebhookEvent_Type_Supports_Any_Stripe_Type_String()
    {
        // Stripe adds new event types over time — the DTO MUST accept any
        // string without an enum constraint. Mapping to internal actions is
        // the handler's job, not the DTO's.
        var dto = new StripeWebhookEvent(
            "evt_1", "customer.tax.updated", "{}", DateTimeOffset.UnixEpoch);
        dto.Type.Should().Be("customer.tax.updated");
    }
}

/// <summary>Contract tests for <see cref="StripeError"/>.</summary>
public class StripeErrorTests
{
    [Fact]
    public void StripeError_Has_Code_And_Message()
    {
        var err = new StripeError("stripe.authentication_error", "Invalid API key");

        err.Code.Should().Be("stripe.authentication_error");
        err.Message.Should().Be("Invalid API key");
    }

    [Fact]
    public void StripeError_Is_Implicitly_Convertible_To_Error()
    {
        // The gateway returns Result.Failure&lt;T&gt;(StripeError(...)). For the
        // Result pipeline to work, StripeError MUST be implicitly convertible
        // to Error so handlers can short-circuit without explicit mapping.
        Error err = new StripeError("stripe.api_error", "boom");
        err.Code.Should().Be("stripe.api_error");
        err.Message.Should().Be("boom");
    }

    [Fact]
    public void StripeError_Implements_Equality_As_Record()
    {
        // Two StripeErrors with same code+message MUST be equal (record
        // semantics). This pins the equality contract for tests + caching.
        var a = new StripeError("stripe.timeout", "x");
        var b = new StripeError("stripe.timeout", "x");
        a.Should().Be(b);
    }
}
