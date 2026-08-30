using System.Text.Json;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.TradeAttachments;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Audit;

/// <summary>
/// Per-aggregate audit decorator for <see cref="IAttachmentSweepRepository"/>
/// (Wave 9, slice 9a.3 — sub-scope A coverage extension).
///
/// <para>
/// <b>Decorator shape</b>: bespoke batch soft-delete — the FIRST
/// "1-call-many-audit-rows" decorator in the codebase. <see cref="IAttachmentSweepRepository.SoftDeleteBatchAsync"/>
/// takes <c>IReadOnlyList&lt;Guid&gt;</c> + returns <c>int</c> (a batch
/// mutation that does NOT fit the canonical <c>IRepository&lt;T&gt;.DeleteAsync(T, ct)</c>
/// single-entity shape). The decorator loads each attachment via the
/// scoped <see cref="DbContext"/>, runs the cross-tenant <c>IsOwner</c>
/// check PER id, then either:
/// <list type="bullet">
///   <item>All owned → call <c>_inner.SoftDeleteBatchAsync</c> + emit one
///         <see cref="AuditAction.Updated"/> audit row per id with
///         <c>EntityType = "TradeAttachment"</c> + <c>ChangesJson</c>
///         capturing <c>isActive: { before: true, after: false }</c>.</item>
///   <item>Any cross-tenant id → emit one <see cref="AuditAction.Denied"/>
///         audit row for THAT id + throw <see cref="UnauthorizedAccessException"/>
///         for the WHOLE batch (transaction abort — the inner is NEVER reached;
///         the owned ids in the batch get NO audit row).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why bespoke (does NOT use the generic
/// <c>DecoratedRepository&lt;T&gt;</c> helper)</b>: the batch mutation
/// shape is fundamentally different from the canonical
/// <c>IRepository&lt;T&gt;.DeleteAsync(T, ct)</c>. Bespoke decorator
/// preserves the batch shape + emits 1 audit row per id. Mirrors the
/// Wave 7 7b.1 + 8a.1 + 8a.2 + 8a.3 + 9a.1 + 9a.2 precedent for
/// bespoke-shape decorators.
/// </para>
///
/// <para>
/// <b>Cross-tenant <c>IsOwner</c> check per id</b>: per spec §9a.3
/// (the spec's Requirement "Audit decorator for AttachmentSweep batch
/// soft-delete"): "Cross-tenant <c>IsOwner</c> check MUST compare
/// <c>attachment.UserId</c> to <c>ITenantContext.CurrentUserId</c>; on
/// mismatch, emit <c>AuditAction.Denied</c> for THAT id + throw
/// <c>UnauthorizedAccessException</c> for the WHOLE batch." The check
/// is per-id because the only caller — the daily
/// <c>AttachmentLifecycleService</c> background sweep — loads a page of
/// expired rows from a SINGLE user but the cross-tenant defensive path
/// could fire if a future caller submits a multi-user batch. The
/// decorator is the only enforcement point; the decorator loads each
/// attachment via the scoped <see cref="DbContext"/> to read the
/// <see cref="TradeAttachment.UserId"/>.
/// </para>
///
/// <para>
/// <b>Why <c>EntityType = "TradeAttachment"</c> (not
/// <c>"AttachmentSweep"</c>)</b>: per design.md §"4. AttachmentSweepAuditDecorator",
/// the user-impacting event is the soft-delete of the attachment itself,
/// NOT the sweep operation. The sweep is the BACKGROUND SERVICE that
/// batches the soft-deletes; the audit row records the per-attachment
/// change so compliance officers can query
/// <c>entity_type = "TradeAttachment"</c> + <c>action = "Updated"</c>
/// + <c>changes @> '{"isActive":{"after":false}}'</c> to find every
/// soft-deleted attachment across all the various entry points (the
/// sweep + any future single-entity soft-delete handler).
/// </para>
///
/// <para>
/// <b>Why <c>InsertAuditAsync</c> is forwarded without audit</b>: this
/// method IS the write to <c>trading.attachments_quota_audit</c> (the
/// sweep's own audit log). Auditing it would create an infinite loop —
/// the decorator would emit <c>audit.events</c> rows for the writes
/// to the sweep's audit log, and the daily retention sweep
/// (<c>AuditRetentionBackgroundService</c>, Wave 9 9b.1) would have to
/// filter them out. Mirrors the Wave 8 8b.2
/// <c>IStripeWebhookEventRepository</c> SKIP rationale: the destination
/// IS the audit log; emitting on top would be doubly-recorded noise.
/// </para>
///
/// <list type="bullet">
///   <item>Wrapping <see cref="IAttachmentSweepRepository.SoftDeleteBatchAsync"/>
///         with the <b>cross-tenant <c>IsOwner</c> check per id</b> +
///         one <see cref="AuditAction.Updated"/> audit row per id with
///         <c>EntityType = "TradeAttachment"</c> + a
///         <c>isActive: { before: true, after: false }</c> diff JSON.</item>
///   <item>Forwarding <see cref="IAttachmentSweepRepository.GetExpiredBatchAsync"/> +
///         <see cref="IAttachmentSweepRepository.GetUserAggregateAsync"/> +
///         <see cref="IAttachmentSweepRepository.GetActiveUserIdsAsync"/> to
///         the inner without audit logging. Reads are not audited.</item>
///   <item>Forwarding <see cref="IAttachmentSweepRepository.InsertAuditAsync"/>
///         to the inner WITHOUT audit logging — auditing it would create
///         an infinite loop.</item>
/// </list>
///
/// <para>
/// <b>Decorator signature</b> — 5 dependencies (5 deps):
/// <c>inner</c> + <c>audit</c> + <c>tenant</c> + <c>clock</c> +
/// <c>db</c> (the shareable <c>DecoratedRepository&lt;T&gt;</c>-style
/// signature). The <c>db</c> is required to load each
/// <see cref="TradeAttachment"/> entity for the <c>IsOwner</c> pre-check
/// + the <c>IsActive</c> diff snapshot. The decorator accepts
/// <see cref="DbContext"/> (base type) rather than the concrete
/// <c>TradingDbContext</c> so the unit-test fixture can register a
/// SQLite-compatible helper DbContext. In production DI, the registered
/// <c>TradingDbContext</c> is resolved into the <see cref="DbContext"/>
/// parameter.
/// </para>
///
/// <para>
/// Registered via Scrutor:
/// <c>services.Decorate&lt;IAttachmentSweepRepository, AttachmentSweepAuditDecorator&gt;()</c>
/// in <see cref="DependencyInjection.TradingModuleRegistration"/> (after
/// the inner <c>AddScoped&lt;IAttachmentSweepRepository&gt;</c>
/// registration).
/// </para>
/// </summary>
public sealed class AttachmentSweepAuditDecorator : IAttachmentSweepRepository
{
    private readonly IAttachmentSweepRepository _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly DbContext _db;

