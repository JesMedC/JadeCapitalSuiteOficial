using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application._Common;
using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Results;
using MediatR;

namespace JadeCapital.Identity.Application.Features.Tenants.ListTenantUsers;

/// <summary>
/// Query to list every user that belongs to a tenant (Wave 6, slice 6c.3).
///
/// <para>
/// The query carries only the target <see cref="TenantId"/>; the handler
/// resolves the caller's tenant from <see cref="ITenantContext.Current"/>
/// (slice 6c.2 JWT resolver). SuperAdmin bypass is kept narrow.
/// </para>
/// </summary>
public sealed record ListTenantUsersQuery(Guid TenantId) : IRequest<Result<IReadOnlyList<TenantUserDto>>>;

/// <summary>
/// Handler that lists tenant members (Wave 6, slice 6c.3).
///
/// <para>
/// <b>6c.3 rules</b>:
/// </para>
/// <list type="number">
///   <item>If the tenant doesn't exist → 404 <c>tenant.not_found</c>.</item>
///   <item>If the caller is not in the target tenant (and not SuperAdmin) → 404
///         <c>tenant.not_found</c> (don't leak existence).</item>
///   <item>If the tenant is <see cref="TenantStatus.Suspended"/> → 422
///         <c>tenant.cannot_modify_suspended</c> (compliance-hold).</item>
///   <item>Empty tenant (no members yet) → 200 with empty array, NOT 404.</item>
/// </list>
///
/// <para>
/// The actual list query lives in <see cref="IUserRepository.ListByTenantIdAsync"/>
/// — the handler delegates so the EF query is unit-testable in isolation
/// and the handler stays trivial to mock.
/// </para>
/// </summary>
public sealed class ListTenantUsersHandler : IRequestHandler<ListTenantUsersQuery, Result<IReadOnlyList<TenantUserDto>>>
{
    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;
    private readonly ITenantContext _tenantContext;

    public ListTenantUsersHandler(
        ITenantRepository tenants,
        IUserRepository users,
        ITenantContext tenantContext)
    {
        _tenants = tenants;
        _users = users;
        _tenantContext = tenantContext;
    }

    public async Task<Result<IReadOnlyList<TenantUserDto>>> Handle(ListTenantUsersQuery req, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(req.TenantId, ct);
        if (tenant is null)
            return Result.Failure<IReadOnlyList<TenantUserDto>>(TenantErrors.NotFound.TenantNotFound);

        // Cross-tenant access returns 404 (security best practice).
        var callerTenant = _tenantContext.Current;
        if (callerTenant is null)
            return Result.Failure<IReadOnlyList<TenantUserDto>>(TenantErrors.NotFound.CrossTenantAccess);
        if (callerTenant.Value != tenant.Id && !_tenantContext.IsSuperAdmin)
            return Result.Failure<IReadOnlyList<TenantUserDto>>(TenantErrors.NotFound.CrossTenantAccess);

        // Compliance-hold: Suspended tenants refuse reads that imply
        // admin activity (list members is an admin operation; pure user
        // self-reads go through /api/users/{id}/profile, not this endpoint).
        var suspendedGuard = Tenant.GuardNotSuspendedForMutation(tenant.Status);
        if (suspendedGuard.IsFailure)
            return Result.Failure<IReadOnlyList<TenantUserDto>>(suspendedGuard.Error);

        var members = await _users.ListByTenantIdAsync(new TenantId(tenant.Id), ct);

        var dtos = members.Select(TenantUserMapping.ToDto).ToArray();
        return Result.Success<IReadOnlyList<TenantUserDto>>(dtos);
    }
}
