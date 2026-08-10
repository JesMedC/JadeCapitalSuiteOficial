namespace JadeCapital.Trading.UnitTests.Application.Dashboard;

public class GetPnlCalendarHandlerTests
{
    private readonly ITradeRepository _trades = Substitute.For<ITradeRepository>();

    private GetPnlCalendarHandler CreateSut() => new(_trades);

    private static Trade CreateClosedTrade(Guid userId, decimal entry, decimal exit, DateTimeOffset closedAt)
    {
        var symbol = Symbol.Create("EUR/USD").Value;
        var v = Money.Create(1000m, Currency.Usd).Value;
        var e = Money.Create(entry, Currency.Usd).Value;
        var trade = Trade.Open(
            Guid.NewGuid(), userId, symbol, AssetClass.Forex, TradeDirection.Long,
            v, e, "USD", null, null,
            closedAt.AddHours(-4)).Value;
        trade.Close(Money.Create(exit, Currency.Usd).Value, closedAt, Substitute.For<IClock>());
        return trade;
    }

    [Fact]
    public async Task Handle_MultipleClosedTradesInMonth_GroupsByDay()
    {
        var userId = Guid.NewGuid();
        var day1 = new DateTimeOffset(2026, 6, 5, 14, 0, 0, TimeSpan.Zero);
        var day5 = new DateTimeOffset(2026, 6, 10, 9, 30, 0, TimeSpan.Zero);
        var day10 = new DateTimeOffset(2026, 6, 15, 16, 0, 0, TimeSpan.Zero);

        var t1 = CreateClosedTrade(userId, 1.10m, 1.20m, day1); // +100
        var t2 = CreateClosedTrade(userId, 1.10m, 1.15m, day5); // +50
        var t3 = CreateClosedTrade(userId, 1.10m, 1.05m, day10); // -50

        _trades.ListByUserIdAndOpenedAtRangeAsync(
                userId,
                new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                Arg.Any<CancellationToken>())
            .Returns(new List<Trade> { t1, t2, t3 });

        var result = await CreateSut().Handle(new GetPnlCalendarQuery(userId, 2026, 6), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Year.Should().Be(2026);
        dto.Month.Should().Be(6);
        dto.Days.Should().HaveCount(3);
        dto.Days[0].Date.Should().Be(new DateOnly(2026, 6, 5));
        dto.Days[0].Pnl.Should().Be(100m);
        dto.Days[0].TradeCount.Should().Be(1);
        dto.Days[1].Date.Should().Be(new DateOnly(2026, 6, 10));
        dto.Days[1].Pnl.Should().Be(50m);
        dto.Days[2].Date.Should().Be(new DateOnly(2026, 6, 15));
        dto.Days[2].Pnl.Should().Be(-50m);
    }

    [Fact]
    public async Task Handle_NoTradesInMonth_ReturnsEmptyDays()
    {
        var userId = Guid.NewGuid();
        _trades.ListByUserIdAndOpenedAtRangeAsync(
                userId,
                new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                Arg.Any<CancellationToken>())
            .Returns(new List<Trade>());

        var result = await CreateSut().Handle(new GetPnlCalendarQuery(userId, 2026, 6), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Days.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MultipleTradesOnSameDay_AggregatesPnl()
    {
        var userId = Guid.NewGuid();
        var sameDay1 = new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.Zero);
        var sameDay2 = new DateTimeOffset(2026, 6, 5, 16, 0, 0, TimeSpan.Zero);
        var t1 = CreateClosedTrade(userId, 1.10m, 1.20m, sameDay1); // +100
        var t2 = CreateClosedTrade(userId, 1.10m, 1.05m, sameDay2); // -50

        _trades.ListByUserIdAndOpenedAtRangeAsync(
                userId,
                new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                Arg.Any<CancellationToken>())
            .Returns(new List<Trade> { t1, t2 });

        var result = await CreateSut().Handle(new GetPnlCalendarQuery(userId, 2026, 6), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Days.Should().HaveCount(1);
        result.Value.Days[0].Date.Should().Be(new DateOnly(2026, 6, 5));
        result.Value.Days[0].Pnl.Should().Be(50m); // +100 - 50
        result.Value.Days[0].TradeCount.Should().Be(2);
    }
}