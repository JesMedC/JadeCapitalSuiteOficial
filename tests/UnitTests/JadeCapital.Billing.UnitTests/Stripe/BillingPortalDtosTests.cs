using FluentAssertions;
using JadeCapital.Billing.Contracts.Portal;
using System.Text.Json;
using Xunit;

namespace JadeCapital.Billing.UnitTests.Stripe;

/// <summary>
/// Contract tests for the FE-facing portal DTOs (Wave 6, slice 6b.1).
///
/// <para>
/// Pins the wire shape that the FE (slice 6b.2) and any external consumer
/// depend on. JSON serialization uses
/// <see cref="JsonNamingPolicy.SnakeCaseLower"/> so the FE receives
/// <c>stripe_subscription_id</c>, <c>current_period_end</c>,
/// <c>cancel_at_period_end</c>, etc. Changing the DTO property names or the
/// JSON naming policy is a breaking change.
/// </para>
///
/// <para>
/// These DTOs are distinct from the upstream
/// <c>JadeCapital.Shared.Kernel.Stripe.Stripe*</c> DTOs: they are the
/// FE-facing subset that the portal renders. Internal-only fields
/// (Stripe metadata, version tokens, raw status enums) stay in the
/// upstream wire DTOs and are not exposed.
/// </para>
/// </summary>
public class BillingPortalDtosTests
{
    private static readonly JsonSerializerOptions SnakeCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    // ============================================
    // BillingPortalSubscriptionDto
    // ============================================

    [Fact]
    public void BillingPortalSubscriptionDto_Has_All_Required_Fields()
    {
        var periodEnd = DateTimeOffset.UnixEpoch.AddDays(30);

        var dto = new BillingPortalSubscriptionDto(
            StripeSubscriptionId: "sub_1",
            Status: "active",
            PlanCode: "pro",
            CurrentPeriodEnd: periodEnd,
            CancelAtPeriodEnd: false);

        dto.StripeSubscriptionId.Should().Be("sub_1");
        dto.Status.Should().Be("active");
        dto.PlanCode.Should().Be("pro");
        dto.CurrentPeriodEnd.Should().Be(periodEnd);
        dto.CancelAtPeriodEnd.Should().BeFalse();
    }

    [Fact]
    public void BillingPortalSubscriptionDto_Json_Contract_Is_SnakeCase()
    {
        var dto = new BillingPortalSubscriptionDto(
            StripeSubscriptionId: "sub_1",
            Status: "active",
            PlanCode: "pro",
            CurrentPeriodEnd: DateTimeOffset.UnixEpoch,
            CancelAtPeriodEnd: true);

        var json = JsonSerializer.Serialize(dto, SnakeCase);

        json.Should().Contain("\"stripe_subscription_id\"");
        json.Should().Contain("\"status\"");
        json.Should().Contain("\"plan_code\"");
        json.Should().Contain("\"current_period_end\"");
        json.Should().Contain("\"cancel_at_period_end\"");
    }

    // ============================================
    // BillingPortalPaymentMethodDto
    // ============================================

    [Fact]
    public void BillingPortalPaymentMethodDto_ExpiresAt_Can_Be_Null()
    {
        // Stripe payment methods without expiry (SEPA debit, bank transfer)
        // must be representable.
        var dto = new BillingPortalPaymentMethodDto(
            Id: "pm_1",
            Brand: "sepa_debit",
            Last4: "1234",
            ExpiresAt: null,
            IsDefault: true);

        dto.ExpiresAt.Should().BeNull();
        dto.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void BillingPortalPaymentMethodDto_Json_Contract_Is_SnakeCase()
    {
        var dto = new BillingPortalPaymentMethodDto(
            Id: "pm_1",
            Brand: "visa",
            Last4: "4242",
            ExpiresAt: DateTimeOffset.UnixEpoch.AddYears(2),
            IsDefault: true);

        var json = JsonSerializer.Serialize(dto, SnakeCase);

        json.Should().Contain("\"id\"");
        json.Should().Contain("\"brand\"");
        json.Should().Contain("\"last4\"");
        json.Should().Contain("\"expires_at\"");
        json.Should().Contain("\"is_default\"");
    }

    // ============================================
    // BillingPortalInvoiceDto
    // ============================================

    [Fact]
    public void BillingPortalInvoiceDto_PaidAt_Can_Be_Null_For_Unpaid()
    {
        // Unpaid invoices (status open/draft) have no PaidAt.
        var dto = new BillingPortalInvoiceDto(
            Id: "in_1",
            Number: "INV-001",
            AmountCents: 1999,
            Currency: "usd",
            IssuedAt: DateTimeOffset.UnixEpoch,
            PaidAt: null,
            Status: "open",
            PdfUrl: "https://stripe.com/inv/1.pdf");

        dto.PaidAt.Should().BeNull();
        dto.Status.Should().Be("open");
    }

    [Fact]
    public void BillingPortalInvoiceDto_AmountCents_Allows_Non_Negative()
    {
        // Stripe's invoice amount_due is always >= 0 (Stripe clamps).
        var dto = new BillingPortalInvoiceDto(
            Id: "in_1",
            Number: "INV-001",
            AmountCents: 0,
            Currency: "usd",
            IssuedAt: DateTimeOffset.UnixEpoch,
            PaidAt: null,
            Status: "open",
            PdfUrl: "https://stripe.com/inv/1.pdf");

        dto.AmountCents.Should().Be(0);
    }

    [Fact]
    public void BillingPortalInvoiceDto_Json_Contract_Is_SnakeCase()
    {
        var dto = new BillingPortalInvoiceDto(
            Id: "in_1",
            Number: "INV-001",
            AmountCents: 1999,
            Currency: "usd",
            IssuedAt: DateTimeOffset.UnixEpoch,
            PaidAt: DateTimeOffset.UnixEpoch.AddHours(1),
            Status: "paid",
            PdfUrl: "https://stripe.com/inv/1.pdf");

        var json = JsonSerializer.Serialize(dto, SnakeCase);

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
