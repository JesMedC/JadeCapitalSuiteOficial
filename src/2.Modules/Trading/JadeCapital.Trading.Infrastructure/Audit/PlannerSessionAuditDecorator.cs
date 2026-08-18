using System.Text.Json;
using JadeCapital.Shared.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Contracts.Planner;
using JadeCapital.Trading.Domain.Planner;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace JadeCapital.Trading.Infrastructure.Audit;

/// <summary>
/// Per-aggregate audit decorator for <see cref="IPlannerSessionRepository"/>
/// (Wave 8, slice 8a.3).
///
/// <para>
/// Mirrors the Wave 7 <see cref="TradeAuditDecorator"/> shape (bespoke) but
/// does NOT use the generic <c>DecoratedRepository&lt;T&gt;</c> helper.
/// <see cref="IPlannerSessionRepository"/> is bespoke with cross-user-scoped
/// read methods (<c>ListByUserAndWeekAsync</c>, <c>ExistsForDateAsync</c>,
/// <c>GetWeekComparisonAsync</c>) that don't fit the canonical
/// <c>IRepository&lt;T&gt;.GetByIdAsync(Guid, ct)</c> shape. Following the
/// 7a.1 <c>RiskProfileAuditDecorator</c> + 7b.1 <c>TradeAuditDecorator</c>
/// + 7b.2 <c>JournalEntryAuditDecorator</c> + 8a.1 <c>AccountAuditDecorator</c>
/// + 8a.2 <c>AlertAuditDecorator</c> + 8a.2 <c>TradeReviewAuditDecorator</c>
/// precedent for bespoke repositories, this decorator is bespoke too.
/// </para>
///
/// <para>
/// <b>CRITICAL — slice 8a.3 IsTerminated deviation (orchestrator preflight
/// decision 8)</b>: <see cref="IPlannerSessionRepository.UpdateAsync(PlannerSession, CancellationToken)"/>
/// emits <see cref="AuditAction.Updated"/> by default but is upgraded to
/// <see cref="AuditAction.Deleted"/> when the entity's
/// <see cref="PlannerSession.Status"/> equals <see cref="PlannerStatus.Cancelled"/>
/// — the only lifecycle-terminated value in <see cref="PlannerStatus"/>
/// (no <c>Terminated</c> / <c>Expired</c> in this enum).
/// </para>
/// <para>
/// The decorator re-implements the <c>IsTerminated</c> reflection check
/// locally (private <see cref="IsTerminated"/> method below) — matches the
/// Wave 7 7b.1 <see cref="TradeAuditDecorator"/> precedent of
/// bespoke-shape decorators re-implementing the rule for consistency.
/// The generic <c>DecoratedRepository&lt;T&gt;.IsTerminated</c> in
/// <c>Shared.Infrastructure</c> already covers the same case via reflection
/// (Status enum name == "Cancelled"), but the bespoke decorator implements
/// it directly for code clarity — the rule is local + explicit, not hidden
/// behind a generic helper.
/// </para>
///
/// <list type="bullet">
///   <item>Forwarding <see cref="IPlannerSessionRepository.GetByIdAsync"/> +
///         <see cref="IPlannerSessionRepository.ListByUserAndWeekAsync"/> +
///         <see cref="IPlannerSessionRepository.ExistsForDateAsync"/> +
///         <see cref="IPlannerSessionRepository.GetWeekComparisonAsync"/> to
///         the inner without audit logging. Reads are not audited.</item>
///   <item>Wrapping <see cref="IPlannerSessionRepository.AddAsync(PlannerSession, CancellationToken)"/>
///         with audit logging — emits <see cref="AuditAction.Created"/>.</item>
///   <item>Wrapping <see cref="IPlannerSessionRepository.UpdateAsync(PlannerSession, CancellationToken)"/>
///         with the <b>cross-tenant <c>IsOwner</c> check</b> +
///         <b>IsTerminated upgrade</b>: cross-tenant rejection emits
///         <see cref="AuditAction.Denied"/> + throws
///         <see cref="UnauthorizedAccessException"/>. When the session's
///         <see cref="PlannerSession.Status"/> == <see cref="PlannerStatus.Cancelled"/>,
///         the action is upgraded from <see cref="AuditAction.Updated"/> to
///         <see cref="AuditAction.Deleted"/>. The inner is NEVER reached on
///         cross-tenant rejection.</item>
/// </list>
///
/// <para>
/// <b>DbContext parameter type</b>: the decorator accepts
/// <see cref="DbContext"/> (base type) rather than the concrete
/// <see cref="Persistence.TradingDbContext"/> so the unit-test fixture
/// can register a SQLite-compatible helper DbContext. In production DI,
/// the registered <c>TradingDbContext</c> is resolved into the
/// <see cref="DbContext"/> parameter.
/// </para>
///
/// <para>
/// Registered via Scrutor:
/// <c>services.Decorate&lt;IPlannerSessionRepository, PlannerSessionAuditDecorator&gt;()</c>
/// in <see cref="DependencyInjection.TradingModuleRegistration"/>.
/// </para>
/// </summary>
public sealed class PlannerSessionAuditDecorator : IPlannerSessionRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IPlannerSessionRepository _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IDiff _diff;
    private readonly DbContext? _db;

    public PlannerSessionAuditDecorator(
        IPlannerSessionRepository inner,
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

    // ===== Read methods — no audit logging (matches Wave 6 + 7 + 8a.1 + 8a.2 precedent) =====

    public Task<PlannerSession?> GetByIdAsync(Guid sessionId, CancellationToken ct)
        => _inner.GetByIdAsync(sessionId, ct);

    public Task<IReadOnlyList<PlannerSession>> ListByUserAndWeekAsync(
        Guid userId, LocalDate weekStart, LocalDate weekEnd, CancellationToken ct)
        => _inner.ListByUserAndWeekAsync(userId, weekStart, weekEnd, ct);

    public Task<bool> ExistsForDateAsync(
        Guid userId, LocalDate sessionDate, CancellationToken ct)
        => _inner.ExistsForDateAsync(userId, sessionDate, ct);

    public Task<PlannerWeekComparisonDto> GetWeekComparisonAsync(
        Guid userId, LocalDate weekStart, LocalDate weekEnd, CancellationToken ct)
        => _inner.GetWeekComparisonAsync(userId, weekStart, weekEnd, ct);

    // ===== Mutation methods with audit logging =====

    public async Task AddAsync(PlannerSession session, CancellationToken ct)
    {
        await _inner.AddAsync(session, ct);
        await TryAuditAsync(BuildEntry(session, AuditAction.Created, changesJson: null), ct);
    }

    public async Task UpdateAsync(PlannerSession session, CancellationToken ct)
    {
        if (!IsOwner(session))
        {
            // Slice 8a.3 deviation mirrors the Wave 7 7b.1 TradeAuditDecorator +
            // 7b.2 JournalEntryAuditDecorator + 8a.1 AccountAuditDecorator +
            // 8a.2 AlertAuditDecorator + 8a.2 TradeReviewAuditDecorator: the
            // audit row's Action is AuditAction.Denied (NOT the would-have-been
            // action) so compliance officers can filter cross-tenant attempts
            // separately from legitimate state changes.
            await LogDeniedAsync(session, ct);
            throw new UnauthorizedAccessException(
                $"PlannerSession {session.Id} does not belong to current user (cross-tenant attempt).");
        }

        // Snapshot before — use EF's ChangeTracker.OriginalValues (when the
        // inner repository is EF-backed) to capture the pre-mutation
        // entity. Falls back to a JSON snapshot of the post-update entity
        // when no DbContext is available or the entity isn't tracked.
        var before = ResolveBefore(session);
        await _inner.UpdateAsync(session, ct);

        // Slice 8a.3 bespoke deviation: when the session's Status is the
        // lifecycle-terminated value (Cancelled), upgrade the action from
        // Updated → Deleted so the audit log reflects the cancellation as
        // a soft-delete equivalent (no separate hard-delete surface in
        // this aggregate; the planner sessions table doesn't expose a
        // hard-delete API).
        var action = IsTerminated(session) ? AuditAction.Deleted : AuditAction.Updated;
        var changesJson = SafeDiff(before, session);
        await TryAuditAsync(BuildEntry(session, action, changesJson), ct);
    }

    // ===== Helpers =====

    /// <summary>
    /// Returns true iff the session belongs to the current user. When no
    /// user is resolved (anonymous / service context), the decorator allows
    /// the operation to proceed — system actors bypass the user-scope check
    /// (matches the Wave 6 + 7 + 8a.1 + 8a.2 precedent).
    /// </summary>
    private bool IsOwner(PlannerSession session)
        => !_tenant.CurrentUserId.HasValue
            || session.UserId == _tenant.CurrentUserId.Value;

    /// <summary>
    /// Termination detection — mirrors the
    /// <see cref="JadeCapital.Shared.Infrastructure.Persistence.DecoratedRepository{T}.IsTerminated"/>
    /// reflection check but adapted for <see cref="PlannerStatus"/>. This
    /// aggregate's lifecycle-terminated enum value is <c>Cancelled</c> only
    /// (no <c>Terminated</c> / <c>Expired</c> in <see cref="PlannerStatus"/>).
    /// Re-implemented locally for consistency with the Wave 7 7b.1
    /// <see cref="TradeAuditDecorator"/> precedent of bespoke decorators
    /// keeping the rule local + explicit.
    /// </summary>
    private static bool IsTerminated(PlannerSession session)
        => session.Status == PlannerStatus.Cancelled;

    /// <summary>
    /// Audit-log a denied cross-tenant attempt. The entry carries the
    /// calling user's id (NOT the entity's user id) so the security trail
    /// shows WHO attempted the cross-tenant access. Fire-and-forget:
    /// never throws.
    /// </summary>
    private Task LogDeniedAsync(PlannerSession session, CancellationToken ct)
    {
        var entry = new AuditEventEntry(
            EntityType: nameof(PlannerSession),
            EntityId: session.Id,
            Action: AuditAction.Denied,
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
    /// Builds the <see cref="AuditEventEntry"/> for the given session +
    /// action. <c>EntityId</c> is the session's primary key;
    /// <c>EntityType</c> is the session type's name. <c>TenantId</c> +
    /// <c>UserId</c> come from the <see cref="ITenantContext"/>.
    /// </summary>
    private AuditEventEntry BuildEntry(PlannerSession session, AuditAction action, string? changesJson)
        => new(
            EntityType: nameof(PlannerSession),
            EntityId: session.Id,
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
    private PlannerSession? ResolveBefore(PlannerSession session)
    {
        if (_db is not null)
        {
            var entry = _db.Entry(session);
            if (entry.State != EntityState.Detached)
            {
                return entry.OriginalValues.ToObject() as PlannerSession;
            }
        }
        return null;
    }

    /// <summary>
    /// Computes the per-field {before, after} diff between the pre-update
    /// snapshot and the post-update session. Falls back to a JSON snapshot
    /// of the post-update entity when the diff helper throws or returns
    /// empty. Mirrors the 7b.1 <c>TradeAuditDecorator.SafeDiff</c> +
    /// 7b.2 <c>JournalEntryAuditDecorator.SafeDiff</c> + 8a.1
    /// <c>AccountAuditDecorator</c> + 8a.2 <c>AlertAuditDecorator</c> +
    /// 8a.2 <c>TradeReviewAuditDecorator</c> pattern.
    /// </summary>
    private string? SafeDiff(PlannerSession? before, PlannerSession after)
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