namespace JadeCapital.Shared.Kernel.Audit;

/// <summary>
/// Audit action enum (Wave 6, slice 6d.1).
///
/// <para>
/// Byte values map directly to the <c>audit.events.action SMALLINT</c>
/// column with the CHECK constraint <c>action IN (0, 1, 2, 3)</c>.
/// Adding a fifth value is a breaking change to the migration; the DB
/// constraint MUST be widened in lock-step.
/// </para>
///
/// <para>
/// <b>Why bytes</b>: the column is SMALLINT in Postgres; EF converts
/// the enum via <c>HasConversion&lt;byte&gt;()</c> for a compact on-disk
/// shape. The migration's CHECK constraint enforces the legal range at
/// the DB level — defense-in-depth against accidental enum expansion.
/// </para>
///
/// <para>
/// <b>Slice 6d.1 usage</b>: <see cref="Created"/> + <see cref="Updated"/> +
/// <see cref="Deleted"/> land in this slice (via the <c>DecoratedRepository</c>
/// pattern in 6d.2). <see cref="Restored"/> is reserved for a future
/// restore API (admin tooling — not in Wave 6).
/// </para>
/// </summary>
public enum AuditAction : byte
{
    /// <summary>A new entity was created.</summary>
    Created = 0,

    /// <summary>An existing entity was mutated.</summary>
    Updated = 1,

    /// <summary>An existing entity was soft-deleted (or hard-deleted if not ISoftDelete).</summary>
    Deleted = 2,

    /// <summary>A previously soft-deleted entity was restored. (Reserved for future restore API.)</summary>
    Restored = 3,

    /// <summary>
    /// Wave 7, slice 7a.1 — a cross-tenant access attempt was rejected.
    /// Emitted by typed audit decorators (e.g. <c>UserAuditDecorator</c>,
    /// <c>RiskProfileAuditDecorator</c>) when the calling user does not own
    /// the entity they tried to mutate. The audit row preserves the attempt
    /// for the compliance trail; the decorator then throws
    /// <see cref="System.UnauthorizedAccessException"/>.
    /// </summary>
    Denied = 4,

    /// <summary>
    /// Wave 7, slice 7a.1 — a contractually invalid mutation was attempted.
    /// Emitted by typed audit decorators when a handler calls
    /// <c>DeleteAsync</c> on an aggregate whose canonical mutation surface
    /// is different (e.g. <c>User</c> uses Tenant reassignment or
    /// <c>User.Cancel()</c>, <c>RiskProfile</c> uses
    /// <c>MarkSupersededAsync</c>, <c>Strategy</c> uses
    /// <c>Strategy.Deactivate(clock)</c>). The decorator emits the audit
    /// row before re-throwing <see cref="System.NotSupportedException"/> so
    /// the misuse is recorded.
    /// </summary>
    Failed = 5,
}
