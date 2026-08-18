using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Results;
using MediatR;

namespace JadeCapital.Identity.Application.Features.Tenants.RemoveTenantUser;

/// <summary>
/// Command to remove a user from a tenant (Wave 6, slice 6c.3).
///
/// <para>
/// <b>Removal semantics</b>: the user is <b>unassigned</b> from the
/// tenant (TenantId set back to null). The user account itself is NOT
/// deleted. This is the "remove from workspace" action — the same user
/// can be re-invited to any other tenant later.
/// </para>
///
/// <para>
/// <b>NOT NULL compatibility</b>: the 6c.3 NOT NULL constraint on
/// <c>identity.users.tenant_id</c> would block a literal "set to NULL".
/// This handler runs AFTER a fallback assignment: the user is reassigned
/// to the 6c.2 Personal default tenant, then removed from the target.
/// For now, the slice implementation uses <see cref="User.UnassignFromTenant"/>
/// directly; the fallback path is a follow-up (the 6c.2 Personal
/// backfill guarantees no NULLs exist in production, so a literal
/// unassign still violates the constraint). See deviation note in
/// apply-progress-wave6-slice-6c-3.md.
/// </para>
/// </summary>
public sealed record RemoveTenantUserCommand(
    Guid TenantId,
    Guid UserId) : IRequest<Result<Guid>>;

/// <summary>
/// Handler that removes a user from a tenant (Wave 6, slice 6c.3).
///
/// <para>
/// <b>6c.3 rules</b>:
/// </para>
/// <list type="number">
///   <item>If the tenant doesn't exist → 404 <c>tenant.not_found</c>.</item>
///   <item>If the caller is not in the target tenant (and not SuperAdmin) → 404
///         <c>tenant.not_found</c>.</item>
///   <item>If the user is the tenant owner → 422
///         <c>tenant.owner_cannot_remove_self</c> (invariant).</item>
///   <item>If the user is not a member of the tenant → 404
///         <c>tenant.user_not_in_tenant</c>.</item>
///   <item>If the Personal default tenant is missing → 404
///         <c>tenant.personal_default_missing</c> (should never happen post-migration;
///         the constraint at the DB level prevents a silent NULL write).</item>
///   <item>Otherwise → reassign to the Personal default tenant via
///         <see cref="User.ReassignToTenantByAdmin"/> + persist + return user id.</item>
/// </list>
///
/// <para>
/// <b>NOT NULL semantics</b>: with the 6c.3 NOT NULL constraint on
/// <c>identity.users.tenant_id</c>, a literal <see cref="User.UnassignFromTenant"/>
/// (which sets the column to NULL) would fail at the DB level. The handler
/// therefore REASSIGNS the user to the Personal default tenant
/// (stable <c>personal-default</c> slug, created by 0026_backfill_personal_tenant.sql
/// + <c>BackfillTenantsRunner</c>). When the target tenant IS itself the
/// Personal default, the reassignment is a no-op (idempotent on the same id).
/// </para>
///
/// <para>
/// Note: the SuspendedTenant guard is intentionally NOT applied here —
/// removal of a member from a Suspended tenant is the COMPLIANCE
/// workflow (the only mutation allowed during the hold). A future
/// admin-only endpoint will enforce the guard separately.
/// </para>
/// </summary>
public sealed class RemoveTenantUserHandler : IRequestHandler<RemoveTenantUserCommand, Result<Guid>>
{
    /// <summary>
    /// Stable slug for the Personal default tenant. Mirrors
    /// <c>BackfillTenantsRunner.PersonalSlug</c> and the
    /// <c>0026_backfill_personal_tenant.sql</c> migration. Keep in sync.
    /// </summary>
    public const string PersonalDefaultSlug = "personal-default";

    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly ITenantContext _tenantContext;

    public RemoveTenantUserHandler(
        ITenantRepository tenants,
        IUserRepository users,
        IUnitOfWork uow,
        ITenantContext tenantContext)
    {
        _tenants = tenants;
        _users = users;
        _uow = uow;
        _tenantContext = tenantContext;
    }

    public async Task<Result<Guid>> Handle(RemoveTenantUserCommand req, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(req.TenantId, ct);
        if (tenant is null)
            return Result.Failure<Guid>(TenantErrors.NotFound.TenantNotFound);

        // Cross-tenant access returns 404.
        var callerTenant = _tenantContext.Current;
        if (callerTenant is null)
            return Result.Failure<Guid>(TenantErrors.NotFound.CrossTenantAccess);
        if (callerTenant.Value != tenant.Id && !_tenantContext.IsSuperAdmin)
            return Result.Failure<Guid>(TenantErrors.NotFound.CrossTenantAccess);

        // Owner-cannot-remove-self invariant. Removing the owner would
        // leave the tenant without a primary owner; transfer-ownership is
        // a separate operation (not in Wave 6).
        if (tenant.OwnerUserId == req.UserId)
            return Result.Failure<Guid>(TenantErrors.Capacity.OwnerCannotRemoveSelf);

        var user = await _users.FindByIdAsync(req.UserId, ct);
        if (user is null)
            return Result.Failure<Guid>(TenantErrors.NotFound.UserNotInTenant);

        // Membership check — the user must already be assigned to THIS
        // tenant. A user assigned to a different tenant surfaces as 404
        // (don't leak cross-tenant membership).
        if (user.TenantId is null || user.TenantId.Value != tenant.Id)
            return Result.Failure<Guid>(TenantErrors.NotFound.UserNotInTenant);

        // Resolve the Personal default tenant. The handler reassigns the
        // removed member here instead of NULLing the column — required by
        // the 6c.3 NOT NULL constraint on identity.users.tenant_id.
        var personalTenant = await _tenants.FindBySlugAsync(PersonalDefaultSlug, ct);
        if (personalTenant is null)
            return Result.Failure<Guid>(TenantErrors.NotFound.PersonalDefaultMissing);

        // Admin-orchestrated reassignment. The target member is NOT the
        // actor (the tenant owner is), so we bypass the self-init guard
        // in User.AssignToTenant. Idempotent on same-id assignment
        // (Personal-is-target case).
        var reassign = user.ReassignToTenantByAdmin(new TenantId(personalTenant.Id));
        if (reassign.IsFailure)
            return Result.Failure<Guid>(reassign.Error);

        await _users.UpdateAsync(user, ct);
        var saved = await _uow.SaveChangesAsync(ct);
        return saved.IsFailure
            ? Result.Failure<Guid>(saved.Error)
            : Result.Success(user.Id);
    }
}
