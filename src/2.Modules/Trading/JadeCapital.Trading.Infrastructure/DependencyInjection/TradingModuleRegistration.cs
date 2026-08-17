using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Alerts;
using JadeCapital.Trading.Application.Alerts.Rules;
using JadeCapital.Trading.Infrastructure.BackgroundServices;
using JadeCapital.Trading.Infrastructure.Persistence;
using JadeCapital.Trading.Infrastructure.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        // Slice 1c.1 — pre-trade checklist persistence.
        services.AddScoped<IPreTradeChecklistRepository, ChecklistRepository>();
        // Slice 1d.1 — post-trade review + attachment persistence.
        services.AddScoped<ITradeReviewRepository, TradeReviewRepository>();
        // Slice 2a.1 — daily journal persistence.
        services.AddScoped<IJournalEntryRepository, JournalEntryRepository>();
        // Slice 3a — strategy persistence (named setups + analytics).
        services.AddScoped<IStrategyRepository, StrategyRepository>();
        // Slice 3b — alerts persistence (BackgroundService writes; API reads).
        services.AddScoped<IAlertRepository, AlertRepository>();
        // Slice 3c — planner sessions persistence (weekly planned-vs-actual).
        services.AddScoped<IPlannerSessionRepository, PlannerSessionRepository>();
        // Slice 4a — scanner filters persistence + stub data source.
        services.AddScoped<IScannerFilterRepository, ScannerFilterRepository>();
        services.AddScoped<IScannerDataSource, InMemoryScannerDataSource>();
        services.AddScoped<IUnitOfWork, TradingUnitOfWork>();

        // ===== Metrics read store (slice 1f) =====
        services.AddScoped<IMetricsQueryStore, MetricsQueryStore>();
        services.AddSingleton<IUserExistenceProbe, TradingUserExistenceProbe>();

        // ===== Slice 3b — Alert evaluation pipeline =====
        // 5 rules + registry + per-user evaluator + hosted background service.
services.AddSingleton<IAlertRule, NoTradesInDaysRule>();
        services.AddSingleton<IAlertRule, DrawdownExceededRule>();
        services.AddSingleton<IAlertRule, RRAverageBelowRule>();
        services.AddSingleton<IAlertRule, CurrentPriceNearStopRule>();
        services.AddSingleton<AlertRegistry>();
        // Scoped — resolves per-tick scoped deps (IAlertRepository, ITradeRepository, etc.)
        // via the IServiceScopeFactory inside the BackgroundService.
        services.AddScoped<AlertEvaluationService>();
        services.AddHostedService<AlertEvaluationBackgroundService>();

        return services;
    }
}
