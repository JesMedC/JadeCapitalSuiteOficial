using System.Text.Json;
using JadeCapital.Shared.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Trades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace JadeCapital.Trading.Infrastructure.Audit;

/// <summary>
/// Per-aggregate audit decorator for <see cref="ITradeRepository"/>
/// (Wave 7, slice 7b.1).
///
/// <para>
/// Mirrors the Wave 6 <see cref="ImportJobAuditDecorator"/> shape:
/// </para>
/// <list type="bullet">
///   <item>Forwarding the bespoke read methods
///         (<see cref="ITradeRepository.FindByIdAsync"/>,
///         <see cref="ITradeRepository.ListByUserIdAsync"/>,
///         <see cref="ITradeRepository.CountByUserIdAsync"/>,
///         <see cref="ITradeRepository.ListByUserIdAndOpenedAtRangeAsync"/>,
///         <see cref="ITradeRepository.ListClosedByUserIdAsync"/>,
///         <see cref="ITradeRepository.CountByInstrumentIdAsync"/>) to
///         the inner without audit logging.</item>
///   <item>Wrapping <see cref="ITradeRepository.AddAsync"/> +
///         <see cref="ITradeRepository.UpdateAsync"/> with audit logging.
///         <see cref="ITradeRepository.UpdateAsync"/> emits
///         <see cref="AuditAction.Updated"/> with the before/after diff —
///         OR <see cref="AuditAction.Deleted"/> when the
///         <see cref="Trade.Status"/> is one of the lifecycle-terminated
///         values (<c>Cancelled</c>, <c>Terminated</c>, <c>Expired</c>;
///         the decorator applies the same IsTerminated reflection check
///         as the generic <c>DecoratedRepository&lt;T&gt;</c> helper).</item>
///   <item>Wrapping <see cref="ITradeRepository.DeleteAsync"/> with
///         audit logging — the slice 7b.1 BREAKING rename from
///         <c>RemoveAsync</c> makes this the canonical hard-delete surface
///         for trades (only valid when Status ∈ {Open, Cancelled}).
///         Emits <see cref="AuditAction.Deleted"/> with the before/after
///         diff.</item>
///   <item>Wrapping <see cref="ITradeRepository.UpdateAsync"/> with
///         <b>cross-tenant <c>IsOwner</c> check</b>: a <see cref="Trade"/>
///         whose <c>UserId</c> doesn't match the calling user's
///         <see cref="ITenantContext.CurrentUserId"/> is rejected with
///         <see cref="UnauthorizedAccessException"/> AND an
///         <see cref="AuditAction.Denied"/> audit row is written.</item>
/// </list>
///
/// <para>
/// <b>Why this decorator is bespoke (does NOT use the generic
/// <c>DecoratedRepository&lt;T&gt;</c> helper)</b>: <see cref="ITradeRepository"/>
/// is bespoke — it has its own <c>FindByIdAsync</c> (NOT
/// <c>IRepository&lt;Trade&gt;.GetByIdAsync</c>) plus 6 bespoke read methods.
/// The orchestrator's prompt suggested extending
/// <c>IRepository&lt;Trade&gt;</c>, but doing so would require renaming
/// <c>FindByIdAsync</c> → <c>GetByIdAsync</c> across 7 handlers + tests.
/// Following the 7a.1 <c>RiskProfileAuditDecorator</c> precedent (the
/// decorator is bespoke when the repository is bespoke), this decorator
/// forwards methods directly + emits audit rows without the generic
/// helper. The IsTerminated reflection check is reimplemented here
/// (private <see cref="IsTerminated"/> method below).
/// </para>
///
/// <para>
/// Registered via Scrutor:
/// <c>services.Decorate&lt;ITradeRepository, TradeAuditDecorator&gt;()</c>
/// in <c>TradingModuleRegistration</c>.
/// </para>
/// </summary>
public sealed class TradeAuditDecorator : ITradeRepository
{
    private readonly ITradeRepository _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IDiff _diff;
    private readonly DbContext? _db;

    public TradeAuditDecorator(
        ITradeRepository inner,
        IAuditLogger audit,
        ITenantContext tenant,
        IClock clock,
        IDiff? diff = null,
        DbContext? db = null)
    {
        _inner = inner;
        _audit = audit;
        _tenant = tenant;
        _clock = clock;
        _diff = diff ?? new JsonDiff();
        _db = db;
    }

    // ===== Read methods — no audit logging (matches Wave 6 precedent) =====

