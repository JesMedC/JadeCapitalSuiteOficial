using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.SoftDelete;
using JadeCapital.Shared.Kernel.Time;
using MediatR;

namespace JadeCapital.Identity.Application.Features.SoftDelete;

/// <summary>
/// Command to soft-delete any <see cref="ISoftDelete"/> aggregate by
/// entity-type name + id (Wave 6, slice 6d.1).
///
/// <para>
/// The handler looks up the <see cref="ISoftDeleteProvider"/> registered
/// for <see cref="EntityType"/> in the <see cref="ISoftDeleteProviderRegistry"/>,
/// loads the entity, marks it deleted, persists the change, and writes
/// an audit event via <see cref="IAuditLogger"/>.
/// </para>
///
/// <para>
/// <b>Why string-typed entity</b>: MediatR requests are concrete types,
/// but the soft-delete API is generic — the same command shape works
/// for every ISoftDelete aggregate (ImportJob in 6d.1; Tenant,
/// Subscription, etc. in 6d.2 / Wave 7). The string lookup keeps the
/// API surface stable while letting the handler dispatch by type.
/// </para>
///
/// <para>
/// <b>Security</b>: the provider MUST apply any tenant filter the
/// underlying entity is subject to (defense-in-depth — a cross-tenant
/// delete attempt collapses to 404, never to a successful delete).
/// </para>
/// </summary>
public sealed record SoftDeleteCommand(
    string EntityType,
    Guid EntityId,
    Guid UserId) : IRequest<Result>;

/// <summary>
/// Handler for the soft-delete command (Wave 6, slice 6d.1).
///
/// <para>
/// Flow:
/// </para>
/// <list type="number">
///   <item>Look up the <see cref="ISoftDeleteProvider"/> by
///         <see cref="SoftDeleteCommand.EntityType"/>. If none is
///         registered → 422 <c>validation.soft_delete.entity_not_soft_deleteable</c>.</item>
///   <item>Load the entity by id. If null → 404
///         <c>notfound.soft_delete.entity_not_found</c>.</item>
///   <item>If <see cref="ISoftDelete.IsDeleted"/> is already true → 404
///         <c>notfound.soft_delete.already_deleted</c>. The 404 (not 410)
///         is intentional: callers cannot probe for the existence of
///         deleted rows.</item>
///   <item>Mark the entity as deleted (sets IsDeleted=true,
///         DeletedAtUtc=clock.UtcNow, DeletedByUserId=cmd.UserId). The
///         entity is mutated via reflection on the public setter so
///         every ISoftDelete aggregate works without an interface
///         change (per 6a.2 deviation pattern — keep Application
///         layer free of EF Core).</item>
///   <item>Persist via the provider's UpdateAsync.</item>
///   <item>Write an audit event via <see cref="IAuditLogger.LogAsync"/>
///         with <see cref="AuditAction.Deleted"/>. The audit event
///         includes EntityType, EntityId, UserId. TenantId and
///         ChangesJson are null at this layer — the 6d.2 AuditLogger
///         impl enriches TenantId from ITenantContext.Current; a
///         future DecoratedRepository decorator computes ChangesJson
///         from the entity diff.</item>
/// </list>
///
/// <para>
/// <b>Defense-in-depth</b>: the Application layer stays free of EF Core
/// (per the 6a.2 deviation). EF exception detection (when
/// <c>DbUpdateConcurrencyException</c> fires) is done via type-name
/// reflection inside the provider's UpdateAsync; the handler trusts the
/// Result it returns.
/// </para>
/// </summary>
public sealed class SoftDeleteHandler : IRequestHandler<SoftDeleteCommand, Result>
{
    private readonly ISoftDeleteProviderRegistry _registry;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;

    public SoftDeleteHandler(
        ISoftDeleteProviderRegistry registry,
        IAuditLogger audit,
        IClock clock)
    {
        _registry = registry;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result> Handle(SoftDeleteCommand cmd, CancellationToken ct)
    {
        // 1) Resolve the provider. Unknown entity type → 422.
        var provider = _registry.GetByEntityType(cmd.EntityType);
        if (provider is null)
            return Result.Failure(SoftDeleteErrors.Validation.EntityNotSoftDeleteable);

        // 2) Load the entity. Not found → 404.
        var loaded = await provider.FindByIdAsync(cmd.EntityId, ct);
        if (loaded is null)
            return Result.Failure(SoftDeleteErrors.NotFound.EntityNotFound);

        // 3) Already-deleted guard. 404 (not 410) — second-delete attempts
        // are indistinguishable from "no such entity" so callers cannot
        // probe for the existence of deleted rows. The audit MUST NOT
        // record a second delete event.
        if (loaded.IsDeleted)
            return Result.Failure(SoftDeleteErrors.NotFound.AlreadyDeleted);

        // 4) Mark the entity as deleted. Per the 6a.2 deviation pattern,
        // mutate via reflection on the public setters — keeps Application
        // EF-free and lets every ISoftDelete aggregate work without an
        // interface change. The provider's UpdateAsync persists.
        loaded.GetType()
            .GetProperty(nameof(ISoftDelete.IsDeleted))!
            .SetValue(loaded, true);
        loaded.GetType()
            .GetProperty(nameof(ISoftDelete.DeletedAtUtc))!
            .SetValue(loaded, _clock.UtcNow);
        loaded.GetType()
            .GetProperty(nameof(ISoftDelete.DeletedByUserId))!
            .SetValue(loaded, cmd.UserId);

        var persistResult = await provider.UpdateAsync(loaded, ct);
        if (persistResult.IsFailure)
            return Result.Failure(persistResult.Error);

        // 5) Write the audit event. Fire-and-forget: the IAuditLogger
        // contract catches all exceptions silently and logs a warning;
        // a failed audit write MUST NOT roll back a successful delete.
        await _audit.LogAsync(new AuditEventEntry(
            EntityType: cmd.EntityType,
            EntityId: cmd.EntityId,
            Action: AuditAction.Deleted,
            TenantId: null,
            UserId: cmd.UserId,
            ChangesJson: null,
            OccurredAt: _clock.UtcNow), ct);

        return Result.Success();
    }
}
