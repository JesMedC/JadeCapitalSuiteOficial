using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Identity.Infrastructure.MultiTenancy;

/// <summary>
/// Hosted service that runs <see cref="BackfillTenantsRunner"/> once on
/// startup (Wave 6, slice 6c.2).
///
/// <para>
/// <b>Schedule</b>: 15s after host start. The delay lets the rest of the
/// pipeline (database connection pool warm-up, migration runner, SignalR,
/// etc.) settle before throwing backfill SQL at the same DB. If the
/// application restarts on a schedule (deployment, crash) the delay does
/// not block readiness — the WebApplication is already serving requests
/// while the background service warms up.
/// </para>
///
/// <para>
/// <b>Retry on next startup</b>: a transient failure (DB timeout,
/// connection reset) is swallowed and logged at <c>Warning</c>. The
/// service does not retry in-loop; the next process restart triggers
/// another attempt. This matches the Wave-4 precedent
/// (<c>RefreshTokenCleanupService</c>): never let background work bring
/// down the application.
/// </para>
///
/// <para>
/// <b>Failure isolation</b>: a non-recoverable exception (corrupt data,
/// missing column) is also caught and logged at <c>Error</c>. We do NOT
/// want a broken backfill to crash the WebApplication at boot.
/// </para>
///
/// <para>
/// <b>Why is this not part of the SQL migration 0026?</b>
/// The migration only handles green-field deploys where the backfill
/// runs once at migration time. An in-place upgrade of a pre-Wave-6
/// database MUST assign Personal tenants to the existing NULL rows
/// AFTER the column is already populated; that's this hosted service's
/// job. The migration + the service are idempotent against each other
/// (each can run, neither breaks the other's state).
/// </para>
/// </summary>
public sealed class BackfillTenantsHostedService : BackgroundService
{
    /// <summary>Delay before the first run. Allows the host's other services to settle.</summary>
    public static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackfillTenantsHostedService> _logger;

    public BackfillTenantsHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<BackfillTenantsHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "BackfillTenantsHostedService started; first run in {Delay}s.",
            InitialDelay.TotalSeconds);

        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return; // host is shutting down before the first run — fine.
        }

        await RunOnceAsync(stoppingToken);
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<IBackfillTenantsRunner>();
            var updated = await runner.RunAsync(ct);
            _logger.LogInformation(
                "BackfillTenantsHostedService: run completed. Rows updated = {Updated}.",
                updated);
        }
        catch (OperationCanceledException)
        {
            throw; // host is shutting down — let it cancel cleanly.
        }
        catch (Exception ex)
        {
            // Same isolation contract as RefreshTokenCleanupService:
            // log + continue. The service does NOT crash the host;
            // the next startup will retry.
            _logger.LogError(ex,
                "BackfillTenantsHostedService: run failed; will retry on next startup.");
        }
    }
}
