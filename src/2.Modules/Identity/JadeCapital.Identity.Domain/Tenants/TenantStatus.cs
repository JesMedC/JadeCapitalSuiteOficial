namespace JadeCapital.Identity.Domain.Tenants;

/// <summary>
/// Lifecycle status of a <see cref="Tenant"/> aggregate (Wave 6, slice 6c.1).
///
/// <para>
/// Transitions: <c>Active → Suspended → Archived</c>. No back-transitions.
/// Enforced by <see cref="Tenant.Suspend"/>, <see cref="Tenant.Archive"/>,
/// and the <c>Try*</c> family.
/// </para>
/// </summary>
public enum TenantStatus : byte
{
    Active = 0,
    Suspended = 1,
    Archived = 2
}
