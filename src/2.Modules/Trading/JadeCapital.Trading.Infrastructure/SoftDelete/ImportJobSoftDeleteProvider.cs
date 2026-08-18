using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.SoftDelete;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Imports;
// Result.Errors aliases are pulled in via JadeCapital.Shared.Kernel.Results;
// for the type-mismatch path we use Error.Infrastructure directly.

namespace JadeCapital.Trading.Infrastructure.SoftDelete;

/// <summary>
/// Provider for soft-deleting <see cref="ImportJob"/> aggregates
/// (Wave 6, slice 6d.1).
///
/// <para>
/// Bridges <see cref="ISoftDeleteProvider"/> (cross-cutting contract in
/// <c>Shared.Kernel</c>) to the existing <see cref="IImportJobRepository"/>
/// (Trading.Application.Abstractions). The provider is registered with DI
/// and the <see cref="ISoftDeleteProviderRegistry"/> aggregates it with
/// providers from every other module.
/// </para>
///
/// <para>
/// <b>Defense-in-depth</b>: <see cref="FindByIdAsync"/> returns the entity
/// as-is from the repository (the existing
/// <see cref="IImportJobRepository.GetByIdAsync"/> already applies user-id
/// scoping in production handlers; the soft-delete handler does the same
/// isolation at the call site). The provider MUST trust the repository's
/// auth boundary — it does NOT add cross-tenant or cross-user checks
/// here.
/// </para>
/// </summary>
internal sealed class ImportJobSoftDeleteProvider : ISoftDeleteProvider
{
    /// <summary>
    /// Stable entity-type name. Convention: the .NET type name
    /// (<c>"ImportJob"</c>). Used by <see cref="ISoftDeleteProviderRegistry"/>
    /// as the lookup key — renaming breaks the SoftDelete API contract.
    /// </summary>
    public string EntityType => nameof(ImportJob);

    private readonly IImportJobRepository _jobs;

    public ImportJobSoftDeleteProvider(IImportJobRepository jobs)
    {
        _jobs = jobs;
    }

    /// <summary>
    /// Load the import job by id. Returns null when the row doesn't exist
    /// or has already been soft-deleted (the EF global query filter in
    /// the production <see cref="JadeCapital.Trading.Infrastructure.Persistence.TradingDbContext"/>
    /// excludes soft-deleted rows from the lookup — the provider's
    /// caller sees a 404, indistinguishable from a hard-delete miss).
    /// </summary>
    public async Task<ISoftDelete?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        var job = await _jobs.GetByIdAsync(id, ct);
        return job;
    }

    /// <summary>
    /// Persist the updated entity. The <see cref="ImportJob.MarkDeleted"/>
    /// call sets IsDeleted=true + DeletedAtUtc + DeletedByUserId; the
    /// repository's UpdateAsync issues the UPDATE statement. Returns
    /// Result.Failure if the EF layer throws <c>DbUpdateConcurrencyException</c>
    /// (a row with a stale version token was updated between the load +
    /// the save — the SoftDeleteHandler maps this to a generic failure;
    /// the 6d.2 AuditLogger wraps the call in a try/catch for resilience).
    /// </summary>
    public async Task<Result> UpdateAsync(ISoftDelete entity, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity is not ImportJob job)
            return Result.Failure(Error.Infrastructure(
                "import_job.soft_delete_type_mismatch",
                $"SoftDeleteProvider for ImportJob received a non-ImportJob entity: {entity.GetType().FullName}."));

        await _jobs.UpdateAsync(job, ct);
        return Result.Success();
    }
}
