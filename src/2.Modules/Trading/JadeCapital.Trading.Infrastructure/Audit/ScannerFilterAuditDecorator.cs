using System.Text.Json;
using JadeCapital.Shared.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Scanner;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace JadeCapital.Trading.Infrastructure.Audit;

/// <summary>
/// Per-aggregate audit decorator for <see cref="IScannerFilterRepository"/>
/// (Wave 9, slice 9a.2 — sub-scope A coverage extension).
///
/// <para>
/// <b>Decorator shape</b>: bespoke CRD-without-Delete:
/// <list type="bullet">
///   <item>Wrapping <see cref="IScannerFilterRepository.AddAsync"/> with
///         the <b>cross-tenant <c>IsOwner</c> check</b> +
///         <see cref="AuditAction.Created"/> audit logging. Created
///         events carry no diff (<c>ChangesJson = null</c>) — the
///         <see cref="ScannerFilter"/> is the audit-relevant entity
///         itself; the row IS the create record.</item>
///   <item>Wrapping <see cref="IScannerFilterRepository.UpdateAsync"/> with
///         the <b>cross-tenant <c>IsOwner</c> check</b> +
///         <see cref="AuditAction.Updated"/> audit logging + a before/after
///         diff JSON. The diff is computed via
///         <see cref="ChangeTracker.OriginalValues"/> (when the inner
///         repository is EF-backed) + the generic
///         <see cref="JsonDiff"/> helper (the decorator stays bespoke
///         to preserve the user-scoped read signatures + the
///         <c>IRepository&lt;ScannerFilter&gt;</c> extension contributed
///         by the Wave 9 §9a.2 interface surgery).</item>
///   <item>Forwarding <see cref="IScannerFilterRepository.GetByIdAsync"/> +
///         <see cref="IScannerFilterRepository.GetByUserAndNameAsync"/> +
///         <see cref="IScannerFilterRepository.ListByUserAsync"/> to the
///         inner without audit logging. Reads are not audited (matches
///         the Wave 6 + 7 + 8a.x + 8b.1 + 9a.1 StripeCustomer + 9a.1
///         AIRiskAdvice precedent: only mutations get audit rows).</item>
///   <item>Wrapping <see cref="IScannerFilterRepository.DeleteAsync"/> with
///         a <b>defensive stub</b>: <see cref="ScannerFilter"/> is a
///         soft-delete-by-flag aggregate (canonical mutation surface is
///         <c>ScannerFilter.Deactivate(IClock)</c> flipping
///         <c>IsActive = false</c>), so a hard delete is contractually
///         invalid. The decorator emits an
///         <see cref="AuditAction.Failed"/> audit row BEFORE re-throwing
///         <see cref="NotSupportedException"/> so the misuse is recorded
///         for the compliance trail. The inner
///         <c>IScannerFilterRepository.DeleteAsync</c> is NEVER reached
///         (the production <c>Persistence.ScannerFilterRepository.DeleteAsync</c>
///         also throws <see cref="NotSupportedException"/> as a second
///         line of defense in case a caller bypasses the decorator).
///         Mirrors the Wave 7 7a.1 <c>UserAuditDecorator</c> + 7b.1
///         <c>StrategyAuditDecorator</c> precedent for non-deletable
///         aggregates.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Cross-tenant <c>IsOwner</c> check on <c>AddAsync</c> + <c>UpdateAsync</b>:
/// even though <c>AddAsync</c> inserts a NEW row (no read-then-update
/// race), the row's <see cref="ScannerFilter.UserId"/> still comes from
/// the caller. The <c>CreateOrUpdateScannerFilterHandler</c> (slice 4a)
/// + <c>RunScannerHandler</c> accept the userId as a command parameter
/// — a cross-tenant invocation could submit a filter carrying another
/// tenant's user id. Per spec §9a.2 (the spec's Requirement "Audit
/// decorator for ScannerFilter aggregate"): "Cross-tenant attempts MUST
/// emit <c>AuditAction.Denied</c> and throw
/// <c>UnauthorizedAccessException</c>." The decorator is the only
/// enforcement point (the handler-side consistency is not guaranteed).
/// </para>
///
/// <para>
/// <b>Why bespoke (does NOT use the generic
/// <c>DecoratedRepository&lt;ScannerFilter&gt;</c> helper)</b>:
/// <see cref="IScannerFilterRepository"/> exposes user-scoped reads
/// (<c>GetByUserAndNameAsync(userId, name, ct)</c> +
/// <c>ListByUserAsync(userId, activeOnly, ct)</c>) that don't fit the
/// generic <c>IRepository&lt;T&gt;.GetByIdAsync(Guid, ct)</c> shape.
/// The bespoke decorator preserves the user-scoped read signatures and
/// wraps the inherited <c>AddAsync</c> + <c>UpdateAsync</c> + the
/// inherited <c>DeleteAsync</c> defensive stub with bespoke audit
/// logging. The diff strategy reuses the shared
/// <see cref="JsonDiff"/> helper (Wave 6 6d.2) for the JSON
/// field-by-field computation.
/// </para>
///
/// <para>
/// <b>Decorator signature</b> — 5 dependencies (5 deps + 1 optional):
/// <c>inner</c> + <c>audit</c> + <c>tenant</c> + <c>clock</c> +
/// <c>diff</c> + <c>db</c> (the shareable <c>DecoratedRepository&lt;T&gt;</c>-style
/// signature). The <c>db</c> is optional for the in-memory test path;
/// the production DI resolves <c>TradingDbContext</c> into the
/// <see cref="DbContext"/> base type automatically.
/// </para>
///
/// <para>
/// <b>DbContext parameter type</b>: the decorator accepts
/// <see cref="DbContext"/> (base type) rather than the concrete
/// <c>TradingDbContext</c> so the unit-test fixture can register a
/// SQLite-compatible helper DbContext. In production DI, the registered
/// <c>TradingDbContext</c> is resolved into the <see cref="DbContext"/>
/// parameter.
/// </para>
///
/// <para>
/// Registered via Scrutor:
/// <c>services.Decorate&lt;IScannerFilterRepository, ScannerFilterAuditDecorator&gt;()</c>
/// in <c>TradingModuleRegistration</c> (after the inner
/// <c>AddScoped&lt;IScannerFilterRepository, ScannerFilterRepository&gt;</c>
/// registration).
/// </para>
/// </summary>
public sealed class ScannerFilterAuditDecorator : IScannerFilterRepository
{
    private readonly IScannerFilterRepository _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IDiff _diff;
    private readonly DbContext? _db;

