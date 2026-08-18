namespace JadeCapital.Shared.Kernel.MultiTenancy;

/// <summary>
/// Marker interface for entities that are owned by a single tenant (Wave 6, slice 6c.2).
///
/// <para>
/// Every aggregate whose persistence model includes a <c>tenant_id</c> column
/// implements this interface so the tenant query filter
/// (<see cref="TenantQueryFilter.WithTenantFilter{T}"/>) can apply a
/// consistent <c>WHERE tenant_id = ?</c> predicate to reads across modules.
/// </para>
///
/// <para>
/// The marker is intentionally minimal — it exposes only the strongly-typed
/// <see cref="TenantId"/> that the EF value-converter unwraps to a raw UUID on
/// persist. Ownership semantics (e.g. "a tenant owns zero or more users")
/// are NOT encoded here; those invariants live in the aggregate's domain
/// behavior (e.g. <see cref="JadeCapital.Identity.Domain.Tenants.Tenant.OwnerUserId"/>
/// + <see cref="JadeCapital.Identity.Domain.Users.User.TenantId"/>).
/// </para>
///
/// <para>
/// <b>Why shared kernel</b>: same justification as <see cref="ITenantContext"/>
/// and <c>IClock</c> — every module (Trading, Billing, Identity) queries
/// tenant-owned data. Placing the marker in Shared.Kernel avoids forcing
/// every consumer to depend on Identity.Domain for the filter contract.
/// </para>
///
/// <para>
/// <b>Slice 6c.2 wiring</b>: at the time of this slice the only entity
/// implementing <c>ITenantOwned</c> end-to-end is <c>Tenant</c> itself (a
/// user is in the tenant they own). Wave 6d.1 widens the marker to
/// <c>ImportJob</c> + adds <c>ISoftDelete</c>; Wave 7 will extend it further
/// to every multi-tenant-aware aggregate.
/// </para>
/// </summary>
public interface ITenantOwned
{
    /// <summary>
    /// The tenant this entity belongs to. May be <c>null</c> for entities
    /// that have not been backfilled yet (Wave 6c.1 ships the column as
    /// nullable; the 6c.2 backfill assigns the Personal tenant to every
    /// pre-existing user; 6c.3 makes it NOT NULL).
    /// </summary>
    TenantId? TenantId { get; }
}
