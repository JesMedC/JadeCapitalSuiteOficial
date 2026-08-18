using JadeCapital.Shared.Kernel.Audit;

namespace JadeCapital.Identity.Infrastructure.Audit;

/// <summary>
/// Placeholder <see cref="IAuditLogger"/> impl for slice 6d.1 (Wave 6).
///
/// <para>
/// The real <c>AuditLogger</c> impl (writes to <see cref="Persistence.AuditDbContext"/>,
/// enriches with ITenantContext.Current + ICurrentUser, swallows exceptions
/// silently) ships in slice 6d.2 alongside the DecoratedRepository pattern.
/// For 6d.1, the SoftDeleteHandler's <c>LogAsync</c> call must not throw
/// and must not break the main mutation — this placeholder satisfies both.
/// </para>
///
/// <para>
/// <b>Why a placeholder</b>: matches the Slice 6c.1 ITenantContext
/// placeholder pattern — the interface is wired end-to-end, but the real
/// data write waits for the slice that owns the persistence concern.
/// Replacing this with the real <c>AuditLogger</c> is a single DI line
/// change in <c>IdentityModuleRegistration</c>.
/// </para>
///
/// <para>
/// <b>Note</b>: this is intentionally NOT a no-op in the strict sense —
/// we DO accept the call (the audit pipeline must not throw). The real
/// 6d.2 impl will add the persistence write + exception swallowing.
/// </para>
/// </summary>
internal sealed class NoOpAuditLogger : IAuditLogger
{
    /// <summary>
    /// Accept the audit entry. The 6d.1 placeholder does NOT persist
    /// anything — slice 6d.2 wires the real <c>AuditLogger</c> impl
    /// that writes to <c>audit.events</c> via <c>AuditDbContext</c>.
    /// </summary>
    public Task LogAsync(AuditEventEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Cancellation propagates — even though we don't do I/O here,
        // the contract honors the token for forward-compatibility with
        // the 6d.2 impl that will call SaveChangesAsync(ct).
        ct.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }
}
