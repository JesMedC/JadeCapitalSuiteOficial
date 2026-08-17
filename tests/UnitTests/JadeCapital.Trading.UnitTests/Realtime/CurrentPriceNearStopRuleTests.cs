using JadeCapital.Shared.Kernel.MarketData;
using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Alerts;
using JadeCapital.Trading.Application.Alerts.Rules;
using JadeCapital.Trading.Domain.Behavioral;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Journal;
using JadeCapital.Trading.Domain.Trades;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using FluentAssertions;

namespace JadeCapital.Trading.UnitTests.Realtime;

// ============================================================================
//  CurrentPriceNearStopRule tests (slice 4c rewrite).
//
//  Wave 3b used EntryPrice as a proxy for the current price (no market data
//  available, fired on stale-trade heuristic). Slice 4c rewires the rule to
//  consume IQuoteProvider — the current price is now the real mid (Bid+Ask)/2,
//  and the rule fires when |currentPrice − EntryPrice| / EntryPrice < 1%.
//
//  Trade.StopLossPrice does not exist in the domain (deferred to a future
//  slice), so EntryPrice remains the reference value the rule measures
//  distance to. The semantics change: the rule now answers
//  "is the live mid within 1% of where the user entered?" rather than
//  "has this open trade gone stale?".
//
//  Failure scenarios:
//   - provider returns null → silent skip (no alert)
//   - provider throws → silent skip (no alert, no exception escapes)
// ============================================================================

public class CurrentPriceNearStopRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 14, 0, 0, TimeSpan.Zero);

    private readonly IQuoteProvider _provider = Substitute.For<IQuoteProvider>();

    private CurrentPriceNearStopRule NewSut() => new(_provider);

    private static Trade BuildOpenTrade(decimal entryPrice, string symbol = "EUR/USD")
    {
        var s = Symbol.Create(symbol).Value;
        var v = Money.Create(1m, Currency.Usd).Value;
        var e = Money.Create(entryPrice, Currency.Usd).Value;
        return Trade.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            s, AssetClass.Forex, TradeDirection.Long,
            v, e, "USD", null, null, Now.AddMinutes(-15)).Value;
    }

    private static AlertContext CtxWith(params Trade[] openTrades) => new(
        UserId: Guid.NewGuid(),
        WindowStart: Now.AddDays(-1),
        WindowEnd: Now,
        ClosedTrades: Array.Empty<Trade>(),
        OpenTrades: openTrades,
        RecentJournals: Array.Empty<JournalEntry>(),
        BehavioralAnalytics: null);

    [Fact]
    public void Fires_WhenLiveMidIsWithin1PercentOfEntry()
    {
        var trade = BuildOpenTrade(entryPrice: 1.1000m);
        var quote = new Quote("EURUSD", 1.0990m, 1.0991m, 0.0001m, 100_000m, Now, QuoteSource.Stub); // mid = 1.09905 → diff ≈ 0.086% < 1%
        _provider.GetQuoteAsync("EURUSD", Arg.Any<CancellationToken>()).Returns(quote);

        var alerts = NewSut().Evaluate(CtxWith(trade));

        alerts.Should().HaveCount(1);
        alerts[0].Severity.Should().Be(JadeCapital.Shared.Kernel.Coaching.Severity.Low);
        alerts[0].Body.Should().Contain("precio actual").And.Contain("EURUSD");
    }

    [Fact]
    public void DoesNotFire_WhenLiveMidIsMoreThan1PercentAwayFromEntry()
    {
        var trade = BuildOpenTrade(entryPrice: 1.1000m);
        var quote = new Quote("EURUSD", 1.0500m, 1.0501m, 0.0001m, 100_000m, Now, QuoteSource.Stub); // mid ≈ 1.05005 → diff ≈ 4.5% > 1%
        _provider.GetQuoteAsync("EURUSD", Arg.Any<CancellationToken>()).Returns(quote);

        var alerts = NewSut().Evaluate(CtxWith(trade));

        alerts.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFire_WhenProviderReturnsNull_ForAnyOpenTrade()
    {
        var trade = BuildOpenTrade(entryPrice: 1.1000m);
        _provider.GetQuoteAsync("EURUSD", Arg.Any<CancellationToken>()).ReturnsNull();

        var alerts = NewSut().Evaluate(CtxWith(trade));

        alerts.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotThrow_WhenProviderThrows_AndEmitsNoAlert()
    {
        var trade = BuildOpenTrade(entryPrice: 1.1000m);
        _provider.GetQuoteAsync("EURUSD", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Quote?>(new InvalidOperationException("feed down")));

        var act = () => NewSut().Evaluate(CtxWith(trade));

        act.Should().NotThrow();
        act().Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFire_WhenNoOpenTrades()
    {
        var alerts = NewSut().Evaluate(CtxWith());

        alerts.Should().BeEmpty();
        _provider.DidNotReceive().GetQuoteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void EvaluatesEachOpenTrade_AndOnlyFiresForTheOneNearEntry()
    {
        var near = BuildOpenTrade(entryPrice: 1.1000m, symbol: "EUR/USD");
        var far = BuildOpenTrade(entryPrice: 1.1000m, symbol: "GBP/USD"); // different symbol

        _provider.GetQuoteAsync("EURUSD", Arg.Any<CancellationToken>())
            .Returns(new Quote("EURUSD", 1.0990m, 1.0991m, 0.0001m, 100_000m, Now, QuoteSource.Stub)); // within 1%

        // GBPUSD provider returns null → silent skip
        _provider.GetQuoteAsync("GBPUSD", Arg.Any<CancellationToken>()).ReturnsNull();

        var alerts = NewSut().Evaluate(CtxWith(near, far));

        // Only the EUR/USD trade fires; GBP/USD skipped because provider returned null.
        alerts.Should().HaveCount(1);
        alerts[0].Body.Should().Contain("EURUSD");
    }

    [Fact]
    public void BodyReferencesCurrentPriceNotEntryPriceProxy()
    {
        var trade = BuildOpenTrade(entryPrice: 1.1000m);
        var quote = new Quote("EURUSD", 1.0990m, 1.0991m, 0.0001m, 100_000m, Now, QuoteSource.Stub);
        _provider.GetQuoteAsync("EURUSD", Arg.Any<CancellationToken>()).Returns(quote);

        var alert = NewSut().Evaluate(CtxWith(trade)).Single();

        // The honest copy must reference the live current price — NOT the
        // legacy "cerca de zona de entrada" stale-proxy copy.
        alert.Body.Should().NotContain("cerca de zona de entrada");
        alert.Body.Should().NotContain("sin tick data real");
        alert.Body.Should().Contain("1.09905");
    }
}