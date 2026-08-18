using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Identity.Infrastructure.Persistence;

/// <summary>
/// Dedicated DbContext for the <see cref="AuditEvent"/> aggregate (Wave 6,
/// slice 6d.1).
///
/// <para>
/// <b>Why a SEPARATE DbContext</b>: this is the defense-in-depth mechanism
/// that enforces the audit log's append-only invariant.
/// </para>
///
/// <list type="bullet">
///   <item><b>Schema isolation</b>: <see cref="AuditEvent"/> lives in the
///         <c>audit</c> schema, NOT <c>identity</c>. The two contexts use
///         different default schemas and cannot accidentally cross-contaminate.</item>
///   <item><b>Write-only surface</b>: this context only exposes
///         <see cref="DbSet{TEntity}.AddAsync"/> via the
///         <c>IAuditLogger</c> impl in 6d.2. No UPDATE/DELETE surface
///         is exposed — the API consumer cannot mutate audit rows even
///         if they wanted to.</item>
///   <item><b>Migration isolation</b>: <c>__ef_migrations</c> for the audit
///         schema is separate from the identity migration history. Rolling
///         back an identity migration does not affect the audit table.</item>
/// </list>
///
/// <para>
/// The 6d.1 slice registers <see cref="AuditDbContext"/> with the same
/// connection string as <see cref="IdentityDbContext"/> (both target the
/// same Postgres instance, different schemas). The 6d.2 <c>AuditLogger</c>
/// impl uses this context for every <c>LogAsync</c> call.
/// </para>
/// </summary>
public sealed class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options) { }

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Schema lives in its own namespace — separate from `identity`.
        modelBuilder.HasDefaultSchema("audit");
        modelBuilder.ApplyConfiguration(new AuditEventConfiguration());
    }
}
