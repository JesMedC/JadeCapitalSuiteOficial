using JadeCapital.Trading.Domain.Planner;

namespace JadeCapital.Trading.UnitTests.Planner;

// ============================================================================
//  PlannerSessionTests — slice 3c (Trader Strategies + Alerts + Planner).
//
//  Six scenarios covering the aggregate invariants:
//   1. Create_ValidArgs_PersistsAllFieldsAndFiresCreatedEvent.
//   2. Create_DefaultsToPlannedStatus.
//   3. Create_EndTimeBeforeStartTime_Rejected.
//   4. Create_NotesOver500Chars_Rejected.
//   5. MarkCompleted_IsIdempotentAndPreservesTimestamps.
//   6. CrossUserUpdate_Rejected (handler-level — aggregate exposes ownership
//      guard via Status setter checks; see MarkStatus_OwnedByOtherUser_Fails).
//
//  Strict TDD: estos tests describen los criterios de aceptacion. La
//  implementacion del aggregate vive en Trading.Domain/Planner/PlannerSession.cs.
// ============================================================================

public class PlannerSessionTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 18, 14, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset LaterNow =
        new(2026, 8, 18, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ValidArgs_PersistsAllFieldsAndFiresCreatedEvent()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var userId = Guid.NewGuid();

        var result = PlannerSession.Create(
            userId,
            sessionDate: new LocalDate(2026, 8, 17),
            plannedStartTime: new TimeOnly(10, 0),
            plannedEndTime: new TimeOnly(12, 0),
            symbol: "EURUSD",
            notes: "London breakout watch",
            clock: clock);

        result.IsSuccess.Should().BeTrue();
        var s = result.Value;
        s.UserId.Should().Be(userId);
        s.SessionDate.Should().Be(new LocalDate(2026, 8, 17));
        s.PlannedStartTime.Should().Be(new TimeOnly(10, 0));
        s.PlannedEndTime.Should().Be(new TimeOnly(12, 0));
        s.Symbol.Should().Be("EURUSD");
        s.Notes.Should().Be("London breakout watch");
        s.Status.Should().Be(PlannerStatus.Planned);
        s.CreatedAt.Should().Be(FixedNow);
        s.UpdatedAt.Should().Be(FixedNow);
        s.DomainEvents.Should().ContainSingle(e => e is PlannerSessionCreatedDomainEvent);
    }

    [Fact]
    public void Create_WithNullOptionals_DefaultsToPlannedAndPersists()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var userId = Guid.NewGuid();

        var result = PlannerSession.Create(
            userId,
            sessionDate: new LocalDate(2026, 8, 17),
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: null,
            notes: null,
            clock: clock);

        result.IsSuccess.Should().BeTrue();
        var s = result.Value;
        s.Status.Should().Be(PlannerStatus.Planned);
        s.PlannedStartTime.Should().BeNull();
        s.PlannedEndTime.Should().BeNull();
        s.Symbol.Should().BeNull();
        s.Notes.Should().BeNull();
    }

    [Fact]
    public void Create_EndTimeBeforeStartTime_Rejected()
    {
        var clock = Substitute.For<IClock>();

        var result = PlannerSession.Create(
            Guid.NewGuid(),
            sessionDate: new LocalDate(2026, 8, 17),
            plannedStartTime: new TimeOnly(12, 0),
            plannedEndTime: new TimeOnly(10, 0),
            symbol: null,
            notes: null,
            clock: clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.planner.end_before_start");
    }

    [Fact]
    public void Create_NotesOver500Chars_Rejected()
    {
        var clock = Substitute.For<IClock>();
        var longNotes = new string('n', 501);

        var result = PlannerSession.Create(
            Guid.NewGuid(),
            sessionDate: new LocalDate(2026, 8, 17),
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: null,
            notes: longNotes,
            clock: clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.planner.notes_too_long");
    }

    [Fact]
    public void Create_EmptyUserId_Rejected()
    {
        var clock = Substitute.For<IClock>();

        var result = PlannerSession.Create(
            Guid.Empty,
            sessionDate: new LocalDate(2026, 8, 17),
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: null,
            notes: null,
            clock: clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.planner.user_id_required");
    }

    [Fact]
    public void MarkCompleted_IsIdempotentAndBumpsUpdatedAt()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);

        var s = PlannerSession.Create(
            Guid.NewGuid(),
            sessionDate: new LocalDate(2026, 8, 17),
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: null,
            notes: null,
            clock: clock).Value;

        // Advance clock before MarkCompleted.
        clock.UtcNow.Returns(LaterNow);

        var firstResult = s.MarkCompleted(clock);
        firstResult.IsSuccess.Should().BeTrue();
        s.Status.Should().Be(PlannerStatus.Completed);
        s.UpdatedAt.Should().Be(LaterNow);

        // Second call: idempotent. Status stays Completed.
        var secondResult = s.MarkCompleted(clock);
        secondResult.IsSuccess.Should().BeTrue();
        s.Status.Should().Be(PlannerStatus.Completed);
    }

    [Fact]
    public void MarkSkipped_IsIdempotent()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);

        var s = PlannerSession.Create(
            Guid.NewGuid(),
            sessionDate: new LocalDate(2026, 8, 17),
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: null,
            notes: null,
            clock: clock).Value;

        var first = s.MarkSkipped(clock);
        first.IsSuccess.Should().BeTrue();
        s.Status.Should().Be(PlannerStatus.Skipped);

        var second = s.MarkSkipped(clock);
        second.IsSuccess.Should().BeTrue();
        s.Status.Should().Be(PlannerStatus.Skipped);
    }

    [Fact]
    public void MarkCancelled_IsIdempotent()
    {
        var clock = Substitute.For<IClock>();

        var s = PlannerSession.Create(
            Guid.NewGuid(),
            sessionDate: new LocalDate(2026, 8, 17),
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: null,
            notes: null,
            clock: clock).Value;

        s.MarkCancelled(clock).IsSuccess.Should().BeTrue();
        s.Status.Should().Be(PlannerStatus.Cancelled);

        s.MarkCancelled(clock).IsSuccess.Should().BeTrue();
        s.Status.Should().Be(PlannerStatus.Cancelled);
    }

    [Fact]
    public void Update_PreservesCreatedAt_AndBumpsUpdatedAt()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);

        var s = PlannerSession.Create(
            Guid.NewGuid(),
            sessionDate: new LocalDate(2026, 8, 17),
            plannedStartTime: new TimeOnly(10, 0),
            plannedEndTime: new TimeOnly(12, 0),
            symbol: "EURUSD",
            notes: "Original",
            clock: clock).Value;

        var createdAt = s.CreatedAt;

        clock.UtcNow.Returns(LaterNow);

        var updateResult = s.Update(
            plannedStartTime: new TimeOnly(11, 0),
            plannedEndTime: new TimeOnly(13, 0),
            symbol: "GBPUSD",
            notes: "Updated",
            clock: clock);

        updateResult.IsSuccess.Should().BeTrue();
        s.PlannedStartTime.Should().Be(new TimeOnly(11, 0));
        s.PlannedEndTime.Should().Be(new TimeOnly(13, 0));
        s.Symbol.Should().Be("GBPUSD");
        s.Notes.Should().Be("Updated");
        s.CreatedAt.Should().Be(createdAt);
        s.UpdatedAt.Should().Be(LaterNow);
    }

    [Fact]
    public void Update_EndBeforeStart_Rejected()
    {
        var clock = Substitute.For<IClock>();

        var s = PlannerSession.Create(
            Guid.NewGuid(),
            sessionDate: new LocalDate(2026, 8, 17),
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: null,
            notes: null,
            clock: clock).Value;

        var updateResult = s.Update(
            plannedStartTime: new TimeOnly(13, 0),
            plannedEndTime: new TimeOnly(12, 0),
            symbol: null,
            notes: null,
            clock: clock);

        updateResult.IsFailure.Should().BeTrue();
        updateResult.Error.Code.Should().Be("validation.planner.end_before_start");
    }

    [Fact]
    public void Update_NotesOver500_Rejected()
    {
        var clock = Substitute.For<IClock>();

        var s = PlannerSession.Create(
            Guid.NewGuid(),
            sessionDate: new LocalDate(2026, 8, 17),
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: null,
            notes: null,
            clock: clock).Value;

        var updateResult = s.Update(
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: null,
            notes: new string('n', 501),
            clock: clock);

        updateResult.IsFailure.Should().BeTrue();
        updateResult.Error.Code.Should().Be("validation.planner.notes_too_long");
    }
}