using JadeCapital.Shared.Kernel.Ai;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Ai;
using JadeCapital.Trading.Contracts.AiRiskAdvisor;
using JadeCapital.Trading.Domain.Ai;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.AiRiskAdvisor.GetPreTradeAdvice;

// ============================================================================
//  GetPreTradeAdvice — slice 5c.1 (Wave 5, AI Risk Advisor Pre-Trade).
//
//  Application handler that drives the AI risk advisor pipeline. Composes
//  the user's recent trading context, invokes the IAIRiskAdvisor (the
//  Infrastructure impl is the 5s-timeout OllamaAIRiskAdvisor), parses the
//  response, and persists the AIRiskAdvice aggregate.
//
//  <para>
//  Drives two callers:
//  <list type="bullet">
//    <item>POST /api/ai/risk-advice — manual advisory request (no TradeId).</item>
//    <item>OpenTradeHandler — auto-advisory at trade-open time (TradeId set).</item>
//  </list>
//
//  Both paths share the same handler; the only difference is the TradeId
//  nullability (manual = null, OpenTrade = populated).
//  </para>
//
//  <para>
//  Result semantics:
//  <list type="bullet">
//    <item>Success → Result&lt;AIRiskAdviceDto&gt; (the freshly-persisted advisory).</item>
//    <item>Failure → AIProvider failure code bubbles up unchanged; no row persisted.</item>
//  </list>
//  </para>
// ============================================================================

public sealed record GetPreTradeAdviceQuery(
    AIRiskAdviceRequest Request) : IRequest<Result<AIRiskAdviceDto>>;

public class GetPreTradeAdviceHandler
    : IRequestHandler<GetPreTradeAdviceQuery, Result<AIRiskAdviceDto>>
{
    private const int DefaultWindowDays = 7;

    private readonly IUserTradingContextProvider _contextProvider;
    private readonly IAIRiskAdvisor _advisor;
    private readonly IAIRiskAdviceRepository _advices;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<GetPreTradeAdviceHandler> _logger;

    public GetPreTradeAdviceHandler(
        IUserTradingContextProvider contextProvider,
        IAIRiskAdvisor advisor,
        IAIRiskAdviceRepository advices,
        IUnitOfWork uow,
        IClock clock,
        ILogger<GetPreTradeAdviceHandler> logger)
    {
        _contextProvider = contextProvider;
        _advisor = advisor;
        _advices = advices;
        _uow = uow;
        _clock = clock;
        _logger = logger;
    }

    public virtual async Task<Result<AIRiskAdviceDto>> Handle(
        GetPreTradeAdviceQuery query,
        CancellationToken ct)
    {
        var req = query.Request;

        // 1. Compose the user's recent trading context (PII-safe aggregates).
        var context = await _contextProvider.GetUserContextAsync(req.UserId, DefaultWindowDays, ct);

        // 2. Invoke the advisor (5s timeout applied internally by the Ollama impl).
        var advisorResult = await _advisor.AdviseAsync(req, ct);

        if (advisorResult.IsFailure)
        {
            _logger.LogWarning(
                "AI risk advisor failed for user {UserId} (trade {TradeId}): {Error}. No row persisted.",
                req.UserId, req.TradeId, advisorResult.Error.Code);
            return Result.Failure<AIRiskAdviceDto>(advisorResult.Error);
        }

        // 3. The advisor already persists the AIRiskAdvice aggregate. We
        //    re-read here ONLY to surface the persisted CreatedAt + Id
        //    through the DTO. (Cheaper alternative: have the advisor return
        //    the persisted entity directly; future slice may refactor.)
        var persisted = advisorResult.Value;

        var dto = new AIRiskAdviceDto(
            Id: persisted.Id,
            UserId: persisted.UserId,
            TradeId: persisted.TradeId,
            Action: persisted.ParsedAction.ToString().ToLowerInvariant(),
            Reason: persisted.Reason,
            Model: persisted.Model,
            LatencyMs: persisted.LatencyMs,
            CreatedAt: persisted.CreatedAt);

        return Result.Success(dto);
    }
}
