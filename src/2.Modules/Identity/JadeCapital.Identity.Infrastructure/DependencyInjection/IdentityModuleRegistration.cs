using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application.Features.SoftDelete;
using JadeCapital.Identity.Contracts.Projections;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.BackgroundJobs;
using JadeCapital.Identity.Infrastructure.MultiTenancy;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Identity.Infrastructure.Projections;
using JadeCapital.Identity.Infrastructure.Security;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.SoftDelete;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Identity.Infrastructure.DependencyInjection;

public static class IdentityModuleRegistration
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ===== EF Core =====
        // Use factory pattern so the connection string is resolved AFTER host build
        // (this allows WebApplicationFactory tests to inject in-memory config first).
        services.AddDbContext<IdentityDbContext>((sp, opts) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var pgConn = cfg.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("ConnectionStrings:Postgres required.");
            opts.UseNpgsql(pgConn, npg =>
                npg.MigrationsHistoryTable("__ef_migrations", "identity"));
        });

        // ===== Slice 6d.1 — AuditDbContext (separate, write-only) =====
        // Lives alongside IdentityDbContext in the same Postgres instance,
        // but uses its own schema (`audit`) and its own __ef_migrations
        // history table (`audit.__ef_migrations`). Defense-in-depth: a
        // bug in IdentityDbContext cannot accidentally UPDATE/DELETE on
        // audit.events — they're structurally separate DbContexts.
        services.AddDbContext<AuditDbContext>((sp, opts) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var pgConn = cfg.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("ConnectionStrings:Postgres required.");
            opts.UseNpgsql(pgConn, npg =>
                npg.MigrationsHistoryTable("__ef_migrations", "audit"));
        });

        // ===== Repos =====
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        // ===== Slice 4d — Attachment quota projection =====
        // Identity owns identity.users.attachment_quota_bytes + attachment_used_bytes
        // (migration 0018). The Trading module reads them via the Contracts
        // projection; this registration wires the EF Core implementation.
        services.AddScoped<IAttachmentQuotaReader, IdentityAttachmentQuotaReader>();
        services.AddScoped<ITemporaryCredentialRepository, TemporaryCredentialRepository>();
        services.AddScoped<IPasswordHistoryRepository, PasswordHistoryRepository>();
        services.AddScoped<IRefreshTokenRevoker, RefreshTokenRevoker>();
        // Slice 1a.1b — single-active risk profile per user (Backs both
        // the API endpoints and the cross-module Identity.Contracts reader).
        services.AddScoped<IRiskProfileRepository, RiskProfileRepository>();
        services.AddScoped<IIdentityUserRiskProfileReader, IdentityUserRiskProfileReader>();
        // Slice 3b — exposes active user Ids to the Trading BackgroundService
        // without forcing Trading to depend on Identity.Domain.
        services.AddScoped<IActiveUserIdsReader, IdentityActiveUserIdsReader>();
        services.AddSingleton<IDistributedLock, InMemoryDistributedLock>();
        services.AddScoped<IUnitOfWork, IdentityUnitOfWork>();

        // ===== Slice 6c.1 — Tenants =====
        // ITenantContext: 6c.2 ships the real HttpContext-bound impl that
        // resolves tenant_id + user id from the JWT claims. The 6c.1
        // placeholder is gone (the file was rewritten in 6c.2 to be the
        // real impl; the registration line stayed the same to keep the
        // DI seam minimal — see MultiTenancy/TenantContext.cs).
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        // ===== Slice 6d.2 — Audit decorator (Typed) =====
        // TenantAuditDecorator wraps TenantRepository via Scrutor's
        // services.Decorate<>(). Every Add/Update/Delete writes an
        // audit.events row via IAuditLogger (enriched with
        // ITenantContext.Current + ICurrentUserId). Reads (GetByIdAsync,
        // FindBySlugAsync, ListByOwnerAsync) bypass the audit surface.
        services.Decorate<ITenantRepository, TenantAuditDecorator>();
        // MediatR resolves handlers by interface; register the concrete
        // types so DI has an entry. (MediatR also scans the Application
        // assembly, so the runtime binding happens twice — that's fine.)
        services.AddScoped<JadeCapital.Identity.Application.Features.Tenants.CreateTenant.CreateTenantHandler>();
        services.AddScoped<JadeCapital.Identity.Application.Features.Tenants.GetTenant.GetTenantHandler>();
        // ===== Slice 6c.3 — Tenant admin endpoints =====
        // 4 handlers + DTO mappers that back PATCH /api/tenants/{id} +
        // GET /api/tenants/{id}/users + POST /api/tenants/{id}/users +
        // DELETE /api/tenants/{id}/users/{userId}. Each uses the real
        // ITenantContext (slice 6c.2) for cross-tenant 404 + capacity
        // enforcement; the SQL migration 0026_NOT_NULL_tenant_id.sql
        // closes the loop on the "ONE migration atómica" strategy.
        services.AddScoped<JadeCapital.Identity.Application.Features.Tenants.UpdateTenant.UpdateTenantHandler>();
        services.AddScoped<JadeCapital.Identity.Application.Features.Tenants.ListTenantUsers.ListTenantUsersHandler>();
        services.AddScoped<JadeCapital.Identity.Application.Features.Tenants.InviteTenantUser.InviteTenantUserHandler>();
        services.AddScoped<JadeCapital.Identity.Application.Features.Tenants.RemoveTenantUser.RemoveTenantUserHandler>();

        // ===== Slice 6c.2 — Tenant middleware + backfill =====
        // BackfillTenantsHostedService fires once, 15s after startup, to
        // assign NULL users to a Personal tenant. The SQL migration 0026
        // covers greenfield deploys; the hosted service handles in-place
        // upgrades of pre-Wave-6 DBs. Both are idempotent against each
        // other (independent surfaces, same slug).
        services.AddScoped<IBackfillTenantsRunner, BackfillTenantsRunner>();
        services.AddHostedService<BackfillTenantsHostedService>();

        // ===== Slice 6d.1 + 6d.2 — Soft-delete + Audit =====
        // IAuditLogger: 6d.1 shipped the NoOp placeholder; 6d.2 swaps in the
        // real AuditLogger impl that writes to the dedicated AuditDbContext
        // (separate schema, separate migration history). The placeholder
        // pattern matches the 6c.1 ITenantContext precedent — the interface
        // was wired end-to-end, the real persistence impl landed in the
        // slice that owns the persistence concern.
        services.AddScoped<IAuditLogger, AuditLogger>();
        // ISoftDeleteProviderRegistry: aggregates every ISoftDeleteProvider
        // registered across all modules (DI auto-collects IEnumerable).
        services.AddScoped<ISoftDeleteProviderRegistry>(sp =>
        {
            var providers = sp.GetRequiredService<IEnumerable<ISoftDeleteProvider>>();
            return new SoftDeleteProviderRegistry(providers);
        });
        // SoftDeleteHandler: MediatR-resolved; register explicitly so DI
        // has an entry (MediatR also scans the Application assembly, so
        // the runtime binding happens twice — that's fine).
        services.AddScoped<SoftDeleteHandler>();

        // ===== Security =====
        services.AddSingleton<IPasswordHasher>(_ =>
            new Pbkdf2PasswordHasher(iterations: configuration.GetValue<int?>("Security:Pbkdf2Iterations") ?? 100_000));
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<JadeCapital.Identity.Application.Abstractions.IPasswordChangeReuseChecker,
            JadeCapital.Identity.Application.Authentication.PasswordChangeReuseChecker>();

        // ===== Background services =====
        services.AddHostedService<RefreshTokenCleanupService>();

        return services;
    }
}