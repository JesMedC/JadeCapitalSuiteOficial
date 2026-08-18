namespace JadeCapital.Shared.Kernel.MultiTenancy;

/// <summary>
/// Per-request context for the calling user (Wave 6, slice 6c.1).
///
/// <para>
/// Every module that reads tenant-scoped data injects this interface. The real
/// implementation (slice 6c.2) reads the <c>tenant_id</c> JWT claim and the
/// <c>sub</c> claim from <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/>,
/// then exposes them via the three properties below.
/// </para>
///
/// <para>
/// Slice 6c.1 ships a <b>placeholder implementation</b> registered in DI that
/// returns <c>null</c> / <c>false</c> for every member. The migration
/// <c>0025_users_tenant_id.sql</c> adds the column as <c>NULL</c> so the
/// placeholder never breaks persistence — every tenant-scoped query in 6c.2+
/// will see "anonymous" until the JWT is enriched in 6c.2.
/// </para>
///
/// <para>
/// <b>Why shared</b>: same justification as <c>IClock</c> (Shared.Kernel/Time/).
/// Identity owns the JWT contract, but every module reads the resolved
/// tenant. Putting the interface in Shared.Kernel breaks the dependency cycle
/// (Trading → Shared.Kernel → ITenantContext, instead of Trading → Identity).
/// </para>
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// Current tenant from the JWT <c>tenant_id</c> claim. Returns <c>null</c>
    /// for anonymous callers (no JWT) and for pre-Wave-6 tokens that don't
    /// carry the claim.
    /// </summary>
    TenantId? Current { get; }

    /// <summary>
    /// Current user id from the JWT <c>sub</c> claim. Returns <c>null</c>
    /// for anonymous callers.
    /// </summary>
    Guid? CurrentUserId { get; }

    /// <summary>
    /// True if the current user has the <c>SuperAdmin</c> role. Used sparingly
    /// for admin endpoints that intentionally cross tenant boundaries
    /// (audit-log viewer, billing-admin, etc.).
    /// </summary>
    bool IsSuperAdmin { get; }
}
