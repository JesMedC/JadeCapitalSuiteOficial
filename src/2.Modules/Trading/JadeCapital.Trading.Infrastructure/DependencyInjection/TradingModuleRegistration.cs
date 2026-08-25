using JadeCapital.Shared.Kernel.MarketData;
using JadeCapital.Shared.Kernel.Storage;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Alerts;
using JadeCapital.Trading.Application.Alerts.Rules;
using JadeCapital.Trading.Application.Attachments;
using JadeCapital.Trading.Application.Features.Realtime;
using JadeCapital.Trading.Infrastructure.Audit;
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
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TradingDbContext>());

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
        // Wave 9 slice 9a.2 — typed audit decorator over IScannerFilterRepository.
        // Sub-scope A: BESPOKE CRUD-WITHOUT-DELETE — wraps AddAsync + UpdateAsync
        // with the cross-tenant IsOwner check + audit logging; reads forwarded
        // without audit. The interface was extended in 9a.2 Phase 1 to inherit
        // from IRepository<ScannerFilter> (gaining DeleteAsync as a defensive
        // stub) — the decorator emits AuditAction.Failed + throws
        // NotSupportedException before the inner is reached, mirroring the
        // Wave 7 7b.1 StrategyAuditDecorator + 7a.1 UserAuditDecorator
        // precedent for non-deletable aggregates. The canonical mutation
        // surface is ScannerFilter.Deactivate(IClock) (flips IsActive = false)
        // + UpdateAsync, NOT a hard delete.
        services.Decorate<IScannerFilterRepository, ScannerFilterAuditDecorator>();
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
        // Wave 9 slice 9a.3 — typed audit decorator over IAttachmentSweepRepository.
        // Sub-scope A: NEW BESPOKE BATCH SOFT-DELETE PATTERN — wraps
        // SoftDeleteBatchAsync with the cross-tenant IsOwner check per id +
        // emits 1 audit row per id with EntityType = "TradeAttachment" (the
        // child aggregate, NOT the sweep operation) + a
        // isActive: { before: true, after: false } diff JSON. The decorator
        // loads each attachment via the scoped DbContext to read attachment.UserId
        // for IsOwner + capture the pre-mutation IsActive for the diff. The
        // 1-call-many-audit-rows pattern: 1 batch call → N audit rows (one per
        // id in the batch), NOT 1 row per batch. Cross-tenant id in the batch
        // emits AuditAction.Denied for THAT id + throws
        // UnauthorizedAccessException for the WHOLE batch (transaction abort —
        // inner is NEVER reached). 3 reads (GetExpiredBatchAsync +
        // GetUserAggregateAsync + GetActiveUserIdsAsync) are forwarded without
        // audit. InsertAuditAsync is forwarded WITHOUT audit — it IS the write
        // to trading.attachments_quota_audit (the sweep's own audit log);
        // auditing it would create an infinite loop. Mirrors the Wave 8 8b.2
        // IStripeWebhookEventRepository SKIP rationale.
        services.Decorate<IAttachmentSweepRepository, AttachmentSweepAuditDecorator>();
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
        // Wave 8 slice 8a.1 — typed audit decorator over IAccountRepository.
        // STANDARD (uses DecoratedRepository<Account> after the slice 8a.1
        // RemoveAsync → DeleteAsync rename + IRepository<Account> extension).
        // Cross-tenant IsOwner check on Account.UserId for UpdateAsync.
        // DeleteAsync is a STUB-free canonical hard-delete surface — the
        // production DeleteAccountHandler is responsible for cross-user
        // validation + trade pre-check (FK RESTRICT).
        services.Decorate<JadeCapital.Trading.Application.Abstractions.IAccountRepository,
                  JadeCapital.Trading.Infrastructure.Audit.AccountAuditDecorator>();
        // Wave 8 slice 8a.1 — typed audit decorator over IInstrumentRepository.
        // STANDARD (uses DecoratedRepository<Instrument> after the slice 8a.1
        // RemoveAsync → DeleteAsync rename + IRepository<Instrument> extension).
        // NO IsOwner check: Instrument is a catalog entity shared across all
        // users (TenantAuditDecorator precedent). Admin mutations on the
        // catalog are legitimate.
        services.Decorate<JadeCapital.Trading.Application.Abstractions.IInstrumentRepository,
                  JadeCapital.Trading.Infrastructure.Audit.InstrumentAuditDecorator>();
        // Wave 8 slice 8a.2 — typed audit decorator over IAlertRepository.
        // BESPOKE (does NOT use DecoratedRepository<Alert> because IAlertRepository
        // is bespoke with ListByUserAsync(userId, activeOnly, now, ct) — a
        // userId-scoped read that doesn't fit the generic IRepository<T> shape).
        // CRITICAL deviation: AddAsync returns bool (true = inserted, false =
        // deduped by the partial UNIQUE INDEX ux_alerts_user_rule_day). The
        // decorator MUST inspect the return value: only emit AuditAction.Created
        // when true; silently skip when false (per orchestrator preflight
        // decision 6). Cross-tenant IsOwner check on Alert.UserId for
        // UpdateAsync.
        services.Decorate<JadeCapital.Trading.Application.Abstractions.IAlertRepository,
                  JadeCapital.Trading.Infrastructure.Audit.AlertAuditDecorator>();
        // Wave 8 slice 8a.2 — typed audit decorator over ITradeReviewRepository.
        // BESPOKE (does NOT use DecoratedRepository<TradeReview> because the
        // interface has first-class attachment ops
        // [AddAttachmentAsync|UpdateAttachmentAsync|RemoveAttachmentAsync]
        // for TradeAttachment — a child entity — that don't fit the generic
        // IRepository<T> shape). Cross-tenant IsOwner check on
        // TradeReview.UserId for UpdateAsync. CRITICAL deviation: attachment
        // ops are forwarded to the inner WITHOUT emitting audit rows
        // (per orchestrator preflight decision 7 — TradeAttachment is a
        // child entity of the review, not a separately-audited aggregate).
        services.Decorate<JadeCapital.Trading.Application.Abstractions.ITradeReviewRepository,
                  JadeCapital.Trading.Infrastructure.Audit.TradeReviewAuditDecorator>();
        // Wave 8 slice 8a.3 — typed audit decorator over IPlannerSessionRepository.
        // BESPOKE (does NOT use DecoratedRepository<PlannerSession> because
        // IPlannerSessionRepository is bespoke with ListByUserAndWeekAsync +
        // ExistsForDateAsync + GetWeekComparisonAsync — cross-user-scoped read
        // methods that don't fit the generic IRepository<T> shape).
        // CRITICAL deviation: UpdateAsync emits AuditAction.Updated by default
        // but is upgraded to AuditAction.Deleted when the session's Status ==
        // PlannerStatus.Cancelled via the bespoke IsTerminated reflection
        // check (re-implemented locally for the single terminated value in
        // PlannerStatus — mirrors the Wave 7 7b.1 TradeAuditDecorator pattern
        // for bespoke-shape decorators). Cross-tenant IsOwner check on
        // PlannerSession.UserId for UpdateAsync.
        services.Decorate<JadeCapital.Trading.Application.Abstractions.IPlannerSessionRepository,
                  JadeCapital.Trading.Infrastructure.Audit.PlannerSessionAuditDecorator>();
        // Wave 8 slice 8a.3 — typed audit decorator over IPreTradeChecklistRepository.
        // BESPOKE WRITE-ONCE — only AddAsync wraps (no UpdateAsync or DeleteAsync
        // on the interface — the checklist is write-once per the entity
        // docstring; cleanup cascades via FK to trading.trades with ON DELETE
        // CASCADE). Mirrors a simplified TenantAuditDecorator shape (Wave 6
        // 6d.2). ListByUserIdAsync forwarded without audit (matches Wave 6 +
        // 7 + 8a.1 + 8a.2 + 8a.3 PlannerSession precedent).
        services.Decorate<JadeCapital.Trading.Application.Abstractions.IPreTradeChecklistRepository,
                  JadeCapital.Trading.Infrastructure.Audit.PreTradeChecklistAuditDecorator>();
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
        // Wave 9 slice 9a.1 — typed audit decorator over ICoachingPromptRepository.
        // Sub-scope A: BESPOKE WRITE-ONCE — only AddAsync wraps (the entity is
        // immutable after Create per the docstring; no UpdateAsync or DeleteAsync
        // on the interface). Cross-tenant IsOwner check on CoachingPrompt.UserId;
        // the surface mirrors the Wave 8 8b.1 StripeCustomer + 9a.1
        // AIRiskAdviceAuditDecorator precedent. The 2 reads (FindByUserAndDateAsync
        // + ListByUserAndWindowAsync) are forwarded without audit.
        services.Decorate<JadeCapital.Trading.Application.Abstractions.ICoachingPromptRepository,
                  JadeCapital.Trading.Infrastructure.Audit.CoachingPromptAuditDecorator>();

        // Slice 5c.1 — AI risk advisor (Scoped — uses IAIProvider + DbContext).
        services.AddScoped<JadeCapital.Trading.Application.Ai.IAIRiskAdvisor,
                  JadeCapital.Trading.Infrastructure.Ai.OllamaAIRiskAdvisor>();
        services.AddScoped<JadeCapital.Trading.Application.Abstractions.IAIRiskAdviceRepository,
                  JadeCapital.Trading.Infrastructure.Persistence.AIRiskAdviceRepository>();
        // Wave 9 slice 9a.1 — typed audit decorator over IAIRiskAdviceRepository.
        // Sub-scope A: BESPOKE WRITE-ONCE — only AddAsync wraps (the entity is
        // immutable after Create per the docstring; no UpdateAsync or DeleteAsync
        // on the interface). Cross-tenant IsOwner check on AIRiskAdvice.UserId;
        // the surface mirrors the Wave 8 8b.1 StripeCustomer precedent (the
        // OllamaAIRiskAdvisor + GetPreTradeAdviceHandler accept userId as a
        // command parameter, so handler-side consistency is not guaranteed).
        // The 1 read (FindByUserAndTradeAsync) is forwarded without audit.
        services.Decorate<JadeCapital.Trading.Application.Abstractions.IAIRiskAdviceRepository,
                  JadeCapital.Trading.Infrastructure.Audit.AIRiskAdviceAuditDecorator>();

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

        // ===== Slice 10.5 — GDPR Art. 17 cascade deletor (Trading side) =====
        // Auto-collected by the Identity-side orchestrator as
        // IEnumerable<IUserCascadeDeletor>. Purges 13 trading aggregates.
        services.AddScoped<JadeCapital.Identity.Application.Abstractions.IUserCascadeDeletor,
            JadeCapital.Trading.Infrastructure.Cascade.TradingUserCascadeDeletor>();

return services;
    }
}
