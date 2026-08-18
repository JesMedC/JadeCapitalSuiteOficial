using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Repository;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Application.Abstractions;

/// <summary>
/// EF-free repository contract for the <see cref="User"/> aggregate
/// (Wave 0; extended in Wave 7, slice 7a.1 to extend
/// <see cref="IRepository{T}"/> so the audit decorator can wrap it).
///
/// <para>
/// The implementation lives in <c>Identity.Infrastructure/Persistence/Repositories.cs</c>
/// and uses EF Core. Application handlers depend on this interface so the
/// unit tests can substitute the dependency without spinning up a database.
/// </para>
/// <para>
/// Slice 7a.1 adds the audit-logging decorator
/// (<c>DecoratedRepository&lt;T&gt;</c>) on top of this interface via
/// Scrutor's <c>services.Decorate&lt;IUserRepository, UserAuditDecorator&gt;()</c>.
/// The 4 inherited methods (<see cref="IRepository{T}.AddAsync"/>,
/// <see cref="IRepository{T}.UpdateAsync"/>,
/// <see cref="IRepository{T}.DeleteAsync"/>,
/// <see cref="IRepository{T}.GetByIdAsync"/>) are audited on success;
/// <see cref="FindByEmailAsync"/> + <see cref="FindByIdAsync"/> +
/// <see cref="ListByTenantIdAsync"/> + <see cref="CountByTenantIdAsync"/>
/// are read-only and never trigger audit events.
/// </para>
/// <para>
/// <b>Why <see cref="IRepository{T}.DeleteAsync"/> is NOT a valid User
/// mutation</b>: the canonical User mutation surface is
/// <c>User.Cancel(reason)</c> (flips <c>Status</c> to <c>Cancelled</c>)
/// + <c>User.AssignToTenant(tenantId)</c> (Tenant reassignment for
/// multi-tenant onboarding/offboarding). There is no domain op that
/// hard-deletes a User. The concrete
/// <c>UserRepository.DeleteAsync(User, ct)</c> is a defensive stub that
/// throws <see cref="NotSupportedException"/> with the canonical message
/// <c>"User deletion happens via Tenant reassignment, not direct delete"</c>.
/// The <c>UserAuditDecorator</c> emits an
/// <c>AuditAction.Failed</c> audit row BEFORE re-throwing so the misuse
/// is recorded for the compliance trail.
/// </para>
/// </summary>
public interface IUserRepository : IRepository<User>
{
    /// <summary>Lookup by email. Returns null if no row matches.</summary>
    Task<User?> FindByEmailAsync(string email, CancellationToken ct);

    /// <summary>Lookup by id. Returns null if no row matches.</summary>
    Task<User?> FindByIdAsync(Guid id, CancellationToken ct);

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