using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Ai;
using JadeCapital.Trading.Application.Features.Coaching.GenerateCoachingPrompt;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Infrastructure.BackgroundServices;

// ============================================================================
//  CoachingPromptService — slice 5b.2 (Wave 5, AI Coaching).
//
//  Hosted service that wakes daily at 03:00 UTC ± 30 min jitter and
//  generates one AI coaching prompt per active user (≥ 5 closed trades in
//  the last 7 days, capped to 100 users per tick per the spec).
//
//  <para>
//  Schedule (per spec scenario "Initial delay computation"):
//   - First run: at the next 03:00 UTC + random jitter in [0, +30 min].
//   - Subsequent runs: every 24h + random jitter in [0, +30 min].
//  </para>
//
//  <para>
//  Failure isolation:
//   - Per-user exception is caught inside <see cref="RunOnceAsync"/>; the
//     loop continues so one user's failure doesn't abort the others.
//   - The outer try/catch in <see cref="ExecuteAsync"/> covers unexpected
//     top-level errors (e.g. IUserTradingContextProvider throwing) so the
//     host NEVER crashes.
//  </para>
//
//  <para>
//  Testing seam: <see cref="RunOnceAsync"/> is public so unit tests drive
//  the loop deterministically without waiting for the daily tick.
//  </para>
// ============================================================================

public sealed class CoachingPromptService : BackgroundService
{
    /// <summary>Target run time per UTC day (03:00).</summary>
    public static readonly TimeSpan TargetRunTime = TimeSpan.FromHours(3);

    /// <summary>Maximum jitter added to the initial delay + each period.</summary>
    public static readonly TimeSpan MaxJitter = TimeSpan.FromMinutes(30);

    /// <summary>Re-tick period — 24h (spec).</summary>
    public static readonly TimeSpan Period = TimeSpan.FromHours(24);

    /// <summary>Default active-user threshold (≥ 5 closed trades in window).</summary>
    public const int DefaultMinClosedTrades = 5;

    /// <summary>Default window in days for active-user threshold.</summary>
    public const int DefaultWindowDays = 7;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CoachingPromptService> _logger;
    private readonly TimeProvider _timeProvider;

    public CoachingPromptService(
        IServiceScopeFactory scopeFactory,
        ILogger<CoachingPromptService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "CoachingPromptService started. Schedule=daily {Target} UTC ± {Jitter} min jitter.",
            TargetRunTime, MaxJitter.TotalMinutes);

        var initialDelay = ComputeInitialDelay(_timeProvider.GetUtcNow(), TargetRunTime, MaxJitter);
        _logger.LogInformation("CoachingPromptService initial delay = {Delay}.", initialDelay);

        try
        {
            await Task.Delay(initialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("CoachingPromptService cancelled during initial delay.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var count = await RunOnceAsync(stoppingToken);
                _logger.LogInformation("CoachingPromptService tick complete. Prompts generated={Count}.", count);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Top-level guard: nothing escapes. Log and continue.
                _logger.LogError(ex, "Coaching prompt tick failed; continuing.");
            }

            var nextDelay = Period + Randomize(MaxJitter);
            try
            {
                await Task.Delay(nextDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("CoachingPromptService stopped.");
    }

    /// <summary>
    /// Drives one tick: list active users, run the handler for each.
    /// Per-user exceptions are caught and logged so the loop continues.
    /// Returns the number of prompts successfully generated.
    /// Public so unit tests can drive the loop deterministically.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var ctxProvider = scope.ServiceProvider.GetRequiredService<IUserTradingContextProvider>();
        var handler = scope.ServiceProvider.GetRequiredService<GenerateCoachingPromptHandler>();

        IReadOnlyList<Guid> userIds;
        try
        {
            userIds = await ctxProvider.GetActiveUserIdsWithMinTradesAsync(
                DefaultMinClosedTrades,
                DefaultWindowDays,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list active users for AI coaching tick.");
            return 0;
        }

        if (userIds.Count == 0)
        {
            _logger.LogDebug("No active users with >= {Threshold} closed trades in last {Days}d.",
                DefaultMinClosedTrades, DefaultWindowDays);
            return 0;
        }

        var generated = 0;
        foreach (var userId in userIds)
        {
            try
            {
                var result = await handler.Handle(new GenerateCoachingPromptCommand(userId), ct);
                if (result.IsSuccess && result.Value > 0)
                {
                    generated++;
                }
                else if (result.IsFailure)
                {
                    _logger.LogWarning(
                        "AI coaching prompt failed for user {UserId}: {Error}. Continuing.",
                        userId, result.Error.Code);
                }
            }
            catch (Exception ex)
            {
                // Defense-in-depth: handler is supposed to map everything to
                // Result.Failure, but a stray exception (e.g. DI misconfig)
                // must not abort the loop.
                _logger.LogWarning(ex, "Coaching prompt handler threw for user {UserId}; continuing.", userId);
            }
        }

        return generated;
    }

    /// <summary>
    /// Initial delay until the next 03:00 UTC ± jitter. Computed deterministically
    /// so the test can drive it via a <see cref="TimeProvider"/>.
    /// </summary>
    public static TimeSpan ComputeInitialDelay(DateTimeOffset now, TimeSpan target, TimeSpan maxJitter)
    {
        var todayTarget = new DateTimeOffset(now.Date, TimeSpan.Zero).Add(target);
        var targetTime = todayTarget > now ? todayTarget : todayTarget.AddDays(1);
        return (targetTime - now) + Randomize(maxJitter);
    }

    private static TimeSpan Randomize(TimeSpan span) =>
        TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * span.TotalMilliseconds);
}