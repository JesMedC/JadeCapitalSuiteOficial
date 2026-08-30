using FluentAssertions;
using JadeCapital.Trading.Application.Features.Strategies.GetStrategyAnalytics;
using JadeCapital.Trading.Contracts.Strategies;
using JadeCapital.Trading.Domain.Strategies;

namespace JadeCapital.Trading.UnitTests.Strategies;

// ============================================================================
//  GetStrategyAnalyticsHandlerTests — slice 3a.
//
//  Three scenarios:
//   1. Aggregates returned correctly (non-empty).
//   2. Empty state (count=0, all numerics 0 or null).
//   3. Foreign-owned strategy → NotFound.
// ============================================================================

public class GetStrategyAnalyticsHandlerTests
{
    private readonly IStrategyRepository _repo = Substitute.For<IStrategyRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public GetStrategyAnalyticsHandlerTests()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 18, 14, 0, 0, TimeSpan.Zero));
    }

    private GetStrategyAnalyticsHandler CreateSut() => new(_repo);

    private Strategy CreateActiveStrategy(Guid userId, string name)
        => Strategy.Create(userId, name, null, null, Timeframe.H1, null, _clock).Value;

    [Fact]
    public async Task Handle_PopulatedAnalytics_ReturnsDtoFromRepo()
    {
        var userId = Guid.NewGuid();
        var strategy = CreateActiveStrategy(userId, "London Break");
        _repo.GetByIdAsync(strategy.Id, Arg.Any<CancellationToken>())
            .Returns(strategy);

        var analytics = new StrategyAnalyticsDto(
            strategy.Id, strategy.Name,
            TradeCount: 10,
            WinCount: 6, LossCount: 4,
            WinRate: 0.60m,
            TotalPnl: 300m,
            Expectancy: 30m,
            ProfitFactor: 2.50m,
            AvgMfe: 60m,
            AvgMae: -15m,
            LastTradeAt: new DateTimeOffset(2026, 8, 17, 14, 0, 0, TimeSpan.Zero));
        _repo.GetAnalyticsAsync(userId, strategy.Id, Arg.Any<CancellationToken>())
            .Returns(analytics);

        var query = new GetStrategyAnalyticsQuery(strategy.Id, userId);

        var result = await CreateSut().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TradeCount.Should().Be(10);
        result.Value.WinRate.Should().Be(0.60m);
        result.Value.ProfitFactor.Should().Be(2.50m);
        result.Value.AvgMfe.Should().Be(60m);
        result.Value.AvgMae.Should().Be(-15m);
    }

    [Fact]
    public async Task Handle_EmptyAnalytics_ReturnsZeroOrNullFields()
    {
        var userId = Guid.NewGuid();
        var strategy = CreateActiveStrategy(userId, "Empty");
        _repo.GetByIdAsync(strategy.Id, Arg.Any<CancellationToken>())
            .Returns(strategy);

        var empty = new StrategyAnalyticsDto(
            strategy.Id, strategy.Name,
            TradeCount: 0, WinCount: 0, LossCount: 0,
            WinRate: 0m, TotalPnl: 0m, Expectancy: 0m,
            ProfitFactor: 0m, AvgMfe: null, AvgMae: null,
            LastTradeAt: null);
        _repo.GetAnalyticsAsync(userId, strategy.Id, Arg.Any<CancellationToken>())
            .Returns(empty);

        var query = new GetStrategyAnalyticsQuery(strategy.Id, userId);

        var result = await CreateSut().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TradeCount.Should().Be(0);
        result.Value.WinRate.Should().Be(0m);
        result.Value.ProfitFactor.Should().Be(0m);
        result.Value.AvgMfe.Should().BeNull();
        result.Value.AvgMae.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ForeignOwned_ReturnsNotFound()
    {
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var strategy = CreateActiveStrategy(ownerId, "Foreign");
        _repo.GetByIdAsync(strategy.Id, Arg.Any<CancellationToken>())
            .Returns(strategy);

        var query = new GetStrategyAnalyticsQuery(strategy.Id, otherId);

        var result = await CreateSut().Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.strategy");
        await _repo.DidNotReceive().GetAnalyticsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}