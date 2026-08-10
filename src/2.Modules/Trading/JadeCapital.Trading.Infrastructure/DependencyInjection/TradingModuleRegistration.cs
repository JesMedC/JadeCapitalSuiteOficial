using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Infrastructure.Persistence;
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
        services.AddScoped<IUnitOfWork, TradingUnitOfWork>();

        return services;
    }
}
