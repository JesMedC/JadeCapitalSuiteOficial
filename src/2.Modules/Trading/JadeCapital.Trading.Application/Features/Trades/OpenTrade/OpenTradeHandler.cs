using JadeCapital.Identity.Contracts.Projections;
using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Shared.Kernel.Validation;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
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
/// 4) Llama a <see cref="Trade.Open"/> factory del dominio que valida
///    invariantes (AccountId/InstrumentId no vacios, currency match, etc.).
/// 5) Persiste trade + checklist en una sola UoW.
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
    private readonly ILogger<OpenTradeHandler> _logger;

    public OpenTradeHandler(
        ITradeRepository trades,
        IAccountRepository accounts,
        IInstrumentRepository instruments,
        IPreTradeChecklistRepository checklists,
        IUnitOfWork uow,
        IClock clock,
        IIdentityUserRiskProfileReader riskProfileReader,
        ILogger<OpenTradeHandler> logger)
    {
        _trades = trades;
        _accounts = accounts;
        _instruments = instruments;
        _checklists = checklists;
        _uow = uow;
        _clock = clock;
        _riskProfileReader = riskProfileReader;
        _logger = logger;
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
}
