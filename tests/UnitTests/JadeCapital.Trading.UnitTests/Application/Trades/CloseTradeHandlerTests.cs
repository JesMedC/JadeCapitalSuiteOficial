using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Trading.UnitTests.Application.Trades;

public class CloseTradeHandlerTests
{
    private readonly ITradeRepository _trades = Substitute.For<ITradeRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILogger<CloseTradeHandler> _logger = Substitute.For<ILogger<CloseTradeHandler>>();

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 15, 14, 0, 0, TimeSpan.Zero);

    private CloseTradeHandler CreateSut() => new(_trades, _uow, _clock, _logger);

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
    public async Task Handle_OpenTradeWithPriceUp_ClosesWithPositivePnL()
    {
        _clock.UtcNow.Returns(FixedNow);
        var userId = Guid.NewGuid();
        var trade = CreateOpenTrade(userId);
        trade.ClearDomainEvents();
        _trades.FindByIdAsync(trade.Id, Arg.Any<CancellationToken>()).Returns(trade);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = new CloseTradeCommand(trade.Id, userId, 1.20m, "USD");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        trade.Status.Should().Be(TradeStatus.Closed);
        trade.ExitPrice!.Amount.Should().Be(1.20m);
        trade.PnL!.Amount.Should().Be(100m);
        trade.ClosedAt.Should().Be(FixedNow);
        result.Value.Status.Should().Be(TradeStatus.Closed);
        result.Value.Pnl.Should().Be(100m);
    }

    [Fact]
    public async Task Handle_AlreadyClosedTrade_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var trade = CreateOpenTrade(userId);
        // Lo cerramos primero.
        trade.Close(Money.Create(1.20m, Currency.Usd).Value,
            new DateTimeOffset(2026, 6, 15, 13, 0, 0, TimeSpan.Zero),
            Substitute.For<IClock>());
        _trades.FindByIdAsync(trade.Id, Arg.Any<CancellationToken>()).Returns(trade);

        var cmd = new CloseTradeCommand(trade.Id, userId, 1.30m, "USD");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.trade.already_closed");
    }

    [Fact]
    public async Task Handle_TradeNotFound_ReturnsNotFound()
    {
        _trades.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var cmd = new CloseTradeCommand(Guid.NewGuid(), Guid.NewGuid(), 1.20m, "USD");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.trade");
    }

    [Fact]
    public async Task Handle_TradeOwnedByOtherUser_ReturnsNotFound()
    {
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var trade = CreateOpenTrade(ownerId);
        _trades.FindByIdAsync(trade.Id, Arg.Any<CancellationToken>()).Returns(trade);

        var cmd = new CloseTradeCommand(trade.Id, otherId, 1.20m, "USD");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        // Unificamos missing + foreign ownership -> NotFound (no leak).
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.trade");
    }
}