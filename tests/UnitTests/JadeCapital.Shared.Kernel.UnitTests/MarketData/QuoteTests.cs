using System.Text.Json;
using JadeCapital.Shared.Kernel.MarketData;
using FluentAssertions;

namespace JadeCapital.Shared.Kernel.UnitTests.MarketData;

public class QuoteTests
{
    private static readonly DateTimeOffset FixedTs =
        new(2026, 8, 19, 14, 32, 0, TimeSpan.Zero);

    private static Quote Make(string symbol = "EURUSD", decimal bid = 1.0850m, decimal ask = 1.0851m)
        => new(symbol, bid, ask, ask - bid, 150_000m, FixedTs, QuoteSource.Stub);

    [Fact]
    public void Spread_EqualsAskMinusBid()
    {
        var q = Make(bid: 1.0800m, ask: 1.0810m);

        q.Spread.Should().Be(0.0010m);
    }

    [Fact]
    public void Record_Equality_HoldsWhenAllFieldsMatch()
    {
        var a = Make();
        var b = Make();

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Record_Equality_DiffersWhenBidDiffers()
    {
        var a = Make(bid: 1.0850m);
        var b = Make(bid: 1.0851m);

        a.Should().NotBe(b);
    }

    [Fact]
    public void JsonSerialization_RoundtripsAllFields()
    {
        var q = Make();

        var json = JsonSerializer.Serialize(q);
        var back = JsonSerializer.Deserialize<Quote>(json);

        back.Should().NotBeNull();
        back!.Symbol.Should().Be(q.Symbol);
        back.Bid.Should().Be(q.Bid);
        back.Ask.Should().Be(q.Ask);
        back.Spread.Should().Be(q.Spread);
        back.Volume24h.Should().Be(q.Volume24h);
        back.Timestamp.Should().Be(q.Timestamp);
        back.Source.Should().Be(q.Source);
    }

    [Fact]
    public void QuoteSource_EnumHasExpectedValues()
    {
        ((byte)QuoteSource.Stub).Should().Be(0);
        ((byte)QuoteSource.Mock).Should().Be(1);
        ((byte)QuoteSource.Live).Should().Be(2);
        ((byte)QuoteSource.Broker).Should().Be(3);
    }
}
