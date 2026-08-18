using JadeCapital.Identity.Domain.Users;

namespace JadeCapital.Identity.Application._Common;

/// <summary>
/// Tenant-user read-model DTO (Wave 6, slice 6c.3).
///
/// <para>
/// Wire shape returned by <c>ListTenantUsersHandler</c> for
/// <c>GET /api/tenants/{id}/users</c>. PII-light: only the fields the
/// admin UI needs (id, email, display name, role, status, joined-at).
/// The password hash + session-version are NEVER projected.
/// </para>
/// </summary>
public sealed record TenantUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    string Status,
    DateTimeOffset? LastLoginAt);

/// <summary>
/// Static mapper from <see cref="User"/> aggregate to <see cref="TenantUserDto"/>
/// (Wave 6, slice 6c.3). Single source of truth for the wire shape.
/// </summary>
public static class TenantUserMapping
{
    public static TenantUserDto ToDto(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new TenantUserDto(
            Id: user.Id,
            Email: user.Email,
            DisplayName: user.DisplayName,
            Role: user.Role.ToString(),
            Status: user.Status.ToString(),
            LastLoginAt: user.LastLoginAt);
    }
}
