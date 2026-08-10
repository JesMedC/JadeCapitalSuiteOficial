namespace JadeCapital.Trading.UnitTests.Application.Trades;

public class GetTradesHandlerTests
{
    private readonly ITradeRepository _trades = Substitute.For<ITradeRepository>();
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IInstrumentRepository _instruments = Substitute.For<IInstrumentRepository>();

    private GetTradesHandler CreateSut() => new(_trades, _accounts, _instruments);

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
    public async Task Handle_PageAndPageSize_ForwardedToRepository()
    {
        var userId = Guid.NewGuid();
        var items = new List<Trade> { CreateOpenTrade(userId), CreateOpenTrade(userId) };
        _trades.ListByUserIdAsync(userId, 2, 25, Arg.Any<CancellationToken>(),
                Arg.Any<TradeStatus?>(), Arg.Any<string?>(), Arg.Any<Guid?>())
            .Returns(items);
        _trades.CountByUserIdAsync(userId, Arg.Any<CancellationToken>(),
                Arg.Any<TradeStatus?>(), Arg.Any<string?>(), Arg.Any<Guid?>())
            .Returns(57);

        var cmd = new GetTradesQuery(userId, Page: 2, PageSize: 25);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.Total.Should().Be(57);
        result.Value.Page.Should().Be(2);
        result.Value.PageSize.Should().Be(25);

        await _trades.Received(1).ListByUserIdAsync(
            userId, 2, 25, Arg.Any<CancellationToken>(),
            Arg.Any<TradeStatus?>(), Arg.Any<string?>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_StatusAndSymbolFilters_ForwardedToRepository()
    {
        var userId = Guid.NewGuid();
        _trades.ListByUserIdAsync(userId, 1, 20, Arg.Any<CancellationToken>(),
                TradeStatus.Closed, "EUR/USD", null)
            .Returns(new List<Trade>());
        _trades.CountByUserIdAsync(userId, Arg.Any<CancellationToken>(),
                TradeStatus.Closed, "EUR/USD", null)
            .Returns(0);

        var cmd = new GetTradesQuery(userId, Page: 1, PageSize: 20,
            StatusFilter: TradeStatus.Closed, SymbolFilter: "EUR/USD");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _trades.Received(1).ListByUserIdAsync(
            userId, 1, 20, Arg.Any<CancellationToken>(), TradeStatus.Closed, "EUR/USD", null);
    }

    [Fact]
    public async Task Handle_PageSizeAboveMax_CappedToHundred()
    {
        var userId = Guid.NewGuid();
        _trades.ListByUserIdAsync(userId, 1, 100, Arg.Any<CancellationToken>(),
                Arg.Any<TradeStatus?>(), Arg.Any<string?>(), Arg.Any<Guid?>())
            .Returns(new List<Trade>());
        _trades.CountByUserIdAsync(userId, Arg.Any<CancellationToken>(),
                Arg.Any<TradeStatus?>(), Arg.Any<string?>(), Arg.Any<Guid?>())
            .Returns(0);

        var cmd = new GetTradesQuery(userId, Page: 1, PageSize: 150);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PageSize.Should().Be(100); // cap defensivo
        await _trades.Received(1).ListByUserIdAsync(
            userId, 1, 100, Arg.Any<CancellationToken>(),
            Arg.Any<TradeStatus?>(), Arg.Any<string?>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_PageBelowOne_NormalizedToOne()
    {
        var userId = Guid.NewGuid();
        _trades.ListByUserIdAsync(userId, 1, 20, Arg.Any<CancellationToken>(),
                Arg.Any<TradeStatus?>(), Arg.Any<string?>(), Arg.Any<Guid?>())
            .Returns(new List<Trade>());
        _trades.CountByUserIdAsync(userId, Arg.Any<CancellationToken>(),
                Arg.Any<TradeStatus?>(), Arg.Any<string?>(), Arg.Any<Guid?>())
            .Returns(0);

        var cmd = new GetTradesQuery(userId, Page: 0, PageSize: 20);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Page.Should().Be(1);
    }

    [Fact]
    public async Task Handle_AccountIdFilter_ForwardedToRepository()
    {
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        _trades.ListByUserIdAsync(userId, 1, 20, Arg.Any<CancellationToken>(),
                Arg.Any<TradeStatus?>(), Arg.Any<string?>(), accountId)
            .Returns(new List<Trade>());
        _trades.CountByUserIdAsync(userId, Arg.Any<CancellationToken>(),
                Arg.Any<TradeStatus?>(), Arg.Any<string?>(), accountId)
            .Returns(0);

        var cmd = new GetTradesQuery(userId, Page: 1, PageSize: 20, AccountIdFilter: accountId);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _trades.Received(1).ListByUserIdAsync(
            userId, 1, 20, Arg.Any<CancellationToken>(),
            Arg.Any<TradeStatus?>(), Arg.Any<string?>(), accountId);
        await _trades.Received(1).CountByUserIdAsync(
            userId, Arg.Any<CancellationToken>(),
            Arg.Any<TradeStatus?>(), Arg.Any<string?>(), accountId);
    }
}