using JadeCapital.Shared.Kernel.Realtime;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Features.Realtime;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Infrastructure.Realtime;

// ============================================================================
//  QuoteHub — slice 4c (Realtime) SignalR hub.
//
//  Server-callable methods:
//   - SubscribeToSymbols / UnsubscribeFromSymbols (client invokes)
//   - OnConnected / OnDisconnected (lifecycle overrides)
//
//  Connection bookkeeping:
//   - OnConnected registers the connection in IQuoteSubscriptionRegistry.
//   - OnDisconnected calls RemoveConnection so the broadcast loop stops
//     pulling quotes for that connection (SignalR auto-removes from
//     groups, but the side-channel must be cleaned too).
//   - Group membership is maintained by SignalR via Groups.AddToGroupAsync;
//     broadcast dispatches via Clients.Group("symbol-{S}").
//
//  Auth: RequireAuthorization on the class makes SignalR reject
//  unauthenticated handshakes (401 before WebSocket upgrade).
// ============================================================================

[Authorize]
public sealed class QuoteHub : Hub<IQuoteClient>
{
    private readonly IMediator _mediator;
    private readonly IQuoteSubscriptionRegistry _registry;
    private readonly ILogger<QuoteHub> _logger;

    public QuoteHub(
        IMediator mediator,
        IQuoteSubscriptionRegistry registry,
        ILogger<QuoteHub> logger)
    {
        _mediator = mediator;
        _registry = registry;
        _logger = logger;
    }

    public async Task SubscribeToSymbols(IReadOnlyList<string> symbols)
    {
        var normalized = NormalizeSymbols(symbols);
        if (normalized.Count == 0) return;

        foreach (var symbol in normalized)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"symbol-{symbol}");
        }

        // Update the side-channel so QuoteBroadcastService picks them up.
        await _mediator.Send(new SubscribeToQuoteCommand(Context.ConnectionId, normalized));

        _logger.LogDebug("Connection {ConnId} subscribed to {Symbols}",
            Context.ConnectionId, string.Join(",", normalized));
    }

    public async Task UnsubscribeFromSymbols(IReadOnlyList<string> symbols)
    {
        var normalized = NormalizeSymbols(symbols);
        if (normalized.Count == 0) return;

        foreach (var symbol in normalized)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"symbol-{symbol}");
        }

        await _mediator.Send(new UnsubscribeFromQuoteCommand(Context.ConnectionId, normalized));

        _logger.LogDebug("Connection {ConnId} unsubscribed from {Symbols}",
            Context.ConnectionId, string.Join(",", normalized));
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogDebug("Connection {ConnId} connected for {User}",
            Context.ConnectionId, Context.UserIdentifier ?? "(anonymous)");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // SignalR auto-removes the connection from groups on disconnect;
        // we still have to clean the side-channel so the broadcast loop
        // stops polling for symbols that no client cares about anymore.
        _registry.RemoveConnection(Context.ConnectionId);

        _logger.LogDebug(exception, "Connection {ConnId} disconnected", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private static IReadOnlyList<string> NormalizeSymbols(IEnumerable<string> symbols) =>
        symbols
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}