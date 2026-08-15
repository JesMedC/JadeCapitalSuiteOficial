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
        services.AddSingleton<IOwnerProjectionLookup, IdentityOwnerProjectionLookup>();

        return services;
    }
}
