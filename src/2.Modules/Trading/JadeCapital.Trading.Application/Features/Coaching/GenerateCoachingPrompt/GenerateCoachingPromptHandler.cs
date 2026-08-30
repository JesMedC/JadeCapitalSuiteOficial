using JadeCapital.Shared.Kernel.Ai;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Ai;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.Coaching.GenerateCoachingPrompt;

// ============================================================================
//  GenerateCoachingPromptCommand — slice 5b.2 (Wave 5, AI Coaching).
//
//  Drives the AI coaching prompt pipeline for a single user. Called by:
//   - <c>CoachingPromptService</c> BG service (one invocation per active user
//     at 03:00 UTC ± 30 min).
//   - Manual trigger endpoint (slice 5b.2 added POST /api/coaching/prompts/generate).
//
//  Result semantics:
//   - Success: Result<int> with the count of prompts CREATED (0 on the
//     "already generated today" idempotency short-circuit + the
//     "user has no trades" short-circuit, 1 on the normal happy path).
//   - Failure: bubbles up the AIProvider failure code unchanged so the
//     caller can log + skip (BG service) or surface to the user (manual).
// ============================================================================

public sealed record GenerateCoachingPromptCommand(Guid UserId) : IRequest<Result<int>>;

// ============================================================================
//  GenerateCoachingPromptHandler — orchestrator. Composes the prompt template
//  from the user's trading context, calls IAIProvider, persists the result.
//  Idempotent within a UTC calendar day — a second invocation for the same
//  user on the same day returns 0 without calling the provider.
// ============================================================================

public class GenerateCoachingPromptHandler
    : IRequestHandler<GenerateCoachingPromptCommand, Result<int>>
{
    private const int DefaultWindowDays = 7;

    private readonly IUserTradingContextProvider _contextProvider;
    private readonly IAIProvider _aiProvider;
    private readonly ICoachingPromptRepository _prompts;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<GenerateCoachingPromptHandler> _logger;

    public GenerateCoachingPromptHandler(
        IUserTradingContextProvider contextProvider,
        IAIProvider aiProvider,
        ICoachingPromptRepository prompts,
        IUnitOfWork uow,
        IClock clock,
        ILogger<GenerateCoachingPromptHandler> logger)
    {
        _contextProvider = contextProvider;
        _aiProvider = aiProvider;
        _prompts = prompts;
        _uow = uow;
        _clock = clock;
        _logger = logger;
    }

    public virtual async Task<Result<int>> Handle(GenerateCoachingPromptCommand req, CancellationToken ct)
    {
        // 1. Idempotency — skip if we already generated one today for this user.
        var today = _clock.UtcNow;
        var existing = await _prompts.FindByUserAndDateAsync(req.UserId, today, ct);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Skipping AI coaching prompt for user {UserId} — already generated today ({CreatedAt}).",
                req.UserId, existing.CreatedAt);
            return Result.Success(0);
        }

        // 2. Compose context.
        var context = await _contextProvider.GetUserContextAsync(req.UserId, DefaultWindowDays, ct);
        if (context.HasNoTrades)
        {
            _logger.LogInformation(
                "Skipping AI coaching prompt for user {UserId} — 0 closed trades in last {Days}d.",
                req.UserId, context.WindowDays);
            return Result.Success(0);
        }

        var promptText = CoachingPromptTemplate.Render(context);

        // 3. Severity: derived from violations count + win-rate.
        var severity = ResolveSeverity(context);

        // 4. Call AI.
        var aiResult = await _aiProvider.GenerateAsync(
            new PromptRequest(
                User: promptText,
                System: null,
                MaxTokens: 512,
                Temperature: 0.3m),
            ct);

        if (aiResult.IsFailure)
        {
            _logger.LogWarning(
                "AI provider failed for user {UserId}: {Error}. No row persisted.",
                req.UserId, aiResult.Error.Code);
            return Result.Failure<int>(aiResult.Error);
        }

        // 5. Persist aggregate (latency is the wall-clock Duration of the AI call).
        var aggregateResult = Trading.Domain.Ai.CoachingPrompt.Create(
            userId: req.UserId,
            promptText: promptText,
            contextJson: SerializeContextJson(context),
            response: aiResult.Value,
            severity: severity,
            clock: _clock);

        if (aggregateResult.IsFailure)
        {
            _logger.LogError(
                "Failed to construct CoachingPrompt for user {UserId}: {Error}. No row persisted.",
                req.UserId, aggregateResult.Error.Code);
            return Result.Failure<int>(aggregateResult.Error);
        }

        await _prompts.AddAsync(aggregateResult.Value, ct);
        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
        {
            _logger.LogWarning(
                "SaveChanges failed for CoachingPrompt (user {UserId}): {Error}.",
                req.UserId, saved.Error.Code);
            return Result.Failure<int>(saved.Error);
        }

        return Result.Success(1);
    }

    private static Trading.Domain.Ai.CoachingPromptSeverity ResolveSeverity(UserTradingContext ctx)
    {
        // Heuristic — derived from Wave 2b rule triggers + win-rate. Used for
        // the FE's color/badge; never for blocking trades.
        if (ctx.Violations.Count >= 3 || ctx.WinRate < 0.4m)
            return Trading.Domain.Ai.CoachingPromptSeverity.High;

        if (ctx.Violations.Count >= 1 || ctx.WinRate < 0.6m)
            return Trading.Domain.Ai.CoachingPromptSeverity.Medium;

        return Trading.Domain.Ai.CoachingPromptSeverity.Low;
    }

    private static string SerializeContextJson(UserTradingContext ctx)
    {
        // PII-safe: only the aggregate fields declared on UserTradingContext.
        // The provider response is JSONB-stored as raw Ollama body — see the
        // aggregate's ProviderResponseText which is the wire Text already.
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