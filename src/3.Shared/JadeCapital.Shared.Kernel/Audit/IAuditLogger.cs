namespace JadeCapital.Shared.Kernel.Audit;

/// <summary>
/// Audit logger interface (Wave 6, slice 6d.1).
///
/// <para>
/// The cross-cutting write surface for every mutation. Any module
/// (Identity, Billing, Trading) calls <see cref="LogAsync"/> after a
/// successful Create / Update / Delete to persist an
/// <c>audit.events</c> row. The 6d.2 <c>AuditLogger</c> impl writes the
/// entry to a dedicated <c>AuditDbContext</c> (write-only, isolated from
/// <c>IdentityDbContext</c>) so UPDATE/DELETE on the audit log is
/// structurally impossible.
/// </para>
///
/// <para>
/// <b>Why Shared.Kernel</b>: auditing is cross-cutting. The
/// <c>AuditEvent</c> aggregate lives in <c>Identity.Domain/Audit</c>,
/// but the <c>IAuditLogger</c> interface is in <c>Shared.Kernel</c> so any
/// module can log without importing Identity. The actual logging impl
/// (and the dedicated <c>AuditDbContext</c>) is in
/// <c>JadeCapital.Identity.Infrastructure</c>.
/// </para>
///
/// <para>
/// <b>No-throw guarantee</b>: <see cref="LogAsync"/> MUST NOT throw. The
/// 6d.2 implementation catches every exception silently and logs a warning
/// to Serilog. The audit log is a defense layer, NOT a critical path —
/// a flaky audit write must NEVER roll back a successful business
/// mutation.
/// </para>
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Records an audit event. Fire-and-forget — failures are logged but
    /// do NOT throw. The caller awaits only for back-pressure; an
    /// implementation may also choose to enqueue and return immediately
    /// (the 6d.2 impl uses <c>await</c> for simplicity, matching the
    /// existing webhook handler pattern from slice 6a.2).
    /// </summary>
    /// <param name="entry">The audit event to record.</param>
    /// <param name="ct">Cancellation token.</param>
    Task LogAsync(AuditEventEntry entry, CancellationToken ct = default);
}
