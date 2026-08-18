using JadeCapital.Billing.Application.Features.Subscriptions;
using JadeCapital.Billing.Application.Stripe;
using JadeCapital.Billing.Infrastructure.Persistence;
using JadeCapital.Billing.Infrastructure.Stripe;
using JadeCapital.Shared.Kernel.Stripe;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stripe;

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
        // Scoped (not Singleton) — the implementation consumes BillingDbContext (Scoped).
        // Singleton lifetime would trigger ASP.NET Core's captive-dependency validation
        // and reject host construction. The implementation is stateless; Scoped is correct.
        services.AddScoped<IOwnerProjectionLookup, IdentityOwnerProjectionLookup>();

        // ===== Wave 6a.1 — Stripe =====
        services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.SectionName));

        // IStripeClient is the testing seam Stripe.NET exposes. We construct
        // it from options so tests can swap a fake.
        services.AddSingleton<IStripeClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<StripeOptions>>().Value;
            var apiKey = opts.ApiKey ?? string.Empty;
            return new StripeClient(apiKey);
        });

        // IStripeGateway: real if ApiKey is set, otherwise StubStripeGateway.
        // Singleton lifetime — Stripe.net's StripeClient + services are
        // thread-safe and the gateway is stateless.
        services.AddSingleton<IStripeGateway>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<StripeOptions>>().Value;
            return string.IsNullOrWhiteSpace(opts.ApiKey)
                ? new StubStripeGateway()
                : ActivatorUtilities.CreateInstance<StripeGateway>(sp);
        });

        // Repository for StripeCustomer — Scoped (depends on BillingDbContext).
        services.AddScoped<IStripeCustomerRepository, Persistence.StripeCustomerRepository>();

        // Wave 6a.2 — Append-only webhook event repository.
        services.AddScoped<IStripeWebhookEventRepository, Persistence.StripeWebhookEventRepository>();

        return services;
    }
}
