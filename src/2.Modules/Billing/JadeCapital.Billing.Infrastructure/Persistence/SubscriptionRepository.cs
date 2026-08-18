using JadeCapital.Billing.Application.Abstractions;
using JadeCapital.Billing.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Billing.Infrastructure.Persistence;

/// <summary>
/// EF Core-backed implementation of <see cref="ISubscriptionRepository"/>
/// (Wave 6, slice 6d.2).
///
/// <para>
/// Thin wrapper around <see cref="BillingDbContext.Subscriptions"/> exposing
/// the 4 <see cref="JadeCapital.Shared.Kernel.Repository.IRepository{T}"/>
/// CRUD methods. The existing
/// <see cref="Features.Subscriptions.ISubscriptionAdminRepository"/> +
/// <see cref="Features.Subscriptions.ISubscriptionAdminUnitOfWork"/> surface
/// remains unchanged for the read + history-entry paths; this repository is
/// the dedicated write seam for the audit decorator.
/// </para>
/// <para>
/// <b>Why a separate surface</b>: the admin flow's history-entry pattern
/// (<c>AddHistoryEntry</c> + <c>SaveChangesAsync</c>) is too narrow for
/// Scrutor's <c>Decorate</c>. The audit decorator needs an <c>AddAsync</c> /
/// <c>UpdateAsync</c> pair that maps cleanly to a single EF change. Slice
/// 6d.2 keeps both surfaces; future slices may consolidate them.
/// </para>
/// </summary>
public sealed class SubscriptionRepository : ISubscriptionRepository
{
    private readonly BillingDbContext _db;

    public SubscriptionRepository(BillingDbContext db) { _db = db; }

    /// <summary>
    /// Single-fetch by primary key. Returns null if no row matches.
    /// Reads NEVER trigger audit events — only mutations do.
    /// </summary>
    public Task<Subscription?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.Subscriptions
            .Include("_history")
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    /// <summary>
    /// Stage a new subscription for insertion. The audit decorator wraps
    /// this call to log an <c>audit.events</c> row with
    /// <see cref="JadeCapital.Shared.Kernel.Audit.AuditAction.Created"/>.
    /// </summary>
    public async Task AddAsync(Subscription subscription, CancellationToken ct)
        => await _db.Subscriptions.AddAsync(subscription, ct);

    /// <summary>
    /// Mark a modified subscription for update. The audit decorator wraps
    /// this call to log an <c>audit.events</c> row with
    /// <see cref="JadeCapital.Shared.Kernel.Audit.AuditAction.Updated"/> +
    /// the JSON diff between the pre-mutation snapshot and the post-mutation
    /// entity.
    /// </summary>
    public Task UpdateAsync(Subscription subscription, CancellationToken ct)
    {
        var entry = _db.Entry(subscription);
        if (entry.State == EntityState.Detached)
            _db.Subscriptions.Update(subscription);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Hard-delete from the change tracker. Rare in this codebase — admin
    /// tooling uses soft-delete paths. The audit decorator logs
    /// <see cref="JadeCapital.Shared.Kernel.Audit.AuditAction.Deleted"/> on
    /// this call.
    /// </summary>
    public Task DeleteAsync(Subscription subscription, CancellationToken ct)
    {
        _db.Subscriptions.Remove(subscription);
        return Task.CompletedTask;
    }
}