namespace JadeCapital.Identity.Domain.Tenants;

/// <summary>
/// Plan tier for a <see cref="Tenant"/> (Wave 6, slice 6c.1).
///
/// <para>
/// Stored as <c>SMALLINT</c> in <c>identity.tenants.plan</c>; the migration
/// also declares a CHECK constraint <c>plan BETWEEN 0 AND 2</c> as
/// defense-in-depth.
/// </para>
/// </summary>
public enum TenantPlan : byte
{
    Personal = 0,
    Pro = 1,
    Enterprise = 2
}
