using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Ai;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Persistence;

// ============================================================================
//  AIRiskAdviceRepository — slice 5c.1 (Wave 5, AI Risk Advisor Pre-Trade).
//
//  EF Core implementation of <see cref="IAIRiskAdviceRepository"/>. The
//  repository NEVER validates invariants — that's the aggregate's job.
//  Rehydration skips validation by design (DB CHECK constraints are the
//  safety net — see <see cref="AIRiskAdvice.Rehydrate"/>).
//
//  Query patterns:
//   - AddAsync: simple Add + SaveChanges (handled by the handler's UoW).
//   - FindByUserAndTradeAsync: WHERE user_id = $1 AND trade_id = $2.
//     Uses the partial index ix_ai_risk_advice_user_trade.
// ============================================================================

public sealed class AIRiskAdviceRepository : IAIRiskAdviceRepository
{
    private readonly TradingDbContext _db;

    public AIRiskAdviceRepository(TradingDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(AIRiskAdvice advice, CancellationToken ct)
    {
        await _db.Set<AIRiskAdvice>().AddAsync(advice, ct);
    }

    public async Task<AIRiskAdvice?> FindByUserAndTradeAsync(
        Guid userId,
        Guid tradeId,
        CancellationToken ct)
    {
        return await _db.Set<AIRiskAdvice>()
            .Where(a => a.UserId == userId && a.TradeId == tradeId)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }
}
