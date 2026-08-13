using JadeCapital.Billing.Application.Features.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Billing.Infrastructure.DependencyInjection;

/// <summary>
/// Composition surface for the Billing module. Slice 0f of
/// <c>jade-trader-os-core-portals</c> wires the EF Core <see cref="Persistence.BillingDbContext"/>
/// (already created in slice 0e) and registers the Admin write-path
/// repositories + UoW + plan/owner lookups. Slice 0g may add UI-specific
/// services; this surface is the slice-0f contract.
/// </summary>
public static class BillingModuleRegistration
{
    public static IServiceCollection AddBillingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ===== EF Core =====
        // Factory pattern so the connection string resolves AFTER host build
        // (allows WebApplicationFactory tests to inject in-memory config first).
        services.AddDbContext<Persistence.BillingDbContext>((sp, opts) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var pgConn = cfg.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("ConnectionStrings:Postgres required.");
            opts.UseNpgsql(pgConn, npg =>
                npg.MigrationsHistoryTable("__ef_migrations", "billing"));
        });

        // ===== Admin write paths =====
        services.AddScoped<ISubscriptionAdminRepository, Persistence.SubscriptionAdminRepository>();
        services.AddScoped<ISubscriptionAdminUnitOfWork, Persistence.BillingAdminUnitOfWork>();
        services.AddScoped<IPlanLookup, Persistence.PlanLookup>();
        services.AddSingleton<IOwnerProjectionLookup, EmptyOwnerProjectionLookup>();

        return services;
    }
}

/// <summary>
/// Default <see cref="IOwnerProjectionLookup"/> that returns an empty
/// projection. Real Identity lookup ships in slice 0g once the user-admin
/// route is wired; until then the Admin API MUST NOT block on a user
/// lookup that does not exist yet — this no-op preserves the
/// <see cref="Identity.Contracts.Projections.IUserOwnerProjection"/> contract
/// without leaking user existence through the Admin surface.
/// </summary>
public sealed class EmptyOwnerProjectionLookup : IOwnerProjectionLookup
{
    public Task<JadeCapital.Identity.Contracts.Projections.IUserOwnerProjection?> FindByUserIdAsync(
        Guid userId, CancellationToken ct = default)
        => Task.FromResult<JadeCapital.Identity.Contracts.Projections.IUserOwnerProjection?>(null);
}
