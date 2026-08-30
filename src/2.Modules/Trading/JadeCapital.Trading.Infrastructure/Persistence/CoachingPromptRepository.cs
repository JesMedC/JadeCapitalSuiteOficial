using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Ai;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Persistence;

// ============================================================================
//  CoachingPromptRepository — slice 5b.2 (Wave 5).
//
//  EF Core implementation of <see cref="ICoachingPromptRepository"/>.
//  Lives in Infrastructure because the dependency arrow is
//  Infrastructure → Application (the ICoachingPromptRepository abstraction
//  is defined in Application).
//
//  Query patterns:
//   - AddAsync: simple Add + SaveChanges (handled by the handler's UoW).
//   - FindByUserAndDateAsync: WHERE user_id = $1 AND created_at::date = $2
//     — uses the partial index ix_coaching_ai_user_created.
//   - ListByUserAndWindowAsync: WHERE user_id = $1 AND created_at >= $2
//     AND created_at < $3 ORDER BY created_at DESC.
//
//  The repository NEVER validates invariants — that's the aggregate's job.
//  Rehydration skips validation by design (DB CHECK constraints are the
//  safety net — see <see cref="CoachingPrompt.Rehydrate"/>).
// ============================================================================

public sealed class CoachingPromptRepository : ICoachingPromptRepository
{
    private readonly TradingDbContext _db;

    public CoachingPromptRepository(TradingDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(CoachingPrompt prompt, CancellationToken ct)
    {
        await _db.Set<CoachingPrompt>().AddAsync(prompt, ct);
    }

    public async Task<CoachingPrompt?> FindByUserAndDateAsync(
        Guid userId,
        DateTimeOffset dayUtc,
        CancellationToken ct)
    {
        // Match by UTC calendar date. We translate to [dayUtc.Date, dayUtc.Date + 1 day).
        var dayStart = new DateTimeOffset(dayUtc.Date, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);

        return await _db.Set<CoachingPrompt>()
            .Where(p => p.UserId == userId
                && p.CreatedAt >= dayStart
                && p.CreatedAt < dayEnd)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<CoachingPrompt>> ListByUserAndWindowAsync(
        Guid userId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        return await _db.Set<CoachingPrompt>()
            .Where(p => p.UserId == userId
                && p.CreatedAt >= from
                && p.CreatedAt < to)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
    }
}