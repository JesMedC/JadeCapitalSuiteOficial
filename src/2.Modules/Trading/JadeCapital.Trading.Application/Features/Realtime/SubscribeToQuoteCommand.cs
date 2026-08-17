using JadeCapital.Trading.Application.Abstractions;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Realtime;

// ============================================================================
//  Realtime subscription commands — slice 4c.
//
//  Thin wrappers over IQuoteSubscriptionRegistry. They exist so the Hub
//  can invoke them via ISender (keeps the call auditable + future-event-
//  friendly) while the registry remains the side-channel-of-record for
//  the broadcast loop. Behavior is intentionally trivial; tests at
//  SubscribeAndUnsubscribeHandlersTests verify pass-through.
// ============================================================================

public sealed record SubscribeToQuoteCommand(
    string ConnectionId,
    IReadOnlyList<string> Symbols) : IRequest;

public sealed class SubscribeToQuoteHandler : IRequestHandler<SubscribeToQuoteCommand>
{
    private readonly IQuoteSubscriptionRegistry _registry;

    public SubscribeToQuoteHandler(IQuoteSubscriptionRegistry registry)
    {
        _registry = registry;
    }

    public Task Handle(SubscribeToQuoteCommand request, CancellationToken cancellationToken)
    {
        _registry.Add(request.ConnectionId, request.Symbols);
        return Task.CompletedTask;
    }
}

public sealed record UnsubscribeFromQuoteCommand(
    string ConnectionId,
    IReadOnlyList<string> Symbols) : IRequest;

public sealed class UnsubscribeFromQuoteHandler : IRequestHandler<UnsubscribeFromQuoteCommand>
{
    private readonly IQuoteSubscriptionRegistry _registry;

    public UnsubscribeFromQuoteHandler(IQuoteSubscriptionRegistry registry)
    {
        _registry = registry;
    }

    public Task Handle(UnsubscribeFromQuoteCommand request, CancellationToken cancellationToken)
    {
        _registry.Remove(request.ConnectionId, request.Symbols);
        return Task.CompletedTask;
    }
}

public sealed record UnsubscribeAllFromQuotesCommand(
    string ConnectionId) : IRequest;

public sealed class UnsubscribeAllFromQuotesHandler : IRequestHandler<UnsubscribeAllFromQuotesCommand>
{
    private readonly IQuoteSubscriptionRegistry _registry;

    public UnsubscribeAllFromQuotesHandler(IQuoteSubscriptionRegistry registry)
    {
        _registry = registry;
    }

    public Task Handle(UnsubscribeAllFromQuotesCommand request, CancellationToken cancellationToken)
    {
        _registry.RemoveConnection(request.ConnectionId);
        return Task.CompletedTask;
    }
}