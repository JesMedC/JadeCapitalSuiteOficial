using FluentAssertions;
using JadeCapital.Trading.Application.Features.Strategies.SetTradeStrategy;
using JadeCapital.Trading.Domain.Strategies;
using JadeCapital.Shared.Kernel.Money;

namespace JadeCapital.Trading.UnitTests.Strategies;

// ============================================================================
//  SetTradeStrategyHandlerTests — slice 3a.
//
//  Three scenarios:
//   1. Valid tag (user's own active strategy) sets Trade.StrategyId and
//      returns the StrategyDto.
//   2. Trade not found (or foreign-owned) → NotFound.
//   3. Strategy not user's (or inactive) → NotFound (no leak existence).
// ============================================================================

public class SetTradeStrategyHandlerTests
{
    private readonly ITradeRepository _trades = Substitute.For<ITradeRepository>();
    private readonly IStrategyRepository _strategies = Substitute.For<IStrategyRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public SetTradeStrategyHandlerTests()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 18, 14, 0, 0, TimeSpan.Zero));
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(1));
    }

    private SetTradeStrategyHandler CreateSut()
        => new(_trades, _strategies, _uow);

    private static Trade CreateOpenTrade(Guid userId)
    {
        var symbol = Symbol.Create("EUR/USD").Value;
        var volume = Money.Create(1000m, Currency.Usd).Value;
        var entry = Money.Create(1.10m, Currency.Usd).Value;
        return Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), userId,
            symbol, AssetClass.Forex, TradeDirection.Long,
            volume, entry, "USD", null, null,
            new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero)).Value;
    }

    [Fact]
    public async Task Handle_ValidTag_SetsStrategyIdAndReturnsStrategyDto()
    {
        var userId = Guid.NewGuid();
        var trade = CreateOpenTrade(userId);
        var strategy = Strategy.Create(
            userId, "London", null, null, Timeframe.H1, null, _clock).Value;

        _trades.FindByIdAsync(trade.Id, Arg.Any<CancellationToken>())
            .Returns(trade);
        _strategies.GetByIdAsync(strategy.Id, Arg.Any<CancellationToken>())
            .Returns(strategy);

        var cmd = new SetTradeStrategyCommand(trade.Id, userId, strategy.Id);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        trade.StrategyId.Should().Be(strategy.Id);
        result.Value.Id.Should().Be(strategy.Id);
        await _trades.Received(1).UpdateAsync(trade, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TradeNotFound_ReturnsTradeNotFound()
    {
        var cmd = new SetTradeStrategyCommand(Guid.NewGuid(), Guid.NewGuid(), null);
        _trades.FindByIdAsync(cmd.TradeId, Arg.Any<CancellationToken>())
            .Returns((Trade?)null);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.trade");
        await _strategies.DidNotReceive().GetByIdAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StrategyNotYours_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var trade = CreateOpenTrade(userId);
        var foreignStrategy = Strategy.Create(
            otherUserId, "Foreign", null, null, Timeframe.H1, null, _clock).Value;

        _trades.FindByIdAsync(trade.Id, Arg.Any<CancellationToken>())
            .Returns(trade);
        _strategies.GetByIdAsync(foreignStrategy.Id, Arg.Any<CancellationToken>())
            .Returns(foreignStrategy);

        var cmd = new SetTradeStrategyCommand(trade.Id, userId, foreignStrategy.Id);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.strategy");
        await _trades.DidNotReceive().UpdateAsync(Arg.Any<Trade>(), Arg.Any<CancellationToken>());
    }
}