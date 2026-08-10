using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Trading.UnitTests.Application.Trades;

public class GetTradeByIdHandlerTests
{
    private readonly ITradeRepository _trades = Substitute.For<ITradeRepository>();

    private GetTradeByIdHandler CreateSut() => new(_trades);

    private static Trade CreateOpenTrade(Guid userId)
    {
        var symbol = Symbol.Create("EUR/USD").Value;
        var volume = Money.Create(1000m, Currency.Usd).Value;
        var entry = Money.Create(1.10m, Currency.Usd).Value;
        return Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), userId,
            symbol, AssetClass.Forex, TradeDirection.Long,
            volume, entry, "USD", null, null,
            new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero)).Value;
    }

    [Fact]
    public async Task Handle_OwnTrade_ReturnsDto()
    {
        var userId = Guid.NewGuid();
        var trade = CreateOpenTrade(userId);
        _trades.FindByIdAsync(trade.Id, Arg.Any<CancellationToken>()).Returns(trade);

        var result = await CreateSut().Handle(new GetTradeByIdQuery(trade.Id, userId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(trade.Id);
    }

    [Fact]
    public async Task Handle_OtherUsersTrade_ReturnsNotFound()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var trade = CreateOpenTrade(owner);
        _trades.FindByIdAsync(trade.Id, Arg.Any<CancellationToken>()).Returns(trade);

        var result = await CreateSut().Handle(new GetTradeByIdQuery(trade.Id, other), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.trade");
    }

    [Fact]
    public async Task Handle_MissingTrade_ReturnsNotFound()
    {
        _trades.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var result = await CreateSut().Handle(new GetTradeByIdQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.trade");
    }
}