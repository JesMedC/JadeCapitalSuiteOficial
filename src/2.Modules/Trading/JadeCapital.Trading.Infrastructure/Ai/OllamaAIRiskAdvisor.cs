using JadeCapital.Shared.Kernel.Ai;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Ai;
using JadeCapital.Trading.Domain.Ai;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Infrastructure.Ai;

// ============================================================================
//  OllamaAIRiskAdvisor — slice 5c.1 (Wave 5, AI Risk Advisor Pre-Trade).
//
//  Infrastructure implementation of <see cref="IAIRiskAdvisor"/>. Talks to
//  the configured <see cref="IAIProvider"/> (OllamaHttpClient in Wave 5b.1)
//  with a 5-second per-call timeout — independent of the provider's own
//  30s default because the advisor runs on the OpenTrade critical path.
//
//  <para>
//  Pipeline:
//  <list type="number">
//    <item>Compose the prompt via <c>AIRiskAdvisorPrompt.Render(req, ctx)</c>.</item>
//    <item>Invoke IAIProvider with a linked CTS that has a 5s cap.</item>
//    <item>Parse the response via <c>AIRiskAdvisorResponseParser.Parse</c>.</item>
//    <item>Build the AIRiskAdvice aggregate via the factory.</item>
//    <item>Persist via IAIRiskAdviceRepository + IUnitOfWork.</item>
//  </list>
//  </para>
//
//  <para>
//  Failure semantics:
//  <list type="bullet">
//    <item>Provider Failure → result is Failure; no row persisted.</item>
//    <item>Provider Success with malformed JSON → parser falls back to Allow (per spec); row persisted with Allow.</item>
//    <item>Aggregate factory Failure → no row persisted (defense in depth; parser should always produce a valid action).</item>
//  </list>
//  </para>
// ============================================================================

public sealed class OllamaAIRiskAdvisor : IAIRiskAdvisor
{
    /// <summary>Per-call timeout. Independent of the IAIProvider's 30s default.</summary>
    private static readonly TimeSpan AdvisoryTimeout = TimeSpan.FromSeconds(5);

    private const int DefaultWindowDays = 7;

    private readonly IUserTradingContextProvider _contextProvider;
    private readonly IAIProvider _provider;
    private readonly IAIRiskAdviceRepository _advices;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<OllamaAIRiskAdvisor> _logger;

    public OllamaAIRiskAdvisor(
        IUserTradingContextProvider contextProvider,
        IAIProvider provider,
        IAIRiskAdviceRepository advices,
        IUnitOfWork uow,
        IClock clock,
        ILogger<OllamaAIRiskAdvisor> logger)
    {
        _contextProvider = contextProvider;
        _provider = provider;
        _advices = advices;
        _uow = uow;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<AIRiskAdvice>> AdviseAsync(
        AIRiskAdviceRequest request,
        CancellationToken ct = default)
    {
        // 1. Compose the user trading context (PII-safe aggregates).
        var context = await _contextProvider.GetUserContextAsync(
            request.UserId, DefaultWindowDays, ct);

        var prompt = AIRiskAdvisorPrompt.Render(request, context);

        // 2. Enforce the 5s critical-path timeout via a linked CTS.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(AdvisoryTimeout);

        var providerResult = await _provider.GenerateAsync(
            new PromptRequest(
                User: prompt,
                System: null,
                MaxTokens: AIRiskAdvisorPrompt.AdvisoryMaxTokens,
                Temperature: AIRiskAdvisorPrompt.AdvisoryTemperature),
            cts.Token);

        if (providerResult.IsFailure)
        {
            _logger.LogWarning(
                "AI provider failed for advisor (user {UserId}, trade {TradeId}): {Error}",
                request.UserId, request.TradeId, providerResult.Error.Code);
            return Result.Failure<AIRiskAdvice>(providerResult.Error);
        }

        // 3. Parse the response — never throws; safe defaults on bad JSON.
        var (action, reason) = AIRiskAdvisorResponseParser.Parse(providerResult.Value.Text);

        // 4. Build the aggregate.
        var contextJson = SerializeContextJson(context);

        var aggregateResult = AIRiskAdvice.Create(
            userId: request.UserId,
            tradeId: request.TradeId,
            contextJson: contextJson,
            response: providerResult.Value,
            reason: reason,
            parsedAction: action,
            clock: _clock);

        if (aggregateResult.IsFailure)
        {
            _logger.LogError(
                "Failed to construct AIRiskAdvice (user {UserId}, trade {TradeId}): {Error}. No row persisted.",
                request.UserId, request.TradeId, aggregateResult.Error.Code);
            return Result.Failure<AIRiskAdvice>(aggregateResult.Error);
        }

        // 5. Persist.
        await _advices.AddAsync(aggregateResult.Value, ct);
        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
        {
            _logger.LogWarning(
                "SaveChanges failed for AIRiskAdvice (user {UserId}, trade {TradeId}): {Error}",
                request.UserId, request.TradeId, saved.Error.Code);
            return Result.Failure<AIRiskAdvice>(saved.Error);
        }

        return Result.Success(aggregateResult.Value);
    }

    private static string SerializeContextJson(UserTradingContext ctx)
    {
        // PII-safe: only the aggregate fields declared on UserTradingContext.
        // The provider response is stored as raw Ollama text on the
        // aggregate; the context JSON is the user-side input that fed the
        // prompt (recorded for transparency / debugging).
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            user_id = ctx.UserId,
            window_days = ctx.WindowDays,
            closed_trades = ctx.ClosedTradeCount,
            winners = ctx.Winners,
            losers = ctx.Losers,
            win_rate = ctx.WinRate,
            avg_rr = ctx.AverageRiskReward,
            instruments_traded = ctx.InstrumentsTraded,
            violations = ctx.Violations,
        });
    }
}
