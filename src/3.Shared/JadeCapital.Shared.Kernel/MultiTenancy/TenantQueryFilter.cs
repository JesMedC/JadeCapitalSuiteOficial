namespace JadeCapital.Shared.Kernel.MultiTenancy;

/// <summary>
/// Static helper that applies the multi-tenant <c>WHERE tenant_id = ?</c>
/// predicate to read-side queries (Wave 6, slice 6c.2).
///
/// <para>
/// Every aggregate that implements <see cref="ITenantOwned"/> MUST have its
/// read paths threaded through <see cref="WithTenantFilter{T}"/>. The helper
/// encapsulates the single rule that keeps cross-tenant reads from leaking:
///
/// <list type="bullet">
///   <item><b>Tenant set, not super-admin</b>: restrict to
///         <c>TenantId == ctx.Current</c>.</item>
///   <item><b>Tenant set, super-admin</b>: same restriction — super-admin
///         does NOT bypass the tenant filter when a tenant IS set. The
///         super-admin bypass only kicks in when no tenant is in scope
///         (admin endpoints that intentionally cross boundaries).</item>
///   <item><b>Tenant null, super-admin</b>: return the query as-is
///         (admin cross-tenant reads).</item>
///   <item><b>Tenant null, NOT super-admin</b>: return empty
///         (defense-in-depth: misconfigured callers cannot accidentally
///         read across tenants).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why static + extension</b>: the helper is stateless. Avoiding a
/// class instance means repositories don't take another DI dependency,
/// the filter is trivially testable, and there is no allocation cost on
/// the hot read path. The EF query provider translates
/// <see cref="WithTenantFilter{T}"/> into a server-side <c>WHERE</c>
/// clause automatically — see the matching tests under
/// <c>tests/UnitTests/JadeCapital.Shared.Kernel.UnitTests/MultiTenancy/TenantQueryFilterTests</c>.
/// </para>
///
/// <para>
/// <b>Soft-delete</b>: <c>ISoftDelete</c> filtering lives in slice 6d.1
/// alongside the audit-event aggregate. It is a separate concern; we do
/// not couple it here.
/// </para>
/// </summary>
public static class TenantQueryFilter
{
    /// <summary>
    /// Applies the tenant-id filter to <paramref name="source"/>. Returns a
    /// new <see cref="IQueryable{T}"/> that callers can compose further
    /// (<c>Where</c>, <c>OrderBy</c>, etc.). Safe to call with a
    /// <c>null</c> <paramref name="ctx"/> — treated the same as
    /// <c>ctx.Current == null &amp;&amp; !ctx.IsSuperAdmin</c> (empty result).
    /// </summary>
    public static IQueryable<T> WithTenantFilter<T>(
        this IQueryable<T> source, ITenantContext? ctx) where T : ITenantOwned
    {
        ArgumentNullException.ThrowIfNull(source);

        // Defense-in-depth: an anonymous caller (or a misconfigured
        // request without an HttpContext) MUST NOT see cross-tenant rows.
        if (ctx is null)
            return source.Take(0);

        // Super-admin cross-tenant reads are intentional for the
        // audit-log viewer + billing-admin paths. Without a tenant in
        // scope we return the unfiltered query.
        if (ctx.Current is null && ctx.IsSuperAdmin)
            return source;

        // Super-admin WITH a tenant still respects the tenant — the
        // bypass is a one-way privilege and admins operating inside a
        // tenant see only that tenant's data.
        if (ctx.Current is null)
            return source.Take(0);

        // Standard case: restrict to the caller's tenant.
        var current = ctx.Current;
        return source.Where(entity => entity.TenantId == current);
    }

    /// <summary>
    /// Explicit opt-out: returns the query as-is, bypassing the tenant
    /// filter. Used by tests, migrations, and admin scripts that
    /// intentionally need every tenant's data.
    ///
    /// <para>
    /// Production read paths MUST NOT call this. The helper exists so the
    /// choice is a deliberate, grep-able line — a reviewer can scan for
    /// <c>WithoutFilter</c> instead of hunting for missing filters.
    /// </para>
    /// </summary>
    public static IQueryable<T> WithoutFilter<T>(this IQueryable<T> source) where T : ITenantOwned
    {
        ArgumentNullException.ThrowIfNull(source);
        return source;
    }
}
