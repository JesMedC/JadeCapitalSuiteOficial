using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Trading.UnitTests.Application.Trades;

public class DeleteTradeHandlerTests
{
    private readonly ITradeRepository _trades = Substitute.For<ITradeRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ILogger<DeleteTradeHandler> _logger = Substitute.For<ILogger<DeleteTradeHandler>>();

    private DeleteTradeHandler CreateSut() => new(_trades, _uow, _logger);

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
    public async Task Handle_OpenTrade_RemovesSuccessfully()
    {
        var userId = Guid.NewGuid();
        var trade = CreateOpenTrade(userId);
        _trades.FindByIdAsync(trade.Id, Arg.Any<CancellationToken>()).Returns(trade);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = new DeleteTradeCommand(trade.Id, userId);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _trades.Received(1).DeleteAsync(trade, Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ClosedTrade_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var trade = CreateOpenTrade(userId);
        trade.Close(Money.Create(1.20m, Currency.Usd).Value,
            new DateTimeOffset(2026, 6, 15, 13, 0, 0, TimeSpan.Zero),
            Substitute.For<IClock>());
        _trades.FindByIdAsync(trade.Id, Arg.Any<CancellationToken>()).Returns(trade);

        var cmd = new DeleteTradeCommand(trade.Id, userId);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.trade.cannot_delete_closed");
    }

    [Fact]
    public async Task Handle_TradeNotFound_ReturnsNotFound()
    {
        _trades.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var cmd = new DeleteTradeCommand(Guid.NewGuid(), Guid.NewGuid());
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.trade");
    }

    [Fact]
    public async Task Handle_CancelledTrade_RemovesSuccessfully()
    {
        var userId = Guid.NewGuid();
        var trade = CreateOpenTrade(userId);
        trade.Cancel(Substitute.For<IClock>());
        _trades.FindByIdAsync(trade.Id, Arg.Any<CancellationToken>()).Returns(trade);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = new DeleteTradeCommand(trade.Id, userId);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}