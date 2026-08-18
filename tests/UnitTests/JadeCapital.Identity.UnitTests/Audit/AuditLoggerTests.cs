using FluentAssertions;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace JadeCapital.Identity.UnitTests.Audit;

/// <summary>
/// Tests for the real <see cref="AuditLogger"/> EF impl (Wave 6, slice 6d.2).
///
/// <para>
/// Eight RED scenarios pinned here (per tasks.md line 468):
/// </para>
/// <list type="number">
///   <item>LogAsync writes the <see cref="AuditEvent"/> row to <c>audit.events</c>
///         via the dedicated <see cref="AuditDbContext"/>.</item>
///   <item>LogAsync catches every exception silently (no-throw contract) —
///         a failing <c>SaveChangesAsync</c> does not propagate.</item>
///   <item>LogAsync enriches the entry with the calling tenant + user from
///         <see cref="ITenantContext"/> when null in the entry.</item>
///   <item>A failing <c>SaveChangesAsync</c> logs a warning via
///         <see cref="ILogger{AuditLogger}"/>, NOT an error — the main
///         mutation committed; the audit is a defense layer.</item>
///   <item>An entry with explicit tenant + user values is preserved as-is —
///         the enrichment step does NOT override caller-supplied values.</item>
///   <item>An entry with null <c>TenantId</c> derives from
///         <see cref="ITenantContext.Current"/> when set.</item>
///   <item>An entry with null <c>UserId</c> derives from
///         <see cref="ITenantContext.CurrentUserId"/> when set.</item>
///   <item>Cancellation token propagates through the SaveChanges call — a
///   pre-cancelled token results in <see cref="OperationCanceledException"/>.</item>
/// </list>
///
/// <para>
/// <b>Why SQLite in-memory</b>: mirrors the 6d.1 <c>AuditEventTests</c> +
/// <c>ImportJobSoftDeleteQueryFilterTests</c> pattern. Lets the EF-level
/// behavior (DbSet.AddAsync + SaveChangesAsync) be exercised without a real
/// Postgres or Testcontainers dependency. The <c>audit.events</c> JSONB
/// column is provider-agnostic for our purposes — SQLite stores it as TEXT
/// but the column mapping in <c>AuditEventConfiguration</c> would emit
/// <c>jsonb</c> on Postgres.
/// </para>
/// </summary>
public class AuditLoggerTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public AuditLoggerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Minimal <see cref="AuditDbContext"/> over the in-memory SQLite
    /// connection. Mirrors the 6d.1 SQLite patterns — focuses the test on
    /// the <see cref="AuditLogger"/> persistence path without spinning up
    /// the full IdentityDbContext.
    /// </summary>
    private AuditDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        var db = new AuditDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static AuditEventEntry NewEntry(
        Guid? tenantId = null,
        Guid? userId = null,
        AuditAction action = AuditAction.Created)
        => new(
            EntityType: "ImportJob",
            EntityId: Guid.NewGuid(),
            Action: action,
            TenantId: tenantId,
            UserId: userId,
            ChangesJson: null,
            OccurredAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task LogAsync_PersistsAuditEvent_ToAuditDbContext()
    {
        // Phase 1 #1: LogAsync adds an AuditEvent row + SaveChanges.
        var db = BuildDb();
        var tenant = Substitute.For<ITenantContext>();
        tenant.Current.Returns((TenantId?)null);
        tenant.CurrentUserId.Returns((Guid?)null);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var logger = NullLogger<AuditLogger>.Instance;
        var sut = new AuditLogger(db, tenant, clock, logger);

        var entry = NewEntry(tenantId: Guid.NewGuid(), userId: Guid.NewGuid());
        await sut.LogAsync(entry, CancellationToken.None);

        var saved = await db.AuditEvents.SingleAsync();
        saved.EntityType.Should().Be(entry.EntityType);
        saved.EntityId.Should().Be(entry.EntityId);
        saved.Action.Should().Be(entry.Action);
        saved.TenantId.Should().Be(entry.TenantId);
        saved.UserId.Should().Be(entry.UserId);
    }

[Fact]
    public async Task LogAsync_DoesNotThrow_WhenSaveChangesFails()
    {
        // Phase 1 #2: the no-throw contract. A DbContext whose SaveChanges
        // throws MUST be swallowed — the main mutation has already committed.
        // We force the failure by closing the underlying SQLite connection
        // (AuditDbContext is sealed, so NSubstitute can't proxy it).
        var brokenDb = BuildDb();
        _connection.Close(); // next SaveChanges will fail with InvalidOperationException
        var tenant = Substitute.For<ITenantContext>();
        tenant.Current.Returns((TenantId?)null);
        tenant.CurrentUserId.Returns((Guid?)null);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var logger = NullLogger<AuditLogger>.Instance;
        var sut = new AuditLogger(brokenDb, tenant, clock, logger);

        var act = async () => await sut.LogAsync(NewEntry(), CancellationToken.None);
        await act.Should().NotThrowAsync("the audit logger MUST NOT propagate failures — the main mutation has already committed.");
    }

    [Fact]
    public async Task LogAsync_EnrichesEntry_FromTenantContext_WhenNull()
    {
        // Phase 1 #3: enrich from ITenantContext when the entry has null tenant/user.
        var db = BuildDb();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenant = Substitute.For<ITenantContext>();
        tenant.Current.Returns(new TenantId(tenantId));
        tenant.CurrentUserId.Returns(userId);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var logger = NullLogger<AuditLogger>.Instance;
        var sut = new AuditLogger(db, tenant, clock, logger);

        await sut.LogAsync(NewEntry(tenantId: null, userId: null), CancellationToken.None);

        var saved = await db.AuditEvents.SingleAsync();
        saved.TenantId.Should().Be(tenantId, "TenantContext.Current enriches the entry when null.");
        saved.UserId.Should().Be(userId, "TenantContext.CurrentUserId enriches the entry when null.");
    }

    [Fact]
    public async Task LogAsync_LogsWarning_WhenSaveChangesFails()
    {
        // Phase 1 #4: a failing SaveChanges logs a warning (not error) — the
        // main mutation has already committed, the audit is a defense layer.
        // Force the failure by closing the SQLite connection (AuditDbContext
        // is sealed; NSubstitute can't proxy it).
        var brokenDb = BuildDb();
        _connection.Close();
        var tenant = Substitute.For<ITenantContext>();
        tenant.Current.Returns((TenantId?)null);
        tenant.CurrentUserId.Returns((Guid?)null);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var logger = Substitute.For<ILogger<AuditLogger>>();
        var sut = new AuditLogger(brokenDb, tenant, clock, logger);

        await sut.LogAsync(NewEntry(), CancellationToken.None);

        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task LogAsync_PreservesExplicitTenantAndUser_WhenProvided()
    {
        // Phase 1 #5: explicit values win. The enrichment step does NOT
        // override caller-supplied tenant/user (used by webhooks and admin
        // tooling that act on behalf of a different user).
        var db = BuildDb();
        var contextTenant = Guid.NewGuid();
        var contextUser = Guid.NewGuid();
        var explicitTenant = Guid.NewGuid();
        var explicitUser = Guid.NewGuid();
        var tenant = Substitute.For<ITenantContext>();
        tenant.Current.Returns(new TenantId(contextTenant));
        tenant.CurrentUserId.Returns(contextUser);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var logger = NullLogger<AuditLogger>.Instance;
        var sut = new AuditLogger(db, tenant, clock, logger);

        await sut.LogAsync(NewEntry(tenantId: explicitTenant, userId: explicitUser), CancellationToken.None);

        var saved = await db.AuditEvents.SingleAsync();
        saved.TenantId.Should().Be(explicitTenant, "caller-supplied TenantId is preserved over TenantContext.Current.");
        saved.UserId.Should().Be(explicitUser, "caller-supplied UserId is preserved over TenantContext.CurrentUserId.");
    }

    [Fact]
    public async Task LogAsync_NullTenantDerives_FromTenantContextCurrent()
    {
        // Phase 1 #6: TenantContext.Current wins when TenantId is null.
        // This is the triangulation test for the enrichment branch.
        var db = BuildDb();
        var contextTenant = Guid.NewGuid();
        var tenant = Substitute.For<ITenantContext>();
        tenant.Current.Returns(new TenantId(contextTenant));
        tenant.CurrentUserId.Returns((Guid?)null);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var logger = NullLogger<AuditLogger>.Instance;
        var sut = new AuditLogger(db, tenant, clock, logger);

        await sut.LogAsync(NewEntry(tenantId: null, userId: Guid.NewGuid()), CancellationToken.None);

        var saved = await db.AuditEvents.SingleAsync();
        saved.TenantId.Should().Be(contextTenant);
    }

    [Fact]
    public async Task LogAsync_NullUserDerives_FromTenantContextCurrentUserId()
    {
        // Phase 1 #7: TenantContext.CurrentUserId wins when UserId is null.
        var db = BuildDb();
        var contextUser = Guid.NewGuid();
        var tenant = Substitute.For<ITenantContext>();
        tenant.Current.Returns((TenantId?)null);
        tenant.CurrentUserId.Returns(contextUser);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var logger = NullLogger<AuditLogger>.Instance;
        var sut = new AuditLogger(db, tenant, clock, logger);

        await sut.LogAsync(NewEntry(tenantId: Guid.NewGuid(), userId: null), CancellationToken.None);

        var saved = await db.AuditEvents.SingleAsync();
        saved.UserId.Should().Be(contextUser);
    }

    [Fact]
    public async Task LogAsync_CancellationTokenPropagates_ToSaveChangesAsync()
    {
        // Phase 1 #8: pre-cancelled token must be honoured by SaveChangesAsync
        // (which throws OperationCanceledException synchronously when the
        // token is already cancelled at entry). The audit logger MUST NOT
        // re-throw it — the no-throw contract covers ALL exceptions,
        // including cancellation. We verify the warning is logged instead.
        var db = BuildDb();
        var tenant = Substitute.For<ITenantContext>();
        tenant.Current.Returns((TenantId?)null);
        tenant.CurrentUserId.Returns((Guid?)null);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var logger = Substitute.For<ILogger<AuditLogger>>();
        var sut = new AuditLogger(db, tenant, clock, logger);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await sut.LogAsync(NewEntry(), cts.Token);
        await act.Should().NotThrowAsync("the no-throw contract covers cancellation too — the audit is non-critical.");

        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}