using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Ai;

namespace JadeCapital.Trading.Infrastructure.Audit;

/// <summary>
/// Per-aggregate audit decorator for <see cref="IAIRiskAdviceRepository"/>
/// (Wave 9, slice 9a.1 — sub-scope A coverage extension).
///
/// <para>
/// <b>Why this decorator is bespoke (does NOT use the generic
/// <c>DecoratedRepository&lt;T&gt;</c> helper)</b>: <see cref="IAIRiskAdviceRepository"/>
/// exposes a write-once shape — only <c>AddAsync</c> +
/// <c>FindByUserAndTradeAsync</c>. The <see cref="AIRiskAdvice"/>
/// aggregate is immutable after <see cref="AIRiskAdvice.Create"/> per the
/// entity docstring ("Immutability: the aggregate has no public setters.
/// EF rehydration uses the Rehydrate factory which is reserved for the
/// repository and skips the validation guards"). Extending
/// <c>IRepository&lt;T&gt;</c> would force <c>UpdateAsync</c> + <c>DeleteAsync</c>
/// methods that the contract forbids — breaking the write-once promise
/// compliance officers rely on for <c>entity_type = "AIRiskAdvice"</c>
/// rows in <c>audit.events</c>. Bespoke decorator preserves the immutable
/// invariant + wraps only <c>AddAsync</c>.
/// </para>
///
/// <para>
/// <b>Why IsOwner cross-tenant check on AddAsync</b>: even though
/// <c>AddAsync</c> inserts a NEW row (no read-then-update race), the
/// row's <see cref="AIRiskAdvice.UserId"/> still comes from the caller.
/// Both <c>OllamaAIRiskAdvisor</c> (invoked from <c>OpenTradeHandler</c>)
/// and <c>GetPreTradeAdviceHandler</c> accept the userId as a command
/// parameter — a cross-tenant invocation could submit an advice carrying
/// another tenant's user id. Per spec §9a.1 (the spec's Requirement
/// "Audit decorator for AIRiskAdvice aggregate"): "Cross-tenant
/// <c>IsOwner</c> check MUST compare <c>advice.UserId</c> to
/// <c>ITenantContext.CurrentUserId</c>; on mismatch, emit
/// <c>AuditAction.Denied</c> and throw <c>UnauthorizedAccessException</c>."
/// This is the only enforcement point — unlike the
/// <see cref="PreTradeChecklistAuditDecorator"/> precedent (which omits
/// the check because the foreign-key consistency is guaranteed by the
/// production OpenTradeHandler), <c>OllamaAIRiskAdvisor</c> +
/// <c>GetPreTradeAdviceHandler</c> accept userId as a command parameter
/// and are invoked from multiple entry points where handler-side
/// consistency is not guaranteed.
/// </para>
///
/// <para>
/// The decorator's <c>AddAsync</c> order is:
/// <list type="number">
///   <item><see cref="IsOwner"/> pre-check against
///         <see cref="ITenantContext.CurrentUserId"/>.</item>
///   <item>On mismatch → <see cref="LogDeniedAsync"/> +
///         <see cref="UnauthorizedAccessException"/>. The inner is
///         NEVER reached.</item>
///   <item>On match → call inner <c>AddAsync</c> →
///         <see cref="AuditAction.Created"/> audit row + the inner
///         entity is persisted.</item>
/// </list>
/// </para>
///
/// <list type="bullet">
///   <item>Wrapping <see cref="IAIRiskAdviceRepository.AddAsync"/> with
///         the <b>cross-tenant <c>IsOwner</c> check</b> +
///         <see cref="AuditAction.Created"/> audit logging. Created
///         events carry no diff (<c>ChangesJson = null</c>) — the
///         AIRiskAdvice is the audit-relevant entity itself; the row
///         IS the create record.</item>
///   <item>Forwarding <see cref="IAIRiskAdviceRepository.FindByUserAndTradeAsync"/>
///         to the inner without audit logging. Reads are not audited
///         (matches the Wave 6 + 7 + 8a.x + 8b.1 + 9a.1 precedent: only
///         mutations get audit rows).</item>
///   <item>No <c>UpdateAsync</c> or <c>DeleteAsync</c> to wrap — the
///         interface has neither (write-once contract pin enforced at the
///         interface level via the integration test).</item>
/// </list>
///
/// <para>
/// No <see cref="Microsoft.EntityFrameworkCore.DbContext"/> parameter
/// is required: the interface is write-once (no <c>UpdateAsync</c>), so
/// EF <c>ChangeTracker.OriginalValues</c> for the pre-mutation diff is
/// never needed. This keeps the decorator's signature minimal (4
/// dependencies: inner + audit + tenant + clock), matching the
/// Wave 8 8b.1 <see cref="JadeCapital.Billing.Infrastructure.Audit.StripeCustomerAuditDecorator"/>
/// minimal shape.
/// </para>
///
/// <para>
/// Registered via Scrutor:
/// <c>services.Decorate&lt;IAIRiskAdviceRepository, AIRiskAdviceAuditDecorator&gt;()</c>
/// in <see cref="DependencyInjection.TradingModuleRegistration"/>.
/// </para>
/// </summary>
public sealed class AIRiskAdviceAuditDecorator : IAIRiskAdviceRepository
{
    private readonly IAIRiskAdviceRepository _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public AIRiskAdviceAuditDecorator(
        IAIRiskAdviceRepository inner,
        IAuditLogger audit,
        ITenantContext tenant,
        IClock clock)
    {
        _inner = inner;
        _audit = audit;
        _tenant = tenant;
        _clock = clock;
    }

