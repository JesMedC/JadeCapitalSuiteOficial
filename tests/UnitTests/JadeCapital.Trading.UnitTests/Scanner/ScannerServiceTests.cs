using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Domain.Scanner;
using JadeCapital.Trading.Domain.Instruments;
using JadeCapital.Trading.Domain.Trades;
using JadeCapital.Shared.Kernel.Money;
using FluentAssertions;
using Xunit;

namespace JadeCapital.Trading.UnitTests.Scanner;

public class ScannerServiceTests
{
    private static readonly IClock Clock = new StaticClockLocal(new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero));

    /// Test stub: build closed trade via factory + Close.
    /// Uses the symbol's quote currency. Exit price varies so EUR and GBP have
    /// distinct R-R values (GBP wins) for the sort test.
    private static Trade MakeClosedWinner(string symbol, decimal exitPriceAdj = 0.1m)
    {
        var sym = JadeCapital.Trading.Domain.ValueObjects.Symbol.FromTrusted(symbol);
        var quote = sym.InferQuoteCurrencyCode();
        var entryPrice = Money.FromTrusted(1.0m, quote);
        var volume = Money.FromTrusted(100m, quote);
        var t = Trade.Open(
            id: Guid.NewGuid(), accountId: Guid.NewGuid(), instrumentId: Guid.NewGuid(),
            userId: Guid.NewGuid(), symbol: sym, assetClass: AssetClass.Forex,
            direction: TradeDirection.Long, volume: volume, entryPrice: entryPrice,
            accountCurrency: quote, strategy: null, notes: null,
            openedAt: new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero)).Value;
        t.Close(Money.FromTrusted(1.0m + exitPriceAdj, quote), new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero), Clock);
        return t;
    }

    private static Instrument MakeInstrument(string code) => Instrument.Create(
        Guid.NewGuid(), code, AssetClass.Forex, 100_000m, 5, 10m, 0m, Clock).Value;

    [Fact]
    public void Run_NoInstruments_ReturnsEmpty()
    {
        var filter = ScannerFilter.Create(Guid.NewGuid(), "f", null, null, null, 1.5m, VolatilityWindow.Daily, null, Clock).Value;
        var results = ScannerService.Run(Array.Empty<Instrument>(), Array.Empty<Trade>(), filter);
        results.Should().BeEmpty();
    }

    [Fact]
    public void Run_FilterBelowThreshold_ExcludesSymbol()
    {
        var userId = Guid.NewGuid();
        var inst = MakeInstrument("EUR/USD");
        var trades = new[] { MakeClosedWinner("EUR/USD") };
        var filter = ScannerFilter.Create(userId, "f", null, null, null, 100m, VolatilityWindow.Daily, null, Clock).Value;
        var results = ScannerService.Run(new[] { inst }, trades, filter);
        results.Should().BeEmpty();
    }

    [Fact]
    public void Run_ReturnsSortedByRiskRewardDescending()
    {
        var eur = MakeInstrument("EUR/USD");
        var gbp = MakeInstrument("GBP/JPY");
        var trades = new[] { MakeClosedWinner("EUR/USD", 0.1m), MakeClosedWinner("GBP/JPY", 0.5m) };
        var filter = ScannerFilter.Create(Guid.NewGuid(), "f", null, null, null, null, VolatilityWindow.Daily, null, Clock).Value;
        var results = ScannerService.Run(new[] { eur, gbp }, trades, filter);
        results.Count.Should().Be(2);
        results[0].Symbol.Should().Be("GBP/JPY");
    }

    [Fact]
    public void Run_RespectsLimit()
    {
        var instruments = Enumerable.Range(0, 5).Select(i => MakeInstrument($"SYM{i}/USD")).ToArray();
        var trades = new Trade[5];
        for (var i = 0; i < 5; i++) trades[i] = MakeClosedWinner($"SYM{i}/USD");
        var filter = ScannerFilter.Create(Guid.NewGuid(), "f", null, null, null, null, VolatilityWindow.Daily, null, Clock).Value;
        var results = ScannerService.Run(instruments, trades, filter, limit: 2);
        results.Count.Should().Be(2);
    }

    private sealed class StaticClockLocal : IClock
    {
        private readonly DateTimeOffset _now;
        public StaticClockLocal(DateTimeOffset now) { _now = now; }
        public DateTimeOffset UtcNow => _now;
    }
}
