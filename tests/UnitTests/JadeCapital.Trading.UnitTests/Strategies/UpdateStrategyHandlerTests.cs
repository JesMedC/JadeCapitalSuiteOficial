using FluentAssertions;
using JadeCapital.Trading.Application.Features.Strategies.UpdateStrategy;
using JadeCapital.Trading.Domain.Strategies;

namespace JadeCapital.Trading.UnitTests.Strategies;

// ============================================================================
//  UpdateStrategyHandlerTests — slice 3a.
//
//  Three scenarios:
//   1. Valid update persists + bumps UpdatedAt.
//   2. Name change colliding with another active strategy → 409.
//   3. Strategy not found (or foreign-owned) → 404.
// ============================================================================

public class UpdateStrategyHandlerTests
{
    private readonly IStrategyRepository _repo = Substitute.For<IStrategyRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public UpdateStrategyHandlerTests()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 18, 14, 0, 0, TimeSpan.Zero));
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(1));
    }

    private UpdateStrategyHandler CreateSut() => new(_repo, _uow, _clock);

    private Strategy CreateActiveStrategy(Guid userId, string name = "Original")
    {
        return Strategy.Create(
            userId, name, null, null, Timeframe.H4, null, _clock).Value;
    }

    [Fact]
    public async Task Handle_ValidArgs_PersistsUpdatedAggregate()
    {
        var userId = Guid.NewGuid();
        var existing = CreateActiveStrategy(userId, "Original");
        _repo.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>())
            .Returns(existing);

        var cmd = new UpdateStrategyCommand(
            existing.Id, userId, "Renamed", "new desc", "BTC/USD",
            (byte)Timeframe.H1, "new rules");

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.Name.Should().Be("Renamed");
        existing.Description.Should().Be("new desc");
        existing.Symbol.Should().Be("BTC/USD");
        existing.Timeframe.Should().Be(Timeframe.H1);
        await _repo.Received(1).UpdateAsync(existing, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NameChangeDuplicate_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var existing = CreateActiveStrategy(userId, "Original");
        _repo.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>())
            .Returns(existing);
        _repo.ExistsByNameAsync(userId, "AlreadyTaken", Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = new UpdateStrategyCommand(
            existing.Id, userId, "AlreadyTaken", null, null, null, null);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.strategy.duplicate_name");
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<Strategy>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StrategyNotFound_ReturnsNotFound()
    {
        var cmd = new UpdateStrategyCommand(
            Guid.NewGuid(), Guid.NewGuid(), "Renamed", null, null, null, null);
        _repo.GetByIdAsync(cmd.StrategyId, Arg.Any<CancellationToken>())
            .Returns((Strategy?)null);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.strategy");
    }

    [Fact]
    public async Task Handle_ForeignOwned_ReturnsNotFound()
    {
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var foreign = CreateActiveStrategy(ownerId, "Foreign");
        _repo.GetByIdAsync(foreign.Id, Arg.Any<CancellationToken>())
            .Returns(foreign);

        var cmd = new UpdateStrategyCommand(
            foreign.Id, otherId, "Renamed", null, null, null, null);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.strategy");
    }
}