    // ===== Read method — no audit logging (matches Wave 6 + 7 + 8a.x + 8b.1 + 9a.1 precedent) =====

    public Task<AIRiskAdvice?> FindByUserAndTradeAsync(
        Guid userId,
        Guid tradeId,
        CancellationToken ct)
        => _inner.FindByUserAndTradeAsync(userId, tradeId, ct);

    // ===== Mutation method with cross-tenant IsOwner check + audit logging =====

    public async Task AddAsync(AIRiskAdvice advice, CancellationToken ct)
    {
        if (!IsOwner(advice))
        {
            // Cross-tenant attempt: emit AuditAction.Denied (NOT the
            // would-have-been Created action) so compliance officers can
            // filter cross-tenant attempts separately from legitimate
            // state changes (matches the Wave 8 8b.1 StripeCustomer
            // precedent — Denied rows always carry the rejection trail,
            // not the would-have-been mutation).
            await LogDeniedAsync(advice, ct);
            throw new UnauthorizedAccessException(
                $"AIRiskAdvice {advice.Id} does not belong to current user (cross-tenant attempt).");
        }

        await _inner.AddAsync(advice, ct);
        await TryAuditAsync(BuildEntry(advice), ct);
    }

    // ===== Helpers =====

    /// <summary>
    /// Returns true iff the AIRiskAdvice belongs to the current user. When
    /// no user is resolved (anonymous / service context — e.g., background
    /// AI reconciliation flows that resolve the user via different means),
    /// the decorator allows the operation to proceed — system actors
    /// bypass the user-scope check (matches the Wave 8 8b.1 StripeCustomer
    /// precedent).
    /// </summary>
    private bool IsOwner(AIRiskAdvice advice)
        => !_tenant.CurrentUserId.HasValue
            || advice.UserId == _tenant.CurrentUserId.Value;

    /// <summary>
    /// Audit-log a denied cross-tenant attempt. The entry carries the
    /// <b>calling user's id</b> (NOT the entity's user id) so the security
    /// trail shows <i>WHO attempted</i> the cross-tenant access — the
    /// attacker is recorded, not the legitimate owner. Fire-and-forget:
    /// never throws (swallowed by <see cref="TryAuditAsync"/>).
    /// </summary>
    private Task LogDeniedAsync(AIRiskAdvice advice, CancellationToken ct)
    {
        var entry = new AuditEventEntry(
            EntityType: nameof(AIRiskAdvice),
            EntityId: advice.Id,
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
    /// Builds the <see cref="AuditEventEntry"/> for the created advisory.
    /// <c>EntityId</c> is the advisory's primary key; <c>EntityType</c> is
    /// the advisory type's name (so compliance officers query by
    /// <c>entity_type = "AIRiskAdvice"</c> + <c>action = "Created"</c>).
    /// <c>TenantId</c> + <c>UserId</c> come from <see cref="ITenantContext"/>.
    /// Created events carry no diff (<c>ChangesJson = null</c>) — the entire
    /// advisory record is captured by the <c>audit.events</c> row's
    /// <c>entity_id</c> + the persisted <c>trading.ai_risk_advice</c>
    /// row.
    /// </summary>
    private AuditEventEntry BuildEntry(AIRiskAdvice advice)
        => new(
            EntityType: nameof(AIRiskAdvice),
            EntityId: advice.Id,
            Action: AuditAction.Created,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: null,
            OccurredAt: _clock.UtcNow);
}
