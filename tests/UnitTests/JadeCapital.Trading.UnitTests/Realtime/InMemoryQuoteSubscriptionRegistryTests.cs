using JadeCapital.Trading.Application.Abstractions;
using FluentAssertions;

namespace JadeCapital.Trading.UnitTests.Realtime;

// ============================================================================
//  InMemoryQuoteSubscriptionRegistry tests — slice 4c (Realtime).
//
//  Verifies the side-channel that backs QuoteBroadcastService. The registry
//  tracks (connectionId → Set<Symbol>) and exposes a snapshot of the union
//  of symbols for the broadcast loop. Connection-scoped tracking lets the
//  hub clean up a single connection's symbols on disconnect without
//  disturbing other connections that share the same symbol.
// ============================================================================

public class InMemoryQuoteSubscriptionRegistryTests
{
    private readonly InMemoryQuoteSubscriptionRegistry _sut = new();

    [Fact]
    public void Add_StoresSymbolsForConnection()
    {
        _sut.Add("conn-1", new[] { "EURUSD", "GBPJPY" });

        _sut.GetSymbolsForConnection("conn-1").Should().BeEquivalentTo(new[] { "EURUSD", "GBPJPY" });
    }

    [Fact]
    public void Add_NormalizesSymbols_ToUppercaseTrimmed()
    {
        _sut.Add("conn-1", new[] { " eurusd ", "GBPJPY" });

        _sut.GetSymbolsForConnection("conn-1").Should().BeEquivalentTo(new[] { "EURUSD", "GBPJPY" });
    }

    [Fact]
    public void Remove_DropsOnlyRequestedSymbols_KeepsRest()
    {
        _sut.Add("conn-1", new[] { "EURUSD", "GBPJPY", "BTCUSD" });
        _sut.Remove("conn-1", new[] { "GBPJPY" });

        _sut.GetSymbolsForConnection("conn-1")
            .Should().BeEquivalentTo(new[] { "EURUSD", "BTCUSD" });
    }

    [Fact]
    public void RemoveConnection_DropsAllSubscriptionsForConnection()
    {
        _sut.Add("conn-1", new[] { "EURUSD" });
        _sut.Add("conn-2", new[] { "EURUSD", "BTCUSD" });

        _sut.RemoveConnection("conn-1");

        _sut.GetSymbolsForConnection("conn-1").Should().BeEmpty();
        _sut.GetSymbolsForConnection("conn-2")
            .Should().BeEquivalentTo(new[] { "EURUSD", "BTCUSD" });
    }

    [Fact]
    public void GetSubscribedSymbols_ReturnsUnionAcrossConnections_WithoutDuplicates()
    {
        _sut.Add("conn-1", new[] { "EURUSD", "GBPJPY" });
        _sut.Add("conn-2", new[] { "EURUSD", "BTCUSD" });

        _sut.GetSubscribedSymbols().Should().BeEquivalentTo(new[] { "EURUSD", "GBPJPY", "BTCUSD" });
    }

    [Fact]
    public void GetSubscribedSymbols_EmptyByDefault()
    {
        _sut.GetSubscribedSymbols().Should().BeEmpty();
    }

    [Fact]
    public void Add_SameSymbolTwiceForSameConnection_KeepsSet()
    {
        _sut.Add("conn-1", new[] { "EURUSD", "EURUSD" });

        _sut.GetSymbolsForConnection("conn-1").Should().HaveCount(1);
    }

    [Fact]
    public void UnknownConnection_ReturnsEmptySymbolSet()
    {
        _sut.GetSymbolsForConnection("conn-unknown").Should().BeEmpty();
    }

    [Fact]
    public void Add_DroppedAfterLastSymbolRemoved_AndRemoveConnectionNoOp()
    {
        _sut.Add("conn-1", new[] { "EURUSD" });
        _sut.Remove("conn-1", new[] { "EURUSD" });

        _sut.GetSymbolsForConnection("conn-1").Should().BeEmpty();
        _sut.GetSubscribedSymbols().Should().BeEmpty();

        _sut.RemoveConnection("conn-1");  // idempotent
        _sut.GetSubscribedSymbols().Should().BeEmpty();
    }
}