using JadeCapital.Shared.Kernel.MultiTenancy;

namespace JadeCapital.Identity.Infrastructure.MultiTenancy;

/// <summary>
/// Placeholder <see cref="ITenantContext"/> implementation for slice 6c.1.
///
/// <para>
/// The real implementation reads the <c>tenant_id</c> JWT claim via
/// <c>HttpContext</c> and ships in slice 6c.2 alongside the
/// <c>TenantContextMiddleware</c>. Until then, every call returns
/// <c>null</c> / <c>false</c> — handlers must treat that as "anonymous"
/// and respond accordingly.
/// </para>
///
/// <para>
/// This file is intentionally tiny (~15 LOC): the seam is in the
/// interface, not the impl. The 6c.2 swap is a one-line change in
/// <c>IdentityModuleRegistration</c>.
/// </para>
/// </summary>
public sealed class TenantContext : ITenantContext
{
    public TenantId? Current => null;
    public Guid? CurrentUserId => null;
    public bool IsSuperAdmin => false;
}
