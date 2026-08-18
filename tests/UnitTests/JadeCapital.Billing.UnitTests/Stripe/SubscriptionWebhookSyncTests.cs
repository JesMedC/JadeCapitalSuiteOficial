using JadeCapital.Billing.Domain.Stripe;
using JadeCapital.Billing.Domain.Subscriptions;
using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Shared.Kernel.Stripe;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Billing.UnitTests.Stripe;

/// <summary>
/// Domain tests for <see cref="SubscriptionWebhookSync"/> (Wave 6, slice 6a.2).
///
/// <para>
/// Maps Stripe's free-form <c>status</c> string to our internal
/// <see cref="SubscriptionStatus"/> enum. The mapping is a domain invariant
/// because status transitions affect billing, entitlement, and audit —
/// getting it wrong is a compliance issue.
/// </para>
///
/// <para>
/// <b>RED-first contract</b>:
/// <list type="bullet">
///   <item><c>"active"</c> → <see cref="SubscriptionStatus.Active"/>.</item>
///   <item><c>"past_due"</c> → <see cref="SubscriptionStatus.PastDue"/>.</item>
///   <item><c>"canceled"</c> → <see cref="SubscriptionStatus.Cancelled"/>.</item>
///   <item><c>"trialing"</c> → <see cref="SubscriptionStatus.Trial"/>.</item>
///   <item><c>"unpaid"</c> → <see cref="SubscriptionStatus.PastDue"/> (defensive
///   — unpaid is more like past_due than Active from the trader's POV).</item>
///   <item>unknown status → Result.Failure with
///   <c>validation.stripe.subscription.unknown_status</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why a new PastDue enum value</b>: the existing
/// <see cref="SubscriptionStatus"/> is {Active, Trial, Cancelled, Expired}.
/// Stripe's <c>past_due</c> and <c>unpaid</c> statuses need a new value
/// to model "not cancelled but the payment failed". We extend the enum
/// (additive change — no migration needed since status is stored as string).
/// </para>
/// </summary>
public class SubscriptionWebhookSyncTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Subscription NewSubscription(SubscriptionStatus status = SubscriptionStatus.Active)
    {
        var plan = Plan.Create(
            Guid.NewGuid(),
            PlanCode.FromTrusted("pro"),
            "Pro",
            Money.FromTrusted(1999m, "USD"),
            isEligibleForSelfService: true).Value;
        var r = Subscription.Create(
            Guid.NewGuid(), UserId, plan, status, null, DateTimeOffset.UtcNow);
        r.IsSuccess.Should().BeTrue();
        return r.Value;
    }

    private static StripeSubscriptionDto NewStripeSub(
        string status = "active",
        bool cancelAtPeriodEnd = false,
        string planCode = "pro",
        DateTimeOffset? periodEnd = null)
    {
        return new StripeSubscriptionDto(
            StripeSubscriptionId: "sub_123",
            Status: status,
            PlanCode: planCode,
            CurrentPeriodEnd: periodEnd ?? DateTimeOffset.UtcNow.AddDays(30),
            CancelAtPeriodEnd: cancelAtPeriodEnd);
    }

    [Fact]
    public void Apply_Active_Stripe_Status_Maps_To_Active()
    {
        var sub = NewSubscription();
        var result = SubscriptionWebhookSync.ApplyToSubscription(
            sub, NewStripeSub(status: "active"), new SystemClock());

        result.IsSuccess.Should().BeTrue();
        sub.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public void Apply_PastDue_Stripe_Status_Maps_To_PastDue()
    {
        var sub = NewSubscription();
        var result = SubscriptionWebhookSync.ApplyToSubscription(
            sub, NewStripeSub(status: "past_due"), new SystemClock());

        result.IsSuccess.Should().BeTrue();
        sub.Status.Should().Be(SubscriptionStatus.PastDue);
    }

    [Fact]
    public void Apply_Canceled_Stripe_Status_Maps_To_Cancelled()
    {
        var sub = NewSubscription();
        var result = SubscriptionWebhookSync.ApplyToSubscription(
            sub, NewStripeSub(status: "canceled"), new SystemClock());

        result.IsSuccess.Should().BeTrue();
        sub.Status.Should().Be(SubscriptionStatus.Cancelled);
    }

    [Fact]
    public void Apply_Trialing_Stripe_Status_Maps_To_Trial()
    {
        var sub = NewSubscription();
        var result = SubscriptionWebhookSync.ApplyToSubscription(
            sub, NewStripeSub(status: "trialing"), new SystemClock());

        result.IsSuccess.Should().BeTrue();
        sub.Status.Should().Be(SubscriptionStatus.Trial);
    }

    [Fact]
    public void Apply_Unpaid_Stripe_Status_Maps_To_PastDue_Defensively()
    {
        // "unpaid" doesn't perfectly map to past_due (it's a separate state
        // in Stripe) but from a billing perspective the trader's subscription
        // is no longer paying. We funnel both to PastDue to avoid a new enum
        // value in 6a.2 — distinguishing unpaid from past_due is a UI
        // concern, not a billing-domain concern.
        var sub = NewSubscription();
        var result = SubscriptionWebhookSync.ApplyToSubscription(
            sub, NewStripeSub(status: "unpaid"), new SystemClock());

        result.IsSuccess.Should().BeTrue();
        sub.Status.Should().Be(SubscriptionStatus.PastDue);
    }

    [Fact]
    public void Apply_Unknown_Stripe_Status_Returns_Failure()
    {
        var sub = NewSubscription();
        var result = SubscriptionWebhookSync.ApplyToSubscription(
            sub, NewStripeSub(status: "frobnicate"), new SystemClock());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.stripe.subscription.unknown_status");
    }

    [Fact]
    public void Apply_Updates_StripeSubscriptionId_On_Subscription()
    {
        var sub = NewSubscription();
        sub.StripeSubscriptionId.Should().BeNull(
            "subscription starts without a Stripe mapping until the first webhook");

        var stripe = NewStripeSub(status: "active");
        var result = SubscriptionWebhookSync.ApplyToSubscription(
            sub, stripe, new SystemClock());

        result.IsSuccess.Should().BeTrue();
        sub.StripeSubscriptionId.Should().Be("sub_123");
    }

    [Fact]
    public void Apply_Emits_History_Entry_When_Status_Changes()
    {
        var sub = NewSubscription(SubscriptionStatus.Trial);
        var beforeHistoryCount = sub.History.Count;

        var result = SubscriptionWebhookSync.ApplyToSubscription(
            sub, NewStripeSub(status: "active"), new SystemClock());

        result.IsSuccess.Should().BeTrue();
        sub.History.Count.Should().Be(beforeHistoryCount + 1,
            "status change must append a history entry for auditability");
    }

    [Fact]
    public void Apply_No_History_Entry_When_Status_Is_Same()
    {
        var sub = NewSubscription(SubscriptionStatus.Active);
        var beforeHistoryCount = sub.History.Count;

        var result = SubscriptionWebhookSync.ApplyToSubscription(
            sub, NewStripeSub(status: "active"), new SystemClock());

        result.IsSuccess.Should().BeTrue();
        sub.History.Count.Should().Be(beforeHistoryCount,
            "no-op transitions MUST NOT append history (consistent with ChangeTier)");
    }
}
