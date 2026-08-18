namespace JadeCapital.Shared.Kernel.Repository;

/// <summary>
/// Generic CRUD repository contract (Wave 6, slice 6d.2).
///
/// <para>
/// The narrowest cross-cutting surface every module's repository can extend.
/// The aggregate-specific interfaces (<c>ITenantRepository</c>,
/// <c>IImportJobRepository</c>, <c>ISubscriptionRepository</c>) keep their
/// extra find-by-X / list-by-X methods but ALL inherit the 4 generic CRUD
/// methods from this interface.
/// </para>
/// <para>
/// <b>Why this exists</b>: the <c>DecoratedRepository&lt;T&gt;</c> decorator
/// in slice 6d.2 wraps an <see cref="IRepository{T}"/> to add audit
/// logging on Add/Update/Delete. Scrutor's <c>services.Decorate</c>
/// extension wraps an existing service registration with a decorator that
/// must implement the same interface — so the decorator works against this
/// narrow contract, not against the aggregate-specific methods.
/// </para>
/// <para>
/// <b>Why GetByIdAsync</b>: the decorator fetches the pre-mutation snapshot
/// (used to compute the JSON diff on Update). Reads never log audit events;
/// they only exist here so the snapshot is reachable through the same
/// DI-injected repository the caller would have used.
/// </para>
/// </summary>
public interface IRepository<T> where T : class
{
    /// <summary>Single-fetch by primary key. Returns null if no row matches.</summary>
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Stage a new entity for insertion. The caller is responsible for SaveChanges.</summary>
    Task AddAsync(T entity, CancellationToken ct);

    /// <summary>Mark an existing entity as modified. The caller is responsible for SaveChanges.</summary>
    Task UpdateAsync(T entity, CancellationToken ct);

    /// <summary>Mark an existing entity for deletion. The caller is responsible for SaveChanges.</summary>
    Task DeleteAsync(T entity, CancellationToken ct);
}