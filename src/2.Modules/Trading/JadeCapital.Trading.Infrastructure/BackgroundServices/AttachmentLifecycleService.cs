using JadeCapital.Identity.Contracts.Projections;
using JadeCapital.Shared.Kernel.Storage;
using JadeCapital.Trading.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Infrastructure.BackgroundServices;

// ============================================================================
//  AttachmentLifecycleService — slice 4d (Wave 4 Attachments).
//
//  Hosted service that runs daily at ~02:00 UTC (±30min jitter). For each
//  active user it sweeps attachments where:
//      expires_at < now() AND is_active = true
//  For each expired row:
//    1) Delete the MinIO object (best-effort — ObjectNotFound is a no-op).
//    2) Soft-delete the DB row (is_active = false).
//    3) Emit an audit row in trading.attachments_quota_audit.
//
//  Failure isolation:
//    - Per-user: catch + log + continue with next user (one bad user
//      must not stop the sweep).
//    - Per-attachment: catch MinioException + log + continue (idempotent
//      soft-delete retries the next sweep).
//    - Outer top-level guard: nothing escapes ExecuteAsync (the host
//      must never crash).
//
//  Schedule:
//    - First run: 30s after startup (gives Identity a chance to come up).
//    - Subsequent runs: every 24h with [0, +30min] jitter to avoid
//      thundering herd across replicas.
// ============================================================================

public sealed class AttachmentLifecycleService : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan MaxJitter = TimeSpan.FromMinutes(30);
    private const int BatchSize = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AttachmentLifecycleService> _logger;

    public AttachmentLifecycleService(
        IServiceScopeFactory scopeFactory,
        ILogger<AttachmentLifecycleService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AttachmentLifecycleService started. Interval={Hours}h, batch={Batch}.",
            SweepInterval.TotalHours, BatchSize);

        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AttachmentLifecycleService sweep failed; continuing.");
            }

            try
            {
                var jitter = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * MaxJitter.TotalMilliseconds);
                await Task.Delay(SweepInterval + jitter, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("AttachmentLifecycleService stopped.");
    }

    /// <summary>
    /// Internal entry point for tests. Runs the sweep once. Public on
    /// purpose so unit tests can drive the loop deterministically without
    /// depending on the BackgroundService timing.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sweepRepo = scope.ServiceProvider.GetRequiredService<IAttachmentSweepRepository>();
        var storage = scope.ServiceProvider.GetRequiredService<IAttachmentStorage>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var quotaReader = scope.ServiceProvider.GetRequiredService<IAttachmentQuotaReader>();

        var userIds = await sweepRepo.GetActiveUserIdsAsync(ct);
        var now = System.DateTimeOffset.UtcNow;

        foreach (var userId in userIds)
        {
            await SweepForUserAsync(userId, now, sweepRepo, storage, uow, quotaReader, ct);
        }

        _logger.LogInformation(
            "AttachmentLifecycleService daily sweep complete. Users={UserCount}, AsOf={AsOf:O}.",
            userIds.Count, now);
    }

    private async Task SweepForUserAsync(
        Guid userId,
        DateTimeOffset now,
        IAttachmentSweepRepository sweepRepo,
        IAttachmentStorage storage,
        IUnitOfWork uow,
        IAttachmentQuotaReader quotaReader,
        CancellationToken ct)
    {
        int cleanedCount = 0;
        long cleanedBytes = 0;
        string? errorMessage = null;

        try
        {
            int skip = 0;
            while (!ct.IsCancellationRequested)
            {
                var batch = await sweepRepo.GetExpiredBatchAsync(now, skip, BatchSize, ct);
                if (batch.Count == 0) break;

                var idsInBatch = batch.Select(b => b.AttachmentId).ToList();
                var keysInBatch = batch.Select(b => b.ObjectKey).ToList();

                // MinIO deletes are per-object — best-effort. We don't fail
                // the sweep if MinIO is down: the next sweep will retry the
                // soft-delete (idempotent), and the bucket lifecycle policy
                // is the belt-and-suspenders cleanup.
                foreach (var key in keysInBatch)
                {
                    try
                    {
                        await storage.DeleteAsync(key, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "MinIO delete failed for {Key}; sweep will retry next run.", key);
                    }
                }

                var deleted = await sweepRepo.SoftDeleteBatchAsync(idsInBatch, ct);
                await uow.SaveChangesAsync(ct);

                cleanedCount += batch.Count;
                cleanedBytes += batch.Sum(b => b.SizeBytes);
                skip += batch.Count;

                // Page was processed; advance. If batch.Count < BatchSize
                // we are at the tail — exit the inner loop.
                if (batch.Count < BatchSize) break;
            }
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            _logger.LogError(ex, "Sweep failed for user {UserId}.", userId);
        }

        // Refresh cached usage + emit audit row regardless of partial failures.
        try
        {
            var (remainingBytes, remainingCount) = await sweepRepo.GetUserAggregateAsync(userId, ct);

            var skippedReason = cleanedCount == 0
                ? (errorMessage is null ? "no_expired" : "error_during_sweep")
                : null;

            await sweepRepo.InsertAuditAsync(
                userId: userId,
                ranAt: now,
                cleanedCount: cleanedCount,
                cleanedBytes: cleanedBytes,
                remainingCount: remainingCount,
                remainingBytes: remainingBytes,
                skippedReason: skippedReason,
                errorMessage: errorMessage,
                ct: ct);
            await uow.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Audit insert failed for user {UserId}; sweep result not recorded.", userId);
        }
    }
}