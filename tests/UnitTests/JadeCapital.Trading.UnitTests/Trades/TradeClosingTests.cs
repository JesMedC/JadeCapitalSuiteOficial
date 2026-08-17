namespace JadeCapital.Trading.UnitTests.Trades;

public class TradeClosingTests
{
    private static readonly DateTimeOffset OpenedAt = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ClosedAt = new(2026, 1, 15, 14, 0, 0, TimeSpan.Zero);

    private static Trade CreateOpenLong(decimal volumeAmount = 1000m, decimal entryPriceAmount = 1.10m)
    {
        var symbol = Symbol.Create("EUR/USD").Value;
        var volume = Money.Create(volumeAmount, Currency.Usd).Value;
        var entry = Money.Create(entryPriceAmount, Currency.Usd).Value;
        return Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            symbol, AssetClass.Forex, TradeDirection.Long,
            volume, entry, "USD", null, null, OpenedAt).Value;
    }

    private static Trade CreateOpenShort(decimal volumeAmount = 1000m, decimal entryPriceAmount = 1.10m)
    {
        var symbol = Symbol.Create("EUR/USD").Value;
        var volume = Money.Create(volumeAmount, Currency.Usd).Value;
        var entry = Money.Create(entryPriceAmount, Currency.Usd).Value;
        return Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            symbol, AssetClass.Forex, TradeDirection.Short,
            volume, entry, "USD", null, null, OpenedAt).Value;
    }

    private static IClock FixedClock() => Substitute.For<IClock>();

    [Fact]
    public void Close_LongWithPriceUp_ProducesPositivePnL()
    {
        var trade = CreateOpenLong(volumeAmount: 1000m, entryPriceAmount: 1.10m);
        trade.ClearDomainEvents();
        var clock = FixedClock();
        var exit = Money.Create(1.20m, Currency.Usd).Value;

        var r = trade.Close(exit, ClosedAt, clock);

        r.IsSuccess.Should().BeTrue();
        trade.Status.Should().Be(TradeStatus.Closed);
        trade.ExitPrice!.Amount.Should().Be(1.20m);
        trade.PnL!.Amount.Should().Be(100m); // (1.20 - 1.10) * 1000
        trade.PnL.Currency.Code.Should().Be("USD");
        trade.ClosedAt.Should().Be(ClosedAt);
        trade.DomainEvents.Should().ContainSingle(e => e is TradeClosedDomainEvent);
    }

    [Fact]
    public void Close_ShortWithPriceDown_ProducesPositivePnL()
    {
        var trade = CreateOpenShort(volumeAmount: 1000m, entryPriceAmount: 1.10m);
        trade.ClearDomainEvents();

        var r = trade.Close(Money.Create(1.00m, Currency.Usd).Value, ClosedAt, FixedClock());

        r.IsSuccess.Should().BeTrue();
        trade.PnL!.Amount.Should().Be(100m); // (1.10 - 1.00) * 1000
        trade.Status.Should().Be(TradeStatus.Closed);
    }

    [Fact]
    public void Close_LongWithPriceDown_ProducesNegativePnL()
    {
        var trade = CreateOpenLong(volumeAmount: 1000m, entryPriceAmount: 1.10m);

        var r = trade.Close(Money.Create(1.00m, Currency.Usd).Value, ClosedAt, FixedClock());

        r.IsSuccess.Should().BeTrue();
        trade.PnL!.Amount.Should().Be(-100m); // (1.00 - 1.10) * 1000
    }

    [Fact]
    public void Close_AlreadyClosed_Fails()
    {
        var trade = CreateOpenLong();
        trade.Close(Money.Create(1.20m, Currency.Usd).Value, ClosedAt, FixedClock());

        var r = trade.Close(Money.Create(1.30m, Currency.Usd).Value, ClosedAt, FixedClock());

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("conflict.trade.already_closed");
    }

    [Fact]
    public void Close_WithZeroExitPrice_Fails()
    {
        var trade = CreateOpenLong();

        var r = trade.Close(Money.Create(0m, Currency.Usd).Value, ClosedAt, FixedClock());

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade.exit_price_must_be_positive");
    }

    [Fact]
    public void Close_WithExitPriceCurrencyMismatch_Fails()
    {
        var trade = CreateOpenLong(); // entry en USD

        // exit en EUR: invalido.
        var r = trade.Close(Money.Create(1.20m, Currency.Eur).Value, ClosedAt, FixedClock());

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade.exit_price_currency_mismatch");
    }

    // ============================================================
    //  Slice 2c — MFE/MAE integration on Trade.Close.
    //
    //  The Wave 2 approximation runs synchronously inside Trade.Close so
    //  MFE/MAE end up on the same aggregate / same SaveChanges / same
    //  transaction as the close itself. After Close, the aggregate exposes
    //  MfeAmount / MaeAmount populated with the deterministic values from
    //  MfeMaeCalculator.
    // ============================================================

    [Fact]
    public void Close_WinnerLong_PopulatesMfeAndMae()
    {
        // 1000 units, entry 1.10 → exit 1.20 → PnL = +100. Winner.
        var trade = CreateOpenLong(volumeAmount: 1000m, entryPriceAmount: 1.10m);

        var r = trade.Close(Money.Create(1.20m, Currency.Usd).Value, ClosedAt, FixedClock());

        r.IsSuccess.Should().BeTrue();
        trade.MfeAmount.Should().Be(100m);
        trade.MaeAmount.Should().Be(0m);
        trade.MfeCurrency.Should().Be("USD");
        trade.MaeCurrency.Should().Be("USD");
    }

    [Fact]
    public void Close_LoserLong_PopulatesMfeAndMae()
    {
        // 1000 units, entry 1.10 → exit 1.00 → PnL = -100. Loser.
        var trade = CreateOpenLong(volumeAmount: 1000m, entryPriceAmount: 1.10m);

        var r = trade.Close(Money.Create(1.00m, Currency.Usd).Value, ClosedAt, FixedClock());

        r.IsSuccess.Should().BeTrue();
        trade.MfeAmount.Should().Be(0m);
        trade.MaeAmount.Should().Be(-100m);
        trade.MfeCurrency.Should().Be("USD");
        trade.MaeCurrency.Should().Be("USD");
    }

    // ============================================================
    //  Slice 2c — ApplyMfeMae validation invariants.
    //
    //  ApplyMfeMae is also exposed for admin / batch recompute use cases
    //  beyond Trade.Close. The validation rules must hold regardless of
    //  the caller (Close path, admin correction, data import).
    // ============================================================

    [Fact]
    public void ApplyMfeMae_NegativeMfe_Fails()
    {
        var trade = CreateOpenLong();

        var r = trade.ApplyMfeMae(mfe: -1m, mae: 0m, currency: "USD");

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade.mfe_must_be_non_negative");
        // Aggregate state remains untouched on failure.
        trade.MfeAmount.Should().BeNull();
        trade.MaeAmount.Should().BeNull();
    }

    [Fact]
    public void ApplyMfeMae_PositiveMae_Fails()
    {
        var trade = CreateOpenLong();

        var r = trade.ApplyMfeMae(mfe: 0m, mae: 1m, currency: "USD");

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade.mae_must_be_non_positive");
        trade.MfeAmount.Should().BeNull();
        trade.MaeAmount.Should().BeNull();
    }
}
