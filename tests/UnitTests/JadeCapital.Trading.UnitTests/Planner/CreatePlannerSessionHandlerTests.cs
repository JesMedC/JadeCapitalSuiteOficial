using FluentAssertions;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Features.Planner.CreatePlannerSession;
using JadeCapital.Trading.Contracts.Planner;

namespace JadeCapital.Trading.UnitTests.Planner;

// ============================================================================
//  CreatePlannerSessionHandlerTests — slice 3c.
//
//  Scenarios:
//   1. Valid create persists the aggregate + returns DTO.
//   2. Duplicate date for same user → 409 (planner.already_exists_for_date).
//   3. End-before-start propagates from PlannerSession.Create.
// ============================================================================

public class CreatePlannerSessionHandlerTests
{
    private readonly IPlannerSessionRepository _repo = Substitute.For<IPlannerSessionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public CreatePlannerSessionHandlerTests()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 18, 14, 0, 0, TimeSpan.Zero));
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(1));
    }

    private CreatePlannerSessionHandler CreateSut() => new(_repo, _uow, _clock);

    private static CreatePlannerSessionCommand ValidCmd() => new(
        UserId: Guid.NewGuid(),
        SessionDate: new LocalDate(2026, 8, 17),
        PlannedStartTime: new TimeOnly(10, 0),
        PlannedEndTime: new TimeOnly(12, 0),
        Symbol: "EURUSD",
        Notes: "London breakout watch");

    [Fact]
    public async Task Handle_ValidArgs_PersistsSessionAndReturnsDto()
    {
        var cmd = ValidCmd();
        _repo.ExistsForDateAsync(cmd.UserId, cmd.SessionDate, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.SessionDate.Should().Be("2026-08-17");
        result.Value.PlannedStartTime.Should().Be("10:00");
        result.Value.PlannedEndTime.Should().Be("12:00");
        result.Value.Symbol.Should().Be("EURUSD");
        result.Value.Status.Should().Be((byte)JadeCapital.Trading.Domain.Planner.PlannerStatus.Planned);

        await _repo.Received(1).AddAsync(
            Arg.Is<JadeCapital.Trading.Domain.Planner.PlannerSession>(s =>
                s.UserId == cmd.UserId && s.SessionDate == cmd.SessionDate),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateDate_ReturnsConflict()
    {
        var cmd = ValidCmd();
        _repo.ExistsForDateAsync(cmd.UserId, cmd.SessionDate, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.planner.already_exists_for_date");
        await _repo.DidNotReceive().AddAsync(
            Arg.Any<JadeCapital.Trading.Domain.Planner.PlannerSession>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EndBeforeStart_PropagatesValidationError()
    {
        var cmd = ValidCmd() with
        {
            PlannedStartTime = new TimeOnly(12, 0),
            PlannedEndTime = new TimeOnly(10, 0),
        };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.planner.end_before_start");
        await _repo.DidNotReceive().AddAsync(
            Arg.Any<JadeCapital.Trading.Domain.Planner.PlannerSession>(),
            Arg.Any<CancellationToken>());
    }
}