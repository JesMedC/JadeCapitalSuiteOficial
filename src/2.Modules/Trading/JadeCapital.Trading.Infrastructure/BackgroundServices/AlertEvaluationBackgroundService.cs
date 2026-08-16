using JadeCapital.Trading.Application.Alerts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Infrastructure.BackgroundServices;

// ============================================================================
//  AlertEvaluationBackgroundService — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Hosted service that periodically evaluates every active user's alerts.
//
//  Schedule (per spec requirement #4):
//   - First run: immediately at startup (so a fresh deploy doesn't wait 5 min).
//   - Subsequent runs: every 5 minutes + random jitter [0, +30s] to
//     avoid synchronized ticks across multiple replicas.
//
//  Failure isolation: the per-user exception is caught inside
//  AlertEvaluationService.EvaluateForUserAsync. The outer try/catch here
//  covers unexpected top-level errors (e.g. IActiveUserIdsReader failing)
//  so the host NEVER crashes (per spec scenario "Service survives transient errors").
//
//  Cancellation: ExecuteAsync exits cleanly when the host stops; the
//  PeriodicTimer honors the stopping token.
// ============================================================================

public sealed class AlertEvaluationBackgroundService : BackgroundService
{
    private const int BaseIntervalMinutes = 5;
    private const int MaxJitterSeconds = 30;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AlertEvaluationBackgroundService> _logger;

    public AlertEvaluationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<AlertEvaluationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AlertEvaluationBackgroundService started. Interval={Minutes}m +/- {Jitter}s jitter.",
            BaseIntervalMinutes, MaxJitterSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Top-level guard: nothing escapes. Log and continue.
                _logger.LogError(ex, "Alert evaluation tick failed; continuing.");
            }

            // Wait for the next tick with jitter.
            var delay = ComputeDelay();
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("AlertEvaluationBackgroundService stopped.");
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<AlertEvaluationService>();
        var users = scope.ServiceProvider.GetRequiredService<JadeCapital.Identity.Contracts.Projections.IActiveUserIdsReader>();

        var inserted = await evaluator.EvaluateAllActiveUsersAsync(users, ct);
        _logger.LogDebug("Alert evaluation tick complete. Inserted={Inserted}.", inserted);
    }

    private static TimeSpan ComputeDelay()
    {
        var jitterSeconds = Random.Shared.Next(0, MaxJitterSeconds + 1);
        return TimeSpan.FromMinutes(BaseIntervalMinutes) + TimeSpan.FromSeconds(jitterSeconds);
    }
}