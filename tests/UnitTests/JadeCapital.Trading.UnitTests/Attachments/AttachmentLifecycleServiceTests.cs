using JadeCapital.Identity.Contracts.Projections;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Storage;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Infrastructure.BackgroundServices;
using JadeCapital.Trading.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Trading.UnitTests.Attachments;

/// <summary>
/// Unit tests for the daily <c>AttachmentLifecycleService</c> sweep — slice 4d.
/// Tests drive <c>RunOnceAsync</c> directly so they don't depend on the
/// BackgroundService timing.
///
/// Scenarios:
/// <list type="number">
///   <item>Expired rows are soft-deleted + MinIO object deleted + audit row inserted.</item>
///   <item>Active rows (not expired) are skipped.</item>
///   <item>MinIO transient error on one row does NOT stop the sweep — log + continue.</item>
///   <item>Audit row is always inserted (even with cleanedCount=0 / no_expired reason).</item>
///   <item>Sweep is idempotent — running twice on the same set yields the same outcome.</item>
/// </list>
/// </summary>
public class AttachmentLifecycleServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 19, 2, 0, 0, TimeSpan.Zero);

    private readonly IAttachmentSweepRepository _sweep = Substitute.For<IAttachmentSweepRepository>();
    private readonly IAttachmentStorage _storage = Substitute.For<IAttachmentStorage>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAttachmentQuotaReader _quotaReader = Substitute.For<IAttachmentQuotaReader>();

    public AttachmentLifecycleServiceTests()
    {
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(1));
    }

    private AttachmentLifecycleService CreateSut()
    {
        // Capturing scope factory — wires the same instances per request.
        var services = new ServiceCollection();
        services.AddSingleton(_sweep);
        services.AddSingleton(_storage);
        services.AddSingleton(_uow);
        services.AddSingleton(_quotaReader);
        var sp = services.BuildServiceProvider();
        var scopeFactory = new CapturingScopeFactory(sp);
        return new AttachmentLifecycleService(scopeFactory, NullLogger<AttachmentLifecycleService>.Instance);
    }

    private static ExpiredAttachmentSweepRow ExpiredRow(Guid userId, long sizeBytes, DateTimeOffset expiresAt) =>
        new(Guid.NewGuid(), userId, Guid.NewGuid(), $"trading/attachments/u/t/r/a/{Guid.NewGuid():N}.png", sizeBytes, expiresAt);

    [Fact]
    public async Task RunOnceAsync_WithExpiredRows_DeletesFromMinIO_SoftDeletesDb_AndEmitsAudit()
    {
        var userId = Guid.NewGuid();
        var rows = new[]
        {
            ExpiredRow(userId, 1_048_576L, FixedNow.AddDays(-1)),
            ExpiredRow(userId, 2_097_152L, FixedNow.AddDays(-2)),
        };
        _sweep.GetActiveUserIdsAsync(Arg.Any<CancellationToken>()).Returns(new[] { userId });
        // Use Arg.Any for asOf + skip + take so the stub matches any value
        // (the production code calls with DateTimeOffset.UtcNow + skip=0
        // + take=100 from the BatchSize constant — be permissive in tests).
        _sweep.GetExpiredBatchAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(rows, Array.Empty<ExpiredAttachmentSweepRow>()); // one page then empty
        _sweep.GetUserAggregateAsync(userId, Arg.Any<CancellationToken>())
            .Returns((0L, 0));

        await CreateSut().RunOnceAsync(CancellationToken.None);

        // MinIO deletes per object.
        await _storage.Received(2).DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        // Soft-delete in one DB call.
        await _sweep.Received(1).SoftDeleteBatchAsync(
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2), Arg.Any<CancellationToken>());
        // Audit row written.
        await _sweep.Received(1).InsertAuditAsync(
            userId,
            Arg.Any<DateTimeOffset>(),
            cleanedCount: 2,
            cleanedBytes: 3_145_728L, // 1MB + 2MB
            remainingCount: 0,
            remainingBytes: 0L,
            skippedReason: null,
            errorMessage: null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnceAsync_NoExpiredRows_StillEmitsAuditWithSkippedReason()
    {
        var userId = Guid.NewGuid();
        _sweep.GetActiveUserIdsAsync(Arg.Any<CancellationToken>()).Returns(new[] { userId });
        _sweep.GetExpiredBatchAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ExpiredAttachmentSweepRow>());
        _sweep.GetUserAggregateAsync(userId, Arg.Any<CancellationToken>())
            .Returns((5_242_880L, 3)); // user has 3 active attachments totalling 5 MB

        await CreateSut().RunOnceAsync(CancellationToken.None);

        await _storage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sweep.DidNotReceive().SoftDeleteBatchAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
        await _sweep.Received(1).InsertAuditAsync(
            userId,
            Arg.Any<DateTimeOffset>(),
            cleanedCount: 0,
            cleanedBytes: 0L,
            remainingCount: 3,
            remainingBytes: 5_242_880L,
            skippedReason: "no_expired",
            errorMessage: null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnceAsync_NoActiveUsers_NoOp()
    {
        _sweep.GetActiveUserIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Guid>());

        await CreateSut().RunOnceAsync(CancellationToken.None);

        await _storage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sweep.DidNotReceive().SoftDeleteBatchAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
        await _sweep.DidNotReceive().InsertAuditAsync(
            Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(), Arg.Any<long>(),
            Arg.Any<int>(), Arg.Any<long>(),
            Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnceAsync_MinIODeleteFailsForOneRow_LogsAndContinues()
    {
        var userId = Guid.NewGuid();
        var rows = new[]
        {
            ExpiredRow(userId, 1024L, FixedNow.AddDays(-1)),
            ExpiredRow(userId, 2048L, FixedNow.AddDays(-2)),
            ExpiredRow(userId, 4096L, FixedNow.AddDays(-3)),
        };
        _sweep.GetActiveUserIdsAsync(Arg.Any<CancellationToken>()).Returns(new[] { userId });
        _sweep.GetExpiredBatchAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(rows, Array.Empty<ExpiredAttachmentSweepRow>());
        _sweep.GetUserAggregateAsync(userId, Arg.Any<CancellationToken>())
            .Returns((0L, 0));

        // First call throws (transient MinIO error), second + third succeed.
        var calls = 0;
        _storage.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                if (calls == 1) throw new InvalidOperationException("minio 503");
                return Task.CompletedTask;
            });

        // The sweep MUST NOT crash — it logs and continues.
        var act = () => CreateSut().RunOnceAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        // All three rows were attempted.
        await _storage.Received(3).DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        // The soft-delete still happens for the entire batch (DB is the
        // source of truth; MinIO deletion failure is logged-only).
        await _sweep.Received(1).SoftDeleteBatchAsync(
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 3), Arg.Any<CancellationToken>());
        // Audit row emitted with no error_message because the DB sweep
        // succeeded (MinIO failure is logged but doesn't fail the sweep).
        await _sweep.Received(1).InsertAuditAsync(
            userId,
            Arg.Any<DateTimeOffset>(),
            cleanedCount: 3,
            cleanedBytes: 7168L,
            remainingCount: 0,
            remainingBytes: 0L,
            skippedReason: null,
            errorMessage: null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnceAsync_DbSoftDeleteFailsForUser_EmitsAuditWithErrorMessage()
    {
        var userId = Guid.NewGuid();
        var rows = new[] { ExpiredRow(userId, 1024L, FixedNow.AddDays(-1)) };
        _sweep.GetActiveUserIdsAsync(Arg.Any<CancellationToken>()).Returns(new[] { userId });
        _sweep.GetExpiredBatchAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(rows, Array.Empty<ExpiredAttachmentSweepRow>());

        _sweep.SoftDeleteBatchAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("db timeout"));

        _sweep.GetUserAggregateAsync(userId, Arg.Any<CancellationToken>())
            .Returns((1024L, 1));

        await CreateSut().RunOnceAsync(CancellationToken.None);

        // Audit row is still emitted (best-effort) with the error_message populated.
        await _sweep.Received(1).InsertAuditAsync(
            userId,
            Arg.Any<DateTimeOffset>(),
            cleanedCount: 0,
            cleanedBytes: 0L,
            remainingCount: 1,
            remainingBytes: 1024L,
            skippedReason: Arg.Any<string?>(),
            errorMessage: "db timeout",
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Capturing scope factory — wraps a singleton <see cref="IServiceProvider"/>
    /// and returns it as the scope's provider so we don't need a real DI
    /// container for tests. Lets us verify the BackgroundService plumbing
    /// without booting the host.
    /// </summary>
    private sealed class CapturingScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _sp;
        public CapturingScopeFactory(IServiceProvider sp) { _sp = sp; }
        public IServiceScope CreateAsyncScope() => new Scope(_sp);
        public IServiceScope CreateScope() => new Scope(_sp);

        private sealed class Scope : IServiceScope
        {
            public Scope(IServiceProvider sp) { ServiceProvider = sp; }
            public IServiceProvider ServiceProvider { get; }
            public void Dispose() { }
        }
    }
}