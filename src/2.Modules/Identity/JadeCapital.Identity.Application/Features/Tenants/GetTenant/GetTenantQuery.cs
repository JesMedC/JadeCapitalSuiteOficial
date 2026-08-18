using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application._Common;
using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using MediatR;

namespace JadeCapital.Identity.Application.Features.Tenants.GetTenant;

/// <summary>
/// Query to fetch a single tenant by id (Wave 6, slice 6c.1).
///
/// <para>
/// Slice 6c.1 uses an explicit <c>ActingUserId</c> argument because the real
/// JWT-derived resolution ships in 6c.2. The handler still prefers the
/// <see cref="ITenantContext.CurrentUserId"/> when it is non-null — that
/// makes the 6c.2 swap a one-line change in the handler.
/// </para>
/// </summary>
public sealed record GetTenantQuery(
    Guid TenantId,
    Guid? ActingUserId = null) : IRequest<Result<TenantDto>>;

/// <summary>
/// Handler that loads a tenant and enforces ownership / cross-tenant rules.
///
/// <para>
/// <b>6c.1 rules</b>:
/// <list type="bullet">
///   <item>If the tenant doesn't exist → 404 <c>tenant.not_found</c>.</item>
///   <item>If the caller is not the owner (and not SuperAdmin) → 404 <c>tenant.not_found</c> (don't leak existence).</item>
///   <item>Super-admin (placeholder default false in 6c.1) can read any tenant; the
///         cross-tenant filter ships in 6c.2 once <c>ITenantContext</c> is wired
///         to the real JWT resolver.</item>
/// </list>
/// </para>
/// </summary>
public sealed class GetTenantHandler : IRequestHandler<GetTenantQuery, Result<TenantDto>>
{
    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public GetTenantHandler(
        ITenantRepository tenants,
        IUserRepository users,
        ITenantContext tenantContext,
        IClock clock)
    {
        _tenants = tenants;
        _users = users;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<TenantDto>> Handle(GetTenantQuery req, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(req.TenantId, ct);
        if (tenant is null)
            return Result.Failure<TenantDto>(TenantErrors.NotFound.TenantNotFound);

        // 6c.2 will resolve the acting user from the JWT via ITenantContext.
        // In 6c.1 the caller passes it explicitly via the query argument.
        var actingUserId = req.ActingUserId
                           ?? _tenantContext.CurrentUserId;

        if (actingUserId is null)
            return Result.Failure<TenantDto>(TenantErrors.NotFound.CrossTenantAccess);

        if (tenant.OwnerUserId != actingUserId.Value && !_tenantContext.IsSuperAdmin)
            return Result.Failure<TenantDto>(TenantErrors.NotFound.CrossTenantAccess);

        return Result.Success(TenantMapping.ToDto(tenant));
    }
}
