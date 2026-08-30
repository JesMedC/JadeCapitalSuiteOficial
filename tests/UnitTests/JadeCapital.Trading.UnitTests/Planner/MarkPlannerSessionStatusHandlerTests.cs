using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Features.Planner.MarkPlannerSessionStatus;
using JadeCapital.Trading.Domain.Planner;

namespace JadeCapital.Trading.UnitTests.Planner;

// ============================================================================
//  MarkPlannerSessionStatusHandlerTests — slice 3c.
//
//  Scenarios:
//   1. Mark Completed flips status (idempotent on second call).
//   2. Mark Skipped flips status.
//   3. Invalid status byte → validation error.
//   4. Foreign-owned session → NotFound.
// ============================================================================

public class MarkPlannerSessionStatusHandlerTests
{
    private readonly IPlannerSessionRepository _repo = Substitute.For<IPlannerSessionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public MarkPlannerSessionStatusHandlerTests()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 18, 14, 0, 0, TimeSpan.Zero));
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(1));
    }

    private MarkPlannerSessionStatusHandler CreateSut() => new(_repo, _uow, _clock);

    [Fact]
    public async Task Handle_MarkCompleted_FlipsStatusAndPersists()
    {
        var userId = Guid.NewGuid();
        var session = PlannerSession.Create(
            userId,
            sessionDate: new LocalDate(2026, 8, 17),
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: null,
            notes: null,
            clock: _clock).Value;

        _repo.GetByIdAsync(session.Id, Arg.Any<CancellationToken>())
            .Returns(session);

        var cmd = new MarkPlannerSessionStatusCommand(session.Id, userId, (byte)PlannerStatus.Completed);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be((byte)PlannerStatus.Completed);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MarkSkipped_FlipsStatus()
    {
        var userId = Guid.NewGuid();
        var session = PlannerSession.Create(
            userId,
            sessionDate: new LocalDate(2026, 8, 17),
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: null,
            notes: null,
            clock: _clock).Value;

        _repo.GetByIdAsync(session.Id, Arg.Any<CancellationToken>())
            .Returns(session);

        var cmd = new MarkPlannerSessionStatusCommand(session.Id, userId, (byte)PlannerStatus.Skipped);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be((byte)PlannerStatus.Skipped);
    }

    [Fact]
    public async Task Handle_InvalidStatusByte_ReturnsValidationError()
    {
        var cmd = new MarkPlannerSessionStatusCommand(Guid.NewGuid(), Guid.NewGuid(), 99);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.planner.invalid_status");
    }

    [Fact]
    public async Task Handle_ForeignOwnedSession_ReturnsNotFound()
    {
        var ownerId = Guid.NewGuid();
        var attackerId = Guid.NewGuid();
        var session = PlannerSession.Create(
            ownerId,
            sessionDate: new LocalDate(2026, 8, 17),
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: null,
            notes: null,
            clock: _clock).Value;

        _repo.GetByIdAsync(session.Id, Arg.Any<CancellationToken>())
            .Returns(session);

        var cmd = new MarkPlannerSessionStatusCommand(
            session.Id, attackerId, (byte)PlannerStatus.Completed);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.planner.not_found");
    }
}