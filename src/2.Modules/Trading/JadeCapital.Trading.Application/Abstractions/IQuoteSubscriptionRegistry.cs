using System.Collections.Concurrent;

namespace JadeCapital.Trading.Application.Abstractions;

// ============================================================================
//  IQuoteSubscriptionRegistry — slice 4c (Realtime) side-channel contract.
//
//  SignalR's IHubContext doesn't expose group enumeration — a broadcast
//  loop can't ask "which symbols have subscribers?". This registry is the
//  authoritative side-channel that QuoteHub mutates and QuoteBroadcastService
//  reads. Per-connection symbol sets let the hub clean up a single
//  connection's subscriptions on disconnect without disturbing other
//  connections that happen to share the same symbol.
// ============================================================================

public interface IQuoteSubscriptionRegistry
{
    /// <summary>
    /// Add a connection's symbol subscriptions. Symbols are normalized to
    /// uppercase + trimmed. Duplicate symbols for the same connection are
    /// idempotent. MUST be safe to call from multiple threads concurrently.
    /// </summary>
    void Add(string connectionId, IEnumerable<string> symbols);

    /// <summary>
    /// Remove a subset of a connection's subscriptions. Symbols not in the
    /// connection's current set are silently ignored (no-op). MUST be safe
    /// to call from multiple threads concurrently.
    /// </summary>
    void Remove(string connectionId, IEnumerable<string> symbols);

    /// <summary>
    /// Drop all subscriptions for a connection (called from hub disconnect).
    /// Idempotent. MUST be safe to call from multiple threads concurrently.
    /// </summary>
    void RemoveConnection(string connectionId);

    /// <summary>
    /// Union of symbols currently subscribed by any connection. Empty when
    /// no clients are connected. Read-only snapshot — safe to enumerate.
    /// </summary>
    IReadOnlyCollection<string> GetSubscribedSymbols();

    /// <summary>
    /// Symbols subscribed by the given connection. Empty when unknown.
    /// Read-only snapshot.
    /// </summary>
    IReadOnlyCollection<string> GetSymbolsForConnection(string connectionId);
}

// ============================================================================
//  InMemoryQuoteSubscriptionRegistry — thread-safe ConcurrentDictionary impl.
//
//  One entry per connectionId; the value is a HashSet<string> protected by
//  the dictionary's bucket lock + an internal sync. We don't use
//  ConcurrentDictionary<string, ConcurrentDictionary<...>> because the
//  outer-level locking on the per-connection value is sufficient and avoids
//  the overhead of nested concurrent collections for the common case of
//  short symbol lists per connection.
// ============================================================================

public sealed class InMemoryQuoteSubscriptionRegistry : IQuoteSubscriptionRegistry
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _byConnection = new();

    public void Add(string connectionId, IEnumerable<string> symbols)
    {
        if (string.IsNullOrWhiteSpace(connectionId)) return;
        var set = _byConnection.GetOrAdd(connectionId, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        lock (set)
        {
            foreach (var s in symbols.Select(NormalizeSymbol).Where(s => s is not null))
                set.Add(s!);
        }
    }

    public void Remove(string connectionId, IEnumerable<string> symbols)
    {
        if (string.IsNullOrWhiteSpace(connectionId)) return;
        if (!_byConnection.TryGetValue(connectionId, out var set)) return;
        lock (set)
        {
            foreach (var s in symbols.Select(NormalizeSymbol).Where(s => s is not null))
                set.Remove(s!);
        }
    }

    public void RemoveConnection(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId)) return;
        _byConnection.TryRemove(connectionId, out _);
    }

    public IReadOnlyCollection<string> GetSubscribedSymbols()
    {
        var union = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in _byConnection.Values)
        {
            lock (set)
            {
                foreach (var s in set) union.Add(s);
            }
        }
        return union;
    }

    public IReadOnlyCollection<string> GetSymbolsForConnection(string connectionId)
    {
        if (!_byConnection.TryGetValue(connectionId, out var set))
            return Array.Empty<string>();
        lock (set)
        {
            return set.ToArray();
        }
    }

    private static string? NormalizeSymbol(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().ToUpperInvariant();
}