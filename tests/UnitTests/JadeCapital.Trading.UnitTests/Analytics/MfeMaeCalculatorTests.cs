using JadeCapital.Trading.Domain.Analytics;

namespace JadeCapital.Trading.UnitTests.Analytics;

// ============================================================================
//  MfeMaeCalculator — slice 2c (Trader Journal Core / MFE/MAE Charts).
//
//  8 unit tests covering the Wave 2 deterministic approximation from
//  openspec/changes/2026-08-17-trader-journal-core/specs/mfe-mae-charts/spec.md:
//
//   1. Winner long    → MFE = P&L,  MAE = 0
//   2. Loser long     → MAE = -|P&L|, MFE = 0
//   3. Winner short   → MFE = P&L,  MAE = 0
//   4. Loser short    → MAE = -|P&L|, MFE = 0
//   5. Open trade     → MFE = null, MAE = null
//   6. Zero P&L       → MFE = 0, MAE = 0 (break-even)
//   7. Large winner   → no decimal overflow
//   8. Currency prop. → returns account currency
//
//  Strict TDD: these tests reference MfeMaeCalculator before it exists in
//  the domain assembly. RED phase. GREEN happens when the calculator is
//  implemented.
//
//  Test fixture pattern: open a trade via the public Trade.Open factory,
//  close it via Trade.Close, then drive MfeMaeCalculator.Compute(trade).
//  Using the public API (instead of constructors) keeps the tests honest —
//  any invariant that holds in real code holds here.
// ============================================================================

public class MfeMaeCalculatorTests
{
    private static readonly DateTimeOffset OpenedAt =
        new(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ClosedAt =
        new(2026, 8, 17, 14, 0, 0, TimeSpan.Zero);

    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid InstrumentId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    /// <summary>
    /// Open + (optionally) close a long trade so the calculator can be
    /// driven against a realistic <see cref="Trade"/> aggregate.
    /// </summary>
    private static Trade BuildLong(
        decimal volume,
        decimal entry,
        decimal exit,
        string accountCurrency = "USD")
    {
        var symbol = Symbol.Create("EUR/USD").Value;
        var trade = Trade.Open(
            Guid.NewGuid(),
            AccountId,
            InstrumentId,
            UserId,
            symbol,
            AssetClass.Forex,
            TradeDirection.Long,
            Money.Create(volume, Currency.Usd).Value,
            Money.Create(entry, Currency.Usd).Value,
            accountCurrency,
            null,
            null,
            OpenedAt).Value;

        if (exit > 0m)
        {
            trade.Close(
                Money.Create(exit, Currency.Usd).Value,
                ClosedAt,
                Substitute.For<IClock>());
        }

        return trade;
    }

    /// <summary>Same as <see cref="BuildLong"/> but for shorts.</summary>
    private static Trade BuildShort(
        decimal volume,
        decimal entry,
        decimal exit,
        string accountCurrency = "USD")
    {
        var symbol = Symbol.Create("EUR/USD").Value;
        var trade = Trade.Open(
            Guid.NewGuid(),
            AccountId,
            InstrumentId,
            UserId,
            symbol,
            AssetClass.Forex,
            TradeDirection.Short,
            Money.Create(volume, Currency.Usd).Value,
            Money.Create(entry, Currency.Usd).Value,
            accountCurrency,
            null,
            null,
            OpenedAt).Value;

        if (exit > 0m)
        {
            trade.Close(
                Money.Create(exit, Currency.Usd).Value,
                ClosedAt,
                Substitute.For<IClock>());
        }

        return trade;
    }

    // ============================================================
    //  Wave 2 deterministic approximation
    // ============================================================

    [Fact]
    public void WinnerLong_MfeEqualsPnl_MaeIsZero()
    {
        // 1000 units × 0.005 = +5 PnL (Long, price went up).
        var trade = BuildLong(volume: 1000m, entry: 1.10m, exit: 1.105m);

        var (mfe, mae, currency) = MfeMaeCalculator.Compute(trade);

        mfe.Should().Be(5m);
        mae.Should().Be(0m);
        currency.Should().Be("USD");
    }

    [Fact]
    public void LoserLong_MaeEqualsNegAbsPnl_MfeIsZero()
    {
        // 1000 units × (-0.005) = -5 PnL (Long, price went down).
        var trade = BuildLong(volume: 1000m, entry: 1.10m, exit: 1.095m);

        var (mfe, mae, currency) = MfeMaeCalculator.Compute(trade);

        mfe.Should().Be(0m);
        mae.Should().Be(-5m);
        currency.Should().Be("USD");
    }

    [Fact]
    public void WinnerShort_MfeEqualsPnl_MaeIsZero()
    {
        // Short: profit when price drops. 1000 × 0.005 = +5 PnL.
        var trade = BuildShort(volume: 1000m, entry: 1.10m, exit: 1.095m);

        var (mfe, mae, currency) = MfeMaeCalculator.Compute(trade);

        mfe.Should().Be(5m);
        mae.Should().Be(0m);
        currency.Should().Be("USD");
    }

    [Fact]
    public void LoserShort_MaeEqualsNegAbsPnl_MfeIsZero()
    {
        // Short: loss when price rises. 1000 × (-0.005) = -5 PnL.
        var trade = BuildShort(volume: 1000m, entry: 1.10m, exit: 1.105m);

        var (mfe, mae, currency) = MfeMaeCalculator.Compute(trade);

        mfe.Should().Be(0m);
        mae.Should().Be(-5m);
        currency.Should().Be("USD");
    }

    [Fact]
    public void OpenTrade_MfeAndMaeAreNull()
    {
        // No close → no PnL → no approximation.
        var trade = BuildLong(volume: 1000m, entry: 1.10m, exit: 0m);

        var (mfe, mae, currency) = MfeMaeCalculator.Compute(trade);

        mfe.Should().BeNull();
        mae.Should().BeNull();
        // Currency still comes from the trade's AccountCurrency even when
        // MFE/MAE are null — the wire contract always returns a currency.
        currency.Should().Be("USD");
    }

    [Fact]
    public void ZeroPnl_BreakEven_MfeAndMaeAreZero()
    {
        // Break-even: exit = entry. PnL = 0. Approximation → both 0.
        var trade = BuildLong(volume: 1000m, entry: 1.10m, exit: 1.10m);

        var (mfe, mae, _) = MfeMaeCalculator.Compute(trade);

        mfe.Should().Be(0m);
        mae.Should().Be(0m);
    }

    [Fact]
    public void LargeWinner_NoDecimalOverflow()
    {
        // 1_000_000_000 units × 0.00001000 = 10_000 PnL (still inside
        // NUMERIC(24,8) max ≈ 9.99e15). Both MFE and the test assert
        // exact equality so any silent overflow would surface as a failure.
        var trade = BuildLong(volume: 1_000_000_000m, entry: 1.10m, exit: 1.10001m);

        var (mfe, mae, _) = MfeMaeCalculator.Compute(trade);

        mfe.Should().Be(10_000m);
        mae.Should().Be(0m);
    }

    [Fact]
    public void CurrencyPropagation_ReturnsAccountCurrency()
    {
        // Trade account in ARS instead of USD — the calculator surfaces the
        // account currency on the wire regardless of the entry/exit pair.
        var trade = BuildLong(volume: 1000m, entry: 1.10m, exit: 1.12m,
            accountCurrency: "ARS");

        var (_, _, currency) = MfeMaeCalculator.Compute(trade);

        currency.Should().Be("ARS");
    }
}