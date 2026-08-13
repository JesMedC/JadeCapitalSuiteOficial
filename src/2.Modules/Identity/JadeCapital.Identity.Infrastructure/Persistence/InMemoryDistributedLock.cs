using System.Collections.Concurrent;
using JadeCapital.Identity.Application.Abstractions;

namespace JadeCapital.Identity.Infrastructure.Persistence;

/// <summary>
/// In-process <see cref="IDistributedLock"/> used by the recovery handlers to
/// serialize password-change operations per user (key = <c>"user:{userId}"</c>).
///
/// Single-instance scope: this implementation serializes within one process
/// only. Multi-instance deployments will need a Redis-backed lock (slice
/// outside the current change scope). The interface contract guarantees the
/// same locking semantics so swapping the implementation does not change the
/// handler code.
/// </summary>
public sealed class InMemoryDistributedLock : IDistributedLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public async Task<IDistributedLockHandle> AcquireAsync(string key, CancellationToken ct = default)
    {
        var gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        return new Handle(gate);
    }

    private sealed class Handle : IDistributedLockHandle
    {
        private readonly SemaphoreSlim _gate;
        private bool _disposed;
        public Handle(SemaphoreSlim gate) => _gate = gate;
        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            try { _gate.Release(); } catch (ObjectDisposedException) { /* shutdown */ }
            return ValueTask.CompletedTask;
        }
    }
}