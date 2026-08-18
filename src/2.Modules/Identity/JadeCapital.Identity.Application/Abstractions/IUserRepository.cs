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

    /// <summary>
    /// Wave 7, slice 7a.1 — User deletion is NOT a valid operation.
    /// The canonical mutation surface is
    /// <see cref="JadeCapital.Identity.Domain.Users.User.Cancel(string)"/>
    /// (flips <c>Status</c> to <c>Cancelled</c>) or
    /// <see cref="JadeCapital.Identity.Domain.Users.User.AssignToTenant"/>
    /// (Tenant reassignment for multi-tenant onboarding/offboarding).
    ///
    /// <para>
    /// The <c>UserAuditDecorator</c> emits an <c>AuditAction.Failed</c>
    /// audit row BEFORE re-throwing this exception so the misuse is recorded
    /// for the compliance trail. This method is part of the
    /// <see cref="IRepository{T}"/>-shaped surface that the typed decorator
    /// pattern in Wave 6 requires; the inner is a defensive STUB that throws
    /// immediately so a misconfigured DI container cannot accidentally hard-delete
    /// a user.
    /// </para>
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// Always thrown. The audit decorator surfaces the failure mode to callers
    /// before this inner method is reached.
    /// </exception>
    Task DeleteAsync(User user, CancellationToken ct);
}

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken ct);
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct);
    Task<IReadOnlyList<RefreshToken>> ListActiveByUserIdAsync(Guid userId, CancellationToken ct);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct);
}