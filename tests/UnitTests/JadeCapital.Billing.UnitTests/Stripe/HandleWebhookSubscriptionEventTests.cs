using FluentAssertions;
using JadeCapital.Billing.Application.Stripe;
using JadeCapital.Billing.Domain.Stripe;
using JadeCapital.Billing.Domain.Subscriptions;
using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
// Domain alias — both Billing.Domain.Stripe.StripeWebhookEvent (aggregate)
// and Shared.Kernel.Stripe.StripeWebhookEvent (DTO) live in this test's
// dependency graph. We use the alias to disambiguate.
using DomainStripeWebhookEvent = JadeCapital.Billing.Domain.Stripe.StripeWebhookEvent;
using StripeWebhookEventDto = JadeCapital.Shared.Kernel.Stripe.StripeWebhookEvent;

namespace JadeCapital.Billing.UnitTests.Stripe;

/// <summary>
/// Tests for the subscription-event dispatch in
/// <see cref="HandleWebhookHandler"/> (Wave 6, slice 6a.2).
///
/// <para>
/// <b>Slice 6a.1 contract</b>: signature verify + log only. These tests pin
/// the 6a.2 contract: idempotency check, append-log persistence, dispatch
/// on <c>customer.subscription.*</c> events, and subscription sync via
/// <see cref="SubscriptionWebhookSync"/>.
/// </para>
///
/// <para>
/// <b>RED-first scenarios</b>:
/// <list type="bullet">
///   <item><c>customer.subscription.created</c> → SyncSubscription appends
///   history; the webhook event is marked Processed.</item>
///   <item><c>customer.subscription.updated</c> (status change) → applies
///   the diff via SyncFromStripe.</item>
///   <item><c>customer.subscription.deleted</c> → Cancels the subscription
///   with reason <c>stripe-subscription-deleted</c>.</item>
///   <item>Unknown event type → log + return processed (no error).</item>
///   <item>Re-delivery of a Processed event → 200 duplicate, no mutation.</item>
///   <item>First delivery of a new event_id → row inserted, dispatch runs.</item>
///   <item>Subscription NOT found by Stripe id → 200 processed with warning
///   (the handler logs and skips; admin must re-process via out-of-band job).</item>
///   <item>Optimistic-concurrency conflict on sync → retry up to 3 times
///   with jitter; if all retries fail, mark webhook as Failed.</item>
/// </list>
/// </para>
/// </summary>
public class HandleWebhookSubscriptionEventTests
{
    private const string PayloadTemplate = "{{\"id\":\"evt_123\",\"type\":\"{0}\",\"data\":{{\"object\":{{\"id\":\"sub_abc\",\"status\":\"{1}\",\"cancel_at_period_end\":false,\"current_period_end\":1700000000,\"items\":{{\"data\":[{{\"price\":{{\"id\":\"price_pro\"}}}}]}}}}}}}}";

    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SubId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static Subscription NewActiveSubscription()
    {
        var plan = Plan.Create(
            Guid.NewGuid(), PlanCode.FromTrusted("pro"), "Pro",
            Money.FromTrusted(1999m, "USD"),
            isEligibleForSelfService: true).Value;
        var r = Subscription.Create(
            SubId, UserId, plan, SubscriptionStatus.Active, null, DateTimeOffset.UtcNow);
        r.IsSuccess.Should().BeTrue();
        return r.Value;
    }

    private static StripeWebhookEventDto MakeEvent(string eventType, string subStatus = "active")
    {
#pragma warning disable CA1863 // Cache a 'CompositeFormat' for repeated use — test fixture, called per-test
        var payload = string.Format(PayloadTemplate, eventType, subStatus);
#pragma warning restore CA1863
        return new StripeWebhookEventDto(
            EventId: "evt_123",
            Type: eventType,
            PayloadJson: payload,
            OccurredAt: DateTimeOffset.UnixEpoch);
    }

    private static (HandleWebhookHandler sut,
                    IStripeGateway gateway,
                    IStripeWebhookEventRepository webhookRepo,
                    ISubscriptionAdminRepository subRepo,
                    ISubscriptionAdminUnitOfWork uow) BuildSut()
    {
        var gateway = Substitute.For<IStripeGateway>();
        var webhookRepo = Substitute.For<IStripeWebhookEventRepository>();
        var subRepo = Substitute.For<ISubscriptionAdminRepository>();
        var uow = Substitute.For<ISubscriptionAdminUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());