    public AttachmentSweepAuditDecorator(
        IAttachmentSweepRepository inner,
        IAuditLogger audit,
        ITenantContext tenant,
        IClock clock,
        DbContext db)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(db);

        _inner = inner;
        _audit = audit;
        _tenant = tenant;
        _clock = clock;
        _db = db;
    }

    // ===== Read methods — no audit logging (matches Wave 6 + 7 + 8a.x + 8b.1 + 9a.1 + 9a.2 precedent) =====

    public Task<IReadOnlyList<ExpiredAttachmentSweepRow>> GetExpiredBatchAsync(
        DateTimeOffset asOf, int skip, int take, CancellationToken ct)
        => _inner.GetExpiredBatchAsync(asOf, skip, take, ct);

    public Task<(long TotalBytes, int Count)> GetUserAggregateAsync(
        Guid userId, CancellationToken ct)
        => _inner.GetUserAggregateAsync(userId, ct);

    public Task<IReadOnlyList<Guid>> GetActiveUserIdsAsync(CancellationToken ct)
        => _inner.GetActiveUserIdsAsync(ct);

    // ===== InsertAuditAsync — forwarded WITHOUT audit (would create infinite loop) =====

    public Task InsertAuditAsync(
        Guid userId,
        DateTimeOffset ranAt,
        int cleanedCount,
        long cleanedBytes,
        int remainingCount,
        long remainingBytes,
        string? skippedReason,
        string? errorMessage,
        CancellationToken ct)
    {
        // CRITICAL: this method IS the write to
        // trading.attachments_quota_audit (the sweep's own audit log).
        // Auditing it would create an infinite loop — the decorator would
        // emit audit.events rows for writes to the sweep's audit log,
        // and the daily retention sweep (Wave 9 9b.1
        // AuditRetentionBackgroundService) would have to filter them out.
        // Mirrors the Wave 8 8b.2 IStripeWebhookEventRepository SKIP
        // rationale: the destination IS the audit log; emitting on top
        // would be doubly-recorded noise. The inner InsertAuditAsync is
        // forwarded bare.
        return _inner.InsertAuditAsync(
            userId, ranAt, cleanedCount, cleanedBytes,
            remainingCount, remainingBytes, skippedReason, errorMessage, ct);
    }

    // ===== Mutation method with cross-tenant IsOwner check per id + audit logging (NEW 1-call-many-audit-rows pattern) =====

    public async Task<int> SoftDeleteBatchAsync(
        IReadOnlyList<Guid> attachmentIds, CancellationToken ct)
    {
        if (attachmentIds.Count == 0) return 0;

        // ===== Step 1: Load each attachment via the scoped DbContext =====
        // The decorator needs each attachment's UserId for the IsOwner
        // pre-check + the IsActive flag for the diff snapshot. We use
        // the scoped DbContext (same instance the inner uses) so the
        // tracked instances are shared — the inner's MarkSwept call
        // mutates the same in-memory objects the decorator holds. To
        // avoid race conditions on the IsActive diff, we capture the
        // pre-mutation IsActive value into a local dictionary BEFORE
        // calling the inner.
        var loaded = await _db.Set<TradeAttachment>()
            .Where(a => attachmentIds.Contains(a.Id))
            .ToListAsync(ct);

        // Snapshot the pre-mutation IsActive per id BEFORE the inner
        // mutates the tracked instances.
        var isActiveBeforeById = loaded.ToDictionary(a => a.Id, a => a.IsActive);

        var byId = loaded.ToDictionary(a => a.Id);

        // ===== Step 2: Cross-tenant IsOwner check per id =====
        // Per spec §9a.3: cross-tenant attempts emit AuditAction.Denied
        // for THAT id + throw UnauthorizedAccessException for the WHOLE
        // batch (transaction abort semantics).
        var deniedIds = new List<Guid>();
        var deniedAttachments = new List<TradeAttachment>();
        foreach (var id in attachmentIds)
        {
            if (!byId.TryGetValue(id, out var attachment))
            {
                // The id wasn't found in the loaded set. The
                // SoftDeleteBatchAsync inner is idempotent (re-running
                // on already-soft-deleted rows is a no-op per the
                // interface contract), but a missing id here means the
                // caller submitted an invalid id. Emit a Failed audit
                // row + throw InvalidOperationException so the misuse is
                // on the audit trail.
                await LogFailedAsync(id, $"TradeAttachment {id} not found by id", ct);
                throw new InvalidOperationException(
                    $"TradeAttachment {id} not found by id — SoftDeleteBatchAsync requires " +
                    "ids of attachments that exist in the database.");
            }

            if (!IsOwner(attachment))
            {
                // Cross-tenant attempt: emit AuditAction.Denied (NOT the
                // would-have-been Updated action) so compliance officers
                // can filter cross-tenant attempts separately from
                // legitimate state changes. The Denied row carries the
                // ATTACKER's user id (not the legitimate owner) — the
                // security trail records WHO attempted the access.
                await LogDeniedAsync(attachment, ct);
                deniedIds.Add(id);
                deniedAttachments.Add(attachment);
            }
        }

        // ===== Step 3: If any id was denied, throw — whole batch aborts =====
        // The owned ids in the batch get NO audit row (since we abort
        // before emitting Updated for them); the inner.SoftDeleteBatchAsync
        // is never called, so the owned attachments are still IsActive = true.
        if (deniedIds.Count > 0)
        {
            throw new UnauthorizedAccessException(
                $"TradeAttachment batch contains {deniedIds.Count} cross-tenant id(s): " +
                $"[{string.Join(", ", deniedIds)}]. The whole batch is aborted — " +
                "no attachments are soft-deleted.");
        }

        // ===== Step 4: All owned → call inner + emit Updated audit per id =====
        var count = await _inner.SoftDeleteBatchAsync(attachmentIds, ct);

        foreach (var attachment in loaded)
        {
            // The decorator captures the pre-mutation IsActive into a
            // local dictionary BEFORE the inner call so the diff is
            // always {before: <pre-mutation>, after: false} regardless
            // of whether the tracked instance was mutated by the inner.
            var isActiveBefore = isActiveBeforeById[attachment.Id];
            var diff = JsonSerializer.Serialize(new
            {
                isActive = new { before = isActiveBefore, after = false }
            });

            // Emit AuditAction.Deleted (NOT Updated) — per spec
            // §"Batch soft-delete emits N audit rows per id" which mandates
            // `action = "Deleted"` for the soft-delete semantic. Compliance
            // officers query audit.events WHERE action = 2 (Deleted) AND
            // entity_type = "TradeAttachment" to surface user-impacting
            // attachment deletions; emitting Updated would silently miss
            // every batch soft-delete row.
            await TryAuditAsync(BuildEntry(attachment, AuditAction.Deleted, diff), ct);
        }

        return count;
    }

    // ===== Helpers =====

    /// <summary>
    /// Returns true iff the TradeAttachment belongs to the current user.
    /// When no user is resolved (anonymous / service context — the
    /// background sweep service uses a SYSTEM context), the decorator
    /// allows the operation to proceed — system actors bypass the
    /// user-scope check (matches the Wave 6 + 7 + 8a.x + 8b.1 + 9a.1 +
    /// 9a.2 precedent).
    /// </summary>
    private bool IsOwner(TradeAttachment attachment)
        => !_tenant.CurrentUserId.HasValue
            || attachment.UserId == _tenant.CurrentUserId.Value;

    /// <summary>
    /// Audit-log a denied cross-tenant attempt. The entry carries the
    /// <b>calling user's id</b> (NOT the entity's user id) so the
    /// security trail shows WHO attempted the cross-tenant access —
    /// the attacker is recorded, not the legitimate owner.
    /// Fire-and-forget: never throws (swallowed by <see cref="TryAuditAsync"/>).
    /// </summary>
    private Task LogDeniedAsync(TradeAttachment attachment, CancellationToken ct)
    {
        var entry = new AuditEventEntry(
            EntityType: nameof(TradeAttachment),
            EntityId: attachment.Id,
            Action: AuditAction.Denied,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: null,
            OccurredAt: _clock.UtcNow);
        return _audit.LogAsync(entry, ct);
    }

    /// <summary>
    /// Audit-log a Failed SoftDeleteBatchAsync attempt (an id that wasn't
    /// found in the database — invalid input). The decorator emits the
    /// audit row BEFORE re-throwing <see cref="InvalidOperationException"/>
    /// so the misuse is on the audit trail. The entry is distinguishable
    /// from a successful audit row by <see cref="AuditAction.Failed"/> = 5.
    /// </summary>
    private Task LogFailedAsync(Guid attachmentId, string reason, CancellationToken ct)
    {
        var entry = new AuditEventEntry(
            EntityType: nameof(TradeAttachment),
            EntityId: attachmentId,
            Action: AuditAction.Failed,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: JsonSerializer.Serialize(new { reason }),
            OccurredAt: _clock.UtcNow);
        return _audit.LogAsync(entry, ct);
    }

    /// <summary>
    /// Defense-in-depth: the <see cref="IAuditLogger.LogAsync"/> contract
    /// is to never throw, but a buggy implementation could. The decorator
    /// swallows any exception here so the main mutation is NEVER rolled
    /// back by a misbehaving audit (matches the Wave 6 + 7 + 8a.x +
    /// 8b.1 + 9a.1 + 9a.2 decorator pattern).
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
    /// Builds the <see cref="AuditEventEntry"/> for a soft-deleted
    /// attachment. <c>EntityId</c> is the attachment's primary key;
    /// <c>EntityType</c> is the attachment type's name
    /// (<c>"TradeAttachment"</c> — the child aggregate, NOT the sweep
    /// operation). <c>TenantId</c> + <c>UserId</c> come from
    /// <see cref="ITenantContext"/>. <c>ChangesJson</c> carries the
    /// <c>isActive: { before: ..., after: false }</c> diff JSON.
    /// </summary>
    private AuditEventEntry BuildEntry(TradeAttachment attachment, AuditAction action, string? changesJson)
        => new(
            EntityType: nameof(TradeAttachment),
            EntityId: attachment.Id,
            Action: action,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: changesJson,
            OccurredAt: _clock.UtcNow);
}