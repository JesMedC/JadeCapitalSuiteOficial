using JadeCapital.Trading.Domain.Ai;

namespace JadeCapital.Trading.Application.Abstractions;

// ============================================================================
//  IAIRiskAdviceRepository — slice 5c.1 (Wave 5, AI Risk Advisor Pre-Trade).
//
//  Persistence abstraction for the AIRiskAdvice aggregate. EF Core
//  implementation lives in Trading.Infrastructure.Persistence.
//
//  Cross-user isolation lives in the handlers (the GET /api/ai/risk-advice/{tradeId}
//  filters by userId from the JWT claim; rows owned by another user are
//  simply not returned). The repository contract is single-row / single-user
//  — multi-user queries are a handler concern.
// ============================================================================

public interface IAIRiskAdviceRepository
{
    /// <summary>Append. The aggregate owns the id; do NOT pre-assign.</summary>
    Task AddAsync(AIRiskAdvice advice, CancellationToken ct);

    /// <summary>
    /// Returns the most-recent AI advisory for the given user + tradeId
    /// combo. Used by GET /api/ai/risk-advice/{tradeId} to fetch the cached
    /// advisory that was attached at OpenTrade time. Returns null when no
    /// advisory exists (pre-Wave-5 trade, or trade opened without a
    /// checklist).
    /// </summary>
    Task<AIRiskAdvice?> FindByUserAndTradeAsync(
        Guid userId,
        Guid tradeId,
        CancellationToken ct);
}
