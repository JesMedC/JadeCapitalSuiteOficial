using JadeCapital.Identity.Domain.Users;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Cascade;
using JadeCapital.Identity.Infrastructure.Configuration;
using JadeCapital.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JadeCapital.Identity.Infrastructure.BackgroundServices;

/// <summary>
/// Daily sweep that hard-deletes users whose 30-day grace period has
/// elapsed (Wave 10, slice 10.5 — GDPR Art. 17).
///
/// <para>
/// Wave 11 slice 11.3 — configuration extraction. The Wave 10.5
/// constants (2-minute initial delay, 24-hour interval, 30-minute
/// jitter, 30-day grace) are now sourced from
/// <see cref="HardDeleteSweepOptions"/> via <see cref="IOptionsMonitor{T}"/>
/// so per-environment tuning is an appsettings change instead of a
/// recompile (Wave 10 W-04 closure). Default values in the options
/// class reproduce the Wave 10.5 behavior verbatim.
///
/// </para>
///
/// <para>
/// Pattern mirrors Wave 9 slice 9b.1 <c>AuditRetentionBackgroundService</c>:
/// <list type="bullet">
///   <item>Per-cycle scope via <see cref="IServiceScopeFactory"/>.</item>
///   <item>Initial delay read from options; defaults to 2 minutes
///         (matches the Wave 10.5 + 9b.1 + Wave 6 6c.2 precedent).</item>
///   <item>Constant <c>[0, +jitterMinutes]</c> jitter per cycle — NOT a
///         fraction of interval (per <c>AttachmentLifecycleService</c>
///         precedent).</item>
///   <item>Per-cycle <c>try { ... } catch { LogError + continue }</c>:
///         the host MUST NEVER crash from a retention / cascade failure.</item>
///   <item>Per-cycle <c>_options.CurrentValue</c> read so config
///         reload via <see cref="IOptionsMonitor{T}.OnChange"/> takes
///         effect on the next cycle (no restart required).</item>
/// </list>
/// </para>
///
/// <para>
/// <see cref="RunOnceAsync"/> is <c>public</c> so xUnit tests can drive
/// a single cycle deterministically without waiting for the timer.
/// </para>
/// </summary>
public sealed class HardDeleteSweepBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<HardDeleteSweepOptions> _options;
    private readonly ILogger<HardDeleteSweepBackgroundService> _logger;

    public HardDeleteSweepBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<HardDeleteSweepOptions> options,
        ILogger<HardDeleteSweepBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue;
        _logger.LogInformation(
            "HardDeleteSweep started; first run in {Delay}s, interval={Interval}h, grace={Days}d, batch={Batch}.",
            opts.InitialDelaySeconds, opts.IntervalHours, opts.GracePeriodDays, opts.BatchLimit);

        try
        {
            await Task.Delay(opts.InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            if (stoppingToken.IsCancellationRequested) break;

            // Re-read options per cycle so live reload takes effect
            // without restart (IOptionsMonitor contract). The total
            // delay is base + jitter: base = IntervalHours, jitter =
            // [0, +MaxJitterMinutes] random.
            var next = _options.CurrentValue;
            var jitterMs = Random.Shared.NextDouble()
                * TimeSpan.FromMinutes(next.MaxJitterMinutes).TotalMilliseconds;
            var interval = TimeSpan.FromHours(next.IntervalHours)
                .Add(TimeSpan.FromMilliseconds(jitterMs));

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Performs one sweep: finds users in <c>ScheduledHardDelete</c>
    /// status whose <c>ScheduledHardDeleteAt</c> is in the past, then
    /// invokes the orchestrator's <c>CascadeHardDeleteAsync</c> (which
    /// triggers the per-module deletors + audit anonymization).
    ///
    /// <para>
    /// Slice 11.3 — the per-cycle batch size is sourced from
    /// <see cref="HardDeleteSweepOptions.BatchLimit"/> via
    /// <see cref="IOptionsMonitor{T}.CurrentValue"/>. Wave 10.5 had no
    /// batch limit (the EF query was unbounded); we now cap the query
    /// so a backlog of overdue users cannot blow up cycle wall-clock
    /// time. The default cap (100) is set in the options class.
    /// </para>
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<UserCascadeDeleterOrchestrator>();
            var opts = _options.CurrentValue;

            var cutoff = DateTimeOffset.UtcNow;
            // Slice 11.3 — cap by opts.BatchLimit so a backlog cannot
            // monopolise the cycle. EF translates `Take(N)` to `LIMIT N`
            // on Postgres; the next cycle picks up the remainder.
            var dueUsers = await db.Users
                .Where(u => u.Status == UserStatus.ScheduledHardDelete
                         && u.ScheduledHardDeleteAt != null
                         && u.ScheduledHardDeleteAt <= cutoff)
                .Take(opts.BatchLimit)
                .Select(u => u.Id)
                .ToListAsync(ct);

            foreach (var userId in dueUsers)
            {
                try
                {
                    // Physical user-row delete is delegated: orchestrator
                    // runs the per-module deletors + audit anonymization,
                    // then we delete the user row last (FK constraint).
                    var deleted = await orchestrator.CascadeHardDeleteAsync(
                        userId,
                        async innerCt => await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(innerCt),
                        ct);
                    _logger.LogInformation(
                        "HardDeleteSweep: purged user {UserId} ({Rows} row(s) across all modules).",
                        userId, deleted);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "HardDeleteSweep: failed to purge {UserId}; will retry next cycle.", userId);
                }
            }

            if (dueUsers.Count == 0)
                _logger.LogDebug("HardDeleteSweep: no users due for hard-delete in this cycle.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HardDeleteSweep: cycle failed; will retry.");
        }
    }
}