    public ScannerFilterAuditDecorator(
        IScannerFilterRepository inner,
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

    // ===== Read methods — no audit logging (matches Wave 6 + 7 + 8a.x + 8b.1 + 9a.1 precedent) =====

    public Task<ScannerFilter?> GetByIdAsync(Guid id, CancellationToken ct)
        => _inner.GetByIdAsync(id, ct);

    public Task<ScannerFilter?> GetByUserAndNameAsync(Guid userId, string name, CancellationToken ct)
        => _inner.GetByUserAndNameAsync(userId, name, ct);

    public Task<IReadOnlyList<ScannerFilter>> ListByUserAsync(
        Guid userId, bool activeOnly, CancellationToken ct)
        => _inner.ListByUserAsync(userId, activeOnly, ct);

    // ===== Mutation methods with cross-tenant IsOwner check + audit logging =====

    public async Task AddAsync(ScannerFilter filter, CancellationToken ct)
    {
        if (!IsOwner(filter))
        {
            // Cross-tenant attempt: emit AuditAction.Denied (NOT the
            // would-have-been Created action) so compliance officers can
            // filter cross-tenant attempts separately from legitimate
            // state changes (matches the Wave 7 7b.1 StrategyAuditDecorator
            // + 7a.1 UserAuditDecorator + 8b.1 StripeCustomer precedent —
            // Denied rows always carry the rejection trail, not the
            // would-have-been mutation).
            await LogDeniedAsync(filter, AuditAction.Denied, ct);
            throw new UnauthorizedAccessException(
                $"ScannerFilter {filter.Id} does not belong to current user (cross-tenant attempt).");
        }

        await _inner.AddAsync(filter, ct);
        await TryAuditAsync(BuildEntry(filter, AuditAction.Created, changesJson: null), ct);
    }

    public async Task UpdateAsync(ScannerFilter filter, CancellationToken ct)
    {
        if (!IsOwner(filter))
        {
            // Slice 9a.2 mirrors the 7b.1 StrategyAuditDecorator shape:
            // the audit row's Action is AuditAction.Denied (not the
            // would-have-been Updated action) so compliance officers can
            // filter cross-tenant attempts separately from legitimate
            // state changes.
            await LogDeniedAsync(filter, AuditAction.Denied, ct);
            throw new UnauthorizedAccessException(
                $"ScannerFilter {filter.Id} does not belong to current user (cross-tenant attempt).");
        }

        // Snapshot before — use EF's ChangeTracker.OriginalValues (when
        // the inner repository is EF-backed) to capture the pre-mutation
        // entity. Falls back to a null before-snapshot when no DbContext
        // is available or the entity isn't tracked; the diff helper then
        // emits a full JSON snapshot of the post-update entity.
        var before = ResolveBefore(filter);
        await _inner.UpdateAsync(filter, ct);

        var changesJson = SafeDiff(before, filter);
        await TryAuditAsync(BuildEntry(filter, AuditAction.Updated, changesJson), ct);
    }

    public async Task DeleteAsync(ScannerFilter filter, CancellationToken ct)
    {
        // Slice 9a.2: DeleteAsync is NOT a valid ScannerFilter mutation.
        // The canonical mutation surface is
        // ScannerFilter.Deactivate(IClock) (flips IsActive = false) +
        // UpdateAsync. Emit an AuditAction.Failed row BEFORE re-throwing
        // so the misuse is on the audit trail. The inner
        // IScannerFilterRepository.DeleteAsync is NEVER reached — the
        // contract is "ScannerFilter deletion is not supported — use
        // Deactivate (IsActive = false)". The production
        // Persistence.ScannerFilterRepository.DeleteAsync also throws
        // NotSupportedException as a second line of defense in case a
        // caller bypasses the decorator. Mirrors the Wave 7 7b.1
        // StrategyAuditDecorator + 7a.1 UserAuditDecorator precedent.
        await LogFailedAsync(filter, ct);
        throw new NotSupportedException(
            "ScannerFilter deletion is not supported — use Deactivate (IsActive = false).");
    }

    // ===== Helpers =====

    /// <summary>
    /// Returns true iff the ScannerFilter belongs to the current user.
    /// When no user is resolved (anonymous / service context — e.g.,
    /// background reconciliation flows that resolve the user via
    /// different means), the decorator allows the operation to proceed
    /// — system actors bypass the user-scope check (matches the Wave 6
    /// + 7 + 8a.x + 8b.1 + 9a.1 precedent).
    /// </summary>
    private bool IsOwner(ScannerFilter filter)
        => !_tenant.CurrentUserId.HasValue
            || filter.UserId == _tenant.CurrentUserId.Value;

    /// <summary>
    /// Audit-log a denied cross-tenant attempt. The entry carries the
    /// <b>calling user's id</b> (NOT the entity's user id) so the
    /// security trail shows WHO attempted the cross-tenant access — the
    /// attacker is recorded, not the legitimate owner. Fire-and-forget:
    /// never throws.
    /// </summary>
    private Task LogDeniedAsync(ScannerFilter filter, AuditAction action, CancellationToken ct)
    {
        var entry = new AuditEventEntry(
            EntityType: nameof(ScannerFilter),
            EntityId: filter.Id,
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
    private Task LogFailedAsync(ScannerFilter filter, CancellationToken ct)
    {
        var entry = new AuditEventEntry(
            EntityType: nameof(ScannerFilter),
            EntityId: filter.Id,
            Action: AuditAction.Failed,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: null,
            OccurredAt: _clock.UtcNow);
        return _audit.LogAsync(entry, ct);
    }

    /// <summary>
    /// Defense-in-depth: the <see cref="IAuditLogger.LogAsync"/> contract
    /// is to never throw, but a buggy implementation could. The decorator
    /// swallows any exception here so the main mutation is NEVER rolled
    /// back by a misbehaving audit (matches the Wave 6 + 7 + 8a.x +
    /// 8b.1 + 9a.1 decorator pattern).
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
    /// Builds the <see cref="AuditEventEntry"/> for the given filter +
    /// action. <c>EntityId</c> is the filter's primary key;
    /// <c>EntityType</c> is the filter type's name. <c>TenantId</c> +
    /// <c>UserId</c> come from the <see cref="ITenantContext"/>.
    /// </summary>
    private AuditEventEntry BuildEntry(ScannerFilter filter, AuditAction action, string? changesJson)
        => new(
            EntityType: nameof(ScannerFilter),
            EntityId: filter.Id,
            Action: action,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: changesJson,
            OccurredAt: _clock.UtcNow);

    /// <summary>
    /// Resolves the pre-mutation snapshot. When the decorator is wired
    /// with a <see cref="DbContext"/> (the production path), we use EF's
    /// <see cref="EntityEntry.OriginalValues"/> — the change tracker
    /// holds the pre-modification values even after the caller mutates
    /// the in-memory entity. Without the DbContext (legacy / in-memory
    /// test path), we return null and the diff helper falls back to a
    /// full post-mutation snapshot.
    /// </summary>
    private ScannerFilter? ResolveBefore(ScannerFilter filter)
    {
        if (_db is not null)
        {
            var entry = _db.Entry(filter);
            if (entry.State != EntityState.Detached)
            {
                return entry.OriginalValues.ToObject() as ScannerFilter;
            }
        }
        return null;
    }

    /// <summary>
    /// Computes the per-field {before, after} diff between the
    /// pre-update snapshot and the post-update filter. Falls back to a
    /// JSON snapshot of the post-update entity when the diff helper
    /// throws or returns empty. Mirrors the generic
    /// <see cref="DecoratedRepository{T}.SafeDiff"/> helper but
    /// adapted for the bespoke ScannerFilterAuditDecorator path.
    /// </summary>
    private string? SafeDiff(ScannerFilter? before, ScannerFilter after)
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
            // Fallback: full snapshot of the post-mutation entity. The
            // audit row will lose the before/after shape but will still
            // preserve the change. The decorator MUST NOT crash the
            // caller's mutation because the diff helper failed.
            return JsonSerializer.Serialize(after);
        }
    }
}
