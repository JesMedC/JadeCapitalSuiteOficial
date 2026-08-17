using JadeCapital.Shared.Kernel.MarketData;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Features.Quotes.GetQuote;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using FluentAssertions;

namespace JadeCapital.Trading.UnitTests.Application.Quotes;

public class GetQuoteHandlerTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 19, 14, 0, 0, TimeSpan.Zero);

    private readonly IQuoteCacheRepository _cache = Substitute.For<IQuoteCacheRepository>();
    private readonly IQuoteProvider _provider = Substitute.For<IQuoteProvider>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly GetQuoteHandler _sut;

    public GetQuoteHandlerTests()
    {
        _clock.UtcNow.Returns(FixedNow);
        _sut = new GetQuoteHandler(_cache, _provider, _clock);
    }

    private static Quote MakeQuote(string symbol, decimal bid = 1.0850m, DateTimeOffset? ts = null)
        => new(symbol, bid, bid + 0.0001m, 0.0001m, 100_000m, ts ?? FixedNow, QuoteSource.Stub);

    [Fact]
    public async Task Handle_CacheHit_FreshQuote_ReturnsCachedWithoutProviderCall()
    {
        var cached = MakeQuote("EURUSD", ts: FixedNow.AddSeconds(-10));
        _cache.GetAsync("EURUSD", Arg.Any<CancellationToken>()).Returns(cached);

        var result = await _sut.Handle(new GetQuoteQuery("EURUSD"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Symbol.Should().Be("EURUSD");
        result.Value.Bid.Should().Be(cached.Bid);
        await _provider.DidNotReceive().GetQuoteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CacheMiss_ProviderReturnsQuote_WritesThroughCache()
    {
        _cache.GetAsync("EURUSD", Arg.Any<CancellationToken>()).ReturnsNull();
        var providerQuote = MakeQuote("EURUSD", ts: FixedNow);
        _provider.GetQuoteAsync("EURUSD", Arg.Any<CancellationToken>()).Returns(providerQuote);

        var result = await _sut.Handle(new GetQuoteQuery("EURUSD"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Bid.Should().Be(providerQuote.Bid);
        await _cache.Received(1).UpsertAsync(
            Arg.Is<Quote>(q => q.Symbol == "EURUSD" && q.Bid == providerQuote.Bid),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StaleCache_RefreshesFromProvider()
    {
        var stale = MakeQuote("EURUSD", ts: FixedNow.AddSeconds(-120));
        _cache.GetAsync("EURUSD", Arg.Any<CancellationToken>()).Returns(stale);
        var fresh = MakeQuote("EURUSD", bid: 1.0900m, ts: FixedNow);
        _provider.GetQuoteAsync("EURUSD", Arg.Any<CancellationToken>()).Returns(fresh);

        var result = await _sut.Handle(new GetQuoteQuery("EURUSD"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Bid.Should().Be(fresh.Bid);
        await _cache.Received(1).UpsertAsync(
            Arg.Is<Quote>(q => q.Bid == fresh.Bid),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CacheMissAndProviderReturnsNull_ReturnsNotFound()
    {
        _cache.GetAsync("ZZZZZ", Arg.Any<CancellationToken>()).ReturnsNull();
        _provider.GetQuoteAsync("ZZZZZ", Arg.Any<CancellationToken>()).ReturnsNull();

        var result = await _sut.Handle(new GetQuoteQuery("ZZZZZ"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.quote");
        await _cache.DidNotReceive().UpsertAsync(Arg.Any<Quote>(), Arg.Any<CancellationToken>());
    }
}
