using JadeCapital.Identity.Domain.Tenants;

namespace JadeCapital.Identity.Application.Abstractions;

/// <summary>
/// EF-free repository contract for the <see cref="Tenant"/> aggregate
/// (Wave 6, slice 6c.1).
///
/// <para>
/// The implementation lives in <c>Identity.Infrastructure/Persistence/TenantRepository.cs</c>
/// and uses EF Core. Application handlers depend on this interface so the
/// unit tests can substitute the dependency without spinning up a database.
/// </para>
///
/// <para>
/// Note: cross-tenant filtering lives in slice 6c.2 (the repository decorator
/// uses <c>ITenantContext.Current</c>). Slice 6c.1 returns the row the caller
/// asks for — the handler is responsible for the cross-tenant check.
/// </para>
/// </summary>
public interface ITenantRepository
{
    /// <summary>Lookup by primary key. Returns null if no row matches.</summary>
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Lookup by unique slug. Returns null if no row matches.</summary>
    Task<Tenant?> FindBySlugAsync(string slug, CancellationToken ct);

    /// <summary>Add a new tenant to the change tracker (not yet persisted).</summary>
    Task AddAsync(Tenant tenant, CancellationToken ct);

    /// <summary>Mark an existing tenant as modified (EF will UPDATE on SaveChanges).</summary>
    Task UpdateAsync(Tenant tenant, CancellationToken ct);

    /// <summary>List tenants owned by the given user. Newest-first.</summary>
    Task<IReadOnlyList<Tenant>> ListByOwnerAsync(Guid ownerUserId, CancellationToken ct);
}
