using JadeCapital.Billing.Application.Abstractions;
using JadeCapital.Billing.Domain.Subscriptions;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Billing.Infrastructure.Audit;

/// <summary>
/// Per-aggregate audit decorator for <see cref="ISubscriptionRepository"/>
/// (Wave 6, slice 6d.2, Phases 3.5 + 3.6).
///
/// <para>
/// Co-located with the <see cref="Subscription"/> aggregate (Billing
/// bounded context) so the decorator can directly inject
/// <see cref="Billing.Infrastructure.Persistence.BillingDbContext"/> for
/// EF <c>ChangeTracker.OriginalValues</c> — the pre-mutation diff source.
/// Putting it in <c>Billing.Infrastructure</c> avoids an
/// Identity.Infrastructure → Billing.Infrastructure → Identity.Infrastructure
/// circular dep that would arise if the decorator lived in
/// Identity.Infrastructure.
/// </para>
/// <list type="bullet">
///   <item>Wrapping Add with audit logging via the generic
///         <see cref="DecoratedRepository{T}"/> core.</item>
///   <item>Wrapping Update/Delete with <b>cross-tenant isolation</b>
///         + audit logging. A <see cref="Subscription"/> whose
///         <c>UserId</c> doesn't match the calling user's
///         <see cref="ITenantContext.CurrentUserId"/> is rejected with
///         <see cref="UnauthorizedAccessException"/> AND a
///         <see cref="AuditAction.Updated"/> /
///         <see cref="AuditAction.Deleted"/> audit row is written.</item>
///   <item>Tier change (<see cref="Subscription.ChangeTier"/>) → audit row
///         with <see cref="AuditAction.Updated"/> + a diff showing the
///         planCode transition.</item>
///   <item>Cancellation (<see cref="Subscription.Cancel"/>) → audit row
///         with <see cref="AuditAction.Deleted"/> + a diff showing the
///         status transition Active → Cancelled.</item>
/// </list>
/// <para>
/// <b>DbContext parameter type</b>: the decorator accepts
/// <see cref="DbContext"/> (base type) for the same reason as
/// <c>ImportJobAuditDecorator</c> — the SQLite test fixture needs a
/// custom test DbContext that sidesteps the production
/// <c>BillingDbContext</c> Npgsql-specific Money complex-type mapping.
/// </para>
/// <para>
/// Registered via Scrutor: <c>services.Decorate&lt;ISubscriptionRepository, SubscriptionAuditDecorator&gt;()</c>
/// in <see cref="DependencyInjection.BillingModuleRegistration"/>.
/// </para>
/// </summary>
public sealed class SubscriptionAuditDecorator : ISubscriptionRepository
{
    private readonly ISubscriptionRepository _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly DecoratedRepository<Subscription> _decorated;

    public SubscriptionAuditDecorator(
        ISubscriptionRepository inner,
        DbContext db,
        IAuditLogger audit,
        ITenantContext tenant,
        IClock clock)
    {
        _inner = inner;
        _audit = audit;
        _tenant = tenant;
        _clock = clock;
        _decorated = new DecoratedRepository<Subscription>(inner, audit, tenant, clock, db: db);
    }

    public Task<Subscription?> GetByIdAsync(Guid id, CancellationToken ct)
        => _inner.GetByIdAsync(id, ct);

    public Task AddAsync(Subscription subscription, CancellationToken ct)
        => _decorated.AddAsync(subscription, ct);

    public async Task UpdateAsync(Subscription subscription, CancellationToken ct)
    {
        if (!IsOwner(subscription))
        {
            await LogDeniedAsync(subscription, AuditAction.Updated, ct);
            throw new UnauthorizedAccessException(
                $"Subscription {subscription.Id} does not belong to current user (cross-tenant attempt).");
        }
        await _decorated.UpdateAsync(subscription, ct);
    }

    public async Task DeleteAsync(Subscription subscription, CancellationToken ct)
    {
        if (!IsOwner(subscription))
        {
            await LogDeniedAsync(subscription, AuditAction.Deleted, ct);
            throw new UnauthorizedAccessException(
                $"Subscription {subscription.Id} does not belong to current user (cross-tenant attempt).");
        }
        await _decorated.DeleteAsync(subscription, ct);
    }

    /// <summary>
    /// Returns true iff the subscription belongs to the current user.
    /// Anonymous / service contexts bypass the user-scope check (system
    /// actors like webhooks + background services).
    /// </summary>
    private bool IsOwner(Subscription subscription)
        => !_tenant.CurrentUserId.HasValue
            || subscription.UserId == _tenant.CurrentUserId.Value;

    /// <summary>
    /// Audit-log a denied cross-tenant attempt. The entry is enqueued
    /// with the calling user's id (not the entity's user id) so the
    /// security trail shows WHO attempted the cross-tenant access.
    /// </summary>
    private Task LogDeniedAsync(Subscription subscription, AuditAction action, CancellationToken ct)
    {
        var entry = new AuditEventEntry(
            EntityType: nameof(Subscription),
            EntityId: subscription.Id,
            Action: action,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: null,
            OccurredAt: _clock.UtcNow);
        return _audit.LogAsync(entry, ct);
    }
}