using JadeCapital.Billing.Application.Stripe;
using JadeCapital.Billing.Domain.Stripe;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Billing.Infrastructure.Audit;

/// <summary>
/// Per-aggregate audit decorator for <see cref="IStripeCustomerRepository"/>
/// (Wave 8, slice 8b.1).
///
/// <para>
/// <b>Why this decorator is bespoke (does NOT use the generic
/// <c>DecoratedRepository&lt;T&gt;</c> helper)</b>: <see cref="IStripeCustomerRepository"/>
/// exposes an immutable-aggregate shape — only <c>AddAsync</c> + 2 reads
/// (<c>GetByUserIdAsync</c> + <c>GetByStripeCustomerIdAsync</c>). There is
/// no <c>UpdateAsync</c> or <c>DeleteAsync</c> on the interface (the entity
/// docstring asserts "Aggregate is immutable after Create — no mutators").
/// Extending <c>IRepository&lt;T&gt;</c> would force <c>UpdateAsync</c> +
/// <c>DeleteAsync</c> methods that the contract forbids — breaking the
/// write-once promise compliance officers rely on for
/// <c>entity_type = "StripeCustomer"</c> rows in <c>audit.events</c>.
/// Bespoke decorator preserves the immutable invariant + wraps only
/// <c>AddAsync</c>.
/// </para>
///
/// <para>
/// <b>Why IsOwner cross-tenant check on AddAsync</b>: even though
/// <c>AddAsync</c> inserts a NEW row (no read-then-update race), the row's
/// <see cref="StripeCustomer.UserId"/> still comes from the caller. A
/// cross-tenant handler could submit a StripeCustomer carrying another
/// tenant's user id. Per spec §8b.1 (the spec's Requirement "Audit
/// decorator for StripeCustomer aggregate"): "Cross-tenant
/// <c>IsOwner</c> check MUST compare <c>customer.UserId</c> to
/// <c>ITenantContext.CurrentUserId</c>; on mismatch, emit
/// <c>AuditAction.Denied</c> and throw <c>UnauthorizedAccessException</c>."
/// This is the only enforcement point — unlike the 8a.3
/// <c>PreTradeChecklistAuditDecorator</c> precedent (which omits the
/// check because the foreign-key consistency is guaranteed by the
/// production OpenTradeHandler), <see cref="CreateOrGetCustomerHandler"/>
/// accepts the userId as a command parameter and is invoked from multiple
/// entry points (auth portal, Stripe webhook reconciliation, etc.) where
/// handler-side consistency is not guaranteed.
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
///   <item>Wrapping <see cref="IStripeCustomerRepository.AddAsync"/> with
///         the <b>cross-tenant <c>IsOwner</c> check</b> +
///         <see cref="AuditAction.Created"/> audit logging. Created
///         events carry no diff (<c>ChangesJson = null</c>) — the
///         StripeCustomer is the audit-relevant entity itself; the row
///         IS the create record.</item>
///   <item>Forwarding <see cref="IStripeCustomerRepository.GetByUserIdAsync"/>
///         + <see cref="IStripeCustomerRepository.GetByStripeCustomerIdAsync"/>
///         to the inner without audit logging. Reads are not audited
///         (matches the Wave 6 + 7 + 8a.1 + 8a.2 + 8a.3 precedent: only
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
/// 8a.3 <c>PreTradeChecklistAuditDecorator</c> minimal shape.
/// </para>
///
/// <para>
/// Registered via Scrutor:
/// <c>services.Decorate&lt;IStripeCustomerRepository, StripeCustomerAuditDecorator&gt;()</c>
/// in <see cref="DependencyInjection.BillingModuleRegistration"/>.
/// </para>
/// </summary>
public sealed class StripeCustomerAuditDecorator : IStripeCustomerRepository
{
    private readonly IStripeCustomerRepository _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public StripeCustomerAuditDecorator(
        IStripeCustomerRepository inner,
        IAuditLogger audit,
        ITenantContext tenant,
        IClock clock)
    {
        _inner = inner;
        _audit = audit;
        _tenant = tenant;
        _clock = clock;
    }

    // ===== Read methods — no audit logging (matches Wave 6 + 7 + 8a.1 + 8a.2 + 8a.3 precedent) =====

    public Task<StripeCustomer?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => _inner.GetByUserIdAsync(userId, ct);

    public Task<StripeCustomer?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default)
        => _inner.GetByStripeCustomerIdAsync(stripeCustomerId, ct);

    // ===== Mutation method with cross-tenant IsOwner check + audit logging =====

    public async Task AddAsync(StripeCustomer customer, CancellationToken ct = default)
    {
        if (!IsOwner(customer))
        {
            // Cross-tenant attempt: emit AuditAction.Denied (NOT the
            // would-have-been Created action) so compliance officers can
            // filter cross-tenant attempts separately from legitimate
            // state changes (matches the Wave 6 + 7 + 8a.1 + 8a.2 + 8a.3
            // precedent — Denied rows always carry the rejection trail,
            // not the would-have-been mutation).
            await LogDeniedAsync(customer, ct);
            throw new UnauthorizedAccessException(
                $"StripeCustomer {customer.Id} does not belong to current user (cross-tenant attempt).");
        }

        await _inner.AddAsync(customer, ct);
        await TryAuditAsync(BuildEntry(customer), ct);
    }

    // ===== Helpers =====

    /// <summary>
    /// Returns true iff the StripeCustomer belongs to the current user. When
    /// no user is resolved (anonymous / service context — e.g., webhooks or
    /// background reconciliation services that resolve the user via
    /// different means), the decorator allows the operation to proceed —
    /// system actors bypass the user-scope check (matches the Wave 6 + 7 +
    /// 8a.1 + 8a.2 + 8a.3 precedent for non-Stripe services).
    /// </summary>
    private bool IsOwner(StripeCustomer customer)
        => !_tenant.CurrentUserId.HasValue
            || customer.UserId == _tenant.CurrentUserId.Value;

    /// <summary>
    /// Audit-log a denied cross-tenant attempt. The entry carries the
    /// <b>calling user's id</b> (NOT the entity's user id) so the security
    /// trail shows <i>WHO attempted</i> the cross-tenant access — the
    /// attacker is recorded, not the legitimate owner. Fire-and-forget:
    /// never throws (swallowed by <see cref="TryAuditAsync"/>).
    /// </summary>
    private Task LogDeniedAsync(StripeCustomer customer, CancellationToken ct)
    {
        var entry = new AuditEventEntry(
            EntityType: nameof(StripeCustomer),
            EntityId: customer.Id,
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
    /// back by a misbehaving audit (matches the Wave 6 + 7 + 8a.1 + 8a.2 +
    /// 8a.3 decorator pattern).
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
    /// Builds the <see cref="AuditEventEntry"/> for the created customer.
    /// <c>EntityId</c> is the customer's primary key; <c>EntityType</c> is
    /// the customer type's name (so compliance officers query by
    /// <c>entity_type = "StripeCustomer"</c> + <c>action = "Created"</c>).
    /// <c>TenantId</c> + <c>UserId</c> come from <see cref="ITenantContext"/>.
    /// Created events carry no diff (<c>ChangesJson = null</c>) — the entire
    /// customer record is captured by the <c>audit.events</c> row's
    /// <c>entity_id</c> + the persisted <c>billing.stripe_customers</c>
    /// row.
    /// </summary>
    private AuditEventEntry BuildEntry(StripeCustomer customer)
        => new(
            EntityType: nameof(StripeCustomer),
            EntityId: customer.Id,
            Action: AuditAction.Created,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: null,
            OccurredAt: _clock.UtcNow);
}
