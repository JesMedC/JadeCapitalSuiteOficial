using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Identity.Infrastructure.Audit;

/// <summary>
/// Per-aggregate audit decorator for <see cref="IUserRepository"/>
/// (Wave 7, slice 7a.1).
///
/// <para>
/// Co-located with the <see cref="User"/> aggregate in
/// <c>Identity.Infrastructure</c> so the decorator can use
/// <see cref="IdentityDbContext"/> for EF
/// <c>ChangeTracker.OriginalValues</c> — the pre-mutation diff source.
/// Mirrors the Wave 6 <see cref="TenantAuditDecorator"/> shape:
/// </para>
/// <list type="bullet">
///   <item>Forwarding <see cref="IUserRepository.FindByEmailAsync"/> +
///         <see cref="IUserRepository.FindByIdAsync"/> +
///         <see cref="IUserRepository.ListByTenantIdAsync"/> +
///         <see cref="IUserRepository.CountByTenantIdAsync"/> to the inner.</item>
///   <item>Wrapping <see cref="IUserRepository.AddAsync"/> +
///         <see cref="IUserRepository.UpdateAsync"/> with audit logging
///         via the generic <see cref="DecoratedRepository{T}"/> core.</item>
///   <item>Wrapping <see cref="IUserRepository.UpdateAsync"/> with a
///         <b>cross-tenant <c>IsOwner</c> check</b>: a <see cref="User"/>
///         whose <c>Id</c> doesn't match the calling user's
///         <see cref="ITenantContext.CurrentUserId"/> (and the caller is
///         not <see cref="ITenantContext.IsSuperAdmin"/>) is rejected with
///         <see cref="UnauthorizedAccessException"/> + an
///         <see cref="AuditAction.Denied"/> audit row.</item>
///   <item><see cref="IUserRepository.DeleteAsync"/> is a defensive STUB:
///         the canonical User mutation surface is
///         <c>User.Cancel(reason)</c> + Tenant reassignment, NOT a hard
///         delete. The decorator emits an
///         <see cref="AuditAction.Failed"/> audit row BEFORE re-throwing
///         <see cref="NotSupportedException"/> so the misuse is recorded
///         for the compliance trail. The inner
///         <c>IUserRepository.DeleteAsync</c> is NEVER reached.</item>
/// </list>
///
/// <para>
/// <b>Why cross-tenant on User is <c>user.Id == _tenant.CurrentUserId</c>
/// (NOT <c>user.UserId</c>)</b>: User has no <c>UserId</c> FK — the
/// aggregate's <see cref="JadeCapital.Shared.Kernel.Primitives.Entity{TId}.Id"/>
/// IS the user. The check is therefore identity-vs-actor rather than
/// membership. <see cref="ITenantContext.IsSuperAdmin"/> bypasses the
/// check for admin operations (e.g. account recovery, GDPR delete).
/// </para>
///
/// <para>
/// Registered via Scrutor:
/// <c>services.Decorate&lt;IUserRepository, UserAuditDecorator&gt;()</c>
/// in <c>IdentityModuleRegistration</c>.
/// </para>
/// </summary>
public sealed class UserAuditDecorator : IUserRepository
{
    private readonly IUserRepository _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly DecoratedRepository<User> _decorated;

    public UserAuditDecorator(
        IUserRepository inner,
        IdentityDbContext db,
        IAuditLogger audit,
        ITenantContext tenant,
        IClock clock)
    {
        _inner = inner;
        _audit = audit;
        _tenant = tenant;
        _clock = clock;
        _decorated = new DecoratedRepository<User>(inner, audit, tenant, clock, db: db);
    }

    public Task<User?> FindByEmailAsync(string email, CancellationToken ct)
        => _inner.FindByEmailAsync(email, ct);

    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct)
        => _inner.FindByIdAsync(id, ct);

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct)
        => _inner.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<User>> ListByTenantIdAsync(TenantId tenantId, CancellationToken ct)
        => _inner.ListByTenantIdAsync(tenantId, ct);

    public Task<int> CountByTenantIdAsync(TenantId tenantId, CancellationToken ct)
        => _inner.CountByTenantIdAsync(tenantId, ct);

    public Task AddAsync(User user, CancellationToken ct)
        => _decorated.AddAsync(user, ct);

    public async Task UpdateAsync(User user, CancellationToken ct)
    {
        if (!IsOwner(user))
        {
            await LogDeniedAsync(user, AuditAction.Updated, ct);
            throw new UnauthorizedAccessException(
                $"User {user.Id} does not belong to current user (cross-tenant attempt).");
        }
        await _decorated.UpdateAsync(user, ct);
    }

    public async Task DeleteAsync(User user, CancellationToken ct)
    {
        // Slice 7a.1: DeleteAsync is NOT a valid User mutation. Emit an
        // AuditAction.Failed row BEFORE re-throwing so the misuse is on
        // the audit trail. The inner IUserRepository.DeleteAsync is NEVER
        // reached — the contract is "user deletion is not supported".
        await LogFailedAsync(user, ct);
        throw new NotSupportedException(
            "User deletion happens via Tenant reassignment, not direct delete.");
    }

    /// <summary>
    /// Returns true iff the target user IS the calling user, OR the
    /// caller is a SuperAdmin (admin-initiated mutation). When no user is
    /// resolved (anonymous / service context), the decorator allows the
    /// operation to proceed — system actors (webhooks, background
    /// services) bypass the user-scope check, matching the Wave 6
    /// <see cref="ImportJobAuditDecorator"/> + <see cref="SubscriptionAuditDecorator"/>
    /// precedent.
    /// </summary>
    private bool IsOwner(User user)
        => !_tenant.CurrentUserId.HasValue
            || user.Id == _tenant.CurrentUserId.Value
            || _tenant.IsSuperAdmin;

    /// <summary>
    /// Audit-log a denied cross-tenant attempt. The entry carries the
    /// calling user's id (NOT the entity's id) so the security trail
    /// shows WHO attempted the cross-tenant access.
    /// </summary>
    private Task LogDeniedAsync(User user, AuditAction action, CancellationToken ct)
    {
        var entry = new AuditEventEntry(
            EntityType: nameof(User),
            EntityId: user.Id,
            Action: action,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: null,
            OccurredAt: _clock.UtcNow);
        return _audit.LogAsync(entry, ct);
    }

    /// <summary>
    /// Audit-log a failed DeleteAsync attempt. The decorator emits the
    /// audit row BEFORE re-throwing so the audit trail reflects the call
    /// even though the caller will see an exception. The entry is
    /// distinguishable from a successful audit row by
    /// <see cref="AuditAction.Failed"/> = 5.
    /// </summary>
    private Task LogFailedAsync(User user, CancellationToken ct)
    {
        var entry = new AuditEventEntry(
            EntityType: nameof(User),
            EntityId: user.Id,
            Action: AuditAction.Failed,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: null,
            OccurredAt: _clock.UtcNow);
        return _audit.LogAsync(entry, ct);
    }
}