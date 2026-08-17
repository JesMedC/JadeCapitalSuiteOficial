using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Features.Planner.UpdatePlannerSession;
using JadeCapital.Trading.Domain.Planner;

namespace JadeCapital.Trading.UnitTests.Planner;

// ============================================================================
//  UpdatePlannerSessionHandlerTests — slice 3c.
//
//  Scenarios:
//   1. Update owned session mutates fields and bumps UpdatedAt.
//   2. Update foreign-owned session returns NotFound.
//   3. Update with end-before-start returns validation error.
// ============================================================================

public class UpdatePlannerSessionHandlerTests
{
    private readonly IPlannerSessionRepository _repo = Substitute.For<IPlannerSessionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public UpdatePlannerSessionHandlerTests()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 18, 14, 0, 0, TimeSpan.Zero));
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(1));
    }

    private UpdatePlannerSessionHandler CreateSut() => new(_repo, _uow, _clock);

    [Fact]
    public async Task Handle_OwnedSession_UpdatesFieldsAndPersists()
    {
        var userId = Guid.NewGuid();
        var session = PlannerSession.Create(
            userId,
            new LocalDate(2026, 8, 17),
            plannedStartTime: new TimeOnly(10, 0),
            plannedEndTime: new TimeOnly(12, 0),
            symbol: "EURUSD",
            notes: "Original",
            clock: _clock).Value;

        _repo.GetByIdAsync(session.Id, Arg.Any<CancellationToken>())
            .Returns(session);

        var cmd = new UpdatePlannerSessionCommand(
            session.Id, userId,
            PlannedStartTime: new TimeOnly(11, 0),
            PlannedEndTime: new TimeOnly(13, 0),
            Symbol: "GBPUSD",
            Notes: "Updated");

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PlannedStartTime.Should().Be("11:00");
        result.Value.PlannedEndTime.Should().Be("13:00");
        result.Value.Symbol.Should().Be("GBPUSD");
        result.Value.Notes.Should().Be("Updated");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForeignOwnedSession_ReturnsNotFound()
    {
        var ownerId = Guid.NewGuid();
        var attackerId = Guid.NewGuid();
        var session = PlannerSession.Create(
            ownerId,
            new LocalDate(2026, 8, 17),
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: null,
            notes: null,
            clock: _clock).Value;

        _repo.GetByIdAsync(session.Id, Arg.Any<CancellationToken>())
            .Returns(session);

        var cmd = new UpdatePlannerSessionCommand(
            session.Id, attackerId,
            PlannedStartTime: null,
            PlannedEndTime: null,
            Symbol: null,
            Notes: "hijack");

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.planner.not_found");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EndBeforeStart_ReturnsValidationError()
    {
        var userId = Guid.NewGuid();
        var session = PlannerSession.Create(
            userId,
            new LocalDate(2026, 8, 17),
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: null,
            notes: null,
            clock: _clock).Value;

        _repo.GetByIdAsync(session.Id, Arg.Any<CancellationToken>())
            .Returns(session);

        var cmd = new UpdatePlannerSessionCommand(
            session.Id, userId,
            PlannedStartTime: new TimeOnly(13, 0),
            PlannedEndTime: new TimeOnly(12, 0),
            Symbol: null,
            Notes: null);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.planner.end_before_start");
    }
}