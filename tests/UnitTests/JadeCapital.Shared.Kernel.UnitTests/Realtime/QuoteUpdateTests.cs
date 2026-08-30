using System.Text.Json;
using JadeCapital.Shared.Kernel.MarketData;
using JadeCapital.Shared.Kernel.Realtime;
using FluentAssertions;

namespace JadeCapital.Shared.Kernel.UnitTests.Realtime;

// ============================================================================
//  QuoteUpdate tests — slice 4c (Realtime).
//
//  Wire shape used by SignalR broadcasts and the FE client. Differs from
//  Quote: exposes `Last` (mid price) + `Ts` (server-side UTC timestamp)
//  instead of Spread/Volume24h, and lives under JadeCapital.Shared.Kernel.Realtime
//  to keep the realtime namespace independent of MarketData internals.
// ============================================================================

public class QuoteUpdateTests
{
    private static readonly DateTimeOffset FixedTs =
        new(2026, 8, 19, 14, 32, 0, TimeSpan.Zero);

    [Fact]
    public void Record_Equality_HoldsWhenAllFieldsMatch()
    {
        var a = new QuoteUpdate("EURUSD", 1.0850m, 1.0851m, 1.08505m, FixedTs, QuoteSource.Stub);
        var b = new QuoteUpdate("EURUSD", 1.0850m, 1.0851m, 1.08505m, FixedTs, QuoteSource.Stub);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Record_Equality_DiffersWhenLastDiffers()
    {
        var a = new QuoteUpdate("EURUSD", 1.0850m, 1.0851m, 1.08505m, FixedTs, QuoteSource.Stub);
        var b = new QuoteUpdate("EURUSD", 1.0850m, 1.0851m, 1.08599m, FixedTs, QuoteSource.Stub);

        a.Should().NotBe(b);
    }

    [Fact]
    public void FromQuote_ProjectsAllFieldsAndComputesLastAsMidPrice()
    {
        var q = new Quote("EURUSD", 1.0850m, 1.0851m, 0.0001m, 150_000m, FixedTs, QuoteSource.Stub);

        var u = QuoteUpdate.FromQuote(q);

        u.Symbol.Should().Be(q.Symbol);
        u.Bid.Should().Be(q.Bid);
        u.Ask.Should().Be(q.Ask);
        u.Last.Should().Be(0.5m * (q.Bid + q.Ask));
        u.Last.Should().Be(1.08505m);
        u.Ts.Should().Be(q.Timestamp);
        u.Source.Should().Be(q.Source);
    }

    [Fact]
    public void JsonSerialization_RoundtripsAllFields_WithCamelCasePropertyNames()
    {
        var u = new QuoteUpdate("GBPUSD", 1.2640m, 1.2642m, 1.26410m, FixedTs, QuoteSource.Mock);

        var json = JsonSerializer.Serialize(u);
        var back = JsonSerializer.Deserialize<QuoteUpdate>(json);

        back.Should().NotBeNull();
        back!.Symbol.Should().Be(u.Symbol);
        back.Bid.Should().Be(u.Bid);
        back.Ask.Should().Be(u.Ask);
        back.Last.Should().Be(u.Last);
        back.Ts.Should().Be(u.Ts);
        back.Source.Should().Be(u.Source);
        json.Should().Contain("\"symbol\":");
        json.Should().Contain("\"ts\":");
        json.Should().Contain("\"source\":");
    }
}