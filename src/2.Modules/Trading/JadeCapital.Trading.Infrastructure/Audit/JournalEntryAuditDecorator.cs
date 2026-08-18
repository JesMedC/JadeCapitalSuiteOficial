using System.Text.Json;
using JadeCapital.Shared.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Journal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace JadeCapital.Trading.Infrastructure.Audit;

/// <summary>
/// Per-aggregate audit decorator for <see cref="IJournalEntryRepository"/>
/// (Wave 7, slice 7b.2).
///
/// <para>
/// <b>Why this decorator is bespoke (does NOT use the generic
/// <c>DecoratedRepository&lt;T&gt;</c> helper)</b>: <see cref="IJournalEntryRepository"/>
/// is bespoke (does NOT extend <c>IRepository&lt;JournalEntry&gt;</c>).
/// It has cross-user-scoped read methods
/// (<see cref="IJournalEntryRepository.GetByUserAndDateAsync"/>,
/// <see cref="IJournalEntryRepository.ListByRangeAsync"/>,
/// <see cref="IJournalEntryRepository.FindByIdAsync"/> — the last
/// takes an explicit <c>userId</c> parameter for cross-user safety)
/// that don't fit the generic shape. Extending <c>IRepository&lt;T&gt;</c>
/// would force a <c>GetByIdAsync(Guid, ct)</c> method that ignores the
/// cross-user scope — a security regression. Following the 7a.1
/// <see cref="JadeCapital.Identity.Infrastructure.Audit.RiskProfileAuditDecorator"/>
/// + 7b.1 <see cref="TradeAuditDecorator"/> precedent for bespoke
/// repositories, this decorator is bespoke too: it forwards methods
/// directly + emits audit rows without the generic helper.
/// </para>
///
/// <para>
/// <b>Why the slice 7b.2 surgery adds a <c>DeleteAsync(JournalEntry, ct)</c>
/// overload</b>: the slice 7b.2 Phase 1 surgery adds an additive
/// overload <see cref="IJournalEntryRepository.DeleteAsync(JournalEntry, CancellationToken)"/>
/// that the decorator wraps. The overload internally calls
/// <see cref="IJournalEntryRepository.DeleteAsync(Guid, CancellationToken)"/>
/// (the original production path). Production handlers that have the
/// entity in scope can use the new overload; production handlers that
/// only have the Guid can still use the original (e.g.
/// <c>DeleteJournalEntryHandler</c>). Both paths emit
/// <see cref="AuditAction.Deleted"/> when going through the decorator —
/// but the Guid-only path is the production fast path (no audit, just
/// the delete), while the entity-arg path goes through the decorator
/// (audited + cross-tenant checked).
/// </para>
///
/// <para>
/// The decorator implements <see cref="IJournalEntryRepository"/> by:
/// </para>
/// <list type="bullet">
///   <item>Forwarding the bespoke read methods
///         (<see cref="GetByUserAndDateAsync"/>,
///         <see cref="ListByRangeAsync"/>,
///         <see cref="FindByIdAsync"/>) to the inner without
///         audit logging (matches the Wave 6 + 7a.1 + 7b.1 precedent:
///         reads are not audited).</item>
///   <item>Wrapping <see cref="AddAsync"/> with audit logging —
///         emits <see cref="AuditAction.Created"/> on success.</item>
///   <item>Wrapping <see cref="UpdateAsync"/> with cross-tenant
///         <c>IsOwner</c> check on <see cref="JournalEntry.UserId"/>
///         + audit logging. Cross-tenant rejection emits
///         <see cref="AuditAction.Denied"/> + throws
///         <see cref="UnauthorizedAccessException"/>. The inner
///         <c>UpdateAsync</c> is NEVER reached on rejection.</item>
///   <item>Wrapping <see cref="DeleteAsync(JournalEntry, CancellationToken)"/>
///         (the new Phase 1 overload) with cross-tenant <c>IsOwner</c>
///         check + audit logging. Cross-tenant rejection emits
///         <see cref="AuditAction.Denied"/> + throws
///         <see cref="UnauthorizedAccessException"/>. The inner
///         is NEVER reached on rejection.</item>
///   <item>Forwarding <see cref="DeleteAsync(Guid, CancellationToken)"/>
///         (the original production path) to the inner directly
///         without audit logging. This is the canonical hard-delete
///         path used by <c>DeleteJournalEntryHandler</c>; the handler
///         is responsible for the cross-tenant / cross-user validation
///         (it pre-loads the entry via <c>FindByIdAsync</c> with
///         explicit <c>userId</c> scope before calling this overload).</item>
/// </list>
///
/// <para>
/// Registered via Scrutor:
/// <c>services.Decorate&lt;IJournalEntryRepository, JournalEntryAuditDecorator&gt;()</c>
/// in <c>TradingModuleRegistration</c>.
/// </para>
/// </summary>
public sealed class JournalEntryAuditDecorator : IJournalEntryRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IJournalEntryRepository _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IDiff _diff;
    private readonly DbContext? _db;

    public JournalEntryAuditDecorator(
        IJournalEntryRepository inner,
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

    // ===== Read methods — no audit logging (matches Wave 6 + 7a.1 + 7b.1 precedent) =====

    public Task<JournalEntry?> GetByUserAndDateAsync(
        Guid userId, LocalDate localDate, CancellationToken ct)
        => _inner.GetByUserAndDateAsync(userId, localDate, ct);

    public Task<IReadOnlyList<JournalEntry>> ListByRangeAsync(
        Guid userId, LocalDate from, LocalDate to, CancellationToken ct)
        => _inner.ListByRangeAsync(userId, from, to, ct);

    public Task<JournalEntry?> FindByIdAsync(
        Guid entryId, Guid userId, CancellationToken ct)
        => _inner.FindByIdAsync(entryId, userId, ct);

    // ===== Mutation methods with audit logging =====

    public async Task<JournalEntry> AddAsync(JournalEntry entry, CancellationToken ct)
    {
        var result = await _inner.AddAsync(entry, ct);
        await TryAuditAsync(BuildEntry(entry, AuditAction.Created, changesJson: null), ct);
        return result;
    }

    public async Task<JournalEntry> UpdateAsync(JournalEntry entry, CancellationToken ct)
    {
        if (!IsOwner(entry))
        {
            // Slice 7b.2 deviation from a would-have-been action emission
            // (ImportJobAuditDecorator uses the would-have-been action;
            // the Wave 7 typed decorators use AuditAction.Denied for
            // filterable cross-tenant attempts): the audit row's Action
            // is AuditAction.Denied so compliance officers can filter
            // cross-tenant attempts separately from legitimate state
            // changes.
            await LogDeniedAsync(entry, ct);
            throw new UnauthorizedAccessException(
                $"JournalEntry {entry.Id} does not belong to current user (cross-tenant attempt).");
        }

        // Snapshot before — use EF's ChangeTracker.OriginalValues (when
        // the inner repository is EF-backed) to capture the pre-mutation
        // entity. Falls back to a full post-mutation JSON snapshot when
        // no DbContext is available or the entity isn't tracked.
        var before = ResolveBefore(entry);
        var result = await _inner.UpdateAsync(entry, ct);

        var changesJson = SafeDiff(before, entry);
        await TryAuditAsync(BuildEntry(entry, AuditAction.Updated, changesJson), ct);
        return result;
    }

    /// <summary>
    /// Slice 7b.2 additive overload — the path the decorator wraps. The
    /// inner <c>DeleteAsync(JournalEntry, ct)</c> internally calls
    /// <c>DeleteAsync(Guid, ct)</c>; the decorator emits the audit row
    /// on this path with a before-snapshot of the entry's content
    /// fields.
    /// </summary>
    public async Task DeleteAsync(JournalEntry entry, CancellationToken ct)
    {
        if (!IsOwner(entry))
        {
            await LogDeniedAsync(entry, ct);
            throw new UnauthorizedAccessException(
                $"JournalEntry {entry.Id} does not belong to current user (cross-tenant attempt).");
        }

        // Snapshot the entry's content fields before the inner deletes
        // the row. The before snapshot is the audit row's ChangesJson —
        // compliance officers can see exactly what the entry contained
        // before the hard delete (premarket_plan, postmarket_reflection,
        // mood, tags, timezone).
        var beforeSnapshot = SnapshotEntry(entry);
        await _inner.DeleteAsync(entry, ct);
        await TryAuditAsync(BuildEntry(entry, AuditAction.Deleted, beforeSnapshot), ct);
    }

    /// <summary>
    /// Forward-only — the original production path used by
    /// <c>DeleteJournalEntryHandler</c>. The handler is responsible
    /// for cross-user validation (it pre-loads the entry via
    /// <c>FindByIdAsync</c> with explicit <c>userId</c> scope before
    /// calling this overload). The decorator does NOT emit an audit
    /// row on this path because the handler's intent is "delete
    /// by id, trust the userId scope was validated upstream" —
    /// adding an audit row here would create a redundant event for
    /// handlers that also call the new entity-arg overload.
    /// </summary>
    public Task DeleteAsync(Guid entryId, CancellationToken ct)
        => _inner.DeleteAsync(entryId, ct);

    // ===== Helpers =====

    /// <summary>
    /// Returns true iff the entry belongs to the current user. When no
    /// user is resolved (anonymous / service context), the decorator
    /// allows the operation to proceed — system actors (webhooks,
    /// background services) bypass the user-scope check (matches the
    /// Wave 6 <c>ImportJobAuditDecorator</c> + 7a.1
    /// <c>UserAuditDecorator</c> + 7b.1 <c>TradeAuditDecorator</c>
    /// precedent).
    /// </summary>
    private bool IsOwner(JournalEntry entry)
        => !_tenant.CurrentUserId.HasValue
            || entry.UserId == _tenant.CurrentUserId.Value
            || _tenant.IsSuperAdmin;

    /// <summary>
    /// Audit-log a denied cross-tenant attempt. The entry carries the
    /// calling user's id (NOT the entity's user id) so the security trail
    /// shows WHO attempted the cross-tenant access. Fire-and-forget:
    /// never throws.
    /// </summary>
    private Task LogDeniedAsync(JournalEntry entry, CancellationToken ct)
    {
        var entry_row = new AuditEventEntry(
            EntityType: nameof(JournalEntry),
            EntityId: entry.Id,
            Action: AuditAction.Denied,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: null,
            OccurredAt: _clock.UtcNow);
        return _audit.LogAsync(entry_row, ct);
    }

    /// <summary>
    /// Defense-in-depth: the <see cref="IAuditLogger"/> contract is to
    /// never throw, but a buggy implementation could. The decorator
    /// swallows any exception here so the main mutation is NEVER rolled
    /// back by a misbehaving audit.
    /// </summary>
    private async Task TryAuditAsync(AuditEventEntry auditEntry, CancellationToken ct)
    {
        try
        {
            await _audit.LogAsync(auditEntry, ct);
        }
        catch
        {
            // Swallow. The decorator's contract is "main mutation must
            // succeed even if audit fails".
        }
    }

    /// <summary>
    /// Builds the <see cref="AuditEventEntry"/> for the given entry +
    /// action. <c>EntityId</c> is the entry's primary key;
    /// <c>EntityType</c> is the entry type's name. <c>TenantId</c> +
    /// <c>UserId</c> come from the <see cref="ITenantContext"/>.
    /// </summary>
    private AuditEventEntry BuildEntry(JournalEntry entry, AuditAction action, string? changesJson)
        => new(
            EntityType: nameof(JournalEntry),
            EntityId: entry.Id,
            Action: action,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: changesJson,
            OccurredAt: _clock.UtcNow);

    /// <summary>
    /// Resolves the pre-mutation snapshot. When the decorator is wired
    /// with a <see cref="DbContext"/> (the production path), we use EF's
    /// <see cref="EntityEntry.OriginalValues"/> — the change tracker holds
    /// the pre-modification values even after the caller mutates the
    /// in-memory entity. Without the DbContext (legacy / in-memory test
    /// path), we return null and the diff helper falls back to a full
    /// post-mutation snapshot.
    /// </summary>
    private JournalEntry? ResolveBefore(JournalEntry entry)
    {
        if (_db is not null)
        {
            var tracked = _db.Entry(entry);
            if (tracked.State != EntityState.Detached)
            {
                return tracked.OriginalValues.ToObject() as JournalEntry;
            }
        }
        return null;
    }

    /// <summary>
    /// Computes the per-field {before, after} diff between the pre-update
    /// snapshot and the post-update entry. Falls back to a JSON snapshot
    /// of the post-update entity when the diff helper throws or returns
    /// empty. Mirrors the 7b.1 <c>TradeAuditDecorator.SafeDiff</c>
    /// pattern.
    /// </summary>
    private string? SafeDiff(JournalEntry? before, JournalEntry after)
    {
        if (before is null)
            return JsonSerializer.Serialize(after, JsonOptions);
        try
        {
            var diff = _diff.Compute(before, after);
            return string.IsNullOrEmpty(diff) ? null : diff;
        }
        catch
        {
            return JsonSerializer.Serialize(after, JsonOptions);
        }
    }

    /// <summary>
    /// Snapshots the entry's pre-delete content fields for the audit
    /// row's before/after diff. The diff captures the entry's
    /// user-facing state so compliance officers can see exactly what
    /// the entry contained before the hard delete.
    /// </summary>
    private static string SnapshotEntry(JournalEntry entry)
        => JsonSerializer.Serialize(new
        {
            premarketPlan = entry.PremarketPlan,
            postmarketReflection = entry.PostmarketReflection,
            moodPre = entry.MoodPre?.Value,
            moodDuring = entry.MoodDuring?.Value,
            moodPost = entry.MoodPost?.Value,
            tags = entry.Tags,
            timezone = entry.Timezone,
        }, JsonOptions);
}
