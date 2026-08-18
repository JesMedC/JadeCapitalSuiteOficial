namespace JadeCapital.Identity.Application._Common;

/// <summary>
/// Tenant read-model DTO (Wave 6, slice 6c.1).
///
/// <para>
/// Wire shape returned by <c>CreateTenantHandler</c> and
/// <c>GetTenantHandler</c>. Enum fields are rendered as their string name
/// (<c>Personal</c>, <c>Active</c>, etc.) for API ergonomics — clients don't
/// need to know the byte value.
/// </para>
/// </summary>
public sealed record TenantDto(
    Guid Id,
    string Name,
    string Slug,
    Guid OwnerUserId,
    string Plan,
    string Status,
    DateTimeOffset CreatedAt);