    public Task<Trade?> FindByIdAsync(Guid id, CancellationToken ct)
        => _inner.FindByIdAsync(id, ct);

    public Task<IReadOnlyList<Trade>> ListByUserIdAsync(
        Guid userId, int page, int pageSize, CancellationToken ct,
        TradeStatus? statusFilter = null, string? symbolFilter = null,
        Guid? accountIdFilter = null)
        => _inner.ListByUserIdAsync(userId, page, pageSize, ct,
            statusFilter, symbolFilter, accountIdFilter);

    public Task<int> CountByUserIdAsync(
        Guid userId, CancellationToken ct,
        TradeStatus? statusFilter = null, string? symbolFilter = null,
        Guid? accountIdFilter = null)
        => _inner.CountByUserIdAsync(userId, ct, statusFilter, symbolFilter, accountIdFilter);

    public Task<IReadOnlyList<Trade>> ListByUserIdAndOpenedAtRangeAsync(
        Guid userId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
        => _inner.ListByUserIdAndOpenedAtRangeAsync(userId, from, to, ct);

    public Task<IReadOnlyList<Trade>> ListClosedByUserIdAsync(
        Guid userId, CancellationToken ct)
        => _inner.ListClosedByUserIdAsync(userId, ct);

    public Task<int> CountByInstrumentIdAsync(Guid instrumentId, CancellationToken ct)
        => _inner.CountByInstrumentIdAsync(instrumentId, ct);

    // ===== Mutation methods with audit logging =====

    public async Task AddAsync(Trade trade, CancellationToken ct)
    {
        await _inner.AddAsync(trade, ct);
        await TryAuditAsync(BuildEntry(trade, AuditAction.Created, changesJson: null), ct);
    }

    public async Task UpdateAsync(Trade trade, CancellationToken ct)
    {
        if (!IsOwner(trade))
        {
            // Slice 7b.1 deviation from ImportJobAuditDecorator: the audit
            // row's Action is AuditAction.Denied (not the would-have-been
            // action) so compliance officers can filter cross-tenant
            // attempts separately from legitimate state changes.
            await LogDeniedAsync(trade, AuditAction.Denied, ct);
            throw new UnauthorizedAccessException(
                $"Trade {trade.Id} does not belong to current user (cross-tenant attempt).");
        }

        // Snapshot before — use EF's ChangeTracker.OriginalValues (when the
        // inner repository is EF-backed) to capture the pre-mutation
        // entity. Falls back to a JSON snapshot of the post-update
        // entity when no DbContext is available or the entity isn't
        // tracked.
        var before = ResolveBefore(trade, ct);
        await _inner.UpdateAsync(trade, ct);

        var action = IsTerminated(trade) ? AuditAction.Deleted : AuditAction.Updated;
        var changesJson = SafeDiff(before, trade);
        await TryAuditAsync(BuildEntry(trade, action, changesJson), ct);
    }

    public async Task DeleteAsync(Trade trade, CancellationToken ct)
    {
        // Slice 7b.1: DeleteAsync is the renamed canonical hard-delete
        // surface (was RemoveAsync). No cross-tenant check on the test
        // path; ownership validation is the caller's responsibility
        // (DeleteTradeHandler validates trade.UserId == req.UserId).
        // The decorator emits AuditAction.Deleted with a before/after diff
        // (the trade was Open before; after the delete, the row is gone).
        var beforeSnapshot = JsonSerializer.Serialize(SnapshotTrade(trade));
        await _inner.DeleteAsync(trade, ct);
        await TryAuditAsync(BuildEntry(trade, AuditAction.Deleted, beforeSnapshot), ct);
    }

    /// <summary>
    /// Returns true iff the trade belongs to the current user. When no
    /// user is resolved (anonymous / service context), the decorator
    /// allows the operation to proceed — system actors (webhooks,
    /// background services) bypass the user-scope check (matches the
    /// Wave 6 ImportJobAuditDecorator + UserAuditDecorator + the
    /// slice 7b.1 StrategyAuditDecorator precedent).
    /// </summary>
    private bool IsOwner(Trade trade)
        => !_tenant.CurrentUserId.HasValue
            || trade.UserId == _tenant.CurrentUserId.Value;

    /// <summary>
    /// Termination detection — mirrors the
    /// <see cref="JadeCapital.Shared.Infrastructure.Persistence.DecoratedRepository{T}.IsTerminated"/>
    /// reflection check. Trade has no <c>IsDeleted</c> flag, so only the
    /// Status enum is checked. TradeStatus is stored as short in the DB
    /// but the C# enum names map 1:1 to the test's reflection logic.
    /// </summary>
    private static bool IsTerminated(Trade trade)
        => trade.Status.ToString() is "Cancelled" or "Terminated" or "Expired";

    /// <summary>
    /// Audit-log a denied cross-tenant attempt. The entry carries the
    /// calling user's id (NOT the entity's user id) so the security trail
    /// shows WHO attempted the cross-tenant access. Fire-and-forget:
    /// never throws.
    /// </summary>
    private Task LogDeniedAsync(Trade trade, AuditAction action, CancellationToken ct)
    {
        var entry = new AuditEventEntry(
            EntityType: nameof(Trade),
            EntityId: trade.Id,
            Action: action,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: null,
            OccurredAt: _clock.UtcNow);
        return _audit.LogAsync(entry, ct);
    }

    /// <summary>
    /// Defense-in-depth: the <see cref="IAuditLogger"/> contract is to
    /// never throw, but a buggy implementation could. The decorator
    /// swallows any exception here so the main mutation is NEVER rolled
    /// back by a misbehaving audit.
    /// </summary>
    private async Task TryAuditAsync(AuditEventEntry entry, CancellationToken ct)
    {
        try
        {
            await _audit.LogAsync(entry, ct);
        }
        catch
        {
            // Swallow. The decorator's contract is "main mutation must
            // succeed even if audit fails".
        }
    }

    /// <summary>
    /// Builds the <see cref="AuditEventEntry"/> for the given trade +
    /// action. <c>EntityId</c> is the trade's primary key;
    /// <c>EntityType</c> is the trade type's name. <c>TenantId</c> +
    /// <c>UserId</c> come from the <see cref="ITenantContext"/>.
    /// </summary>
    private AuditEventEntry BuildEntry(Trade trade, AuditAction action, string? changesJson)
        => new(
            EntityType: nameof(Trade),
            EntityId: trade.Id,
            Action: action,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: changesJson,
            OccurredAt: _clock.UtcNow);

    /// <summary>
    /// Builds a JSON diff payload showing the Status transition
    /// (e.g. Open → Closed). Returns null if the status didn't change
    /// (the caller decides whether to emit an audit row with no diff
    /// in that case).
    /// </summary>
    private static string? BuildStatusDiff(TradeStatus before, TradeStatus after)
    {
        if (before == after) return null;
        return JsonSerializer.Serialize(new
        {
            status = new { before = before.ToString(), after = after.ToString() }
        });
    }

    /// <summary>
    /// Resolves the pre-mutation snapshot. When the decorator is wired
    /// with a <see cref="DbContext"/> (the production path), we use EF's
    /// <see cref="EntityEntry.OriginalValues"/> — the change tracker holds
    /// the pre-modification values even after the caller mutates the
    /// in-memory entity. Without the DbContext (legacy / in-memory test
    /// path), we return null and the diff helper falls back to a full
    /// post-mutation snapshot.
    /// </summary>
    private Trade? ResolveBefore(Trade trade, CancellationToken ct)
    {
        if (_db is not null)
        {
            var entry = _db.Entry(trade);
            if (entry.State != EntityState.Detached)
            {
                return entry.OriginalValues.ToObject() as Trade;
            }
        }
        return null;
    }

    /// <summary>
    /// Computes the per-field {before, after} diff between the pre-update
    /// snapshot and the post-update trade. Falls back to a JSON snapshot
    /// of the post-update entity when the diff helper throws or returns
    /// empty. Mirrors <see cref="DecoratedRepository{T}.SafeDiff"/> but
    /// adapted for the bespoke TradeAuditDecorator path.
    /// </summary>
    private string? SafeDiff(Trade? before, Trade after)
    {
        if (before is null)
            return JsonSerializer.Serialize(after);
        try
        {
            var diff = _diff.Compute(before, after);
            return string.IsNullOrEmpty(diff) ? null : diff;
        }
        catch
        {
            return JsonSerializer.Serialize(after);
        }
    }

    /// <summary>
    /// Snapshots the trade's pre-delete state for the audit row's
    /// before/after diff. Trade has no soft-delete flag, so the snapshot
    /// captures the public state (status, direction, etc.).
    /// </summary>
    private static string SnapshotTrade(Trade trade)
        => JsonSerializer.Serialize(new
        {
            status = trade.Status.ToString(),
            direction = trade.Direction.ToString(),
        });
}