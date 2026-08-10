namespace JadeCapital.Trading.UnitTests.Application.Dashboard;

public class GetDashboardSummaryHandlerTests
{
    private readonly ITradeRepository _trades = Substitute.For<ITradeRepository>();

    private GetDashboardSummaryHandler CreateSut() => new(_trades);

    private static readonly DateTimeOffset From = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private static Trade CreateClosedLong(Guid userId, decimal entry, decimal exit, decimal volume = 1000m)
    {
        var symbol = Symbol.Create("EUR/USD").Value;
        var v = Money.Create(volume, Currency.Usd).Value;
        var e = Money.Create(entry, Currency.Usd).Value;
        var trade = Trade.Open(
            Guid.NewGuid(), userId, symbol, AssetClass.Forex, TradeDirection.Long,
            v, e, "USD", null, null,
            new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero)).Value;
        trade.Close(Money.Create(exit, Currency.Usd).Value,
            new DateTimeOffset(2026, 6, 15, 14, 0, 0, TimeSpan.Zero),
            Substitute.For<IClock>());
        return trade;
    }

    private static Trade CreateClosedShort(Guid userId, decimal entry, decimal exit, decimal volume = 1000m)
    {
        var symbol = Symbol.Create("EUR/USD").Value;
        var v = Money.Create(volume, Currency.Usd).Value;
        var e = Money.Create(entry, Currency.Usd).Value;
        var trade = Trade.Open(
            Guid.NewGuid(), userId, symbol, AssetClass.Forex, TradeDirection.Short,
            v, e, "USD", null, null,
            new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero)).Value;
        trade.Close(Money.Create(exit, Currency.Usd).Value,
            new DateTimeOffset(2026, 6, 15, 14, 0, 0, TimeSpan.Zero),
            Substitute.For<IClock>());
        return trade;
    }

    private static Trade CreateOpenTrade(Guid userId)
    {
        var symbol = Symbol.Create("EUR/USD").Value;
        var v = Money.Create(1000m, Currency.Usd).Value;
        var e = Money.Create(1.10m, Currency.Usd).Value;
        return Trade.Open(
            Guid.NewGuid(), userId, symbol, AssetClass.Forex, TradeDirection.Long,
            v, e, "USD", null, null,
            new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero)).Value;
    }

    [Fact]
    public async Task Handle_MixOfWinsLossesAndOpens_ComputesAllKpis()
    {
        var userId = Guid.NewGuid();
        // Wins: 2 (150 + 50 = 200).
        var win1 = CreateClosedLong(userId, entry: 1.10m, exit: 1.25m); // +150
        var win2 = CreateClosedLong(userId, entry: 1.10m, exit: 1.15m); // +50
        // Loss: 1 (-100).
        var loss1 = CreateClosedLong(userId, entry: 1.10m, exit: 1.00m); // -100
        // Open: 1.
        var open1 = CreateOpenTrade(userId);

        _trades.ListByUserIdAndOpenedAtRangeAsync(userId, From, To, Arg.Any<CancellationToken>())
            .Returns(new List<Trade> { win1, win2, loss1, open1 });

        var result = await CreateSut().Handle(
            new GetDashboardSummaryQuery(userId, From, To), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.TotalCount.Should().Be(4);
        dto.OpenCount.Should().Be(1);
        dto.ClosedCount.Should().Be(3);
        dto.WinsCount.Should().Be(2);
        dto.LossesCount.Should().Be(1);
        dto.WinRate.Should().BeApproximately(0.6667m, 0.001m);
        dto.TotalPnL.Should().Be(100m); // 150 + 50 - 100
        dto.BestTrade.Should().Be(150m);
        dto.WorstTrade.Should().Be(-100m);
        dto.AvgTrade.Should().BeApproximately(33.3333m, 0.001m);
        dto.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Handle_NoTrades_ReturnsZeroedKpisAndDefaultCurrency()
    {
        var userId = Guid.NewGuid();
        _trades.ListByUserIdAndOpenedAtRangeAsync(userId, From, To, Arg.Any<CancellationToken>())
            .Returns(new List<Trade>());

        var result = await CreateSut().Handle(
            new GetDashboardSummaryQuery(userId, From, To), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.TotalCount.Should().Be(0);
        dto.ClosedCount.Should().Be(0);
        dto.WinsCount.Should().Be(0);
        dto.WinRate.Should().Be(0m);
        dto.TotalPnL.Should().Be(0m);
        dto.BestTrade.Should().Be(0m);
        dto.WorstTrade.Should().Be(0m);
        dto.AvgTrade.Should().Be(0m);
        dto.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Handle_ShortWinnerWithBetterPrice_ProducesPositivePnL()
    {
        var userId = Guid.NewGuid();
        // Short entry 1.20, exit 1.10 -> (1.20 - 1.10) * 1000 = +100.
        var shortWin = CreateClosedShort(userId, entry: 1.20m, exit: 1.10m);

        _trades.ListByUserIdAndOpenedAtRangeAsync(userId, From, To, Arg.Any<CancellationToken>())
            .Returns(new List<Trade> { shortWin });

        var result = await CreateSut().Handle(
            new GetDashboardSummaryQuery(userId, From, To), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.WinsCount.Should().Be(1);
        result.Value.TotalPnL.Should().Be(100m);
        result.Value.BestTrade.Should().Be(100m);
    }
}