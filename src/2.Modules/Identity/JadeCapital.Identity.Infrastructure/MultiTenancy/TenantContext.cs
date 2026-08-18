using System.Security.Claims;
using JadeCapital.Shared.Kernel.MultiTenancy;
using Microsoft.AspNetCore.Http;

namespace JadeCapital.Identity.Infrastructure.MultiTenancy;

/// <summary>
/// Real <see cref="ITenantContext"/> implementation (Wave 6, slice 6c.2).
///
/// <para>
/// Reads three values from <c>HttpContext.User</c> at the moment the
/// property is accessed (NOT cached at construction, so admin role
/// promotions and refresh-token rotations are picked up on the next
/// call):
/// </para>
/// <list type="bullet">
///   <item><b>tenant_id</b> claim → <see cref="Current"/></item>
///   <item><b>sub</b> (NameIdentifier) claim → <see cref="CurrentUserId"/></item>
///   <item><b>role</b> claim whose value is <c>"SuperAdmin"</c> →
///         <see cref="IsSuperAdmin"/></item>
/// </list>
///
/// <para>
/// <b>Why per-call resolution (not cached)</b>: the request scope is
/// the lifetime of the underlying <c>HttpContext</c>, but properties
/// resolve at access-time so the middleware-membership change (e.g. a
/// role bump followed by a token refresh) is observed on the next
/// read without rebuilding the scope.
/// </para>
///
/// <para>
/// <b>Slice 6c.2 swap from placeholder</b>: the 6c.1 placeholder
/// returned <c>null</c> / <c>false</c> for every member. This class
/// is the "real" implementation wired in
/// <c>IdentityModuleRegistration.AddIdentityInfrastructure</c>. The
/// DI registration line is unchanged — same name
/// (<c>TenantContext</c>), just a different body.
/// </para>
///
/// <para>
/// <b>Why HttpContext-bound (not constructor-bound)</b>: tenant
/// context is per-request state. Reading it via
/// <see cref="IHttpContextAccessor"/> instead of capturing the
/// principal at construction keeps the class safely shareable across
/// scopes (a singleton accessor wrapping a per-request principal is
/// the standard ASP.NET pattern).
/// </para>
/// </summary>
public sealed class TenantContext : ITenantContext
{
    /// <summary>
    /// JWT claim name that carries the tenant id. Matches the
    /// <c>tenant_id</c> claim minted in slice 6c.2's JWT mint fix
    /// (see <c>JwtTokenService</c>).
    /// </summary>
    public const string TenantClaimName = "tenant_id";

    /// <summary>
    /// Role name that grants cross-tenant reads. Single source of
    /// truth — referenced by tests, the middleware, and the audit-log
    /// viewer authorization policy (slice 6d.1+).
    /// </summary>
    public const string SuperAdminRoleName = "SuperAdmin";

    private readonly IHttpContextAccessor _accessor;

    public TenantContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
    }

    public TenantId? Current
    {
        get
        {
            var user = _accessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return null;

            // The middleware (slice 6c.2) rejects malformed / missing
            // claims BEFORE the request reaches a handler. If a
            // malformed claim somehow slips through (manual scope,
            // admin script), we return null instead of throwing —
            // see TenantContextTests.Current_ReturnsNull_WhenMalformedGuid.
            var raw = user.FindFirstValue(TenantClaimName);
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            return Guid.TryParse(raw, out var id) ? new TenantId(id) : null;
        }
    }

    public Guid? CurrentUserId
    {
        get
        {
            var user = _accessor.HttpContext?.User;
            var raw = user?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : (Guid?)null;
        }
    }

    public bool IsSuperAdmin
    {
        get
        {
            var user = _accessor.HttpContext?.User;
            return user?.IsInRole(SuperAdminRoleName) ?? false;
        }
    }
}
