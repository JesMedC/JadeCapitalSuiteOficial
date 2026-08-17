using JadeCapital.Identity.Contracts.Projections;
using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Shared.Kernel.Validation;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Application.Ai;
using JadeCapital.Trading.Domain.Ai;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Trading.Domain.PreTradeChecklists;
using JadeCapital.Trading.Domain.Trades;
using JadeCapital.Trading.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.Trades.OpenTrade;

/// <summary>
/// Abre un trade:
/// 1) Valida formato de Symbol/Currency (FluentValidator ya paso).
/// 2) Construye VOs (Symbol, Money) que revalidan formato y normalizan.
/// 3) Si el comando trae un <c>Checklist</c> opcional:
///    - Resuelve el <c>RiskRewardTargetUsed</c> desde el perfil activo
///      (vía <see cref="IIdentityUserRiskProfileReader"/>), o cae al
///      default 1.0 si no hay perfil.
///    - Construye el <see cref="PreTradeChecklist"/> aggregate.
///    - Si falla (RR &lt; target, confluences fuera de rango, etc.) el
///      handler retorna <c>Result.Failure</c> SIN persistir el trade.
/// 4) [Slice 5c.1] Si el checklist esta presente y la IA esta registrada
///    (<see cref="IAIRiskAdvisor"/> != null), invoca al advisor antes de
///    persistir. Si devuelve <c>Block</c>, el handler retorna
///    <c>Result.Failure(ai_risk.blocked)</c> mapeado a 422. Si devuelve
///    <c>Warning</c>, el advisory se adjunta al checklist.
/// 5) Llama a <see cref="Trade.Open"/> factory del dominio que valida
///    invariantes (AccountId/InstrumentId no vacios, currency match, etc.).
/// 6) Persiste trade + checklist en una sola UoW.
/// </summary>
public sealed class OpenTradeHandler : IRequestHandler<OpenTradeCommand, Result<TradeDto>>
{
    /// <summary>
    /// Default risk/reward target cuando el usuario no tiene perfil de
    /// riesgo activo. 1.0 es el piso que el RiskRewardRatio VO accepta —
    /// significa "no exigimos un RR minimo mayor a 1:1".
    /// </summary>
    private const decimal DefaultRiskRewardTarget = 1.0m;

    private readonly ITradeRepository _trades;
    private readonly IAccountRepository _accounts;
    private readonly IInstrumentRepository _instruments;
    private readonly IPreTradeChecklistRepository _checklists;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly IIdentityUserRiskProfileReader _riskProfileReader;
    private readonly IAIRiskAdvisor? _advisor;
    private readonly ILogger<OpenTradeHandler> _logger;

    public OpenTradeHandler(
        ITradeRepository trades,
        IAccountRepository accounts,
        IInstrumentRepository instruments,
        IPreTradeChecklistRepository checklists,
        IUnitOfWork uow,
        IClock clock,
        IIdentityUserRiskProfileReader riskProfileReader,
        ILogger<OpenTradeHandler> logger,
        IAIRiskAdvisor? advisor = null)
    {
        _trades = trades;
        _accounts = accounts;
        _instruments = instruments;
        _checklists = checklists;
        _uow = uow;
        _clock = clock;
        _riskProfileReader = riskProfileReader;
        _logger = logger;
        _advisor = advisor;
    }

