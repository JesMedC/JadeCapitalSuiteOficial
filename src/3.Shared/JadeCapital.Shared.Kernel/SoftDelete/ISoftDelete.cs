namespace JadeCapital.Shared.Kernel.SoftDelete;

/// <summary>
/// Soft-delete contract (Wave 6, slice 6d.1).
///
/// <para>
/// Every aggregate that supports deletion MUST implement this interface. The
/// shape is frozen here — three members, no exceptions. Adding a fourth
/// member (e.g. a restore timestamp) is a cross-module breaking change.
/// </para>
///
/// <para>
/// <b>EF global query filter</b>: each entity implementing <see cref="ISoftDelete"/>
/// declares <c>b.HasQueryFilter(e =&gt; !e.IsDeleted)</c> in its
/// <c>IEntityTypeConfiguration</c>. The filter is omittable via
/// <c>IgnoreQueryFilters()</c> in test fixtures that need to see soft-deleted
/// rows (admin tooling, audit reconstruction, integration tests).
/// </para>
///
/// <para>
/// <b>Opt-in per aggregate</b>: not every entity is soft-deleteable.
/// <c>AuditEvent</c> and <c>StripeWebhookEvent</c> are append-only and never
/// deleted. The interface is applied only where business rules allow
/// deletion.
/// </para>
///
/// <para>
/// <b>Why Shared.Kernel</b>: soft-delete is cross-cutting. Every module
/// (Identity, Billing, Trading) implements this on its user-owned
/// aggregates. Placing the interface here avoids forcing every consumer
/// to depend on Identity.Domain for the contract.
/// </para>
/// </summary>
public interface ISoftDelete
{
    /// <summary>
    /// True when the entity has been soft-deleted. Defaults to false on
    /// fresh entities. EF's global query filter excludes rows where this is
    /// true; use <c>IgnoreQueryFilters()</c> to include them.
    /// </summary>
    bool IsDeleted { get; }

    /// <summary>
    /// UTC timestamp at which the entity was soft-deleted. <c>null</c> when
    /// <see cref="IsDeleted"/> is false. Set by <c>MarkDeleted</c> on the
    /// aggregate.
    /// </summary>
    DateTimeOffset? DeletedAtUtc { get; }

    /// <summary>
    /// UserId (FK to <c>identity.users.id</c>) of the actor that performed
    /// the soft-delete. <c>null</c> when <see cref="IsDeleted"/> is false.
    /// Required when IsDeleted is true (defense-in-depth: every deletion
    /// has an accountable actor for the audit log).
    /// </summary>
    Guid? DeletedByUserId { get; }
}
