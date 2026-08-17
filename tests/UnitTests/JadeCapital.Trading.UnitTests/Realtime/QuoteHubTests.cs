using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Features.Realtime;
using JadeCapital.Trading.Infrastructure.Realtime;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using FluentAssertions;

namespace JadeCapital.Trading.UnitTests.Realtime;

// ============================================================================
//  QuoteHub tests — slice 4c (Realtime).
//
//  Hub<T> exposes Context, Groups, and Clients as publicly settable properties,
//  so tests can wire fakes directly without spinning up a SignalR host.
//  We verify the registry + mediator interactions (the side effects that
//  matter) and treat Groups.AddToGroupAsync as a black-box we accept is called.
// ============================================================================

public class QuoteHubTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IQuoteSubscriptionRegistry _registry = new InMemoryQuoteSubscriptionRegistry();
    private readonly IGroupManager _groups = Substitute.For<IGroupManager>();
    private HubCallerContext _ctx = null!;

    public QuoteHubTests()
    {
        // The Hub sends MediatR commands; in tests we wire those to the real
        // handlers so the side effects (registry mutations) actually fire.
        var subscribe = new SubscribeToQuoteHandler(_registry);
        var unsubscribe = new UnsubscribeFromQuoteHandler(_registry);
        _mediator.Send(Arg.Any<SubscribeToQuoteCommand>(), Arg.Any<CancellationToken>())
            .Returns(async call => await subscribe.Handle(call.ArgAt<SubscribeToQuoteCommand>(0), call.ArgAt<CancellationToken>(1)));
        _mediator.Send(Arg.Any<UnsubscribeFromQuoteCommand>(), Arg.Any<CancellationToken>())
            .Returns(async call => await unsubscribe.Handle(call.ArgAt<UnsubscribeFromQuoteCommand>(0), call.ArgAt<CancellationToken>(1)));
        _mediator.Send(Arg.Any<UnsubscribeAllFromQuotesCommand>(), Arg.Any<CancellationToken>())
            .Returns(async call => await new UnsubscribeAllFromQuotesHandler(_registry).Handle(call.ArgAt<UnsubscribeAllFromQuotesCommand>(0), call.ArgAt<CancellationToken>(1)));
    }

    private QuoteHub NewHub(string connectionId)
    {
        _ctx = Substitute.For<HubCallerContext>();
        _ctx.ConnectionId.Returns(connectionId);
        var hub = new QuoteHub(_mediator, _registry, NullLogger<QuoteHub>.Instance)
        {
            Context = _ctx,
            Groups = _groups,
        };
        return hub;
    }

    [Fact]
    public async Task SubscribeToSymbols_AddsConnectionToGroup_AndDispatchesSubscribeCommand()
    {
        var hub = NewHub("conn-1");

        await hub.SubscribeToSymbols(new[] { "EURUSD", "GBPJPY" });

        await _groups.Received(1).AddToGroupAsync("conn-1", "symbol-EURUSD");
        await _groups.Received(1).AddToGroupAsync("conn-1", "symbol-GBPJPY");

        await _mediator.Received(1).Send(
            Arg.Is<SubscribeToQuoteCommand>(c =>
                c.ConnectionId == "conn-1" &&
                c.Symbols.OrderBy(s => s).SequenceEqual(new[] { "EURUSD", "GBPJPY" })),
            Arg.Any<CancellationToken>());

        _registry.GetSymbolsForConnection("conn-1").Should().BeEquivalentTo(new[] { "EURUSD", "GBPJPY" });
    }

    [Fact]
    public async Task SubscribeToSymbols_NormalizesToUppercase_AndDedupes()
    {
        var hub = NewHub("conn-1");

        await hub.SubscribeToSymbols(new[] { " eurusd ", "EURUSD", "GBPJPY" });

        // 2 unique symbols after normalize + dedupe
        await _groups.Received(1).AddToGroupAsync("conn-1", "symbol-EURUSD");
        await _groups.Received(1).AddToGroupAsync("conn-1", "symbol-GBPJPY");
        await _groups.DidNotReceive().AddToGroupAsync("conn-1", Arg.Is<string>(s => s != "symbol-EURUSD" && s != "symbol-GBPJPY"));
    }

    [Fact]
    public async Task UnsubscribeFromSymbols_RemovesConnectionFromGroup_AndDispatchesUnsubscribeCommand()
    {
        var hub = NewHub("conn-1");
        await hub.SubscribeToSymbols(new[] { "EURUSD", "GBPJPY" });
        _mediator.ClearReceivedCalls();

        await hub.UnsubscribeFromSymbols(new[] { "EURUSD" });

        await _groups.Received(1).RemoveFromGroupAsync("conn-1", "symbol-EURUSD");
        await _mediator.Received(1).Send(
            Arg.Is<UnsubscribeFromQuoteCommand>(c => c.ConnectionId == "conn-1" && c.Symbols.SequenceEqual(new[] { "EURUSD" })),
            Arg.Any<CancellationToken>());

        _registry.GetSymbolsForConnection("conn-1").Should().BeEquivalentTo(new[] { "GBPJPY" });
    }

    [Fact]
    public async Task OnDisconnectedAsync_RemovesConnectionFromRegistry()
    {
        var hub = NewHub("conn-1");
        await hub.SubscribeToSymbols(new[] { "EURUSD" });
        _registry.GetSubscribedSymbols().Should().Contain("EURUSD");

        await hub.OnDisconnectedAsync(null);

        _registry.GetSymbolsForConnection("conn-1").Should().BeEmpty();
        _registry.GetSubscribedSymbols().Should().NotContain("EURUSD");
    }

    [Fact]
    public async Task SubscribeToSymbols_EmptyList_IsNoOp()
    {
        var hub = NewHub("conn-1");

        await hub.SubscribeToSymbols(Array.Empty<string>());

        await _groups.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>());
        await _mediator.DidNotReceive().Send(Arg.Any<SubscribeToQuoteCommand>(), Arg.Any<CancellationToken>());
    }
}