    public async Task<Result<TradeDto>> Handle(OpenTradeCommand req, CancellationToken ct)
    {
        var symbolResult = Symbol.Create(req.Symbol);
        if (symbolResult.IsFailure)
            return Result.Failure<TradeDto>(symbolResult.Error);

        var volumeCurrencyResult = Currency.Create(req.VolumeCurrency);
        if (volumeCurrencyResult.IsFailure)
            return Result.Failure<TradeDto>(volumeCurrencyResult.Error);

        var entryPriceCurrencyResult = Currency.Create(req.EntryPriceCurrency);
        if (entryPriceCurrencyResult.IsFailure)
            return Result.Failure<TradeDto>(entryPriceCurrencyResult.Error);

        var volumeResult = Money.Create(req.Volume, volumeCurrencyResult.Value);
        if (volumeResult.IsFailure)
            return Result.Failure<TradeDto>(volumeResult.Error);

        var entryPriceResult = Money.Create(req.EntryPrice, entryPriceCurrencyResult.Value);
        if (entryPriceResult.IsFailure)
            return Result.Failure<TradeDto>(entryPriceResult.Error);

        // ===== Pre-trade checklist (opcional) =====
        // Si el payload trae un checklist, resolvemos el target y construimos
        // el aggregate ANTES del Trade.Open para que un fallo de checklist
        // NO persista un trade parcialmente.
        PreTradeChecklist? checklist = null;
        if (req.Checklist is not null)
        {
            var targetResult = await ResolveRiskRewardTargetAsync(req, ct);
            if (targetResult.IsFailure)
                return Result.Failure<TradeDto>(targetResult.Error);

            var submission = new PreTradeChecklistSubmission(
                req.Checklist.Emotionality,
                req.Checklist.SetupQuality,
                req.Checklist.RiskRewardAtEntry,
                targetResult.Value,
                req.Checklist.ConfluencesCount);

            var checklistResult = PreTradeChecklist.Create(
                tradeId: Guid.Empty, // placeholder; will be replaced below
                userId: req.UserId,
                submission: submission,
                submittedAt: _clock.UtcNow);

            if (checklistResult.IsFailure)
                return Result.Failure<TradeDto>(checklistResult.Error);

            checklist = checklistResult.Value;
        }

        var tradeId = Guid.NewGuid();
        var now = _clock.UtcNow;

        // Si tenemos checklist, rebuildeamos con el tradeId real (que solo se
        // conoce despues de Guid.NewGuid()). Es un segundo Create contra el
        // mismo payload ya validado, asi que no vuelve a fallar.
        if (checklist is not null)
        {
            var submission = checklist.Submission;
            var rebuiltResult = PreTradeChecklist.Create(
                tradeId: tradeId,
                userId: req.UserId,
                submission: submission,
                submittedAt: now);

            // Defensive: rebuilt no deberia fallar porque el submission ya
            // paso validacion arriba. Pero si el codepath cambia en el
            // futuro, queremos fallar ruidosamente en lugar de silenciar.
            if (rebuiltResult.IsFailure)
                return Result.Failure<TradeDto>(rebuiltResult.Error);

            checklist = rebuiltResult.Value;
        }

        var openResult = Trade.Open(
            tradeId,
            req.AccountId,
            req.InstrumentId,
            req.UserId,
            symbolResult.Value,
            req.AssetClass,
            req.Direction,
            volumeResult.Value,
            entryPriceResult.Value,
            req.VolumeCurrency,
            req.Strategy,
            req.Notes,
            now);

        if (openResult.IsFailure)
            return Result.Failure<TradeDto>(openResult.Error);

        var trade = openResult.Value;

        // ===== [Slice 5c.1] AI risk advisor (optional) =====
        // Only invoked when (a) the request carries a checklist AND (b) the
        // IAIRiskAdvisor is registered. If the advisor is null (legacy DI),
        // the entire block is skipped — the trade opens on the legacy path.
        // If the advisor fails, we log + silently fall back (no block).
        // If the advisor returns Block, we short-circuit with 422.
        if (checklist is not null && _advisor is not null)
        {
            var advisorReq = new AIRiskAdviceRequest(
                UserId: req.UserId,
                TradeId: tradeId,
                TradeSymbol: req.Symbol,
                Direction: req.Direction.ToString(),
                Volume: req.Volume,
                VolumeCurrency: req.VolumeCurrency,
                EntryPrice: req.EntryPrice,
                StopLoss: null,
                RiskRewardAtEntry: checklist.Submission.RiskRewardAtEntry,
                SetupQuality: checklist.Submission.SetupQuality.ToString());

            AIRiskAdvice advice;
            try
            {
                var advisorResult = await _advisor.AdviseAsync(advisorReq, ct);
                if (advisorResult.IsFailure)
                {
                    _logger.LogWarning(
                        "AI risk advisor failed for OpenTrade (user {UserId}, trade {TradeId}): {Error}. " +
                        "Falling back to legacy path (no advisory attached).",
                        req.UserId, tradeId, advisorResult.Error.Code);
                    advice = null!;
                }
                else
                {
                    advice = advisorResult.Value;
                }
            }
            catch (Exception ex)
            {
                // Defense in depth — the IAIProvider contract is no-throw on
                // transient failures, but anything that escapes is caught
                // here so the OpenTrade flow never crashes.
                _logger.LogWarning(ex,
                    "AI risk advisor threw for OpenTrade (user {UserId}, trade {TradeId}). " +
                    "Falling back to legacy path (no advisory attached).",
                    req.UserId, tradeId);
                advice = null!;
            }

            if (advice is not null)
            {
                if (advice.ParsedAction == AIRiskAction.Block)
                {
                    _logger.LogInformation(
                        "AI risk advisor blocked OpenTrade (user {UserId}, trade {TradeId}, reason={Reason}).",
                        req.UserId, tradeId, advice.Reason);
                    return Result.Failure<TradeDto>(AIRiskErrors.Errors.Blocked(advice.Reason));
                }

                // Warning or Allow — attach the advisory to the checklist and
                // continue with the legacy OpenTrade flow.
                var advisoryJson = SerializeAdvice(advice);
                var attachResult = checklist.AttachAIRiskAdvisory(advisoryJson);
                if (attachResult.IsFailure)
                {
                    _logger.LogWarning(
                        "Failed to attach AI advisory to checklist (user {UserId}, trade {TradeId}): {Error}. " +
                        "Continuing without advisory.",
                        req.UserId, tradeId, attachResult.Error.Code);
                }
            }
        }

        await _trades.AddAsync(trade, ct);
        if (checklist is not null)
            await _checklists.AddAsync(checklist, ct);

        var saved = await _uow.SaveChangesAsync(ct);
        DomainGuard.EnsureSuccess(saved);

        _logger.LogInformation(
            "Trade {TradeId} opened for user {UserId} (with checklist: {HasChecklist}).",
            trade.Id, trade.UserId, checklist is not null);

        // Hidratar accountName + instrument para el DTO (evita N+1 en el FE).
        var account = await _accounts.FindByIdAsync(trade.AccountId, ct);
        var instrument = await _instruments.FindByIdAsync(trade.InstrumentId, ct);

        return Result.Success(trade.ToDto(
            accountName: account?.Name,
            instrument: instrument is null ? null : new InstrumentSummaryDto(
                instrument.Symbol.Value,
                instrument.AssetClasses,
                instrument.ContractSize,
                instrument.DecimalPlaces,
                instrument.PipValue,
                instrument.PayoutPercent)));
    }

