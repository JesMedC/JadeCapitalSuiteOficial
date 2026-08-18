using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application._Common;
using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using MediatR;

namespace JadeCapital.Identity.Application.Features.Tenants.UpdateTenant;

/// <summary>
/// Command to update a tenant's mutable fields (Wave 6, slice 6c.3).
///
/// <para>
/// The handler accepts partial updates — every parameter is nullable so
/// the client can patch just one field. Currently mutable:
/// </para>
/// <list type="bullet">
///   <item><see cref="Name"/> — validated by <see cref="Tenant.Rename"/></item>
///   <item><see cref="Plan"/> — validated by <see cref="Tenant.ChangePlan"/></item>
/// </list>
///
/// <para>
/// <b>NOT mutable here</b>: <see cref="Tenant.Slug"/> (URL stability),
/// <see cref="Tenant.OwnerUserId"/> (immutable), <see cref="Tenant.Status"/>
/// (separate Suspend/Archive endpoints — not in 6c.3).
/// </para>
/// </summary>
public sealed record UpdateTenantCommand(
    Guid TenantId,
    string? Name,
    TenantPlan? Plan) : IRequest<Result<TenantDto>>;

/// <summary>
/// Handler that patches a tenant (Wave 6, slice 6c.3).
///
/// <para>
/// <b>6c.3 rules</b>:
/// </para>
/// <list type="number">
///   <item>If the tenant doesn't exist → 404 <c>tenant.not_found</c>.</item>
///   <item>If the caller is not in the target tenant (and not SuperAdmin) → 404
///         <c>tenant.not_found</c> (don't leak existence).</item>
///   <item>If the tenant is <see cref="TenantStatus.Suspended"/> → 422
///         <c>tenant.cannot_modify_suspended</c> (compliance-hold invariant).</item>
///   <item>Domain validation (name length, plan enum) → propagate via
///         <see cref="Result.Failure{T}"/> with the aggregate's error code.</item>
///   <item>No-op update (null Name AND null Plan) → 200 returning the current DTO
///         without round-tripping through <see cref="IUnitOfWork.SaveChangesAsync"/>.</item>
/// </list>
///
/// <para>
/// Uses <see cref="ITenantContext.Current"/> (slice 6c.2 JWT resolver) as
/// the source of truth for the caller's tenant. SuperAdmin bypass is kept
/// narrow (admin tooling), not a general cross-tenant escape hatch.
/// </para>
/// </summary>
public sealed class UpdateTenantHandler : IRequestHandler<UpdateTenantCommand, Result<TenantDto>>
{
    private readonly ITenantRepository _tenants;
    private readonly IUnitOfWork _uow;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public UpdateTenantHandler(
        ITenantRepository tenants,
        IUnitOfWork uow,
        ITenantContext tenantContext,
        IClock clock)
    {
        _tenants = tenants;
        _uow = uow;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<TenantDto>> Handle(UpdateTenantCommand req, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(req.TenantId, ct);
        if (tenant is null)
            return Result.Failure<TenantDto>(TenantErrors.NotFound.TenantNotFound);

        // Cross-tenant access returns 404 (security best practice; do NOT
        // leak that the tenant exists). SuperAdmin is the narrow bypass.
        var callerTenant = _tenantContext.Current;
        if (callerTenant is null)
            return Result.Failure<TenantDto>(TenantErrors.NotFound.CrossTenantAccess);
        if (callerTenant.Value != tenant.Id && !_tenantContext.IsSuperAdmin)
            return Result.Failure<TenantDto>(TenantErrors.NotFound.CrossTenantAccess);

        // Compliance-hold invariant: Suspended tenants are read-only.
        var suspendedGuard = Tenant.GuardNotSuspendedForMutation(tenant.Status);
        if (suspendedGuard.IsFailure)
            return Result.Failure<TenantDto>(suspendedGuard.Error);

        // Apply name first (so the error code reflects the first failing field).
        if (req.Name is not null)
        {
            var rename = tenant.Rename(req.Name, _clock);
            if (rename.IsFailure)
                return Result.Failure<TenantDto>(rename.Error);
        }

        if (req.Plan is not null)
        {
            var changePlan = tenant.ChangePlan(req.Plan.Value, _clock);
            if (changePlan.IsFailure)
                return Result.Failure<TenantDto>(changePlan.Error);
        }

        // Persist only when at least one field was applied.
        if (req.Name is not null || req.Plan is not null)
        {
            await _tenants.UpdateAsync(tenant, ct);
            var saved = await _uow.SaveChangesAsync(ct);
            if (saved.IsFailure)
                return Result.Failure<TenantDto>(saved.Error);
        }

        return Result.Success(TenantMapping.ToDto(tenant));
    }
}
