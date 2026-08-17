using JadeCapital.Shared.Kernel.MarketData;
using JadeCapital.Shared.Kernel.Storage;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Alerts;
using JadeCapital.Trading.Application.Alerts.Rules;
using JadeCapital.Trading.Application.Attachments;
using JadeCapital.Trading.Application.Features.Realtime;
using JadeCapital.Trading.Infrastructure.BackgroundServices;
using JadeCapital.Trading.Infrastructure.Persistence;
using JadeCapital.Trading.Infrastructure.Queries;
using JadeCapital.Trading.Infrastructure.Realtime;
using JadeCapital.Trading.Infrastructure.Storage;
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
        // Slice 4b — market data quote cache + deterministic in-memory provider.
        services.AddScoped<IQuoteCacheRepository, QuoteCacheRepository>();
        services.AddSingleton<IQuoteProvider, InMemoryQuoteProvider>();
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

// ===== Slice 4c — Realtime (SignalR QuoteHub + QuoteBroadcastService) =====
// Singleton registry — the broadcast loop and the hub share state via
// the same instance regardless of scope.
services.AddSingleton<IQuoteSubscriptionRegistry, InMemoryQuoteSubscriptionRegistry>();
// MediatR handlers for Subscribe/Unsubscribe commands live in
// Trading.Application.Features.Realtime. They are picked up by the
// existing AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(...))
// in Program.cs which scans the Trading.Application assembly.
services.AddScoped<SubscribeToQuoteHandler>();
services.AddScoped<UnsubscribeFromQuoteHandler>();
services.AddScoped<UnsubscribeAllFromQuotesHandler>();
// The hub itself is scoped (SignalR resolves per-call); the broadcast
// service is a singleton host that polls the registry every 5s.
services.AddScoped<QuoteHub>();
services.AddHostedService<QuoteBroadcastService>();

// ===== Slice 4d — Attachments (quota + lifecycle + thumbnails + virus stub) =====
// IVirusScanner singleton — the no-op is stateless; Wave 6 swaps the DI
// registration for a real ClamAV impl without touching consumers.
services.AddSingleton<JadeCapital.Shared.Kernel.Storage.IVirusScanner,
                       JadeCapital.Trading.Infrastructure.Storage.VirusScannerNoOp>();

// Usage repository — one SUM + COUNT query per request; cheap.
services.AddScoped<ITradeAttachmentUsageRepository, TradeAttachmentUsageRepository>();
// Sweep repository — used only by AttachmentLifecycleService; bounded-batch pages.
services.AddScoped<IAttachmentSweepRepository, AttachmentSweepRepository>();
// Thumbnail generator — wraps the MinIO SDK presigned-GET call + transform params.
services.AddScoped<IAttachmentThumbnailGenerator, MinioThumbnailGenerator>();

// Application-layer handlers/services that need DI.
services.AddScoped<AttachmentQuotaEnforcer>();
services.AddScoped<GetAttachmentUsageHandler>();
services.AddScoped<GetAttachmentThumbnailHandler>();

// Daily lifecycle sweep (BackgroundService) + per-user quota gate (RequestAttachmentUploadHandler).
services.AddHostedService<JadeCapital.Trading.Infrastructure.BackgroundServices.AttachmentLifecycleService>();

return services;
}
}
