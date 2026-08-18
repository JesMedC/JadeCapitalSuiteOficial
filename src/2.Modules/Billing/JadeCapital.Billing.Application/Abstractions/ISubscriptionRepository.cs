using JadeCapital.Billing.Domain.Subscriptions;
using JadeCapital.Shared.Kernel.Repository;

namespace JadeCapital.Billing.Application.Abstractions;

/// <summary>
/// Write-capable persistence abstraction for the <see cref="Subscription"/>
/// aggregate (Wave 6, slice 6d.2).
///
/// <para>
/// The existing <see cref="Features.Subscriptions.ISubscriptionAdminRepository"/>
/// is read-focused (list/load/lookup); the existing
/// <see cref="Features.Subscriptions.ISubscriptionAdminUnitOfWork"/> is
/// write-focused but uses a different shape (history-entry append + SaveChanges).
/// Slice 6d.2 introduces this <see cref="IRepository{T}"/>-conforming
/// surface so the audit-logging decorator
/// (<c>DecoratedRepository&lt;Subscription&gt;</c>) can wrap it cleanly via
/// Scrutor.
/// </para>
/// <para>
/// The 4 inherited methods map 1:1 to EF Core <c>Add</c>/<c>Update</c>/<c>Remove</c>:
/// </para>
/// <list type="bullet">
///   <item><see cref="IRepository{T}.AddAsync"/> — stage a new aggregate for
///         insertion. The audit decorator logs <c>AuditAction.Created</c>
///         with no diff payload.</item>
///   <item><see cref="IRepository{T}.UpdateAsync"/> — stage a modified
///         aggregate for <c>SaveChanges</c>. The audit decorator logs
///         <c>AuditAction.Updated</c> with a JSON diff between the
///         pre-mutation snapshot and the post-mutation entity.</item>
///   <item><see cref="IRepository{T}.DeleteAsync"/> — stage a removal.
///         Rare in this codebase — admin tooling uses soft-delete paths.</item>
///   <item><see cref="IRepository{T}.GetByIdAsync"/> — used by the decorator
///         to capture the pre-mutation snapshot. Never triggers an audit
///         event.</item>
/// </list>
/// </summary>
public interface ISubscriptionRepository : IRepository<Subscription>
{
}