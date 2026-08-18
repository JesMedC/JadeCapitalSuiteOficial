using JadeCapital.Shared.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Audit;

/// <summary>
/// Per-aggregate audit decorator for <see cref="IAccountRepository"/>
/// (Wave 8, slice 8a.1).
///
/// <para>
/// Co-located with the <see cref="Account"/> aggregate (Trading
/// bounded context) so the decorator can use the Trading-bounded
/// <see cref="DbContext"/> for EF <c>ChangeTracker.OriginalValues</c>
/// — the pre-mutation diff source. Putting it in
/// <c>Trading.Infrastructure</c> avoids an Identity.Infrastructure →
/// Trading.Infrastructure → Identity.Infrastructure circular dep
/// that would arise if the decorator lived in Identity.Infrastructure
/// (which already references <c>DecoratedRepository&lt;T&gt;</c> for Tenant).
/// </para>
/// <list type="bullet">
///   <item>Forwarding <see cref="IAccountRepository.FindByIdAsync"/> +
///         <see cref="IAccountRepository.GetByIdAsync"/> +
///         <see cref="IAccountRepository.ListByUserIdAsync"/> to the inner
///         without audit logging. Reads never log audit events
///         (matches the Wave 6 + 7a.1 + 7b.1 precedent).</item>
///   <item>Wrapping <see cref="IAccountRepository.AddAsync"/> +
///         <see cref="IAccountRepository.UpdateAsync"/> +
///         <see cref="IAccountRepository.DeleteAsync"/> with audit logging
///         via the generic <see cref="DecoratedRepository{T}"/> core
///         (the slice 8a.1 <c>IRepository&lt;Account&gt;</c> extension +
///         <c>RemoveAsync</c> → <c>DeleteAsync</c> rename make the
///         interface fit the canonical generic CRUD surface).</item>
///   <item>Wrapping <see cref="IAccountRepository.UpdateAsync"/> with
///         <b>cross-tenant <c>IsOwner</c> check</b>: an <see cref="Account"/>
///         whose <c>UserId</c> doesn't match the calling user's
///         <see cref="ITenantContext.CurrentUserId"/> is rejected with
///         <see cref="UnauthorizedAccessException"/> AND a
///         <see cref="AuditAction.Denied"/> audit row is written —
///         the security/compliance trail for the attempt itself.
///         The diff payload on cross-tenant attempts is null because
///         we never read the inner state.</item>
/// </list>
///
/// <para>
/// <b>Why <c>IsOwner</c> checks <c>account.UserId == currentUserId</c>
/// (NOT <c>account.Id</c>)</b>: Account is a user-owned aggregate — its
/// <c>UserId</c> FK identifies the owner. The check is therefore
/// membership-vs-actor (mirrors the 7b.1 TradeAuditDecorator pattern).
/// </para>
///
/// <para>
/// <b>DbContext parameter type</b>: the decorator accepts
/// <see cref="DbContext"/> (base type) rather than the concrete
/// <see cref="Persistence.TradingDbContext"/> so the unit-test fixture
/// can register a SQLite-compatible helper DbContext (the full
/// <c>TradingDbContext</c> carries Npgsql-specific array mappings for
/// <c>JournalEntry.Tags</c> that fail to compose on SQLite). In production
/// DI, the registered <c>TradingDbContext</c> is resolved into the
/// <see cref="DbContext"/> parameter.
/// </para>
///
/// <para>
/// Registered via Scrutor:
/// <c>services.Decorate&lt;IAccountRepository, AccountAuditDecorator&gt;()</c>
/// in <see cref="DependencyInjection.TradingModuleRegistration"/>.
/// </para>
/// </summary>
public sealed class AccountAuditDecorator : IAccountRepository
{
    private readonly IAccountRepository _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly DecoratedRepository<Account> _decorated;

    public AccountAuditDecorator(
        IAccountRepository inner,
        DbContext db,
        IAuditLogger audit,
        ITenantContext tenant,
        IClock clock)
    {
        _inner = inner;
        _audit = audit;
        _tenant = tenant;
        _clock = clock;
        _decorated = new DecoratedRepository<Account>(inner, audit, tenant, clock, db: db);
    }

    // ===== Read methods — no audit logging (matches Wave 6 + 7 precedent) =====

    public Task<Account?> FindByIdAsync(Guid id, CancellationToken ct)
        => _inner.FindByIdAsync(id, ct);

    public Task<Account?> GetByIdAsync(Guid id, CancellationToken ct)
        => _inner.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<Account>> ListByUserIdAsync(Guid userId, CancellationToken ct)
        => _inner.ListByUserIdAsync(userId, ct);

    // ===== Mutation methods with audit logging =====

    public Task AddAsync(Account account, CancellationToken ct)
        => _decorated.AddAsync(account, ct);

    public async Task UpdateAsync(Account account, CancellationToken ct)
    {
        if (!IsOwner(account))
        {
            // Slice 8a.1 deviation mirrors the Wave 7 7b.1 TradeAuditDecorator:
            // the audit row's Action is AuditAction.Denied (not the
            // would-have-been action) so compliance officers can filter
            // cross-tenant attempts separately from legitimate state changes.
            await LogDeniedAsync(account, AuditAction.Denied, ct);
            throw new UnauthorizedAccessException(
                $"Account {account.Id} does not belong to current user (cross-tenant attempt).");
        }
        await _decorated.UpdateAsync(account, ct);
    }

    public Task DeleteAsync(Account account, CancellationToken ct)
        => _decorated.DeleteAsync(account, ct);

    /// <summary>
    /// Returns true iff the account belongs to the current user. When no
    /// user is resolved (anonymous / service context), the decorator
    /// allows the operation to proceed — system actors (webhooks,
    /// background services) bypass the user-scope check (matches the
    /// Wave 6 ImportJobAuditDecorator + UserAuditDecorator +
    /// TradeAuditDecorator precedent).
    /// </summary>
    private bool IsOwner(Account account)
        => !_tenant.CurrentUserId.HasValue
            || account.UserId == _tenant.CurrentUserId.Value;

    /// <summary>
    /// Audit-log a denied cross-tenant attempt. The entry carries the
    /// calling user's id (NOT the entity's user id) so the security trail
    /// shows WHO attempted the cross-tenant access. Fire-and-forget:
    /// never throws (the <see cref="IAuditLogger.LogAsync"/> contract
    /// swallows exceptions).
    /// </summary>
    private Task LogDeniedAsync(Account account, AuditAction action, CancellationToken ct)
    {
        var entry = new AuditEventEntry(
            EntityType: nameof(Account),
            EntityId: account.Id,
            Action: action,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: null,
            OccurredAt: _clock.UtcNow);
        return _audit.LogAsync(entry, ct);
    }
}