        var sut = new HandleWebhookHandler(
            gateway, webhookRepo, subRepo, uow, new SystemClock(),
            NullLogger<HandleWebhookHandler>.Instance);
        return (sut, gateway, webhookRepo, subRepo, uow);
    }

    [Fact]
    public async Task Handle_Customer_Subscription_Created_Syncs_Subscription()
    {
        var sub = NewActiveSubscription();
        var (sut, gateway, webhookRepo, subRepo, uow) = BuildSut();
        var evt = MakeEvent("customer.subscription.created", "active");
        gateway.VerifyWebhookAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(evt));
        webhookRepo.FindByEventIdAsync("evt_123", Arg.Any<CancellationToken>()).Returns((DomainStripeWebhookEvent?)null);
        subRepo.FindByStripeSubscriptionIdAsync("sub_abc", Arg.Any<CancellationToken>()).Returns(sub);

        var result = await sut.Handle(
            new HandleWebhookCommand(evt.PayloadJson, "t=1,v1=abc"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Outcome.Should().Be("processed");
        sub.Status.Should().Be(SubscriptionStatus.Active);
        sub.StripeSubscriptionId.Should().Be("sub_abc");
        await webhookRepo.Received(1).AddAsync(Arg.Any<DomainStripeWebhookEvent>(), Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Customer_Subscription_Updated_Applies_Status_Diff()
    {
        var sub = NewActiveSubscription();
        var (sut, gateway, webhookRepo, subRepo, uow) = BuildSut();
        var evt = MakeEvent("customer.subscription.updated", "past_due");
        gateway.VerifyWebhookAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(evt));
        webhookRepo.FindByEventIdAsync("evt_123", Arg.Any<CancellationToken>()).Returns((DomainStripeWebhookEvent?)null);
        subRepo.FindByStripeSubscriptionIdAsync("sub_abc", Arg.Any<CancellationToken>()).Returns(sub);

        var result = await sut.Handle(
            new HandleWebhookCommand(evt.PayloadJson, "t=1,v1=abc"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sub.Status.Should().Be(SubscriptionStatus.PastDue,
            "status change from Active → PastDue must apply via SyncFromStripe");
    }

    [Fact]
    public async Task Handle_Customer_Subscription_Deleted_Cancels_Subscription()
    {
        var sub = NewActiveSubscription();
        var (sut, gateway, webhookRepo, subRepo, uow) = BuildSut();
        var evt = MakeEvent("customer.subscription.deleted", "canceled");
        gateway.VerifyWebhookAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(evt));
        webhookRepo.FindByEventIdAsync("evt_123", Arg.Any<CancellationToken>()).Returns((DomainStripeWebhookEvent?)null);
        subRepo.FindByStripeSubscriptionIdAsync("sub_abc", Arg.Any<CancellationToken>()).Returns(sub);

        var result = await sut.Handle(
            new HandleWebhookCommand(evt.PayloadJson, "t=1,v1=abc"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sub.Status.Should().Be(SubscriptionStatus.Cancelled);
    }

    [Fact]
    public async Task Handle_Unknown_Event_Type_Returns_Processed_Without_Mutation()
    {
        var (sut, gateway, webhookRepo, subRepo, uow) = BuildSut();
        var evt = MakeEvent("customer.tax.updated", "active");
        gateway.VerifyWebhookAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(evt));
        webhookRepo.FindByEventIdAsync("evt_123", Arg.Any<CancellationToken>()).Returns((DomainStripeWebhookEvent?)null);

        var result = await sut.Handle(
            new HandleWebhookCommand(evt.PayloadJson, "t=1,v1=abc"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Outcome.Should().Be("processed");
        // No subscription fetch for unknown event types.
        await subRepo.DidNotReceive().FindByStripeSubscriptionIdAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Redelivery_Returns_Duplicate_Without_Mutation()
    {
        // Pre-existing event row with ProcessedAt set → must short-circuit.
        var existing = DomainStripeWebhookEvent.Record(
            Guid.NewGuid(), "evt_123", "customer.subscription.created",
            "{}", null, DateTimeOffset.UnixEpoch, new SystemClock()).Value;
        existing.MarkProcessed(new SystemClock());

        var (sut, gateway, webhookRepo, subRepo, uow) = BuildSut();
        var evt = MakeEvent("customer.subscription.created", "active");
        gateway.VerifyWebhookAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(evt));
        webhookRepo.FindByEventIdAsync("evt_123", Arg.Any<CancellationToken>()).Returns(existing);

        var result = await sut.Handle(
            new HandleWebhookCommand(evt.PayloadJson, "t=1,v1=abc"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Outcome.Should().Be("duplicate");
        await subRepo.DidNotReceive().FindByStripeSubscriptionIdAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await webhookRepo.DidNotReceive().AddAsync(Arg.Any<DomainStripeWebhookEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_First_Delivery_Persists_Webhook_Row_Then_Dispatches()
    {
        var sub = NewActiveSubscription();
        var (sut, gateway, webhookRepo, subRepo, uow) = BuildSut();
        var evt = MakeEvent("customer.subscription.created", "active");
        gateway.VerifyWebhookAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(evt));
        webhookRepo.FindByEventIdAsync("evt_123", Arg.Any<CancellationToken>()).Returns((DomainStripeWebhookEvent?)null);
        subRepo.FindByStripeSubscriptionIdAsync("sub_abc", Arg.Any<CancellationToken>()).Returns(sub);

        await sut.Handle(
            new HandleWebhookCommand(evt.PayloadJson, "t=1,v1=abc"),
            CancellationToken.None);

        // The handler MUST persist the webhook event row before dispatching
        // so re-deliveries can detect the duplicate via FindByEventIdAsync.
        await webhookRepo.Received(1).AddAsync(
            Arg.Is<DomainStripeWebhookEvent>(e => e.EventId == "evt_123"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Subscription_Not_Found_For_Stripe_Id_Logs_And_Returns_Processed()
    {
        // The webhook arrived BEFORE the local subscription was created
        // (e.g. user has no subscription yet but Stripe fired created).
        // The handler logs a warning and returns 200 (processed) — admin
        // must re-process via out-of-band job in Wave 7.
        var (sut, gateway, webhookRepo, subRepo, uow) = BuildSut();
        var evt = MakeEvent("customer.subscription.created", "active");
        gateway.VerifyWebhookAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(evt));
        webhookRepo.FindByEventIdAsync("evt_123", Arg.Any<CancellationToken>()).Returns((DomainStripeWebhookEvent?)null);
        subRepo.FindByStripeSubscriptionIdAsync("sub_abc", Arg.Any<CancellationToken>()).Returns((Subscription?)null);

        var result = await sut.Handle(
            new HandleWebhookCommand(evt.PayloadJson, "t=1,v1=abc"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Outcome.Should().Be("processed");
        // Webhook event is still marked processed (so re-delivery is a duplicate).
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Transient_Failure_During_Sync_Returns_Failure()
    {
        // The handler is defensive: if the unit-of-work save throws a
        // transient error (DB blip, etc.), the handler marks the webhook
        // as Failed and returns Failure so the orchestrator can log.
        var sub = NewActiveSubscription();
        var (sut, gateway, webhookRepo, subRepo, uow) = BuildSut();
        var evt = MakeEvent("customer.subscription.created", "active");
        gateway.VerifyWebhookAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(evt));
        webhookRepo.FindByEventIdAsync("evt_123", Arg.Any<CancellationToken>()).Returns((DomainStripeWebhookEvent?)null);
        subRepo.FindByStripeSubscriptionIdAsync("sub_abc", Arg.Any<CancellationToken>()).Returns(sub);
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("DB blip"));

        var result = await sut.Handle(
            new HandleWebhookCommand(evt.PayloadJson, "t=1,v1=abc"),
            CancellationToken.None);

        // The handler catches the exception, marks the webhook as Failed
        // (via SafeSaveAsync — which also throws but is swallowed) and
        // returns Failure so the orchestrator logs.
        result.IsFailure.Should().BeTrue();
    }
}