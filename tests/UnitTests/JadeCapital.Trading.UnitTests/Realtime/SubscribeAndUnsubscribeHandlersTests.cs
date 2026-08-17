using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Features.Realtime;
using MediatR;
using FluentAssertions;
using NSubstitute;

namespace JadeCapital.Trading.UnitTests.Realtime;

// ============================================================================
//  SubscribeToQuotesHandler / UnsubscribeFromQuotesHandler tests — slice 4c.
//
//  These handlers wrap IQuoteSubscriptionRegistry mutation so the Hub can
//  invoke them via ISender (auditable + future-event-friendly) while keeping
//  the registry contract as the side-channel-of-record for the broadcast
//  loop. The tests verify the registry ends up in the expected state.
// ============================================================================

public class SubscribeAndUnsubscribeHandlersTests
{
    private readonly IQuoteSubscriptionRegistry _registry = Substitute.For<IQuoteSubscriptionRegistry>();
    private readonly IRequestHandler<SubscribeToQuoteCommand> _subscribe;
    private readonly IRequestHandler<UnsubscribeFromQuoteCommand> _unsubscribe;

    public SubscribeAndUnsubscribeHandlersTests()
    {
        _subscribe = new SubscribeToQuoteHandler(_registry);
        _unsubscribe = new UnsubscribeFromQuoteHandler(_registry);
    }

    [Fact]
    public async Task Subscribe_PassesConnectionIdAndSymbolsToRegistry()
    {
        var cmd = new SubscribeToQuoteCommand("conn-1", new[] { "EURUSD", "GBPJPY" });

        await _subscribe.Handle(cmd, CancellationToken.None);

        _registry.Received(1).Add("conn-1", Arg.Is<IEnumerable<string>>(s =>
            s.SequenceEqual(new[] { "EURUSD", "GBPJPY" })));
    }

    [Fact]
    public async Task Subscribe_EmptySymbolList_StillCallsRegistry_NoOpOnCaller()
    {
        var cmd = new SubscribeToQuoteCommand("conn-1", Array.Empty<string>());

        await _subscribe.Handle(cmd, CancellationToken.None);

        _registry.Received(1).Add("conn-1", Arg.Any<IEnumerable<string>>());
    }

    [Fact]
    public async Task Unsubscribe_PassesConnectionIdAndSymbolsToRegistry()
    {
        var cmd = new UnsubscribeFromQuoteCommand("conn-1", new[] { "GBPJPY" });

        await _unsubscribe.Handle(cmd, CancellationToken.None);

        _registry.Received(1).Remove("conn-1", Arg.Is<IEnumerable<string>>(s =>
            s.SequenceEqual(new[] { "GBPJPY" })));
    }

    [Fact]
    public async Task Unsubscribe_EmptySymbolList_StillCallsRegistry()
    {
        var cmd = new UnsubscribeFromQuoteCommand("conn-1", Array.Empty<string>());

        await _unsubscribe.Handle(cmd, CancellationToken.None);

        _registry.Received(1).Remove("conn-1", Arg.Any<IEnumerable<string>>());
    }

    [Fact]
    public async Task UnsubscribeAll_RemovesEntAllSubscriptionForConnection()
    {
        var cmd = new UnsubscribeAllFromQuotesCommand("conn-1");

        await new UnsubscribeAllFromQuotesHandler(_registry).Handle(cmd, CancellationToken.None);

        _registry.Received(1).RemoveConnection("conn-1");
    }
}