using JadeCapital.Shared.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Contracts.Strategies;
using JadeCapital.Trading.Domain.Strategies;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Audit;

/// <summary>
/// Per-aggregate audit decorator for <see cref="IStrategyRepository"/>
/// (Wave 7, slice 7b.1).
///
/// <para>
/// Mirrors the Wave 6 <see cref="ImportJobAuditDecorator"/> shape:
/// </para>
/// <list type="bullet">
///   <item>Forwarding <see cref="IStrategyRepository.ListByUserAsync"/> +
///         <see cref="IStrategyRepository.ExistsByNameAsync"/> +
///         <see cref="IStrategyRepository.GetAnalyticsAsync"/> +
///         <see cref="IRepository{T}.GetByIdAsync"/> to the inner.</item>
///   <item>Wrapping Add + Update with audit logging via the generic
///         <see cref="DecoratedRepository{T}"/> core.</item>
///   <item>Wrapping Update + Delete with <b>cross-tenant isolation</b>
///         + audit logging. A <see cref="Strategy"/> whose
///         <c>UserId</c> doesn't match the calling user's
///         <see cref="ITenantContext.CurrentUserId"/> is rejected with
///         <see cref="UnauthorizedAccessException"/> AND an
///         <see cref="AuditAction.Denied"/> audit row is written.</item>
///   <item><see cref="IStrategyRepository.DeleteAsync"/> is a defensive
///         STUB: the canonical Strategy mutation surface is
///         <c>Strategy.Update(...)</c> + <c>Strategy.Deactivate(clock)</c>,
///         NOT a hard delete. The decorator emits an
///         <see cref="AuditAction.Failed"/> audit row BEFORE re-throwing
///         <see cref="NotSupportedException"/> so the misuse is recorded
///         for the compliance trail. The inner
///         <c>IStrategyRepository.DeleteAsync</c> is NEVER reached.</item>
/// </list>
///
/// <para>
/// <b>Wave 7 user decision #3 — Deactivate is <c>Updated</c>, not
/// <c>Deleted</c></b>: when <see cref="Strategy.Deactivate(IClock)"/>
/// flips <c>IsActive</c> from <c>true</c> to <c>false</c>, the existing
/// <see cref="DecoratedRepository{T}.IsTerminated"/> reflection check does
/// NOT upgrade <c>Updated → Deleted</c> — the check only fires on
/// <c>IsDeleted == true</c> or <c>Status ∈ {Cancelled, Terminated, Expired}</c>
/// (neither matches Strategy's IsActive flag). The result is an
/// <see cref="AuditAction.Updated"/> event with
/// <c>isActive: true → false</c> in the diff — the semantically correct
/// audit trail for "soft-delete-via-flag".
/// </para>
///
/// <para>
/// Registered via Scrutor:
/// <c>services.Decorate&lt;IStrategyRepository, StrategyAuditDecorator&gt;()</c>
/// in <c>TradingModuleRegistration</c>.
/// </para>
/// </summary>
public sealed class StrategyAuditDecorator : IStrategyRepository
{
    private readonly IStrategyRepository _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly DecoratedRepository<Strategy> _decorated;

    public StrategyAuditDecorator(
        IStrategyRepository inner,
        DbContext db,
        IAuditLogger audit,
        ITenantContext tenant,
        IClock clock)
    {
        _inner = inner;
        _audit = audit;
        _tenant = tenant;
        _clock = clock;
        _decorated = new DecoratedRepository<Strategy>(inner, audit, tenant, clock, db: db);
    }

    public Task<Strategy?> GetByIdAsync(Guid id, CancellationToken ct)
        => _inner.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<Strategy>> ListByUserAsync(
        Guid userId, bool activeOnly, CancellationToken ct)
        => _inner.ListByUserAsync(userId, activeOnly, ct);

    public Task<bool> ExistsByNameAsync(Guid userId, string name, CancellationToken ct)
        => _inner.ExistsByNameAsync(userId, name, ct);

    public Task<StrategyAnalyticsDto> GetAnalyticsAsync(
        Guid userId, Guid strategyId, CancellationToken ct)
        => _inner.GetAnalyticsAsync(userId, strategyId, ct);

    public Task AddAsync(Strategy strategy, CancellationToken ct)
        => _decorated.AddAsync(strategy, ct);

    public async Task UpdateAsync(Strategy strategy, CancellationToken ct)
    {
        if (!IsOwner(strategy))
        {
            // Slice 7b.1 deviation from ImportJobAuditDecorator: the audit
            // row's Action is AuditAction.Denied (not the would-have-been
            // action) so compliance officers can filter cross-tenant
            // attempts separately from legitimate state changes.
            await LogDeniedAsync(strategy, AuditAction.Denied, ct);
            throw new UnauthorizedAccessException(
                $"Strategy {strategy.Id} does not belong to current user (cross-tenant attempt).");
        }
        await _decorated.UpdateAsync(strategy, ct);
    }

    public async Task DeleteAsync(Strategy strategy, CancellationToken ct)
    {
        // Slice 7b.1: DeleteAsync is NOT a valid Strategy mutation. Emit
        // an AuditAction.Failed row BEFORE re-throwing so the misuse is on
        // the audit trail. The inner IStrategyRepository.DeleteAsync is
        // NEVER reached — the contract is "strategy deletion is not
        // supported; use Strategy.Deactivate(clock) instead".
        await LogFailedAsync(strategy, ct);
        throw new NotSupportedException(
            "Strategy deletion happens via Deactivation, not direct delete.");
    }

    /// <summary>
    /// Returns true iff the strategy belongs to the current user. When no
    /// user is resolved (anonymous / service context), the decorator
    /// allows the operation to proceed — system actors (webhooks,
    /// background services) bypass the user-scope check (matches the
    /// Wave 6 ImportJobAuditDecorator + UserAuditDecorator precedent).
    /// </summary>
    private bool IsOwner(Strategy strategy)
        => !_tenant.CurrentUserId.HasValue
            || strategy.UserId == _tenant.CurrentUserId.Value;

    /// <summary>
    /// Audit-log a denied cross-tenant attempt. The entry carries the
    /// calling user's id (NOT the entity's user id) so the security trail
    /// shows WHO attempted the cross-tenant access. Fire-and-forget:
    /// never throws (the <see cref="IAuditLogger.LogAsync"/> contract
    /// swallows exceptions).
    /// </summary>
    private Task LogDeniedAsync(Strategy strategy, AuditAction action, CancellationToken ct)
    {
        var entry = new AuditEventEntry(
            EntityType: nameof(Strategy),
            EntityId: strategy.Id,
            Action: action,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: null,
            OccurredAt: _clock.UtcNow);
        return _audit.LogAsync(entry, ct);
    }

    /// <summary>
    /// Audit-log a failed DeleteAsync attempt. The decorator emits the
    /// audit row BEFORE re-throwing so the audit trail reflects the call
    /// even though the caller will see an exception. The entry is
    /// distinguishable from a successful audit row by
    /// <see cref="AuditAction.Failed"/> = 5.
    /// </summary>
    private Task LogFailedAsync(Strategy strategy, CancellationToken ct)
    {
        var entry = new AuditEventEntry(
            EntityType: nameof(Strategy),
            EntityId: strategy.Id,
            Action: AuditAction.Failed,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: null,
            OccurredAt: _clock.UtcNow);
        return _audit.LogAsync(entry, ct);
    }
}