using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Contracts.Coaching;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Coaching.GetAiCoachingPrompts;

// ============================================================================
//  GetAiCoachingPromptsQuery — slice 5b.2 (Wave 5).
//
//  GET /api/coaching/ai-prompts?period=7d|30d|90d|all
//
//  Reads the user's AI coaching prompts from <c>trading.coaching_prompts_ai</c>
//  in the requested window, sorted by <c>created_at DESC</c>. Cross-user
//  isolation lives at the repository layer (the handler passes <c>UserId</c>
//  from the JWT claim; rows owned by another user are simply not returned).
//
//  Empty history → 200 OK with `{ period, prompts: [] }`. The FE renders
//  an empty state ("We'll generate your first AI prompt overnight — keep
//  trading").
//
//  Window semantics match <see cref="GetCoachingPromptsQuery"/>:
//   - 7d / 30d / 90d → windowStart = now - N days, windowEnd = now.
//   - all            → windowStart = DateTimeOffset.MinValue,
//                       windowEnd = now.
// ============================================================================

public sealed record GetAiCoachingPromptsQuery(
    Guid UserId,
    CoachingPeriod Period) : IRequest<Result<AiCoachingPromptsDto>>;

public sealed class GetAiCoachingPromptsHandler
    : IRequestHandler<GetAiCoachingPromptsQuery, Result<AiCoachingPromptsDto>>
{
    private readonly ICoachingPromptRepository _prompts;
    private readonly IClock _clock;

    public GetAiCoachingPromptsHandler(
        ICoachingPromptRepository prompts,
        IClock clock)
    {
        _prompts = prompts;
        _clock = clock;
    }

    public async Task<Result<AiCoachingPromptsDto>> Handle(
        GetAiCoachingPromptsQuery req,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var windowStart = req.Period switch
        {
            CoachingPeriod.All   => DateTimeOffset.MinValue,
            CoachingPeriod.Days7  => now.AddDays(-7),
            CoachingPeriod.Days30 => now.AddDays(-30),
            CoachingPeriod.Days90 => now.AddDays(-90),
            _                    => now.AddDays(-30),
        };

        var prompts = await _prompts.ListByUserAndWindowAsync(req.UserId, windowStart, now, ct);

        var dtos = prompts.Select(CoachingMapping.ToDto).ToList();

        var periodString = req.Period switch
        {
            CoachingPeriod.Days7 => "7d",
            CoachingPeriod.Days30 => "30d",
            CoachingPeriod.Days90 => "90d",
            CoachingPeriod.All => "all",
            _ => "30d",
        };

        return Result.Success(new AiCoachingPromptsDto(periodString, dtos));
    }
}