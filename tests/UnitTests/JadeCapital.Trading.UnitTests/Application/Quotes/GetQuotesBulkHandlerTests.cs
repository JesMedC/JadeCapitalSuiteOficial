using JadeCapital.Shared.Kernel.MarketData;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Features.Quotes.GetQuotesBulk;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using FluentAssertions;

namespace JadeCapital.Trading.UnitTests.Application.Quotes;

public class GetQuotesBulkHandlerTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 19, 14, 0, 0, TimeSpan.Zero);

    private readonly IQuoteCacheRepository _cache = Substitute.For<IQuoteCacheRepository>();
    private readonly IQuoteProvider _provider = Substitute.For<IQuoteProvider>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly GetQuotesBulkHandler _sut;

    public GetQuotesBulkHandlerTests()
    {
        _clock.UtcNow.Returns(FixedNow);
        _sut = new GetQuotesBulkHandler(_cache, _provider, _clock);
    }

    private static Quote MakeQuote(string symbol, decimal bid, DateTimeOffset? ts = null)
        => new(symbol, bid, bid + 0.0001m, 0.0001m, 100_000m, ts ?? FixedNow, QuoteSource.Stub);

    [Fact]
    public async Task Handle_AllKnown_ReturnsCacheQuotes()
    {
        var cached = new[]
        {
            MakeQuote("EURUSD", 1.0850m, ts: FixedNow.AddSeconds(-5)),
            MakeQuote("GBPJPY", 150.5m, ts: FixedNow.AddSeconds(-5)),
        };
        _cache.GetManyAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(cached);

        var result = await _sut.Handle(new GetQuotesBulkQuery(new[] { "EURUSD", "GBPJPY" }), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        await _provider.DidNotReceive().GetQuotesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MixedKnownAndUnknown_FetchesUnknownsFromProvider()
    {
        _cache.GetManyAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { MakeQuote("EURUSD", 1.0850m, ts: FixedNow.AddSeconds(-5)) });
        _provider.GetQuotesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { MakeQuote("GBPJPY", 150.5m, ts: FixedNow) });

        var result = await _sut.Handle(new GetQuotesBulkQuery(new[] { "EURUSD", "GBPJPY" }), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Select(q => q.Symbol).Should().BeEquivalentTo(new[] { "EURUSD", "GBPJPY" });
        await _provider.Received(1).GetQuotesAsync(
            Arg.Is<IEnumerable<string>>(s => s.SequenceEqual(new[] { "GBPJPY" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AllUnknownOrEmpty_ReturnsEmpty()
    {
        _cache.GetManyAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Quote>());
        _provider.GetQuotesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Quote>());

        var result = await _sut.Handle(new GetQuotesBulkQuery(new[] { "UNKNOWN1", "UNKNOWN2" }), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
