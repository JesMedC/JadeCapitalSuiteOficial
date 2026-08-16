using FluentAssertions;
using JadeCapital.Trading.Application.Features.Strategies.ListStrategies;
using JadeCapital.Trading.Domain.Strategies;

namespace JadeCapital.Trading.UnitTests.Strategies;

// ============================================================================
//  ListStrategiesHandlerTests — slice 3a.
//
//  Two scenarios:
//   1. Returns ONLY the requesting user's strategies (cross-user isolation).
//   2. activeOnly=true filters out soft-deleted rows.
// ============================================================================

public class ListStrategiesHandlerTests
{
    private readonly IStrategyRepository _repo = Substitute.For<IStrategyRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public ListStrategiesHandlerTests()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 18, 14, 0, 0, TimeSpan.Zero));
    }

    private ListStrategiesHandler CreateSut() => new(_repo);

    [Fact]
    public async Task Handle_OnlyReturnsRequestingUsersStrategies()
    {
        var userId = Guid.NewGuid();
        var ownActive = Strategy.Create(
            userId, "Mine", null, null, Timeframe.H1, null, _clock).Value;
        _repo.ListByUserAsync(userId, true, Arg.Any<CancellationToken>())
            .Returns(new[] { ownActive });

        var query = new ListStrategiesQuery(userId, ActiveOnly: true);

        var result = await CreateSut().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].UserId.Should().Be(userId);
        result.Value[0].Name.Should().Be("Mine");
        await _repo.Received(1).ListByUserAsync(
            userId, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ActiveOnlyFalse_PassesFalseToRepository()
    {
        var userId = Guid.NewGuid();
        var active = Strategy.Create(
            userId, "Active", null, null, Timeframe.H1, null, _clock).Value;
        var softDeleted = Strategy.Create(
            userId, "SoftDeleted", null, null, Timeframe.H4, null, _clock).Value;
        softDeleted.Deactivate(_clock);

        _repo.ListByUserAsync(userId, false, Arg.Any<CancellationToken>())
            .Returns(new[] { active, softDeleted });

        var query = new ListStrategiesQuery(userId, ActiveOnly: false);

        var result = await CreateSut().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        await _repo.Received(1).ListByUserAsync(
            userId, false, Arg.Any<CancellationToken>());
    }
}