    /// <summary>
    /// Resuelve el <c>RiskRewardTarget</c> a usar para este checklist:
    /// <list type="number">
    ///   <item>Si el caller envio un target explicito (&gt; 0), usamos ese.</item>
    ///   <item>Si no, leemos el perfil activo del usuario. Si existe,
    ///   usamos su <c>RiskRewardTarget</c>.</item>
    ///   <item>Si no hay perfil activo (lector retorna null), caemos al
    ///   default <see cref="DefaultRiskRewardTarget"/> = 1.0. Esto
    ///   matchea el spec scenario "No active risk profile".</item>
    /// </list>
    /// </summary>
    private async Task<Result<decimal>> ResolveRiskRewardTargetAsync(
        OpenTradeCommand req, CancellationToken ct)
    {
        if (req.Checklist is null)
            return Result.Success(DefaultRiskRewardTarget);

        if (req.Checklist.RiskRewardTargetUsed > 0m)
            return Result.Success(req.Checklist.RiskRewardTargetUsed);

        var snapshot = await _riskProfileReader.GetActiveAsync(req.UserId, ct);
        return Result.Success(snapshot?.RiskRewardTarget ?? DefaultRiskRewardTarget);
    }

    /// <summary>
    /// Serializes the AI risk advisor output into the JSON payload that
    /// persists on <c>pre_trade_checklists.ai_advisory</c>. The shape is
    /// opaque to the domain — the FE renders the fields verbatim.
    /// </summary>
    private static string SerializeAdvice(AIRiskAdvice advice)
    {
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            advice_id = advice.Id,
            action = advice.ParsedAction.ToString().ToLowerInvariant(),
            reason = advice.Reason,
            model = advice.Model,
            latency_ms = advice.LatencyMs,
            created_at = advice.CreatedAt,
        });
    }
}

/// <summary>
/// Error catalog for the AI risk advisor integration in OpenTrade (slice 5c.1).
/// The error code <c>ai_risk.blocked</c> is the wire-stable identifier that
/// the FE matches on to render the override modal.
/// </summary>
public static class AIRiskErrors
{
    public static class Errors
    {
        public static Error Blocked(string reason) =>
            Error.Failure("ai_risk.blocked",
                $"AI risk advisor recommends blocking this trade: {reason}");
    }
}
