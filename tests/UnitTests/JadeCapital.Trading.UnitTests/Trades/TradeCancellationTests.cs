namespace JadeCapital.Trading.UnitTests.Trades;

public class TradeCancellationTests
{
    private static readonly DateTimeOffset OpenedAt = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private static Trade CreateOpenTrade()
    {
        var symbol = Symbol.Create("EUR/USD").Value;
        var volume = Money.Create(1000m, Currency.Usd).Value;
        var entry = Money.Create(1.10m, Currency.Usd).Value;
        return Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), symbol, AssetClass.Forex, TradeDirection.Long,
            volume, entry, "USD", null, null, OpenedAt).Value;
    }

    [Fact]
    public void Cancel_OpenTrade_TransitionsToCancelled()
    {
        var trade = CreateOpenTrade();
        trade.ClearDomainEvents();
        var clock = Substitute.For<IClock>();
        var cancelTime = new DateTimeOffset(2026, 1, 15, 11, 0, 0, TimeSpan.Zero);
        clock.UtcNow.Returns(cancelTime);

        var r = trade.Cancel(clock);

        r.IsSuccess.Should().BeTrue();
        trade.Status.Should().Be(TradeStatus.Cancelled);
        trade.ClosedAt.Should().Be(cancelTime);
        trade.PnL.Should().BeNull();
        trade.DomainEvents.Should().ContainSingle(e => e is TradeCancelledDomainEvent);
    }

    [Fact]
    public void Cancel_AlreadyClosed_Fails()
    {
        var trade = CreateOpenTrade();
        trade.Close(Money.Create(1.20m, Currency.Usd).Value,
            new DateTimeOffset(2026, 1, 15, 14, 0, 0, TimeSpan.Zero), Substitute.For<IClock>());

        var r = trade.Cancel(Substitute.For<IClock>());

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("conflict.trade.already_closed");
    }

    [Fact]
    public void Cancel_AlreadyCancelled_Fails()
    {
        var trade = CreateOpenTrade();
        trade.Cancel(Substitute.For<IClock>());

        var r = trade.Cancel(Substitute.For<IClock>());

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("conflict.trade.already_closed");
    }
}
