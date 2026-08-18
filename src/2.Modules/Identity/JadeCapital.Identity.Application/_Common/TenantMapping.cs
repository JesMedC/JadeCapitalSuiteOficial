using JadeCapital.Identity.Application._Common;
using JadeCapital.Identity.Domain.Tenants;

namespace JadeCapital.Identity.Application._Common;

/// <summary>
/// Static mapper from <see cref="Tenant"/> aggregate to <see cref="TenantDto"/>
/// (Wave 6, slice 6c.1).
///
/// <para>
/// Single source of truth for the wire shape — every handler that returns a
/// tenant goes through here so the response stays consistent across the
/// <c>POST /api/tenants</c> and <c>GET /api/tenants/{id}</c> endpoints.
/// </para>
/// </summary>
public static class TenantMapping
{
    public static TenantDto ToDto(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        return new TenantDto(
            Id: tenant.Id,
            Name: tenant.Name,
            Slug: tenant.Slug,
            OwnerUserId: tenant.OwnerUserId,
            Plan: tenant.Plan.ToString(),
            Status: tenant.Status.ToString(),
            CreatedAt: tenant.CreatedAt);
    }
}
