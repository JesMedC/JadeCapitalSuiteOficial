using System.Text.Json;
using JadeCapital.Shared.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Alerts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace JadeCapital.Trading.Infrastructure.Audit;

/// <summary>
/// Per-aggregate audit decorator for <see cref="IAlertRepository"/>
/// (Wave 8, slice 8a.2).
///
/// <para>
/// <b>Why this decorator is bespoke (does NOT use the generic
/// <c>DecoratedRepository&lt;T&gt;</c> helper)</b>: <see cref="IAlertRepository"/>
/// is bespoke (does NOT extend <c>IRepository&lt;Alert&gt;</c>). It has a
/// <c>ListByUserAsync(userId, activeOnly, now, ct)</c> read method that
/// scopes the list to a specific user — extending <c>IRepository&lt;Alert&gt;</c>
/// would force a parameterless list that ignores cross-user scope, a
/// security regression. Following the 7a.1 <c>RiskProfileAuditDecorator</c>
/// + 7b.1 <c>TradeAuditDecorator</c> + 7b.2 <c>JournalEntryAuditDecorator</c>
/// + 8a.1 <c>AccountAuditDecorator</c> precedent for bespoke repositories,
/// this decorator is bespoke too: it forwards methods directly + emits
/// audit rows without the generic helper.
/// </para>
///
/// <para>
/// <b>CRITICAL — slice 8a.2 bespoke deviation (orchestrator preflight
/// decision 6)</b>: <see cref="IAlertRepository.AddAsync(Alert, CancellationToken)"/>
/// returns <c>bool</c>. <c>true</c> = row inserted; <c>false</c> = row
/// rejected by the partial UNIQUE INDEX <c>ux_alerts_user_rule_day</c>
/// (a row for <c>(user_id, rule_id, UTC-date)</c> already exists). The
/// decorator MUST inspect the return value:
/// </para>
/// <list type="bullet">
///   <item><c>true</c> → emit <see cref="AuditAction.Created"/>.</item>
///   <item><c>false</c> → emit NO audit row (the row was not created;
///         the existing row's audit history is preserved — emitting a
///         duplicate Created event for the dedup target would be
///         misleading).</item>
/// </list>
/// <para>
/// This conditional emit is the key behavioral difference from a
/// "naive" audit decorator that blindly wraps <c>AddAsync</c>. Mirrors
/// the dedup semantics of the production <c>AlertRepository</c>
/// (<see cref="Persistence.AlertRepository.AddAsync"/>).
/// </para>
/// <list type="bullet">
///   <item>Forwarding <see cref="IAlertRepository.ListByUserAsync"/> +
///         <see cref="IAlertRepository.GetByIdAsync"/> to the inner
///         without audit logging. Reads are not audited (matches the
///         Wave 6 + 7 + 8a.1 precedent: only mutations get audit rows).</item>
///   <item>Wrapping <see cref="IAlertRepository.AddAsync"/> with the
///         <b>conditional Created audit</b>: only emits when the inner
///         returns <c>true</c>.</item>
///   <item>Wrapping <see cref="IAlertRepository.UpdateAsync"/> with the
///         <b>cross-tenant <c>IsOwner</c> check</b> on
///         <see cref="Alert.UserId"/> + audit logging with the
///         before/after diff. The Acknowledge transition
///         (<c>acknowledgedAt: null → now</c>) shows up in the diff.
///         Cross-tenant rejection emits <see cref="AuditAction.Denied"/>
///         + throws <see cref="UnauthorizedAccessException"/>. The inner
///         is NEVER reached on rejection.</item>
/// </list>
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
/// <c>services.Decorate&lt;IAlertRepository, AlertAuditDecorator&gt;()</c>
/// in <see cref="DependencyInjection.TradingModuleRegistration"/>.
/// </para>
/// </summary>
public sealed class AlertAuditDecorator : IAlertRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IAlertRepository _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IDiff _diff;
    private readonly DbContext? _db;

    public AlertAuditDecorator(
        IAlertRepository inner,
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

    // ===== Read methods — no audit logging (matches Wave 6 + 7 + 8a.1 precedent) =====

    public Task<IReadOnlyList<Alert>> ListByUserAsync(
        Guid userId, bool activeOnly, DateTimeOffset now, CancellationToken ct)
        => _inner.ListByUserAsync(userId, activeOnly, now, ct);

    public Task<Alert?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct)
        => _inner.GetByIdAsync(id, userId, ct);

    // ===== Mutation methods with audit logging =====

    public async Task<bool> AddAsync(Alert alert, CancellationToken ct)
    {
        // CRITICAL — slice 8a.2 deviation: the inner returns true when
        // the row was inserted, false when the dedup UNIQUE INDEX
        // (ux_alerts_user_rule_day) rejected the insert. The decorator
        // MUST check the return value: only emit Created when true.
        // On false, the row was not created — the existing dedup target's
        // audit history is preserved (we do NOT emit a duplicate Created
        // event for the dedup hit).
        var inserted = await _inner.AddAsync(alert, ct);
        if (!inserted)
        {
            // Dedup hit — silently skip the audit row. The inner has
            // already detached the entity to keep the change tracker
            // clean (see AlertRepository.AddAsync).
            return false;
        }
        await TryAuditAsync(BuildEntry(alert, AuditAction.Created, changesJson: null), ct);
        return true;
    }

    public async Task UpdateAsync(Alert alert, CancellationToken ct)
    {
        if (!IsOwner(alert))
        {
            // Slice 8a.2 deviation mirrors the Wave 7 7b.1 TradeAuditDecorator +
            // 8a.1 AccountAuditDecorator: the audit row's Action is
            // AuditAction.Denied (not the would-have-been action) so
            // compliance officers can filter cross-tenant attempts
            // separately from legitimate state changes.
            await LogDeniedAsync(alert, ct);
            throw new UnauthorizedAccessException(
                $"Alert {alert.Id} does not belong to current user (cross-tenant attempt).");
        }

        // Snapshot before — use EF's ChangeTracker.OriginalValues (when the
        // inner repository is EF-backed) to capture the pre-mutation
        // entity. Falls back to a full post-mutation JSON snapshot when
        // no DbContext is available or the entity isn't tracked.
        var before = ResolveBefore(alert);
        await _inner.UpdateAsync(alert, ct);

        var changesJson = SafeDiff(before, alert);
        await TryAuditAsync(BuildEntry(alert, AuditAction.Updated, changesJson), ct);
    }

    // ===== Helpers =====

    /// <summary>
    /// Returns true iff the alert belongs to the current user. When no
    /// user is resolved (anonymous / service context), the decorator
    /// allows the operation to proceed — system actors (background
    /// services like AlertEvaluationBackgroundService) bypass the
    /// user-scope check (matches the Wave 6 + 7 + 8a.1 precedent).
    /// </summary>
    private bool IsOwner(Alert alert)
        => !_tenant.CurrentUserId.HasValue
            || alert.UserId == _tenant.CurrentUserId.Value;

    /// <summary>
    /// Audit-log a denied cross-tenant attempt. The entry carries the
    /// calling user's id (NOT the entity's user id) so the security trail
    /// shows WHO attempted the cross-tenant access. Fire-and-forget:
    /// never throws.
    /// </summary>
    private Task LogDeniedAsync(Alert alert, CancellationToken ct)
    {
        var entry_row = new AuditEventEntry(
            EntityType: nameof(Alert),
            EntityId: alert.Id,
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
    /// Builds the <see cref="AuditEventEntry"/> for the given alert +
    /// action. <c>EntityId</c> is the alert's primary key;
    /// <c>EntityType</c> is the alert type's name. <c>TenantId</c> +
    /// <c>UserId</c> come from the <see cref="ITenantContext"/>.
    /// </summary>
    private AuditEventEntry BuildEntry(Alert alert, AuditAction action, string? changesJson)
        => new(
            EntityType: nameof(Alert),
            EntityId: alert.Id,
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
    private Alert? ResolveBefore(Alert alert)
    {
        if (_db is not null)
        {
            var tracked = _db.Entry(alert);
            if (tracked.State != EntityState.Detached)
            {
                return tracked.OriginalValues.ToObject() as Alert;
            }
        }
        return null;
    }

    /// <summary>
    /// Computes the per-field {before, after} diff between the pre-update
    /// snapshot and the post-update alert. Falls back to a JSON snapshot
    /// of the post-update entity when the diff helper throws or returns
    /// empty. Mirrors the 7b.1 <c>TradeAuditDecorator.SafeDiff</c> +
    /// 7b.2 <c>JournalEntryAuditDecorator.SafeDiff</c> pattern.
    /// </summary>
    private string? SafeDiff(Alert? before, Alert after)
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
}
