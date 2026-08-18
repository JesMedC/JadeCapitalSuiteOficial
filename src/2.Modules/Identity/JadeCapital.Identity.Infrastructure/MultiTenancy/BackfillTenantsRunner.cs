using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Identity.Infrastructure.MultiTenancy;

/// <summary>
/// Wave-6c.2 user-tenant backfill (Wave 6, slice 6c.2).
///
/// <para>
/// <b>Why this exists</b>: <see cref="User.TenantId"/> is NULLABLE
/// in 6c.1. The 6c.3 NOT NULL constraint requires every user row to
/// have a tenant by then. The 6c.2 SQL migration 0026 creates a
/// single shared "Personal" tenant and rewrites all NULL
/// <c>tenant_id</c> rows in one transaction. This runner is the
/// in-process equivalent: a hosted service fires it on startup so
/// the backfill runs even on a DB that pre-dated 6c.2.
///
/// The script-and-runner pair is deliberate: the SQL handles green-field
/// deployments (a fresh DB never has NULL rows because the seeding
/// happens during migration), while the hosted service handles in-place
/// upgrades of pre-Wave-6 databases.
/// </para>
///
/// <para>
/// <b>Idempotency</b>: the runner is safe to invoke repeatedly:
/// <list type="bullet">
///   <item>Counts users with NULL <c>tenant_id</c> and short-circuits at 0.</item>
///   <item>Looks up the Personal tenant by stable slug <c>personal-default</c>;
///         creates it only when missing.</item>
///   <item>Re-runs on the same DB touch 0 rows.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Failure mode</b>: a transient DB failure (timeout, retryable
/// connection error) propagates to the hosted service which catches it
/// and retries on the next startup. The runner itself does NOT swallow
/// exceptions — a non-recoverable error MUST surface.
/// </para>
/// </summary>
public sealed class BackfillTenantsRunner : IBackfillTenantsRunner
{
    /// <summary>
    /// Stable slug for the backfill's Personal tenant. Used by the
    /// 0026 SQL migration so the script-and-runner pair target the
    /// same row (idempotency across both surfaces).
    /// </summary>
    public const string PersonalSlug = "personal-default";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackfillTenantsRunner> _logger;

    public BackfillTenantsRunner(
        IServiceScopeFactory scopeFactory,
        ILogger<BackfillTenantsRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        // Each call opens its own scope — the runner resolves the
        // DbContext on every invocation so a long-lived singleton
        // (the hosted service) does not accumulate tracked entities.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var nullCount = await db.Users
            .Where(u => u.TenantId == null)
            .CountAsync(ct);
        if (nullCount == 0)
        {
            _logger.LogDebug("BackfillTenants: no rows need assignment; nothing to do.");
            return 0;
        }

        var personal = await db.Tenants
            .FirstOrDefaultAsync(t => t.Slug == PersonalSlug, ct);
        var personalCreated = false;
        if (personal is null)
        {
            // Synthetic owner for the Personal tenant: pick the
            // earliest NULL user so the FK is satisfied. If the
            // first user is already assigned to another tenant,
            // fall back to a phantom owner (the SQL migration uses
            // a similar fallback). In practice the runner always
            // fires while at least one NULL row exists, so the
            // fallback is defensive.
            var firstNullUserId = await db.Users
                .Where(u => u.TenantId == null)
                .OrderBy(u => u.Id)        // stable Guids; avoids DateTimeOffset ORDER BY (SQLite-incompatible)
                .Select(u => u.Id)
                .FirstAsync(ct);
            personal = Tenant.FromTrusted(
                id: Guid.NewGuid(),
                name: "Personal",
                slug: PersonalSlug,
                ownerUserId: firstNullUserId,
                plan: TenantPlan.Personal,
                status: TenantStatus.Active,
                createdAt: DateTimeOffset.UtcNow,
                updatedAt: null);
            await db.Tenants.AddAsync(personal, ct);
            await db.SaveChangesAsync(ct); // persist Personal BEFORE the bulk UPDATE so the FK is satisfied.
            personalCreated = true;
            _logger.LogInformation(
                "BackfillTenants: created Personal tenant {PersonalId} (owner={OwnerId}, users={Count}).",
                personal.Id, firstNullUserId, nullCount);
        }

        // Bulk assignment via ExecuteUpdate: emits a single UPDATE
        // statement (no entity hydration), idempotent against the
        // assigned rows because we re-filter by tenant_id IS NULL.
        var personalTenantId = new JadeCapital.Shared.Kernel.MultiTenancy.TenantId(personal.Id);
        var updated = await db.Users
            .Where(u => u.TenantId == null)
            .ExecuteUpdateAsync(
                u => u.SetProperty(x => x.TenantId, personalTenantId),
                ct);

        _logger.LogInformation(
            "BackfillTenants: assigned {Updated} user(s) to Personal tenant {PersonalId} (created={Created}).",
            updated, personal.Id, personalCreated);
        return updated;
    }
}
