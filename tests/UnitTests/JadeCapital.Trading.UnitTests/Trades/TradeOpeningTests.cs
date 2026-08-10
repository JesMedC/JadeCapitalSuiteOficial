namespace JadeCapital.Trading.UnitTests.Trades;

public class TradeOpeningTests
{
    private static readonly DateTimeOffset OpenedAt = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private static Symbol CreateValidSymbol() => Symbol.Create("EUR/USD").Value;
    private static Money CreateValidVolume() => Money.Create(1000m, Currency.Usd).Value;
    private static Money CreateValidEntryPrice() => Money.Create(1.10m, Currency.Usd).Value;

    [Fact]
    public void Open_WithValidData_CreatesOpenTrade()
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var r = Trade.Open(
            id, userId, CreateValidSymbol(), AssetClass.Forex, TradeDirection.Long,
            CreateValidVolume(), CreateValidEntryPrice(), "USD", "trend-following",
            "Some notes here", OpenedAt);

        r.IsSuccess.Should().BeTrue();
        var t = r.Value;
        t.Id.Should().Be(id);
        t.UserId.Should().Be(userId);
        t.Symbol.Value.Should().Be("EUR/USD");
        t.AssetClass.Should().Be(AssetClass.Forex);
        t.Direction.Should().Be(TradeDirection.Long);
        t.Status.Should().Be(TradeStatus.Open);
        t.Volume.Amount.Should().Be(1000m);
        t.EntryPrice.Amount.Should().Be(1.10m);
        t.ExitPrice.Should().BeNull();
        t.PnL.Should().BeNull();
        t.ClosedAt.Should().BeNull();
        t.Strategy.Should().Be("trend-following");
        t.Notes.Should().Be("Some notes here");
        t.AccountCurrency.Should().Be("USD");
        t.DomainEvents.Should().ContainSingle(e => e is TradeOpenedDomainEvent);
    }

    [Fact]
    public void Open_WithZeroVolume_Fails()
    {
        var r = Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), CreateValidSymbol(), AssetClass.Forex,
            TradeDirection.Long, Money.Create(0m, Currency.Usd).Value,
            CreateValidEntryPrice(), "USD", null, null, OpenedAt);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade.volume_must_be_positive");
    }

    [Fact]
    public void Open_WithNegativeVolume_Fails()
    {
        var r = Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), CreateValidSymbol(), AssetClass.Forex,
            TradeDirection.Long, Money.Create(-10m, Currency.Usd).Value,
            CreateValidEntryPrice(), "USD", null, null, OpenedAt);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade.volume_must_be_positive");
    }

    [Fact]
    public void Open_WithZeroEntryPrice_Fails()
    {
        var r = Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), CreateValidSymbol(), AssetClass.Forex,
            TradeDirection.Long, CreateValidVolume(),
            Money.Create(0m, Currency.Usd).Value, "USD", null, null, OpenedAt);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade.entry_price_must_be_positive");
    }

    [Fact]
    public void Open_WithEntryPriceCurrencyMismatch_Fails()
    {
        // EUR/USD: quote es USD. Pasar EUR debe fallar.
        var eurEntry = Money.Create(1.10m, Currency.Eur).Value;

        var r = Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), CreateValidSymbol(), AssetClass.Forex,
            TradeDirection.Long, CreateValidVolume(), eurEntry, "USD", null, null, OpenedAt);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade.entry_price_currency_mismatch");
    }

    [Fact]
    public void Open_WithEmptyId_Fails()
    {
        var r = Trade.Open(
            Guid.Empty, Guid.NewGuid(), CreateValidSymbol(), AssetClass.Forex,
            TradeDirection.Long, CreateValidVolume(), CreateValidEntryPrice(),
            "USD", null, null, OpenedAt);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade.id_required");
    }

    [Fact]
    public void Open_WithEmptyUserId_Fails()
    {
        var r = Trade.Open(
            Guid.NewGuid(), Guid.Empty, CreateValidSymbol(), AssetClass.Forex,
            TradeDirection.Long, CreateValidVolume(), CreateValidEntryPrice(),
            "USD", null, null, OpenedAt);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade.user_id_required");
    }

    [Fact]
    public void Open_WithStrategyTooLong_Fails()
    {
        var longStrategy = new string('x', Trade.MaxStrategyLength + 1);

        var r = Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), CreateValidSymbol(), AssetClass.Forex,
            TradeDirection.Long, CreateValidVolume(), CreateValidEntryPrice(),
            "USD", longStrategy, null, OpenedAt);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade.strategy_too_long");
    }

    [Fact]
    public void Open_WithNotesTooLong_Fails()
    {
        var longNotes = new string('n', Trade.MaxNotesLength + 1);

        var r = Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), CreateValidSymbol(), AssetClass.Forex,
            TradeDirection.Long, CreateValidVolume(), CreateValidEntryPrice(),
            "USD", null, longNotes, OpenedAt);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade.notes_too_long");
    }

    [Fact]
    public void Open_WithEmptyAccountCurrency_Fails()
    {
        var r = Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), CreateValidSymbol(), AssetClass.Forex,
            TradeDirection.Long, CreateValidVolume(), CreateValidEntryPrice(),
            "", null, null, OpenedAt);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade.account_currency_required");
    }

    [Fact]
    public void Open_LowercaseAccountCurrency_NormalizesToUppercase()
    {
        var r = Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), CreateValidSymbol(), AssetClass.Forex,
            TradeDirection.Long, CreateValidVolume(), CreateValidEntryPrice(),
            "usd", null, null, OpenedAt);

        r.IsSuccess.Should().BeTrue();
        r.Value.AccountCurrency.Should().Be("USD");
    }
}
