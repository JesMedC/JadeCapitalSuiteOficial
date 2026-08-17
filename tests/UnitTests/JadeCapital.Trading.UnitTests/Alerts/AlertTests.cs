using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Domain.Alerts;
using JadeCapital.Trading.Domain.Common;

namespace JadeCapital.Trading.UnitTests.Alerts;

// ============================================================================
//  Alert aggregate tests — slice 3b (Trader Strategies + Alerts + Planner).
//
//  RED test contract from the alerts spec (requirements #4 + #8):
//   - Create: title 1..MaxTitleLength, body 1..MaxBodyLength, userId valid,
//     RuleId non-empty, Severity in range. Default Severity = Low,
//     default AcknowledgedAt = null, default ExpiresAt = null.
//   - Acknowledge: sets AcknowledgedAt to clock.UtcNow; idempotent.
//   - UserId empty / RuleId empty / title empty or too long / body too long
//     propagate TradingDomainErrors.Alert.* with stable codes.
//
//  Implementation lives in JadeCapital.Trading.Domain/Alerts/Alert.cs.
// ============================================================================

public class AlertTests
{
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly DateTimeOffset _now = new(2026, 8, 18, 14, 0, 0, TimeSpan.Zero);

    public AlertTests()
    {
        _clock.UtcNow.Returns(_now);
    }

    private Alert CreateValid(
        string ruleId = "NoTradesInDaysRule",
        string title = "Sin trades hace 5 días",
        string body = "Revisa tu plan: ¿descanso intencional o falta de disciplina?",
        Guid? userId = null,
        Severity severity = Severity.Low,
        DateTimeOffset? expiresAt = null)
        => Alert.Create(
            userId: userId ?? Guid.NewGuid(),
            ruleId: ruleId,
            severity: severity,
            title: title,
            body: body,
            ctaRoute: "/app/journal",
            ctaLabel: "Revisar",
            expiresAt: expiresAt,
            clock: _clock).Value;

    // ============== Create happy-path ==============

    [Fact]
    public void Create_ValidArgs_ReturnsSuccessWithDefaults()
    {
        var alert = CreateValid();

        alert.Id.Should().NotBe(Guid.Empty);
        alert.UserId.Should().NotBe(Guid.NewGuid()); // should be the userId we passed
        alert.RuleId.Should().Be("NoTradesInDaysRule");
        alert.Title.Should().Be("Sin trades hace 5 días");
        alert.Body.Should().Be("Revisa tu plan: ¿descanso intencional o falta de disciplina?");
        alert.Severity.Should().Be(JadeCapital.Shared.Kernel.Coaching.Severity.Low);
        alert.CtaRoute.Should().Be("/app/journal");
        alert.CtaLabel.Should().Be("Revisar");
        alert.AcknowledgedAt.Should().BeNull();
        alert.ExpiresAt.Should().BeNull();
        alert.CreatedAt.Should().Be(_now);
    }

    [Fact]
    public void Create_ExpiresAtProvided_PersistsIt()
    {
        var expiry = _now.AddDays(30);
        var alert = CreateValid(expiresAt: expiry);

        alert.ExpiresAt.Should().Be(expiry);
    }

    // ============== Validation errors ==============

    [Fact]
    public void Create_EmptyUserId_Fails()
    {
        var result = Alert.Create(
            userId: Guid.Empty,
            ruleId: "X", severity: Severity.Low,
            title: "t", body: "b",
            ctaRoute: "/x", ctaLabel: "x",
            expiresAt: null,
            clock: _clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.alert.user_id_required");
    }

    [Fact]
    public void Create_EmptyRuleId_Fails()
    {
        var result = Alert.Create(
            userId: Guid.NewGuid(),
            ruleId: "", severity: Severity.Low,
            title: "t", body: "b",
            ctaRoute: "/x", ctaLabel: "x",
            expiresAt: null,
            clock: _clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.alert.rule_id_required");
    }

    [Fact]
    public void Create_EmptyTitle_Fails()
    {
        var result = Alert.Create(
            userId: Guid.NewGuid(),
            ruleId: "X", severity: Severity.Low,
            title: "", body: "b",
            ctaRoute: "/x", ctaLabel: "x",
            expiresAt: null,
            clock: _clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.alert.title_required");
    }

    [Fact]
    public void Create_TitleOver120Chars_Fails()
    {
        var result = Alert.Create(
            userId: Guid.NewGuid(),
            ruleId: "X", severity: Severity.Low,
            title: new string('x', Alert.MaxTitleLength + 1), body: "b",
            ctaRoute: "/x", ctaLabel: "x",
            expiresAt: null,
            clock: _clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.alert.title_too_long");
    }

    [Fact]
    public void Create_EmptyBody_Fails()
    {
        var result = Alert.Create(
            userId: Guid.NewGuid(),
            ruleId: "X", severity: Severity.Low,
            title: "t", body: "",
            ctaRoute: "/x", ctaLabel: "x",
            expiresAt: null,
            clock: _clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.alert.body_required");
    }

    [Fact]
    public void Create_BodyOver500Chars_Fails()
    {
        var result = Alert.Create(
            userId: Guid.NewGuid(),
            ruleId: "X", severity: Severity.Low,
            title: "t", body: new string('x', Alert.MaxBodyLength + 1),
            ctaRoute: "/x", ctaLabel: "x",
            expiresAt: null,
            clock: _clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.alert.body_too_long");
    }

    // ============== Acknowledge ==============

    [Fact]
    public void Acknowledge_SetsAcknowledgedAtToClockNow()
    {
        var alert = CreateValid();

        var ackResult = alert.Acknowledge(_clock);

        ackResult.IsSuccess.Should().BeTrue();
        alert.AcknowledgedAt.Should().Be(_now);
    }

    [Fact]
    public void Acknowledge_Idempotent_PreservesOriginalTimestamp()
    {
        var alert = CreateValid();
        alert.Acknowledge(_clock);

        var laterTime = _now.AddMinutes(5);
        _clock.UtcNow.Returns(laterTime);

        var ackResult = alert.Acknowledge(_clock);

        ackResult.IsSuccess.Should().BeTrue();
        alert.AcknowledgedAt.Should().Be(_now);
    }
}