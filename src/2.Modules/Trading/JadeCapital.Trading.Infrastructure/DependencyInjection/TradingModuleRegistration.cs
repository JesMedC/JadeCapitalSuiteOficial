using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Infrastructure.Persistence;
using JadeCapital.Trading.Infrastructure.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Trading.Infrastructure.DependencyInjection;

public static class TradingModuleRegistration
{
    public static IServiceCollection AddTradingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ===== EF Core =====
        // Use factory pattern para resolver la connection string DESPUES del host build
        // (permite que WebApplicationFactory de tests inyecte config primero).
        services.AddDbContext<TradingDbContext>((sp, opts) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var pgConn = cfg.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("ConnectionStrings:Postgres required.");
            opts.UseNpgsql(pgConn, npg =>
                npg.MigrationsHistoryTable("__ef_migrations", "trading"));
        });

        // ===== Repos =====
        services.AddScoped<ITradeRepository, TradeRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IInstrumentRepository, InstrumentRepository>();
        services.AddScoped<IUnitOfWork, TradingUnitOfWork>();

        // ===== Metrics read store (slice 1f) =====
        services.AddScoped<IMetricsQueryStore, MetricsQueryStore>();
        services.AddSingleton<IUserExistenceProbe, TradingUserExistenceProbe>();

        return services;
    }
}
