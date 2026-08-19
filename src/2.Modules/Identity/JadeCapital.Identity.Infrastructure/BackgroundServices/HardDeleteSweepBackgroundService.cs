using JadeCapital.Identity.Domain.Users;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Cascade;
using JadeCapital.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Identity.Infrastructure.BackgroundServices;

/// <summary>
/// Daily sweep that hard-deletes users whose 30-day grace period has
/// elapsed (Wave 10, slice 10.5 — GDPR Art. 17).
///
/// <para>
/// Pattern mirrors Wave 9 slice 9b.1 <c>AuditRetentionBackgroundService</c>:
/// <list type="bullet">
///   <item>Per-cycle scope via <see cref="IServiceScopeFactory"/>.</</item>
///   <item>Initial delay 2 minutes (matches the 9b.1 + Wave 6 6c.2 pattern).</</item>
///   <item>Constant <c>[0, +30min]</c> jitter per cycle — not a fraction of
///         interval (per <c>AttachmentLifecycleService</c> precedent).</</item>
///   <item>Per-cycle <c>try { ... } catch { LogError + continue }</c>:
///         the host MUST NEVER crash from a retention / cascade failure.</</item>
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
    private readonly ILogger<HardDeleteSweepBackgroundService> _logger;

    public HardDeleteSweepBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<HardDeleteSweepBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public const int GracePeriodDays = 30;
    private const double MaxJitterMs = 30d * 60d * 1000d;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "HardDeleteSweep started; first run in 2 minutes, grace={Days}d, jitter=[0,+30min].",
            GracePeriodDays);

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            if (stoppingToken.IsCancellationRequested) break;

            var jitterMs = Random.Shared.NextDouble() * MaxJitterMs;
            try
            {
                await Task.Delay(TimeSpan.FromHours(24).Add(TimeSpan.FromMilliseconds(jitterMs)), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Performs one sweep: finds users in <c>ScheduledHardDelete</c>
    /// status whose <c>ScheduledHardDeleteAt</c> is in the past, then
    /// invokes the orchestrator's <c>CascadeHardDeleteAsync</c> (which
    /// triggers the per-module deletors + audit anonymization).
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<UserCascadeDeleterOrchestrator>();

            var cutoff = DateTimeOffset.UtcNow;
            var dueUsers = await db.Users
                .Where(u => u.Status == UserStatus.ScheduledHardDelete
                         && u.ScheduledHardDeleteAt != null
                         && u.ScheduledHardDeleteAt <= cutoff)
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