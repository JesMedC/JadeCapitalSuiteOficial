using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Metrics;
using JadeCapital.Trading.Domain.Trades;

namespace JadeCapital.Trading.UnitTests.Domain.Metrics;

/// <summary>
/// Tests del calculo puro de metricas. No toca EF / DB / IMetricsQueryStore:
/// el handler alimenta al calculator con una lista de Trade ya hidratada.
/// Las metricas se recalculan en cada request (read model, no cache) — el
/// calculator debe ser deterministico dado el mismo input.
/// </summary>
public class MetricsCalculatorTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private static Trade OpenTrade(
        Guid userId,
        string symbol,
        DateTimeOffset openedAt,
        decimal pnl = 0m)
    {
        var s = Symbol.Create(symbol).Value;
        var v = Money.Create(1000m, Currency.Usd).Value;
        var e = Money.Create(1.10m, Currency.Usd).Value;
        var trade = Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), userId,
            s, AssetClass.Forex, TradeDirection.Long,
            v, e, "USD", null, null, openedAt).Value;

        if (pnl != 0m)
        {
            var exit = pnl > 0 ? 1.10m + pnl / 1000m : 1.10m + pnl / 1000m;
            trade.Close(
                Money.Create(exit, Currency.Usd).Value,
                openedAt.AddHours(2),
                Substitute.For<IClock>());
        }
        return trade;
    }

    private static IReadOnlyList<Trade> Empty() => Array.Empty<Trade>();

    // ====================================================
    // Empty / all-open
    // ====================================================

    [Fact]
    public void Compute_EmptyTradeList_ReturnsZeroedMetricsAndEmptyCurves()
    {
        var result = MetricsCalculator.Compute(
            Empty(), totalOpen: 0, totalClosed: 0, totalTrades: 0,
            fromPeriod: null, nowUtc: T0);

        result.TotalTrades.Should().Be(0);
        result.TotalClosedTrades.Should().Be(0);
        result.TotalOpenTrades.Should().Be(0);
        result.WinRate.Should().Be(0m);
        result.Expectancy.Should().Be(0m);
        result.ProfitFactor.Should().Be(0m);
        result.Payoff.Should().Be(0m);
        result.Sqn.Should().Be(0m);
        result.MaxDrawdown.Should().Be(0m);
        result.MaxDrawdownAmount.Should().Be(0m);
        result.MaxDrawdownPercent.Should().Be(0m);
        result.EquityCurve.Should().BeEmpty();
        result.SymbolStats.Should().BeEmpty();
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public void Compute_AllOpenTrades_ReturnsZeroClosedMetrics()
    {
        var userId = Guid.NewGuid();
        var opens = new[]
        {
            OpenTrade(userId, "EUR/USD", T0, pnl: 0m),
            OpenTrade(userId, "GBP/USD", T0.AddDays(1), pnl: 0m),
            OpenTrade(userId, "XAU/USD", T0.AddDays(2), pnl: 0m),
        };

        var result = MetricsCalculator.Compute(
            opens, totalOpen: 3, totalClosed: 0, totalTrades: 3,
            fromPeriod: null, nowUtc: T0);

        result.TotalTrades.Should().Be(3);
        result.TotalOpenTrades.Should().Be(3);
        result.TotalClosedTrades.Should().Be(0);
        result.WinRate.Should().Be(0m);
        result.Expectancy.Should().Be(0m);
        result.ProfitFactor.Should().Be(0m);
        result.Payoff.Should().Be(0m);
        result.Sqn.Should().Be(0m);
        result.MaxDrawdown.Should().Be(0m);
        result.MaxDrawdownAmount.Should().Be(0m);
        result.MaxDrawdownPercent.Should().Be(0m);
        result.EquityCurve.Should().BeEmpty();
        result.SymbolStats.Should().BeEmpty();
    }

    // ====================================================
    // Expectancy + Profit Factor + Payoff
    // ====================================================

    [Fact]
    public void Compute_MixedWinsAndLosses_ExpectancyMatchesFormula()
    {
        var userId = Guid.NewGuid();
        // 6 closed: 3 wins (+100, +150, +50) y 3 losses (-80, -50, -70)
        // grossWins = 300, grossLosses = 200, N = 6
        // expectancy = (300 - 200) / 6 = 16.6667
        var trades = new[]
        {
            ClosedTrade(userId, "EUR/USD", T0, pnl: 100m),
            ClosedTrade(userId, "EUR/USD", T0.AddDays(1), pnl: 150m),
            ClosedTrade(userId, "EUR/USD", T0.AddDays(2), pnl: 50m),
            ClosedTrade(userId, "EUR/USD", T0.AddDays(3), pnl: -80m),
            ClosedTrade(userId, "EUR/USD", T0.AddDays(4), pnl: -50m),
            ClosedTrade(userId, "EUR/USD", T0.AddDays(5), pnl: -70m),
        };

        var result = MetricsCalculator.Compute(
            trades, totalOpen: 0, totalClosed: 6, totalTrades: 6,
            fromPeriod: null, nowUtc: T0);

        result.Expectancy.Should().BeApproximately(16.6667m, 0.001m);
        result.ProfitFactor.Should().BeApproximately(1.5m, 0.001m);
        result.WinRate.Should().BeApproximately(50m, 0.001m);
        result.Payoff.Should().BeApproximately(1.5m, 0.001m); // avgWin=100, avgLoss=66.67 -> 100/66.67=1.5
    }

    [Fact]
    public void Compute_OnlyWins_ProfitFactorIsZeroNotInfinity()
    {
        var userId = Guid.NewGuid();
        var trades = new[]
        {
            ClosedTrade(userId, "EUR/USD", T0, pnl: 100m),
            ClosedTrade(userId, "EUR/USD", T0.AddDays(1), pnl: 50m),
        };

        var result = MetricsCalculator.Compute(
            trades, totalOpen: 0, totalClosed: 2, totalTrades: 2,
            fromPeriod: null, nowUtc: T0);

        result.ProfitFactor.Should().Be(0m);
        result.Payoff.Should().Be(0m);
        result.Expectancy.Should().Be(75m); // (150 - 0) / 2
        result.WinRate.Should().Be(100m);
    }

    [Fact]
    public void Compute_OnlyLosses_ProfitFactorIsZeroAndExpectancyNegative()
    {
        var userId = Guid.NewGuid();
        var trades = new[]
        {
            ClosedTrade(userId, "EUR/USD", T0, pnl: -50m),
            ClosedTrade(userId, "EUR/USD", T0.AddDays(1), pnl: -30m),
        };

        var result = MetricsCalculator.Compute(
            trades, totalOpen: 0, totalClosed: 2, totalTrades: 2,
            fromPeriod: null, nowUtc: T0);

        result.ProfitFactor.Should().Be(0m);
        result.Payoff.Should().Be(0m);
        result.Expectancy.Should().Be(-40m); // (0 - 80) / 2
        result.WinRate.Should().Be(0m);
    }

    // ====================================================
    // SQN (System Quality Number)
    // ====================================================

    [Fact]
    public void Compute_ClosedTradesWithConstantStDev_SqnMatchesFormula()
    {
        var userId = Guid.NewGuid();
        // 8 trades @ 15 + 8 trades @ 35. Mean = 25.
        // sample stdev (N-1) = sqrt(1600/15) ~= 10.327
        // SQN = sqrt(16) * 25 / 10.327 ~= 9.68
        var pnls = new decimal[] {
            15m, 35m, 15m, 35m, 15m, 35m, 15m, 35m,
            15m, 35m, 15m, 35m, 15m, 35m, 15m, 35m
        };
        var trades = pnls.Select((p, i) =>
            ClosedTrade(userId, "EUR/USD", T0.AddDays(i), pnl: p)).ToArray();

        var result = MetricsCalculator.Compute(
            trades, totalOpen: 0, totalClosed: 16, totalTrades: 16,
            fromPeriod: null, nowUtc: T0);

        result.Sqn.Should().BeApproximately(9.68m, 0.05m);
    }

    [Fact]
    public void Compute_FewerThanTwoClosedTrades_SqnIsZero()
    {
        var userId = Guid.NewGuid();
        var trades = new[]
        {
            ClosedTrade(userId, "EUR/USD", T0, pnl: 100m),
        };

        var result = MetricsCalculator.Compute(
            trades, totalOpen: 0, totalClosed: 1, totalTrades: 1,
            fromPeriod: null, nowUtc: T0);

        result.Sqn.Should().Be(0m);
    }

    // ====================================================
    // Max Drawdown
    // ====================================================

    [Fact]
    public void Compute_PeakToTroughSequence_MaxDrawdownIsNegativePeakDrop()
    {
        var userId = Guid.NewGuid();
        // equity curve (running): +100, -50, +150, -120
        // running totals: 100, 50, 200, 80
        // peaks: 100, 100, 200, 200
        // drawdowns: 0, -50, 0, -120
        // max DD amount = -120
        // drawdown percent = -120 / 200 * 100 = -60%
        var trades = new[]
        {
            ClosedTrade(userId, "EUR/USD", T0, pnl: 100m),
            ClosedTrade(userId, "EUR/USD", T0.AddDays(1), pnl: -50m),
            ClosedTrade(userId, "EUR/USD", T0.AddDays(2), pnl: 150m),
            ClosedTrade(userId, "EUR/USD", T0.AddDays(3), pnl: -120m),
        };

        var result = MetricsCalculator.Compute(
            trades, totalOpen: 0, totalClosed: 4, totalTrades: 4,
            fromPeriod: null, nowUtc: T0);

        result.MaxDrawdownAmount.Should().BeApproximately(-120m, 0.001m);
        result.MaxDrawdownPercent.Should().BeApproximately(-60m, 0.01m);
        result.MaxDrawdown.Should().BeApproximately(-120m, 0.001m);
    }

    // ====================================================
    // Symbol stats
    // ====================================================

    [Fact]
    public void Compute_MultipleSymbols_GroupsTradesAndSortsByNetPnlDescending()
    {
        var userId = Guid.NewGuid();
        // EUR/USD: 3 wins (+100, +50, +50), 1 loss (-40) => netPnl = +160
        // GBP/USD: 1 win (+200), 2 losses (-100, -50) => netPnl = +50
        var trades = new[]
        {
            ClosedTrade(userId, "EUR/USD", T0, pnl: 100m),
            ClosedTrade(userId, "EUR/USD", T0.AddDays(1), pnl: 50m),
            ClosedTrade(userId, "EUR/USD", T0.AddDays(2), pnl: 50m),
            ClosedTrade(userId, "EUR/USD", T0.AddDays(3), pnl: -40m),
            ClosedTrade(userId, "GBP/USD", T0.AddDays(4), pnl: 200m),
            ClosedTrade(userId, "GBP/USD", T0.AddDays(5), pnl: -100m),
            ClosedTrade(userId, "GBP/USD", T0.AddDays(6), pnl: -50m),
        };

        var result = MetricsCalculator.Compute(
            trades, totalOpen: 0, totalClosed: 7, totalTrades: 7,
            fromPeriod: null, nowUtc: T0);

        result.SymbolStats.Should().HaveCount(2);
        result.SymbolStats[0].Symbol.Should().Be("EUR/USD");
        result.SymbolStats[0].Trades.Should().Be(4);
        result.SymbolStats[0].WinRate.Should().Be(75m);
        result.SymbolStats[0].TotalPnl.Should().BeApproximately(160m, 0.001m);
        result.SymbolStats[1].Symbol.Should().Be("GBP/USD");
        result.SymbolStats[1].Trades.Should().Be(3);
        result.SymbolStats[1].WinRate.Should().BeApproximately(33.3333m, 0.001m);
        result.SymbolStats[1].TotalPnl.Should().BeApproximately(50m, 0.001m);
    }

    // ====================================================
    // Equity curve
    // ====================================================

    [Fact]
    public void Compute_EquityCurvePointsHaveTimestampEquityAndDrawdown()
    {
        var userId = Guid.NewGuid();
        var trades = new[]
        {
            ClosedTrade(userId, "EUR/USD", T0, pnl: 100m),
            ClosedTrade(userId, "EUR/USD", T0.AddDays(1), pnl: 50m),
            ClosedTrade(userId, "EUR/USD", T0.AddDays(2), pnl: -120m),
        };

        var result = MetricsCalculator.Compute(
            trades, totalOpen: 0, totalClosed: 3, totalTrades: 3,
            fromPeriod: null, nowUtc: T0);

        result.EquityCurve.Should().HaveCount(3);
        result.EquityCurve[0].Timestamp.Should().Be(T0);
        result.EquityCurve[0].Equity.Should().Be(100m);
        result.EquityCurve[0].Drawdown.Should().Be(0m);
        result.EquityCurve[1].Timestamp.Should().Be(T0.AddDays(1));
        result.EquityCurve[1].Equity.Should().Be(150m);
        result.EquityCurve[1].Drawdown.Should().Be(0m);
        result.EquityCurve[2].Timestamp.Should().Be(T0.AddDays(2));
        result.EquityCurve[2].Equity.Should().Be(30m);
        result.EquityCurve[2].Drawdown.Should().BeApproximately(-120m, 0.001m);
    }

    // ====================================================
    // Helpers
    // ====================================================

    private static Trade ClosedTrade(Guid userId, string symbol, DateTimeOffset openedAt, decimal pnl)
    {
        var s = Symbol.Create(symbol).Value;
        var v = Money.Create(1000m, Currency.Usd).Value;
        var e = Money.Create(1.10m, Currency.Usd).Value;
        var trade = Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), userId,
            s, AssetClass.Forex, TradeDirection.Long,
            v, e, "USD", null, null, openedAt).Value;
        // pnl es Long, exit - entry = pnl / volume => exit = entry + pnl/1000
        var exit = 1.10m + pnl / 1000m;
        trade.Close(
            Money.Create(exit, Currency.Usd).Value,
            openedAt.AddHours(2),
            Substitute.For<IClock>());
        return trade;
    }
}
