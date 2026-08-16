using JadeCapital.Trading.Domain.Strategies;

namespace JadeCapital.Trading.UnitTests.Strategies;

// ============================================================================
//  StrategyTests — slice 3a (Trader Strategies + Alerts + Planner).
//
//  Eight scenarios covering the aggregate invariants:
//   1. Valid create: persists all fields, fires StrategyCreatedDomainEvent.
//   2. Name required: empty/whitespace rejected with strategy.name_required.
//   3. Name too long: > MaxNameLength rejected with strategy.name_too_long.
//   4. Description too long: > MaxDescriptionLength rejected.
//   5. Rules too long: > MaxRulesLength rejected.
//   6. Update preserves CreatedAt: UpdatedAt > CreatedAt.
//   7. Deactivate is idempotent: second call doesn't change UpdatedAt
//      (or is a no-op) and IsActive stays false.
//   8. InvalidTimeframe: Timeframe.Unspecified (0) rejected.
//
//  Strict TDD: these tests reference the Strategy aggregate that is
//  implemented in parallel. They define the acceptance criteria.
// ============================================================================

public class StrategyTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 18, 14, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset LaterNow =
        new(2026, 8, 18, 16, 0, 0, TimeSpan.Zero);

    private static IClock FrozenClock(DateTimeOffset at) =>
        Substitute.For<IClock>();

    public StrategyTests()
    {
        // Default clock returns FixedNow for Setup; per-test overrides
        // mutate the return to assert time-travel effects on Touch().
    }

    [Fact]
    public void Create_WithValidArgs_ReturnsSuccessAndPersistsAllFields()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var userId = Guid.NewGuid();

        var result = Strategy.Create(
            userId,
            name: "London Break",
            description: "Breakout during London session",
            symbol: "EUR/USD",
            timeframe: Timeframe.H1,
            rules: "Enter on 1H close above range high with volume confirmation.",
            clock: clock);

        result.IsSuccess.Should().BeTrue();
        var s = result.Value;
        s.UserId.Should().Be(userId);
        s.Name.Should().Be("London Break");
        s.Description.Should().Be("Breakout during London session");
        s.Symbol.Should().Be("EUR/USD");
        s.Timeframe.Should().Be(Timeframe.H1);
        s.Rules.Should().Be("Enter on 1H close above range high with volume confirmation.");
        s.IsActive.Should().BeTrue();
        s.CreatedAt.Should().Be(FixedNow);
        s.UpdatedAt.Should().Be(FixedNow);
        s.DomainEvents.Should().ContainSingle(e => e is StrategyCreatedDomainEvent);
    }

    [Fact]
    public void Create_WithNullUserId_FailsWithUserIdRequired()
    {
        var clock = Substitute.For<IClock>();

        var result = Strategy.Create(
            Guid.Empty,
            name: "Test",
            description: null,
            symbol: null,
            timeframe: null,
            rules: null,
            clock: clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.strategy.user_id_required");
    }

    [Fact]
    public void Create_WithEmptyOrWhitespaceName_FailsWithNameRequired()
    {
        var clock = Substitute.For<IClock>();

        foreach (var bad in new[] { "", " ", "\t", "\n", "   " })
        {
            var result = Strategy.Create(
                Guid.NewGuid(),
                name: bad,
                description: null,
                symbol: null,
                timeframe: null,
                rules: null,
                clock: clock);

            result.IsFailure.Should().BeTrue($"name={bad ?? "<null>"} should fail");
            result.Error.Code.Should().Be("validation.strategy.name_required");
        }
    }

    [Fact]
    public void Create_WithNameOver64Chars_FailsWithNameTooLong()
    {
        var clock = Substitute.For<IClock>();
        var longName = new string('a', 65);

        var result = Strategy.Create(
            Guid.NewGuid(),
            name: longName,
            description: null,
            symbol: null,
            timeframe: null,
            rules: null,
            clock: clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.strategy.name_too_long");
    }

    [Fact]
    public void Create_WithDescriptionOver1000Chars_FailsWithDescriptionTooLong()
    {
        var clock = Substitute.For<IClock>();
        var longDesc = new string('d', 1001);

        var result = Strategy.Create(
            Guid.NewGuid(),
            name: "X",
            description: longDesc,
            symbol: null,
            timeframe: null,
            rules: null,
            clock: clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.strategy.description_too_long");
    }

    [Fact]
    public void Create_WithRulesOver2000Chars_FailsWithRulesTooLong()
    {
        var clock = Substitute.For<IClock>();
        var longRules = new string('r', 2001);

        var result = Strategy.Create(
            Guid.NewGuid(),
            name: "X",
            description: null,
            symbol: null,
            timeframe: null,
            rules: longRules,
            clock: clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.strategy.rules_too_long");
    }

    [Fact]
    public void Create_WithUnspecifiedTimeframe_FailsWithInvalidTimeframe()
    {
        var clock = Substitute.For<IClock>();

        var result = Strategy.Create(
            Guid.NewGuid(),
            name: "X",
            description: null,
            symbol: null,
            timeframe: Timeframe.Unspecified,
            rules: null,
            clock: clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.strategy.invalid_timeframe");
    }

    [Fact]
    public void Create_WithOutOfRangeByteTimeframe_FailsWithInvalidTimeframe()
    {
        // Cast through unchecked to simulate a corrupt caller passing 42.
        var clock = Substitute.For<IClock>();
        byte bogus = 42;

        var result = Strategy.Create(
            Guid.NewGuid(),
            name: "X",
            description: null,
            symbol: null,
            timeframe: (Timeframe)bogus,
            rules: null,
            clock: clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.strategy.invalid_timeframe");
    }

    [Fact]
    public void Update_PreservesCreatedAt_AndBumpsUpdatedAt()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);

        var create = Strategy.Create(
            Guid.NewGuid(),
            name: "Original",
            description: null,
            symbol: null,
            timeframe: Timeframe.M15,
            rules: null,
            clock: clock);
        var s = create.Value;
        var createdAt = s.CreatedAt;

        // Advance clock before Update.
        clock.UtcNow.Returns(LaterNow);

        var updateResult = s.Update(
            name: "Renamed",
            description: "New desc",
            symbol: "BTC/USD",
            timeframe: Timeframe.H1,
            rules: "New rules",
            clock: clock);

        updateResult.IsSuccess.Should().BeTrue();
        s.Name.Should().Be("Renamed");
        s.Description.Should().Be("New desc");
        s.Symbol.Should().Be("BTC/USD");
        s.Timeframe.Should().Be(Timeframe.H1);
        s.Rules.Should().Be("New rules");
        s.CreatedAt.Should().Be(createdAt);
        s.UpdatedAt.Should().Be(LaterNow);
        s.DomainEvents.Should().Contain(e => e is StrategyUpdatedDomainEvent);
    }

    [Fact]
    public void Deactivate_IsIdempotent_AndSetsIsActiveFalse()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);

        var s = Strategy.Create(
            Guid.NewGuid(), "X", null, null, Timeframe.H4, null, clock).Value;

        // First deactivate flips IsActive.
        s.Deactivate(clock).IsSuccess.Should().BeTrue();
        s.IsActive.Should().BeFalse();

        // Second call is idempotent (success, no further state change).
        s.Deactivate(clock).IsSuccess.Should().BeTrue();
        s.IsActive.Should().BeFalse();

        // Emits a soft-deleted event once (the second call is a no-op).
        s.DomainEvents.OfType<StrategySoftDeletedDomainEvent>().Should().HaveCount(1);

        // Activate reverses it.
        s.Activate(clock).IsSuccess.Should().BeTrue();
        s.IsActive.Should().BeTrue();
        // Activate is idempotent too.
        s.Activate(clock).IsSuccess.Should().BeTrue();
        s.IsActive.Should().BeTrue();
    }
}