using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Contracts.AiRiskAdvisor;
using JadeCapital.Trading.Domain.Ai;
using MediatR;

namespace JadeCapital.Trading.Application.Features.AiRiskAdvisor.GetCachedRiskAdvice;

// ============================================================================
//  GetCachedRiskAdvice — slice 5c.1 (Wave 5, AI Risk Advisor Pre-Trade).
//
//  Serves GET /api/ai/risk-advice/{tradeId}. Reads the AIRiskAdvice row
//  pinned to the (userId, tradeId) combo. Returns 404 when no advisory
//  exists (pre-Wave-5 trade, or trade opened without a checklist).
//
//  Cross-user isolation: the endpoint filters by userId from the JWT claim.
//  A user can NEVER read another user's advisory (the repository is
//  guarded by the WHERE clause).
// ============================================================================

public sealed record GetCachedRiskAdviceQuery(
    Guid UserId,
    Guid TradeId) : IRequest<Result<AIRiskAdviceDto>>;

public sealed class GetCachedRiskAdviceHandler
    : IRequestHandler<GetCachedRiskAdviceQuery, Result<AIRiskAdviceDto>>
{
    private readonly IAIRiskAdviceRepository _advices;

    public GetCachedRiskAdviceHandler(IAIRiskAdviceRepository advices)
    {
        _advices = advices;
    }

    public async Task<Result<AIRiskAdviceDto>> Handle(
        GetCachedRiskAdviceQuery query,
        CancellationToken ct)
    {
        var advice = await _advices.FindByUserAndTradeAsync(
            query.UserId, query.TradeId, ct);

        if (advice is null)
        {
            return Result.Failure<AIRiskAdviceDto>(AIRiskAdviceErrors.Errors.NotFound);
        }

        var dto = new AIRiskAdviceDto(
            Id: advice.Id,
            UserId: advice.UserId,
            TradeId: advice.TradeId,
            Action: advice.ParsedAction.ToString().ToLowerInvariant(),
            Reason: advice.Reason,
            Model: advice.Model,
            LatencyMs: advice.LatencyMs,
            CreatedAt: advice.CreatedAt);

        return Result.Success(dto);
    }
}
