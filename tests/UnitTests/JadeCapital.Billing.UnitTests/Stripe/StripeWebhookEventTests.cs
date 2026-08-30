using JadeCapital.Billing.Domain.Stripe;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Billing.UnitTests.Stripe;

/// <summary>
/// Domain tests for the <see cref="StripeWebhookEvent"/> aggregate (Wave 6,
/// slice 6a.2).
///
/// <para>
/// <b>RED-first contract</b>:
/// <list type="bullet">
///   <item><c>Record</c> factory: required fields (event_id, event_type,
///   payload_json, signature_header, received_at) validated; result Success.</item>
///   <item><c>Record</c> factory: empty <c>EventId</c> → invalid_event_id.</item>
///   <item><c>Record</c> factory: empty <c>EventType</c> → invalid_event_type.</item>
///   <item><c>Record</c> factory: empty <c>PayloadJson</c> → invalid_payload_json.</item>
///   <item><c>MarkProcessed</c> → sets <c>ProcessedAt</c> + clears <c>ProcessingError</c>.</item>
///   <item><c>MarkFailed</c> → sets <c>ProcessingError</c> + leaves
///   <c>ProcessedAt</c> NULL.</item>
///   <item>Status property: <c>Pending</c> initially, <c>Processed</c> after
///   <c>MarkProcessed</c>, <c>Failed</c> after <c>MarkFailed</c>.</item>
///   <item>Append-only: no mutators other than <c>MarkProcessed</c> /
///   <c>MarkFailed</c>; aggregate does NOT expose public setters on
///   <c>EventId</c>, <c>EventType</c>, <c>PayloadJson</c>.</item>
/// </list>
/// </para>
/// </summary>
public class StripeWebhookEventTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; init; } =
            new(2026, 8, 19, 14, 32, 0, TimeSpan.Zero);
    }

    private static readonly IClock Clock = new FixedClock();

    [Fact]
    public void Record_Valid_Inputs_Returns_Success()
    {
        var id = Guid.NewGuid();
        var received = Clock.UtcNow;
        var result = StripeWebhookEvent.Record(
            id, "evt_123", "customer.subscription.created",
            "{\"id\":\"evt_123\"}", "t=1,v1=abc", received, Clock);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(id);
        result.Value.EventId.Should().Be("evt_123");
        result.Value.EventType.Should().Be("customer.subscription.created");
        result.Value.PayloadJson.Should().Contain("evt_123");
        result.Value.SignatureHeader.Should().Be("t=1,v1=abc");
        result.Value.ReceivedAt.Should().Be(received);
        result.Value.ProcessedAt.Should().BeNull();
        result.Value.ProcessingError.Should().BeNull();
    }

    [Fact]
    public void Record_Empty_EventId_Returns_InvalidEventId()
    {
        var result = StripeWebhookEvent.Record(
            Guid.NewGuid(), "", "ping", "{}", null, Clock.UtcNow, Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.stripe.webhook_event.invalid_event_id");
    }

    [Fact]
    public void Record_Empty_EventType_Returns_InvalidEventType()
    {
        var result = StripeWebhookEvent.Record(
            Guid.NewGuid(), "evt_1", "", "{}", null, Clock.UtcNow, Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.stripe.webhook_event.invalid_event_type");
    }

    [Fact]
    public void Record_Empty_PayloadJson_Returns_InvalidPayloadJson()
    {
        var result = StripeWebhookEvent.Record(
            Guid.NewGuid(), "evt_1", "ping", "", null, Clock.UtcNow, Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.stripe.webhook_event.invalid_payload_json");
    }

    [Fact]
    public void Record_SignatureHeader_Allows_Null()
    {
        // Signature header is optional in our record (e.g. when the gateway
        // accepts synthetic events from the stub in dev). The aggregator
        // preserves the raw value when present and tolerates absence.
        var result = StripeWebhookEvent.Record(
            Guid.NewGuid(), "evt_1", "ping", "{}", null, Clock.UtcNow, Clock);

        result.IsSuccess.Should().BeTrue();
        result.Value.SignatureHeader.Should().BeNull();
    }

    [Fact]
    public void MarkProcessed_Sets_ProcessedAt_And_Clears_Error()
    {
        var evt = StripeWebhookEvent.Record(
            Guid.NewGuid(), "evt_1", "ping", "{}", null, Clock.UtcNow, Clock).Value;

        evt.MarkFailed("transient", Clock);
        evt.MarkProcessed(Clock);

        evt.ProcessedAt.Should().Be(Clock.UtcNow);
        evt.ProcessingError.Should().BeNull();
        evt.Status.Should().Be(StripeWebhookEventStatus.Processed);
    }

    [Fact]
    public void MarkFailed_Sets_Error_And_Leaves_ProcessedAt_Null()
    {
        var evt = StripeWebhookEvent.Record(
            Guid.NewGuid(), "evt_1", "ping", "{}", null, Clock.UtcNow, Clock).Value;

        evt.MarkFailed("transient", Clock);

        evt.ProcessedAt.Should().BeNull();
        evt.ProcessingError.Should().Be("transient");
        evt.Status.Should().Be(StripeWebhookEventStatus.Failed);
    }

    [Fact]
    public void Status_Defaults_To_Pending_After_Record()
    {
        var evt = StripeWebhookEvent.Record(
            Guid.NewGuid(), "evt_1", "ping", "{}", null, Clock.UtcNow, Clock).Value;

        evt.Status.Should().Be(StripeWebhookEventStatus.Pending);
    }

    [Fact]
    public void Aggregate_Does_Not_Expose_Public_Setters_On_Immutable_Fields()
    {
        // Append-only contract: the aggregate MUST NOT allow callers to
        // mutate EventId / EventType / PayloadJson / ReceivedAt after Record.
        // The setters are private (verified via reflection).
        var t = typeof(StripeWebhookEvent);
        var immutableFields = new[] { "EventId", "EventType", "PayloadJson", "ReceivedAt" };

        foreach (var fieldName in immutableFields)
        {
            var prop = t.GetProperty(fieldName);
            prop.Should().NotBeNull($"StripeWebhookEvent.{fieldName} must exist");

            var setter = prop!.GetSetMethod(nonPublic: false);
            setter.Should().BeNull(
                $"StripeWebhookEvent.{fieldName} setter MUST be non-public (append-only)");
        }
    }
}
