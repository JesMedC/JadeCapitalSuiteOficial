using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Shared.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Identity.Infrastructure.Persistence;

/// <summary>
/// Per-aggregate audit decorator for <see cref="ITenantRepository"/>
/// (Wave 6, slice 6d.2).
///
/// <para>
/// Implements <see cref="ITenantRepository"/> by:
/// </para>
/// <list type="bullet">
///   <item>Forwarding <see cref="ITenantRepository.FindBySlugAsync"/> +
///         <see cref="ITenantRepository.ListByOwnerAsync"/> + <see cref="IRepository{T}.GetByIdAsync"/>
///         to the inner.</item>
///   <item>Wrapping Add/Update/Delete with audit logging via the generic
///         <see cref="DecoratedRepository{T}"/> core. The
///         <see cref="IdentityDbContext"/> is injected so the decorator
///         can use EF's <c>ChangeTracker.OriginalValues</c> to compute
///         the pre-mutation diff even when the caller mutates the
///         tracked entity in place before calling <c>UpdateAsync</c>
///         (the standard production flow: handler
///         <c>repo.GetByIdAsync → entity.Mutate → repo.UpdateAsync</c>).</item>
/// </list>
/// <para>
/// Registered via Scrutor: <c>services.Decorate&lt;ITenantRepository, TenantAuditDecorator&gt;()</c>.
/// Cross-tenant isolation is NOT a concern for the Tenant aggregate itself
/// (Tenant IS the tenant boundary; ownership is enforced at the handler
/// layer in 6c.3). Future slices that audit cross-tenant mutations
/// (ImportJob + Subscription) will use a similar pattern with a
/// <c>_tenant.CurrentUserId == entity.UserId</c> pre-check inside the
/// decorator.
/// </para>
/// </summary>
public sealed class TenantAuditDecorator : ITenantRepository
{
    private readonly ITenantRepository _inner;
    private readonly DecoratedRepository<Tenant> _decorated;

    public TenantAuditDecorator(
        ITenantRepository inner,
        IdentityDbContext db,
        IAuditLogger audit,
        ITenantContext tenant,
        IClock clock)
    {
        _inner = inner;
        _decorated = new DecoratedRepository<Tenant>(inner, audit, tenant, clock, db: db);
    }

    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct)
        => _inner.GetByIdAsync(id, ct);

    public Task<Tenant?> FindBySlugAsync(string slug, CancellationToken ct)
        => _inner.FindBySlugAsync(slug, ct);

    public Task AddAsync(Tenant tenant, CancellationToken ct)
        => _decorated.AddAsync(tenant, ct);

    public Task UpdateAsync(Tenant tenant, CancellationToken ct)
        => _decorated.UpdateAsync(tenant, ct);

    public Task DeleteAsync(Tenant tenant, CancellationToken ct)
        => _decorated.DeleteAsync(tenant, ct);

    public Task<IReadOnlyList<Tenant>> ListByOwnerAsync(Guid ownerUserId, CancellationToken ct)
        => _inner.ListByOwnerAsync(ownerUserId, ct);
}