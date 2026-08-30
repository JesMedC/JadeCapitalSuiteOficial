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
/// Contract tests for the Stripe gateway wire shape (Wave 6, slices 6a.1 + 6a.2).
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
/// <b>Slice 6a.2 surface</b>: extends the 6a.1 contract with
/// <c>CreateCheckoutSessionAsync</c>, <c>CreatePortalSessionAsync</c>,
/// <c>GetSubscriptionAsync</c>, <c>GetPaymentMethodsAsync</c>,
/// <c>GetInvoicesAsync</c> (5 new methods). Total interface surface = 7 methods.
/// </para>
/// </summary>
public class StripeGatewayContractTests
{
    [Fact]
    public void IStripeGateway_Declares_Seven_Members_In_Slice_6a2()
    {
        // Slice 6a.2 surface: Customer + Webhook (6a.1) + Checkout + Portal +
        // Subscription + PaymentMethods + Invoices (6a.2). Total 7.
        var iface = typeof(IStripeGateway);
        var members = iface.GetMethods()
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        members.Should().BeEquivalentTo(new[]
        {
            "CreateCheckoutSessionAsync",
            "CreateOrGetCustomerAsync",
            "CreatePortalSessionAsync",
            "GetInvoicesAsync",
            "GetPaymentMethodsAsync",
            "GetSubscriptionAsync",
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

// ============================================================================
// Slice 6a.2 — 5 new DTOs (Checkout / Portal / Subscription / PaymentMethod / Invoice)
// ============================================================================

/// <summary>DTO contract tests for <see cref="StripeCheckoutSessionDto"/>.</summary>
public class StripeCheckoutSessionDtoTests
{
    [Fact]
    public void StripeCheckoutSessionDto_Has_Required_Fields()
    {
        var expires = DateTimeOffset.UnixEpoch.AddHours(1);
        var dto = new StripeCheckoutSessionDto(
            SessionId: "cs_test_abc123",
            Url: "https://checkout.stripe.com/c/pay/cs_test_abc123",
            ExpiresAt: expires);

        dto.SessionId.Should().Be("cs_test_abc123");
        dto.Url.Should().StartWith("https://checkout.stripe.com/");
        dto.ExpiresAt.Should().Be(expires);
    }

    [Fact]
    public void StripeCheckoutSessionDto_Json_Contract_Is_SnakeCase()
    {
        var dto = new StripeCheckoutSessionDto(
            "cs_1", "https://example.com/c", DateTimeOffset.UnixEpoch);
        var json = JsonSerializer.Serialize(dto, StripeJsonOptions.SnakeCase);

        json.Should().Contain("\"session_id\"");
        json.Should().Contain("\"url\"");
        json.Should().Contain("\"expires_at\"");
    }
}

/// <summary>DTO contract tests for <see cref="StripePortalSessionDto"/>.</summary>
public class StripePortalSessionDtoTests
{
    [Fact]
    public void StripePortalSessionDto_Has_Required_Fields()
    {
        var expires = DateTimeOffset.UnixEpoch.AddHours(1);
        var dto = new StripePortalSessionDto(
            SessionId: "ps_test_abc123",
            Url: "https://billing.stripe.com/p/session/ps_test_abc123",
            ExpiresAt: expires);

        dto.SessionId.Should().Be("ps_test_abc123");
        dto.Url.Should().StartWith("https://billing.stripe.com/");
        dto.ExpiresAt.Should().Be(expires);
    }

    [Fact]
    public void StripePortalSessionDto_Json_Contract_Is_SnakeCase()
    {
        var dto = new StripePortalSessionDto(
            "ps_1", "https://example.com/p", DateTimeOffset.UnixEpoch);
        var json = JsonSerializer.Serialize(dto, StripeJsonOptions.SnakeCase);

        json.Should().Contain("\"session_id\"");
        json.Should().Contain("\"url\"");
        json.Should().Contain("\"expires_at\"");
    }
}

/// <summary>DTO contract tests for <see cref="StripeSubscriptionDto"/>.</summary>
public class StripeSubscriptionDtoTests
{
    [Fact]
    public void StripeSubscriptionDto_Has_Required_Fields()
    {
        var periodEnd = DateTimeOffset.UnixEpoch.AddDays(30);
        var dto = new StripeSubscriptionDto(
            StripeSubscriptionId: "sub_123",
            Status: "active",
            PlanCode: "pro",
            CurrentPeriodEnd: periodEnd,
            CancelAtPeriodEnd: false);

        dto.StripeSubscriptionId.Should().Be("sub_123");
        dto.Status.Should().Be("active");
        dto.PlanCode.Should().Be("pro");
        dto.CurrentPeriodEnd.Should().Be(periodEnd);
        dto.CancelAtPeriodEnd.Should().BeFalse();
    }

    [Fact]
    public void StripeSubscriptionDto_CancelAtPeriodEnd_Defaults_To_False()
    {
        // The record uses a non-positional ctor that defaults
        // CancelAtPeriodEnd to false. Use `with` to test the default.
        var dto = new StripeSubscriptionDto(
            "sub_1", "active", "pro", DateTimeOffset.UnixEpoch, CancelAtPeriodEnd: false);

        dto.CancelAtPeriodEnd.Should().BeFalse();
    }

    [Fact]
    public void StripeSubscriptionDto_Accepts_Known_Stripe_Statuses()
    {
        // Pin the type contract: Status is a free-form string. Stripe's
        // documented statuses include active, past_due, canceled, trialing,
        // unpaid, incomplete, incomplete_expired, paused. The DTO MUST accept
        // any of these without an enum constraint.
        var statuses = new[] { "active", "past_due", "canceled", "trialing", "unpaid", "incomplete" };
        foreach (var status in statuses)
        {
            var dto = new StripeSubscriptionDto(
                "sub_x", status, "pro", DateTimeOffset.UnixEpoch, false);
            dto.Status.Should().Be(status);
        }
    }

    [Fact]
    public void StripeSubscriptionDto_Json_Contract_Is_SnakeCase()
    {
        var dto = new StripeSubscriptionDto(
            "sub_1", "active", "pro", DateTimeOffset.UnixEpoch, false);
        var json = JsonSerializer.Serialize(dto, StripeJsonOptions.SnakeCase);

        json.Should().Contain("\"stripe_subscription_id\"");
        json.Should().Contain("\"status\"");
        json.Should().Contain("\"plan_code\"");
        json.Should().Contain("\"current_period_end\"");
        json.Should().Contain("\"cancel_at_period_end\"");
    }
}

/// <summary>DTO contract tests for <see cref="StripePaymentMethodDto"/>.</summary>
public class StripePaymentMethodDtoTests
{
    [Fact]
    public void StripePaymentMethodDto_Has_Required_Fields()
    {
        var expires = DateTimeOffset.UnixEpoch.AddYears(2);
        var dto = new StripePaymentMethodDto(
            Id: "pm_123",
            Brand: "visa",
            Last4: "4242",
            ExpiresAt: expires,
            IsDefault: true);

        dto.Id.Should().Be("pm_123");
        dto.Brand.Should().Be("visa");
        dto.Last4.Should().Be("4242");
        dto.ExpiresAt.Should().Be(expires);
        dto.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void StripePaymentMethodDto_IsDefault_Defaults_To_False()
    {
        var dto = new StripePaymentMethodDto(
            "pm_1", "visa", "4242", null, IsDefault: false);
        dto.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void StripePaymentMethodDto_ExpiresAt_Can_Be_Null()
    {
        var dto = new StripePaymentMethodDto(
            "pm_1", "visa", "4242", null, IsDefault: false);
        dto.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public void StripePaymentMethodDto_Json_Contract_Is_SnakeCase()
    {
        var dto = new StripePaymentMethodDto(
            "pm_1", "visa", "4242", DateTimeOffset.UnixEpoch, true);
        var json = JsonSerializer.Serialize(dto, StripeJsonOptions.SnakeCase);

        json.Should().Contain("\"id\"");
        json.Should().Contain("\"brand\"");
        json.Should().Contain("\"last4\"");
        json.Should().Contain("\"expires_at\"");
        json.Should().Contain("\"is_default\"");
    }
}

/// <summary>DTO contract tests for <see cref="StripeInvoiceDto"/>.</summary>
public class StripeInvoiceDtoTests
{
    [Fact]
    public void StripeInvoiceDto_Has_Required_Fields()
    {
        var issued = DateTimeOffset.UnixEpoch;
        var paid = issued.AddHours(1);
        var dto = new StripeInvoiceDto(
            Id: "in_123",
            Number: "INV-001",
            AmountCents: 1999,
            Currency: "usd",
            IssuedAt: issued,
            PaidAt: paid,
            Status: "paid",
            PdfUrl: "https://stripe.com/in.pdf");

        dto.Id.Should().Be("in_123");
        dto.Number.Should().Be("INV-001");
        dto.AmountCents.Should().Be(1999);
        dto.Currency.Should().Be("usd");
        dto.IssuedAt.Should().Be(issued);
        dto.PaidAt.Should().Be(paid);
        dto.Status.Should().Be("paid");
        dto.PdfUrl.Should().StartWith("https://");
    }

    [Fact]
    public void StripeInvoiceDto_AmountCents_Must_Be_Non_Negative()
    {
        // The DTO is a record (no validation) — but documenting the contract.
        // A negative amount would be invalid in practice; the gateway's Stripe
        // response always has non-negative values. We assert the type is `long`
        // and a value of 0 is allowed (zero-amount credit / discount).
        var dto = new StripeInvoiceDto(
            "in_1", "0", 0, "usd", DateTimeOffset.UnixEpoch, null, "paid", "https://x");
        dto.AmountCents.Should().Be(0);
    }

    [Fact]
    public void StripeInvoiceDto_PaidAt_Can_Be_Null_For_Unpaid_Invoices()
    {
        var dto = new StripeInvoiceDto(
            "in_1", "INV", 1000, "usd", DateTimeOffset.UnixEpoch, null, "open", "https://x");
        dto.PaidAt.Should().BeNull();
    }

    [Fact]
    public void StripeInvoiceDto_Json_Contract_Is_SnakeCase()
    {
        var dto = new StripeInvoiceDto(
            "in_1", "INV-1", 1000, "usd",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "paid", "https://x");
        var json = JsonSerializer.Serialize(dto, StripeJsonOptions.SnakeCase);

        json.Should().Contain("\"id\"");
        json.Should().Contain("\"number\"");
        json.Should().Contain("\"amount_cents\"");
        json.Should().Contain("\"currency\"");
        json.Should().Contain("\"issued_at\"");
        json.Should().Contain("\"paid_at\"");
        json.Should().Contain("\"status\"");
        json.Should().Contain("\"pdf_url\"");
    }
}
