using JadeCapital.Shared.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Audit;

/// <summary>
/// Per-aggregate audit decorator for <see cref="IImportJobRepository"/>
/// (Wave 6, slice 6d.2).
///
/// <para>
/// Co-located with the <see cref="ImportJob"/> aggregate (Trading
/// bounded context) so the decorator can use the Trading-bounded
/// <see cref="DbContext"/> for EF <c>ChangeTracker.OriginalValues</c>
/// — the pre-mutation diff source. Putting it in
/// <c>Trading.Infrastructure</c> avoids an Identity.Infrastructure →
/// Trading.Infrastructure → Identity.Infrastructure circular dep
/// that would arise if the decorator lived in Identity.Infrastructure
/// (which already references <c>DecoratedRepository&lt;T&gt;</c> for Tenant).
/// </para>
/// <list type="bullet">
///   <item>Forwarding <see cref="IImportJobRepository.FindActiveBySha256Async"/>
///         + <see cref="IRepository{T}.GetByIdAsync"/> to the inner.</item>
///   <item>Wrapping Add with audit logging via the generic
///         <see cref="DecoratedRepository{T}"/> core.</item>
///   <item>Wrapping Update/Delete with <b>cross-tenant isolation</b>
///         + audit logging. An <see cref="ImportJob"/> whose
///         <c>UserId</c> doesn't match the calling user's
///         <see cref="ITenantContext.CurrentUserId"/> is rejected with
///         <see cref="UnauthorizedAccessException"/> AND a
///         <see cref="AuditAction.Updated"/> /
///         <see cref="AuditAction.Deleted"/> audit row is written —
///         the security/compliance trail for the attempt itself.
///         The diff payload on cross-tenant attempts is null because
///         we never read the inner state.</item>
/// </list>
/// <para>
/// <b>DbContext parameter type</b>: the decorator accepts
/// <see cref="DbContext"/> (base type) rather than the concrete
/// <see cref="Persistence.TradingDbContext"/> so the unit-test fixture
/// can register a SQLite-compatible helper DbContext (the full
/// <c>TradingDbContext</c> carries Npgsql-specific array mappings for
/// <c>JournalEntry.Tags</c> that fail to compose on SQLite — see
/// <c>ImportJobRepositoryIntegrationTests.CombinedTradingAuditDbContext</c>).
/// In production DI, the registered <c>TradingDbContext</c> is resolved
/// into the <see cref="DbContext"/> parameter.
/// </para>
/// <para>
/// Registered via Scrutor: <c>services.Decorate&lt;IImportJobRepository, ImportJobAuditDecorator&gt;()</c>
/// in <see cref="DependencyInjection.TradingModuleRegistration"/>.
/// </para>
/// </summary>
public sealed class ImportJobAuditDecorator : IImportJobRepository
{
    private readonly IImportJobRepository _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly DecoratedRepository<ImportJob> _decorated;

    public ImportJobAuditDecorator(
        IImportJobRepository inner,
        DbContext db,
        IAuditLogger audit,
        ITenantContext tenant,
        IClock clock)
    {
        _inner = inner;
        _audit = audit;
        _tenant = tenant;
        _clock = clock;
        _decorated = new DecoratedRepository<ImportJob>(inner, audit, tenant, clock, db: db);
    }

    public Task<ImportJob?> GetByIdAsync(Guid id, CancellationToken ct)
        => _inner.GetByIdAsync(id, ct);

    public Task<ImportJob?> FindActiveBySha256Async(Guid userId, string sha256, CancellationToken ct)
        => _inner.FindActiveBySha256Async(userId, sha256, ct);

    public Task AddAsync(ImportJob job, CancellationToken ct)
        => _decorated.AddAsync(job, ct);

    public async Task UpdateAsync(ImportJob job, CancellationToken ct)
    {
        if (!IsOwner(job))
        {
            await LogDeniedAsync(job, AuditAction.Updated, ct);
            throw new UnauthorizedAccessException(
                $"Import job {job.Id} does not belong to current user (cross-tenant attempt).");
        }
        await _decorated.UpdateAsync(job, ct);
    }

    public async Task DeleteAsync(ImportJob job, CancellationToken ct)
    {
        if (!IsOwner(job))
        {
            await LogDeniedAsync(job, AuditAction.Deleted, ct);
            throw new UnauthorizedAccessException(
                $"Import job {job.Id} does not belong to current user (cross-tenant attempt).");
        }
        await _decorated.DeleteAsync(job, ct);
    }

    /// <summary>
    /// Returns true iff the job belongs to the current user. When no
    /// user is resolved (anonymous / service context), the decorator
    /// allows the operation to proceed — system actors (webhooks,
    /// background services) bypass the user-scope check.
    /// </summary>
    private bool IsOwner(ImportJob job)
        => !_tenant.CurrentUserId.HasValue
            || job.UserId == _tenant.CurrentUserId.Value;

    /// <summary>
    /// Audit-log a denied cross-tenant attempt. The entry is enqueued
    /// with the calling user's id (not the entity's user id) so the
    /// security trail shows WHO attempted the cross-tenant access.
    /// Fire-and-forget: never throws (the
    /// <see cref="IAuditLogger.LogAsync"/> contract swallows exceptions).
    /// </summary>
    private Task LogDeniedAsync(ImportJob job, AuditAction action, CancellationToken ct)
    {
        var entry = new AuditEventEntry(
            EntityType: nameof(ImportJob),
            EntityId: job.Id,
            Action: action,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: null,
            OccurredAt: _clock.UtcNow);
        return _audit.LogAsync(entry, ct);
    }
}