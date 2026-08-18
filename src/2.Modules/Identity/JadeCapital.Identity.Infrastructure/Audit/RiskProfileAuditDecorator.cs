using System.Text.Json;
using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.RiskProfile;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Identity.Infrastructure.Audit;

/// <summary>
/// Per-aggregate audit decorator for <see cref="IRiskProfileRepository"/>
/// (Wave 7, slice 7a.1).
///
/// <para>
/// Co-located with the <see cref="RiskProfile"/> aggregate in
/// <c>Identity.Infrastructure</c>. The decorator is <b>BESPOKE</b> — it
/// does NOT use the generic <c>DecoratedRepository&lt;T&gt;</c> helper,
/// because RiskProfile has no <c>UpdateAsync</c> in its canonical
/// mutation surface: the supersede IS the termination.
/// </para>
///
/// <para>
/// The decorator wraps each <see cref="IRiskProfileRepository"/> method
/// directly:
/// </para>
/// <list type="bullet">
///   <item><see cref="AddAsync"/>: forward to the inner, then emit
///         <see cref="AuditAction.Created"/> with no diff (Created events
///         carry no before-state).</item>
///   <item><see cref="MarkSupersededAsync"/>: cross-tenant <c>IsOwner</c>
///         check on <c>profile.UserId</c> first (loads the profile via
///         <see cref="IRiskProfileRepository.GetByIdAsync"/>), then call
///         the inner supersede, then emit <see cref="AuditAction.Deleted"/>
///         with a supersession diff payload. The diff uses the actual
///         aggregate fields (<c>IsActive</c> true→false + <c>SupersededAt</c>
///         null→now) — NOT the design.md sketch's <c>SupersededBy</c> key,
///         because the <see cref="RiskProfile"/> aggregate has no
///         <c>SupersededBy</c> field. Documented as a deviation.</item>
///   <item><see cref="DeleteAsync"/>: defensive STUB — RiskProfile
///         termination is canonical via <c>MarkSupersededAsync</c>, NOT
///         a direct delete. The decorator emits
///         <see cref="AuditAction.Failed"/> + re-throws
///         <see cref="NotSupportedException"/> BEFORE the inner is reached.</item>
///   <item><see cref="GetActiveAsync"/> + <see cref="GetByIdAsync"/> +
///         <see cref="ListByUserAsync"/>: forward to the inner (no audit).</item>
/// </list>
///
/// <para>
/// Registered via Scrutor:
/// <c>services.Decorate&lt;IRiskProfileRepository, RiskProfileAuditDecorator&gt;()</c>
/// in <c>IdentityModuleRegistration</c>.
/// </para>
/// </summary>
public sealed class RiskProfileAuditDecorator : IRiskProfileRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IRiskProfileRepository _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public RiskProfileAuditDecorator(
        IRiskProfileRepository inner,
        IAuditLogger audit,
        ITenantContext tenant,
        IClock clock)
    {
        _inner = inner;
        _audit = audit;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task AddAsync(RiskProfile profile, CancellationToken ct = default)
    {
        await _inner.AddAsync(profile, ct);
        await LogAsync(
            new AuditEventEntry(
                EntityType: nameof(RiskProfile),
                EntityId: profile.Id,
                Action: AuditAction.Created,
                TenantId: _tenant.Current?.Value,
                UserId: _tenant.CurrentUserId,
                ChangesJson: null,
                OccurredAt: _clock.UtcNow),
            ct);
    }

    public async Task<Result> MarkSupersededAsync(Guid id, IClock clock, CancellationToken ct = default)
    {
        // Load the profile first to enforce cross-tenant IsOwner check.
        var profile = await _inner.GetByIdAsync(id, ct);
        if (profile is null)
        {
            // No profile to supersede — let the inner decide the error
            // (it returns Result.Failure(RiskProfileErrors.NotFound)). The
            // audit row is NOT written for a not-found; the inner failure
            // surfaces to the handler unchanged.
            return await _inner.MarkSupersededAsync(id, clock, ct);
        }

        if (!IsOwner(profile))
        {
            await LogAsync(
                new AuditEventEntry(
                    EntityType: nameof(RiskProfile),
                    EntityId: profile.Id,
                    Action: AuditAction.Denied,
                    TenantId: _tenant.Current?.Value,
                    UserId: _tenant.CurrentUserId,
                    ChangesJson: null,
                    OccurredAt: _clock.UtcNow),
                ct);
            throw new UnauthorizedAccessException(
                $"RiskProfile {profile.Id} does not belong to current user (cross-tenant attempt).");
        }

        var result = await _inner.MarkSupersededAsync(id, clock, ct);

        // Emit Deleted audit + supersession diff on success. The diff uses
        // the actual aggregate fields (IsActive true→false + SupersededAt
        // null→now). The design.md sketch suggested SupersededBy as a key
        // — that field does NOT exist on RiskProfile; the entity models
        // the supersession via IsActive + SupersededAt only.
        var now = clock.UtcNow;
        var changesJson = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["isActive"] = new
            {
                before = (object?)true,
                after = (object?)false,
            },
            ["supersededAt"] = new
            {
                before = (object?)null,
                after = (object?)now.ToString("O"),
            },
        }, JsonOptions);

        await LogAsync(
            new AuditEventEntry(
                EntityType: nameof(RiskProfile),
                EntityId: profile.Id,
                Action: AuditAction.Deleted,
                TenantId: _tenant.Current?.Value,
                UserId: _tenant.CurrentUserId,
                ChangesJson: changesJson,
                OccurredAt: _clock.UtcNow),
            ct);

        return result;
    }

    public async Task DeleteAsync(RiskProfile profile, CancellationToken ct = default)
    {
        // Slice 7a.1: DeleteAsync is NOT a valid RiskProfile termination.
        // The canonical surface is MarkSupersededAsync (paired with a new
        // AddAsync in one UoW to preserve the single-active invariant).
        // Emit an AuditAction.Failed row BEFORE re-throwing so the misuse
        // is on the audit trail. The inner IRiskProfileRepository.DeleteAsync
        // is NEVER reached — the contract is "RiskProfile deletion is not
        // supported".
        await LogAsync(
            new AuditEventEntry(
                EntityType: nameof(RiskProfile),
                EntityId: profile.Id,
                Action: AuditAction.Failed,
                TenantId: _tenant.Current?.Value,
                UserId: _tenant.CurrentUserId,
                ChangesJson: null,
                OccurredAt: _clock.UtcNow),
            ct);
        throw new NotSupportedException(
            "RiskProfile deletion happens via MarkSupersededAsync, not direct delete.");
    }

    public Task<RiskProfile?> GetActiveAsync(Guid userId, CancellationToken ct = default)
        => _inner.GetActiveAsync(userId, ct);

    public Task<RiskProfile?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _inner.GetByIdAsync(id, ct);

    /// <summary>
    /// Returns true iff the target profile's UserId matches the calling
    /// actor's CurrentUserId, OR the caller is SuperAdmin. Anonymous /
    /// service contexts bypass the user-scope check (system actors like
    /// webhooks + background services) — matching the Wave 6
    /// ImportJobAuditDecorator / SubscriptionAuditDecorator precedent.
    /// </summary>
    private bool IsOwner(RiskProfile profile)
        => !_tenant.CurrentUserId.HasValue
            || profile.UserId == _tenant.CurrentUserId.Value
            || _tenant.IsSuperAdmin;

    /// <summary>
    /// Fire-and-forget audit log call. The <see cref="IAuditLogger"/>
    /// contract is to never throw — the decorator wraps each call in
    /// try/catch silently so a buggy logger cannot break the mutation.
    /// </summary>
    private async Task LogAsync(AuditEventEntry entry, CancellationToken ct)
    {
        try
        {
            await _audit.LogAsync(entry, ct);
        }
        catch
        {
            // Swallow. The mutation is the system-of-record; the audit
            // row is a defense layer that MUST NOT roll back the
            // mutation on failure.
        }
    }
}