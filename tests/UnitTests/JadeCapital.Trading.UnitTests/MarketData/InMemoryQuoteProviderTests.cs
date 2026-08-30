using JadeCapital.Shared.Kernel.MarketData;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using FluentAssertions;

namespace JadeCapital.Trading.UnitTests.MarketData;

public class InMemoryQuoteProviderTests
{
    private static readonly DateTimeOffset Fixed =
        new(2026, 8, 19, 14, 0, 0, TimeSpan.Zero);

    private static FixedClock ClockAt(DateTimeOffset now) => new(now);

    [Fact]
    public async Task GetQuoteAsync_SameSymbolSameTime_ReturnsEqualQuotes()
    {
        var clock = ClockAt(Fixed);
        var sut = new InMemoryQuoteProvider(clock);

        var a = await sut.GetQuoteAsync("EURUSD", CancellationToken.None);
        var b = await sut.GetQuoteAsync("EURUSD", CancellationToken.None);

        a.Should().NotBeNull();
        b.Should().NotBeNull();
        a!.Bid.Should().Be(b!.Bid);
        a.Ask.Should().Be(b.Ask);
        a.Spread.Should().Be(b.Spread);
        a.Timestamp.Should().Be(b.Timestamp);
    }

    [Fact]
    public async Task GetQuoteAsync_DifferentTime_ReturnsDifferentQuote()
    {
        var clock = ClockAt(Fixed);
        var sut = new InMemoryQuoteProvider(clock);

        var at14 = await sut.GetQuoteAsync("EURUSD", CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(5));
        var at14_05 = await sut.GetQuoteAsync("EURUSD", CancellationToken.None);

        at14.Should().NotBeNull();
        at14_05.Should().NotBeNull();
        at14!.Bid.Should().NotBe(at14_05!.Bid);
    }

    [Fact]
    public async Task GetQuoteAsync_CasingIsCaseInsensitive()
    {
        var sut = new InMemoryQuoteProvider(ClockAt(Fixed));

        var upper = await sut.GetQuoteAsync("EURUSD", CancellationToken.None);
        var lower = await sut.GetQuoteAsync("eurusd", CancellationToken.None);

        upper.Should().NotBeNull();
        lower.Should().NotBeNull();
        upper!.Bid.Should().Be(lower!.Bid);
    }

    [Fact]
    public async Task GetQuoteAsync_UnknownSymbol_ReturnsNull()
    {
        var sut = new InMemoryQuoteProvider(ClockAt(Fixed));

        var q = await sut.GetQuoteAsync("ZZZZZ", CancellationToken.None);

        q.Should().BeNull();
    }

    [Fact]
    public async Task GetQuotesAsync_MixedKnownAndUnknown_DropsUnknownSilently()
    {
        var sut = new InMemoryQuoteProvider(ClockAt(Fixed));

        var list = await sut.GetQuotesAsync(new[] { "EURUSD", "ZZZZZ", "GBPJPY" }, CancellationToken.None);

        list.Should().HaveCount(2);
        list.Select(q => q.Symbol).Should().BeEquivalentTo(new[] { "EURUSD", "GBPJPY" });
    }

    [Fact]
    public async Task GetQuoteAsync_SeedProducesBoundedBidAndAsk()
    {
        var sut = new InMemoryQuoteProvider(ClockAt(Fixed));

        var q = await sut.GetQuoteAsync("EURUSD", CancellationToken.None);

        q.Should().NotBeNull();
        q!.Bid.Should().BeGreaterThan(0m);
        q.Ask.Should().BeGreaterThan(q.Bid);
        q.Spread.Should().Be(q.Ask - q.Bid);
        q.Volume24h.Should().BeGreaterThan(0m);
    }

    private sealed class FixedClock : IClock
    {
        private DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) { _now = now; }
        public DateTimeOffset UtcNow => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
