using JadeCapital.Trading.Application.Features.Alerts.AcknowledgeAlert;
using JadeCapital.Trading.Application.Features.Alerts.GetAlertById;
using JadeCapital.Trading.Application.Features.Alerts.GetAlerts;

namespace JadeCapital.Trading.UnitTests.Alerts;

// ============================================================================
//  Alert handlers tests — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Three handlers x 1-2 scenarios each:
//   - GetAlerts: returns DTO list, activeOnly forwarded.
//   - GetAlertById: returns DTO on hit; 404 on miss / cross-user.
//   - Acknowledge: sets acknowledged_at via repo; idempotent.
//
//  Uses AlertAggregate directly so we can assert the full state (CreatedAt,
//  AcknowledgedAt) without going through EF hydration.
// ============================================================================

public class AlertHandlerTests
{
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAlertRepository _alerts = Substitute.For<IAlertRepository>();
    private readonly DateTimeOffset _now = new(2026, 8, 18, 14, 0, 0, TimeSpan.Zero);

    public AlertHandlerTests()
    {
        _clock.UtcNow.Returns(_now);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(1));
    }

    [Fact]
    public async Task GetAlerts_ForwardsActiveOnlyAndReturnsDtos()
    {
        var aggregate = BuildAlert();
        _alerts.ListByUserAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[] { aggregate });

        var handler = new GetAlertsHandler(_alerts, _clock);
        var result = await handler.Handle(
            new GetAlertsQuery(aggregate.UserId, ActiveOnly: true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].RuleId.Should().Be("NoTradesInDays");
        await _alerts.Received(1).ListByUserAsync(
            aggregate.UserId, true, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAlertById_ReturnsDto_OnHit()
    {
        var aggregate = BuildAlert();
        _alerts.GetByIdAsync(aggregate.Id, aggregate.UserId, Arg.Any<CancellationToken>())
            .Returns(aggregate);

        var handler = new GetAlertByIdHandler(_alerts);
        var result = await handler.Handle(
            new GetAlertByIdQuery(aggregate.UserId, aggregate.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(aggregate.Id);
    }

    [Fact]
    public async Task GetAlertById_ReturnsNotFound_OnMiss()
    {
        _alerts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((JadeCapital.Trading.Domain.Alerts.Alert?)null);

        var handler = new GetAlertByIdHandler(_alerts);
        var result = await handler.Handle(
            new GetAlertByIdQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.alert.not_found");
    }

    [Fact]
    public async Task Acknowledge_SetsTimestampAndPersists()
    {
        var aggregate = BuildAlert();
        _alerts.GetByIdAsync(aggregate.Id, aggregate.UserId, Arg.Any<CancellationToken>())
            .Returns(aggregate);

        var handler = new AcknowledgeAlertHandler(_alerts, _clock, _uow);
        var result = await handler.Handle(
            new AcknowledgeAlertCommand(aggregate.UserId, aggregate.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        aggregate.AcknowledgedAt.Should().Be(_now);
        await _alerts.Received(1).UpdateAsync(aggregate, Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Acknowledge_Idempotent_PreservesOriginalTimestamp()
    {
        var aggregate = BuildAlert();
        aggregate.Acknowledge(_clock); // first ack
        var firstAck = aggregate.AcknowledgedAt;

        _clock.UtcNow.Returns(_now.AddMinutes(5)); // advance clock
        _alerts.GetByIdAsync(aggregate.Id, aggregate.UserId, Arg.Any<CancellationToken>())
            .Returns(aggregate);

        var handler = new AcknowledgeAlertHandler(_alerts, _clock, _uow);
        var result = await handler.Handle(
            new AcknowledgeAlertCommand(aggregate.UserId, aggregate.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        aggregate.AcknowledgedAt.Should().Be(firstAck);
        await _alerts.Received(1).UpdateAsync(aggregate, Arg.Any<CancellationToken>());
    }

    private JadeCapital.Trading.Domain.Alerts.Alert BuildAlert()
    {
        var userId = Guid.NewGuid();
        return JadeCapital.Trading.Domain.Alerts.Alert.Create(
            userId: userId,
            ruleId: "NoTradesInDays",
            severity: Severity.Low,
            title: "5 días sin operar",
            body: "Revisa tu plan.",
            ctaRoute: "/app/journal",
            ctaLabel: "Reflexionar",
            expiresAt: null,
            clock: _clock).Value;
    }
}