using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.PreTradeChecklists;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Audit;

/// <summary>
/// Per-aggregate audit decorator for <see cref="IPreTradeChecklistRepository"/>
/// (Wave 8, slice 8a.3).
///
/// <para>
/// Mirrors a simplified <c>TenantAuditDecorator</c> shape (Wave 6 6d.2) —
/// bespoke, smallest decorator in the wave. The
/// <see cref="IPreTradeChecklistRepository"/> interface has only
/// <c>AddAsync</c> + <c>ListByUserIdAsync</c>; the checklist is write-once
/// per the entity docstring ("UNA fila por trade — enforced por UNIQUE INDEX
/// sobre trade_id en la DB. La API no expone UPDATE del checklist").
/// </para>
///
/// <para>
/// <b>Why this decorator is bespoke (does NOT use the generic
/// <c>DecoratedRepository&lt;T&gt;</c> helper)</b>: the write-once interface
/// is even smaller than the canonical <c>IRepository&lt;T&gt;</c> CRUD
/// surface — it has only 2 methods (1 mutation + 1 read), no <c>GetByIdAsync</c>
/// / <c>UpdateAsync</c> / <c>DeleteAsync</c>. Extending <c>IRepository&lt;T&gt;</c>
/// would silently add mutation methods that the entity docstring forbids.
/// Bespoke decorator preserves the write-once invariant + wraps only
/// <c>AddAsync</c>.
/// </para>
///
/// <para>
/// <b>IsOwner / cross-tenant check on AddAsync</b> (per spec requirement):
/// compares <c>checklist.UserId</c> to <see cref="ITenantContext.CurrentUserId"/>.
/// On mismatch, emits <see cref="AuditAction.Denied"/> with reason
/// "cross-tenant mutation attempt" and throws <see cref="UnauthorizedAccessException"/>.
/// The check is defense-in-depth — production handlers always pass
/// <c>UserId</c> from the authenticated context, but the decorator
/// enforces the invariant at the audit layer for compliance.
/// </para>
///
/// <list type="bullet">
///   <item>Wrapping <see cref="IPreTradeChecklistRepository.AddAsync"/> with
///         audit logging — emits <see cref="AuditAction.Created"/>.</item>
///   <item>Forwarding <see cref="IPreTradeChecklistRepository.ListByUserIdAsync"/>
///         to the inner without audit logging. Reads are not audited.</item>
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
/// <c>services.Decorate&lt;IPreTradeChecklistRepository, PreTradeChecklistAuditDecorator&gt;()</c>
/// in <see cref="DependencyInjection.TradingModuleRegistration"/>.
/// </para>
/// </summary>
public sealed class PreTradeChecklistAuditDecorator : IPreTradeChecklistRepository
{
    private readonly IPreTradeChecklistRepository _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public PreTradeChecklistAuditDecorator(
        IPreTradeChecklistRepository inner,
        IAuditLogger audit,
        ITenantContext tenant,
        IClock clock,
        DbContext? db = null)
    {
        _inner = inner;
        _audit = audit;
        _tenant = tenant;
        _clock = clock;
        // DbContext parameter is accepted (not used) to mirror the
        // slice 8a.1 + 8a.2 decorator signatures — keeps the DI
        // registration shape consistent. The write-once interface has
        // no UpdateAsync so ChangeTracker.OriginalValues is never needed.
        _ = db;
    }

    // ===== Read method — no audit logging (matches Wave 6 + 7 + 8a.1 + 8a.2 + 8a.3 PlannerSession precedent) =====

    public Task<IReadOnlyList<PreTradeChecklist>> ListByUserIdAsync(
        Guid userId, CancellationToken ct)
        => _inner.ListByUserIdAsync(userId, ct);

    // ===== Mutation method with audit logging =====

    public async Task AddAsync(PreTradeChecklist checklist, CancellationToken ct)
    {
        // IsOwner / cross-tenant check (spec requirement): the audit decorator
        // is the last line of defense for the write-once invariant. If the
        // checklist's UserId doesn't match the caller's tenant context, emit
        // AuditAction.Denied + throw UnauthorizedAccessException. The actual
        // insert never happens — the inner.AddAsync is gated behind the check.
        var currentUserId = _tenant.CurrentUserId;
        if (currentUserId.HasValue && checklist.UserId != currentUserId.Value)
        {
            await TryAuditAsync(BuildDeniedEntry(checklist, currentUserId.Value), ct);
            throw new UnauthorizedAccessException(
                $"Cross-tenant PreTradeChecklist write attempt: caller={currentUserId.Value}, checklist.UserId={checklist.UserId}.");
        }

        await _inner.AddAsync(checklist, ct);
        await TryAuditAsync(BuildEntry(checklist), ct);
    }

    // ===== Helpers =====

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
    /// Builds the <see cref="AuditEventEntry"/> for the created checklist.
    /// <c>EntityId</c> is the checklist's primary key;
    /// <c>EntityType</c> is the checklist type's name. <c>TenantId</c> +
    /// <c>UserId</c> come from the <see cref="ITenantContext"/>. Created
    /// events carry no diff (<c>ChangesJson = null</c>) — the entire
    /// pre-trade submission snapshot is captured by the
    /// <c>PreTradeChecklistSubmittedDomainEvent</c> for projection consumers.
    /// </summary>
    private AuditEventEntry BuildEntry(PreTradeChecklist checklist)
        => new(
            EntityType: nameof(PreTradeChecklist),
            EntityId: checklist.Id,
            Action: AuditAction.Created,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: null,
            OccurredAt: _clock.UtcNow);

    /// <summary>
    /// Builds the <see cref="AuditEventEntry"/> for a cross-tenant denial.
    /// Records <c>AuditAction.Denied</c> with <c>ChangesJson</c> capturing the
    /// attempted-vs-actual user mismatch for the compliance trail.
    /// </summary>
    private AuditEventEntry BuildDeniedEntry(PreTradeChecklist checklist, Guid callerUserId)
        => new(
            EntityType: nameof(PreTradeChecklist),
            EntityId: checklist.Id,
            Action: AuditAction.Denied,
            TenantId: _tenant.Current?.Value,
            UserId: callerUserId,
            ChangesJson: "{\"reason\":{\"before\":null,\"after\":\"cross-tenant mutation attempt\"}}",
            OccurredAt: _clock.UtcNow);
}