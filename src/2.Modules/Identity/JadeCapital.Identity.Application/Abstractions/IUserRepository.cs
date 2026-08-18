using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string email, CancellationToken ct);
    Task<User?> FindByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(User user, CancellationToken ct);
    Task UpdateAsync(User user, CancellationToken ct);

    /// <summary>
    /// Slice 6c.3 — list every user that belongs to the given tenant.
    /// Used by <c>ListTenantUsersHandler</c> to back the
    /// <c>GET /api/tenants/{id}/users</c> endpoint. The implementation
    /// applies the tenant filter at the SQL level (no in-memory scan).
    /// </summary>
    Task<IReadOnlyList<User>> ListByTenantIdAsync(TenantId tenantId, CancellationToken ct);

    /// <summary>
    /// Slice 6c.3 — count of users that currently belong to the given
    /// tenant. Used by <c>InviteTenantUserHandler</c> to enforce
    /// <c>Tenant.MaxUsersForPlan(plan)</c> BEFORE creating the invite
    /// (cheap, single SQL <c>COUNT(*)</c>).
    /// </summary>
    Task<int> CountByTenantIdAsync(TenantId tenantId, CancellationToken ct);
}

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken ct);
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct);
    Task<IReadOnlyList<RefreshToken>> ListActiveByUserIdAsync(Guid userId, CancellationToken ct);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct);
}