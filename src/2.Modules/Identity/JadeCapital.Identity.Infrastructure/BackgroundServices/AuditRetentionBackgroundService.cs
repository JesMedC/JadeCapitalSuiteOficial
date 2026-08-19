using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Audit.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JadeCapital.Identity.Infrastructure.BackgroundServices;

/// <summary>
/// Background service that drives <see cref="IAuditRetentionService"/>
/// on a schedule (Wave 9, slice 9b.1 — audit retention, sub-scope C).
///
/// <para>
/// <b>Schedule</b>: first run after <see cref="AuditRetentionOptions.InitialDelay"/>
/// (default 2 minutes) to let the rest of the pipeline settle
/// (matches the <c>BackfillTenantsHostedService</c> 15s precedent scaled
/// for a heavier first run). Subsequent runs every
/// <see cref="AuditRetentionOptions.CleanupIntervalHours"/> (default 24h)
/// with a <c>[0, +30min]</c> random jitter to avoid thundering herd
/// across replicas (matches the <c>AttachmentLifecycleService</c>
/// precedent — constant jitter, NOT a fraction of the interval).
/// </para>
///
/// <para>
/// <b>Per-run isolation</b>: every cycle is wrapped in
/// <c>try { ... } catch { LogError + continue }</c> so a transient
/// failure (DB timeout, connection reset) NEVER crashes the host
/// (matches the <c>RefreshTokenCleanupService</c> + <c>BackfillTenantsHostedService</c>
/// precedent). The next cycle still fires on schedule.
/// </para>
///
/// <para>
/// <b>RunOnceAsync</b> is <c>public</c> so unit tests can drive a
/// single cycle deterministically without waiting for the timer (the
/// Wave 4 4d <c>AttachmentLifecycleService.RunOnceAsync</c> precedent).
/// </para>
/// </summary>
public sealed class AuditRetentionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AuditRetentionOptions> _options;
    private readonly ILogger<AuditRetentionBackgroundService> _logger;

    public AuditRetentionBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AuditRetentionOptions> options,
        ILogger<AuditRetentionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue;

        _logger.LogInformation(
            "AuditRetentionBackgroundService started; first run in {Delay}s, interval={Interval}h, batch={Batch}.",
            opts.InitialDelaySeconds, opts.CleanupIntervalHours, opts.BatchLimit);

        // Initial settle — let the host's other services warm up.
        try
        {
            await Task.Delay(opts.InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // First run + cycle loop.
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            if (stoppingToken.IsCancellationRequested) break;

            var next = _options.CurrentValue;
            var intervalWithJitter = ApplyJitter(Random.Shared);

            try
            {
                await Task.Delay(intervalWithJitter, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("AuditRetentionBackgroundService stopped.");
    }

    /// <summary>
    /// Performs a single retention cycle. Public so unit tests can
    /// drive the loop deterministically without waiting for the timer.
    ///
    /// <para>
    /// The method creates its own DI scope (the BackgroundService is
    /// Singleton; <see cref="IAuditRetentionService"/> is Scoped). On
    /// exception: <c>LogError + swallow</c> — the host MUST NOT crash
    /// from a retention failure (matches the
    /// <c>RefreshTokenCleanupService</c> precedent).
    /// </para>
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var retention = scope.ServiceProvider.GetRequiredService<IAuditRetentionService>();
            var options = _options.CurrentValue;

            var cutoff = DateTimeOffset.UtcNow.AddDays(-options.RetentionDays);
            var deleted = await retention.PurgeOldAsync(cutoff, options.BatchLimit, ct);

            if (deleted > 0)
                _logger.LogInformation(
                    "RetentionRun: deleted {Deleted} row(s) older than {Cutoff:o} (batch limit {Limit}).",
                    deleted, cutoff, options.BatchLimit);
            else
                _logger.LogDebug(
                    "RetentionRun: no rows older than {Cutoff:o} (batch limit {Limit}).",
                    cutoff, options.BatchLimit);
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down — let it cancel cleanly.
            throw;
        }
        catch (Exception ex)
        {
            // Defense-in-depth: a retention failure MUST NOT crash the host.
            // Log + continue; the next cycle still fires on schedule.
            _logger.LogError(ex,
                "RetentionRun: failed; will retry on next cycle.");
        }
    }

    /// <summary>
    /// Applies <c>[0, +30min]</c> jitter (CONSTANT, NOT a fraction of
    /// the interval) to the base interval. Per spec + the
    /// <c>AttachmentLifecycleService</c> precedent. Public so unit tests
    /// can pin the jitter to 0 (deterministic timing assertions).
    /// </summary>
    internal static TimeSpan ApplyJitter(Random rng) => TimeSpan.FromMilliseconds(rng.NextDouble() * MaxJitterMs);

    private const double MaxJitterMs = 30d * 60d * 1000d;
}