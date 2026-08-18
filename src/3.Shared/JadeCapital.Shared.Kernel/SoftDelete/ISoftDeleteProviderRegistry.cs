namespace JadeCapital.Shared.Kernel.SoftDelete;

/// <summary>
/// Registry of all soft-delete providers registered with the DI container
/// (Wave 6, slice 6d.1).
///
/// <para>
/// The DI container collects every <see cref="ISoftDeleteProvider"/>
/// implementation (one per soft-deletable aggregate) and the registry
/// exposes them by <see cref="ISoftDeleteProvider.EntityType"/>. The
/// <c>SoftDeleteHandler</c> in <c>Identity.Application</c> uses the
/// registry to resolve the right provider for each
/// <c>SoftDeleteCommand</c>.
/// </para>
///
/// <para>
/// <b>Unknown entity types</b>: when the registry is queried for a
/// type that no provider registered, it returns <c>null</c>. The
/// <c>SoftDeleteHandler</c> maps this to a 422
/// <c>validation.soft_delete.entity_not_soft_deleteable</c> error —
/// the caller can distinguish "no such entity" (404) from
/// "soft-delete not supported" (422).
/// </para>
/// </summary>
public interface ISoftDeleteProviderRegistry
{
    /// <summary>
    /// Returns the provider registered for the given entity type, or
    /// <c>null</c> when no provider is registered. The lookup is
    /// case-sensitive and exact-match.
    /// </summary>
    ISoftDeleteProvider? GetByEntityType(string entityType);
}
