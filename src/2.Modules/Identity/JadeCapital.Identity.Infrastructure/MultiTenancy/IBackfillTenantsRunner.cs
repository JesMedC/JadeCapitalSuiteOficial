namespace JadeCapital.Identity.Infrastructure.MultiTenancy;

/// <summary>
/// Operational boundary for the Wave-6c.2 user-tenant backfill (Wave 6,
/// slice 6c.2).
///
/// <para>
/// Implemented by <see cref="BackfillTenantsRunner"/>; consumed by
/// <see cref="BackfillTenantsHostedService"/> on a 15s post-startup delay.
/// The seam exists so the runner is unit-testable (with an in-process
/// SQLite DbContext) and so a future admin endpoint can trigger the
/// same operation on demand without re-creating a hosted service.
/// </para>
/// </summary>
public interface IBackfillTenantsRunner
{
    /// <summary>
    /// Idempotent: ensures every <c>identity.users</c> row with a NULL
    /// <c>tenant_id</c> is assigned to a Personal tenant. Returns the
    /// number of rows updated (0 if the backfill has nothing to do).
    /// </summary>
    Task<int> RunAsync(CancellationToken ct = default);
}
