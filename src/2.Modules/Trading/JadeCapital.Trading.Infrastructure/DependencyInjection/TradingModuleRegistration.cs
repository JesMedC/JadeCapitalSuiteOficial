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
using JadeCapital.Trading.Infrastructure.SoftDelete;
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

// ===== Slice 5a.1 + 5a.2 — Imports (CSV + MT4/MT5 importers + ImportJob tracking) =====
// Parsers: registered as IImportRowParser so the streaming pipeline can
// pick the right one via CanParse score. Order matters — the dispatcher
// picks the FIRST registered parser with CanParse >= 0.8, so MT4/MT5 must
// come BEFORE CSV to give the more specific signatures priority over the
// generic CSV fallback (per 5a.2 spec §"5a.2 Phase 2.2").
services.AddScoped<JadeCapital.Shared.Kernel.Imports.IImportRowParser,
                  JadeCapital.Trading.Infrastructure.Imports.Mt4ImportRowParser>();
services.AddScoped<JadeCapital.Shared.Kernel.Imports.IImportRowParser,
                  JadeCapital.Trading.Infrastructure.Imports.CsvImportRowParser>();

// Application-layer streaming pipeline + handlers.
services.AddScoped<JadeCapital.Trading.Application.Features.Imports.StreamImportService>();
services.AddScoped<JadeCapital.Trading.Application.Features.Imports.BeginImport.BeginImportHandler>();
services.AddScoped<JadeCapital.Trading.Application.Features.Imports.GetImportStatus.GetImportStatusHandler>();

// Infrastructure-layer repositories + dedupe service.
        services.AddScoped<JadeCapital.Trading.Application.Abstractions.IImportJobRepository,
                  JadeCapital.Trading.Infrastructure.Persistence.ImportJobRepository>();
        // Slice 6d.2 — typed audit decorator over IImportJobRepository.
        // Co-located with the AddScoped above because Scrutor's Decorate
        // requires the underlying service to be registered first. The
        // decorator lives in Trading.Infrastructure/Audit/ (Trading → Trading)
        // to avoid an Identity.Infrastructure → Trading.Infrastructure →
        // Identity.Infrastructure circular dep.
        services.Decorate<JadeCapital.Trading.Application.Abstractions.IImportJobRepository,
                  JadeCapital.Trading.Infrastructure.Audit.ImportJobAuditDecorator>();
        // Wave 7 slice 7b.1 — typed audit decorator over IStrategyRepository.
        // Mirrors the ImportJobAuditDecorator shape: cross-tenant IsOwner
        // check on Strategy.UserId + DeleteAsync defensive stub emitting
        // AuditAction.Failed before re-throwing NotSupportedException.
        services.Decorate<JadeCapital.Trading.Application.Abstractions.IStrategyRepository,
                  JadeCapital.Trading.Infrastructure.Audit.StrategyAuditDecorator>();
        // Wave 7 slice 7b.1 — typed audit decorator over ITradeRepository.
        // BESPOKE (does NOT use DecoratedRepository<T> because ITradeRepository
        // is bespoke with FindByIdAsync + 6 read methods). Mirrors the
        // ImportJobAuditDecorator shape with cross-tenant IsOwner check
        // on Trade.UserId. The slice 7b.1 BREAKING rename from
        // RemoveAsync → DeleteAsync makes the canonical hard-delete
        // surface emit AuditAction.Deleted with before/after diff.
        services.Decorate<JadeCapital.Trading.Application.Abstractions.ITradeRepository,
                  JadeCapital.Trading.Infrastructure.Audit.TradeAuditDecorator>();
        // Wave 7 slice 7b.2 — typed audit decorator over IJournalEntryRepository.
        // BESPOKE (does NOT use DecoratedRepository<T> because IJournalEntryRepository
        // is bespoke with cross-user-scoped read methods — FindByIdAsync takes an
        // explicit userId parameter). The decorator wraps the slice 7b.2 Phase 1
        // additive DeleteAsync(JournalEntry, ct) overload + emits AuditAction.Deleted
        // with a before/after content snapshot. Cross-tenant IsOwner check on
        // entry.UserId. DeleteAsync(Guid, ct) is forwarded without audit (the
        // production handler is responsible for cross-user validation).
        services.Decorate<JadeCapital.Trading.Application.Abstractions.IJournalEntryRepository,
                  JadeCapital.Trading.Infrastructure.Audit.JournalEntryAuditDecorator>();
        services.AddScoped<JadeCapital.Trading.Application.Abstractions.IImportRowDedupeService,
                  JadeCapital.Trading.Infrastructure.Persistence.ImportRowDedupeService>();

// ===== Slice 5b.2 — AI Coaching (background service + repository + EF provider) =====
//
// Provider (IUserTradingContextProvider) — Scoped: resolves per-tick scoped deps
// (ITradeRepository, IClock, etc.) inside the BackgroundService's scope.
        services.AddScoped<JadeCapital.Trading.Application.Ai.IUserTradingContextProvider,
                  JadeCapital.Trading.Infrastructure.Ai.EfUserTradingContextProvider>();

        // AI coaching prompt repository (Scoped — same lifetime as DbContext).
        services.AddScoped<JadeCapital.Trading.Application.Abstractions.ICoachingPromptRepository,
                  JadeCapital.Trading.Infrastructure.Persistence.CoachingPromptRepository>();

        // Slice 5c.1 — AI risk advisor (Scoped — uses IAIProvider + DbContext).
        services.AddScoped<JadeCapital.Trading.Application.Ai.IAIRiskAdvisor,
                  JadeCapital.Trading.Infrastructure.Ai.OllamaAIRiskAdvisor>();
        services.AddScoped<JadeCapital.Trading.Application.Abstractions.IAIRiskAdviceRepository,
                  JadeCapital.Trading.Infrastructure.Persistence.AIRiskAdviceRepository>();

        // BackgroundService — daily tick at 03:00 UTC ± 30min jitter. Resolves
        // GenerateCoachingPromptHandler + IUserTradingContextProvider from a per-tick
        // scope via IServiceScopeFactory.
        services.AddHostedService<JadeCapital.Trading.Infrastructure.BackgroundServices.CoachingPromptService>();

        // ===== Slice 6d.1 — Soft-delete =====
        // Register ImportJobSoftDeleteProvider so the
        // ISoftDeleteProviderRegistry (wired in IdentityModuleRegistration)
        // picks it up via IEnumerable<ISoftDeleteProvider>. 6d.2 / Wave 7
        // add more providers (Tenant, Subscription, etc.).
        services.AddScoped<JadeCapital.Shared.Kernel.SoftDelete.ISoftDeleteProvider,
            ImportJobSoftDeleteProvider>();

return services;
    }
}
