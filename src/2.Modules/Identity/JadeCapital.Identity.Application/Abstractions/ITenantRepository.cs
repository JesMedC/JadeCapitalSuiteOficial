using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Shared.Kernel.Repository;

namespace JadeCapital.Identity.Application.Abstractions;

/// <summary>
/// EF-free repository contract for the <see cref="Tenant"/> aggregate
/// (Wave 6, slice 6c.1; extended in slice 6d.2 to extend
/// <see cref="IRepository{T}"/> so the audit decorator can wrap it).
///
/// <para>
/// The implementation lives in <c>Identity.Infrastructure/Persistence/TenantRepository.cs</c>
/// and uses EF Core. Application handlers depend on this interface so the
/// unit tests can substitute the dependency without spinning up a database.
/// </para>
/// <para>
/// Slice 6d.2 adds the audit-logging decorator
/// (<c>DecoratedRepository&lt;T&gt;</c>) on top of this interface via
/// Scrutor's <c>services.Decorate&lt;ITenantRepository, TenantAuditDecorator&gt;()</c>.
/// The 4 inherited methods (<see cref="IRepository{T}.AddAsync"/>,
/// <see cref="IRepository{T}.UpdateAsync"/>,
/// <see cref="IRepository{T}.DeleteAsync"/>,
/// <see cref="IRepository{T}.GetByIdAsync"/>) are audited on success;
/// <see cref="FindBySlugAsync"/> + <see cref="ListByOwnerAsync"/> are
/// read-only and never trigger audit events.
/// </para>
/// </summary>
public interface ITenantRepository : IRepository<Tenant>
{
    /// <summary>Lookup by unique slug. Returns null if no row matches.</summary>
    Task<Tenant?> FindBySlugAsync(string slug, CancellationToken ct);

    /// <summary>List tenants owned by the given user. Newest-first.</summary>
    Task<IReadOnlyList<Tenant>> ListByOwnerAsync(Guid ownerUserId, CancellationToken ct);
}