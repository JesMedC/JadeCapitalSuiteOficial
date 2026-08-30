using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.SoftDelete;

namespace JadeCapital.Shared.Kernel.SoftDelete;

/// <summary>
/// Provider abstraction for soft-deletable aggregates (Wave 6, slice 6d.1).
///
/// <para>
/// Each implementation knows how to load + persist ONE <see cref="ISoftDelete"/>
/// entity type. The provider hides the concrete repository behind a
/// cross-cutting interface so the <c>SoftDeleteHandler</c> in
/// <c>Identity.Application</c> can soft-delete any ISoftDelete aggregate
/// without taking a hard reference to every module's repository.
/// </para>
///
/// <para>
/// <b>Why Shared.Kernel</b>: same justification as <see cref="ISoftDelete"/>
/// — soft-delete is cross-cutting, and every module needs the provider
/// abstraction in order to register one of its aggregates. Placing the
/// interface here avoids forcing every consumer to depend on
/// Identity.Application for the contract.
/// </para>
/// </summary>
public interface ISoftDeleteProvider
{
    /// <summary>
    /// The entity type name. Used by <see cref="ISoftDeleteProviderRegistry"/>
    /// as the lookup key. Convention: the .NET type name (e.g. <c>"ImportJob"</c>,
    /// <c>"Tenant"</c>). Stable across versions — renaming breaks the
    /// SoftDelete API contract.
    /// </summary>
    string EntityType { get; }

    /// <summary>
    /// Loads the entity by id. Returns <c>null</c> when not found.
    /// The implementation MUST apply any tenant filter that the entity
    /// is subject to (the cross-tenant defense-in-depth pattern from
    /// slice 6c.2).
    /// </summary>
    Task<ISoftDelete?> FindByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Persists the entity after <see cref="ISoftDelete.IsDeleted"/> +
    /// <see cref="ISoftDelete.DeletedAtUtc"/> + <see cref="ISoftDelete.DeletedByUserId"/>
    /// have been set. Returns failure if the DB layer rejects the change
    /// (e.g. <c>DbUpdateConcurrencyException</c>).
    /// </summary>
    Task<Result> UpdateAsync(ISoftDelete entity, CancellationToken ct);
}
