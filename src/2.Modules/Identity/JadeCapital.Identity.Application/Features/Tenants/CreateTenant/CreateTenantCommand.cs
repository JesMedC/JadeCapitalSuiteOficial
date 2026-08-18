using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application._Common;
using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using MediatR;

namespace JadeCapital.Identity.Application.Features.Tenants.CreateTenant;

/// <summary>
/// Command to create a new tenant (Wave 6, slice 6c.1).
/// </summary>
public sealed record CreateTenantCommand(
    string Name,
    string Slug,
    Guid OwnerUserId) : IRequest<Result<TenantDto>>;

/// <summary>
/// Handler that creates a new tenant aggregate.
///
/// <para>
/// Pipeline:
/// </para>
/// <list type="number">
///   <item>Validate the owner user exists (otherwise 404 <c>tenant.owner_not_found</c>).</item>
///   <item>Validate the slug is not already taken (otherwise 409 <c>tenant.slug_taken</c>).</item>
///   <item>Build the aggregate via <see cref="Tenant.Create"/>; that method enforces every
///         invariant in design.md § Tenant (name length, slug regex, plan enum, etc.).</item>
///   <item>Persist via <see cref="ITenantRepository.AddAsync"/> + <see cref="IUnitOfWork.SaveChangesAsync"/>.</item>
/// </list>
///
/// <para>
/// Domain validation lives in <see cref="Tenant.Create"/> (Domain layer); the
/// handler only orchestrates — it does NOT duplicate validation.
/// </para>
/// </summary>
public sealed class CreateTenantHandler : IRequestHandler<CreateTenantCommand, Result<TenantDto>>
{
    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public CreateTenantHandler(
        ITenantRepository tenants,
        IUserRepository users,
        IUnitOfWork uow,
        IClock clock)
    {
        _tenants = tenants;
        _users = users;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<TenantDto>> Handle(CreateTenantCommand req, CancellationToken ct)
    {
        var owner = await _users.FindByIdAsync(req.OwnerUserId, ct);
        if (owner is null)
            return Result.Failure<TenantDto>(TenantErrors.NotFound.OwnerNotFound);

        var slugTaken = await _tenants.FindBySlugAsync(req.Slug, ct);
        if (slugTaken is not null)
            return Result.Failure<TenantDto>(TenantErrors.Conflict.SlugTaken);

        var createResult = Tenant.Create(
            id: Guid.NewGuid(),
            name: req.Name,
            slug: req.Slug,
            ownerUserId: req.OwnerUserId,
            plan: TenantPlan.Personal,
            clock: _clock);

        // Domain validation failures (name empty, slug format, etc.) come
        // back as Result.Failure — propagate them to the caller without
        // throwing. Throwing would surface as 500 via the global handler;
        // returning the failure lets the API layer emit 422 with the error
        // code preserved.
        if (createResult.IsFailure)
            return Result.Failure<TenantDto>(createResult.Error);

        var tenant = createResult.Value;

        await _tenants.AddAsync(tenant, ct);

        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
            return Result.Failure<TenantDto>(saved.Error);

        return Result.Success(TenantMapping.ToDto(tenant));
    }
}
