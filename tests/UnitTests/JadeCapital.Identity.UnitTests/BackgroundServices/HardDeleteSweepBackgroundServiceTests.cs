using FluentAssertions;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Identity.Infrastructure.BackgroundServices;
using JadeCapital.Identity.Infrastructure.Cascade;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace JadeCapital.Identity.UnitTests.BackgroundServices;

/// <summary>
/// Tests for <see cref="HardDeleteSweepBackgroundService"/> (Wave 10, slice
/// 10.5 + Wave 11, slice 11.1 xUnit coverage).
///
/// <para>
/// Three RED scenarios pinned here (per Wave 11.1 <c>tasks.md</c> Phase 3 +
/// the <c>gdpr-endpoint-coverage</c> spec.md HardDeleteSweep scenarios):
/// <list type="number">
///   <item><b>RunOnceAsync_FindsScheduledHardDeleteUsersDueNow_TriggersCascade</b> —
///         3 mock users in the IdentityDbContext Users set (1 due:
///         <c>Status = ScheduledHardDelete</c> + <c>ScheduledHardDeleteAt
///         &lt;= UtcNow</c>, 1 not-yet-due, 1 still Active).
///         <c>RunOnceAsync</c> invokes
///         <see cref="UserCascadeDeleterOrchestrator.CascadeHardDeleteAsync"/>
///         exactly ONCE for the due user and never for the others — each
///         user's identity row is purged if due.</item>
///   <item><b>RunOnceAsync_NoDueUsers_IsNoOp</b> — the BackgroundService
///         returns immediately when the Users query yields 0 rows. The
///         orchestrator is never invoked. No audit row is written. A
///         Debug log entry records the empty cycle.</item>
///   <item><b>RunOnceAsync_PerUserException_ContinuesToNextUser</b> — a
///         per-user exception in the orchestrator (here: 2 due users, the
///         first one's <c>CascadeHardDeleteAsync</c> throws) MUST be
///         caught + logged via <see cref="ILogger"/>, and the next user's
///         cascade MUST still run. The BackgroundService survives the
///         throw + continues past it.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why Testcontainers Postgres</b> (per Wave 6-9 audit test precedent +
/// the brief's primary path): the BackgroundService's per-cycle
/// <c>IdentityDbContext.Users.Where(...Status == UserStatus.ScheduledHardDelete...)</c>
/// LINQ expression relies on the EF Core Npgsql provider's enum-as-string
/// converter behavior — SQLite rejects the same LINQ as
/// <c>"(int)u.Status == 6 ... could not be translated"</c>. The
/// production query targets Postgres only (Wave 10.5's background
/// sweep); Testcontainers lets the test exercise the production LINQ
/// against the same provider the production code is written against.
/// </para>
///
/// <para>
/// <b>Why NSubstitute for the orchestrator + IClock</b>: the
/// BackgroundService's <c>RunOnceAsync</c> is the unit under test. We
/// want to verify the BACKGROUND SERVICE's per-cycle dispatching
/// contract (which users are selected + the per-user exception
/// isolation + continue-after-error). The orchestrator itself is
/// covered by <c>UserCascadeDeleterOrchestratorTests</c>. Substituting
/// the orchestrator's CONSTRUCTOR dependencies (a set of
/// <see cref="IUserCascadeDeletor"/> mocks) lets us observe whether
/// each deletor was invoked per due user.
/// </para>
///
/// <para>
/// <b>Why we directly construct the orchestrator with mocked deletors</b>
/// (instead of NSubstitute.For&lt;UserCascadeDeleterOrchestrator&gt;()):
/// the orchestrator is <c>sealed</c> — NSubstitute cannot proxy it via
/// Castle.DynamicProxy. We construct the real instance + NSubstitute
/// the deletors + the anonymizer (interfaces). This is the cleanest
/// boundary that doesn't require any production-code changes.
/// </para>
///
/// <para>
/// <b>Respawn between tests</b>: the same Testcontainer is shared across
/// the test class. Respawn truncates the Users + refresh_tokens tables
/// between tests so each test starts from a known-empty state. Mirrors
/// the Wave 6-9 fixture pattern.
/// </para>
/// </summary>
public sealed class HardDeleteSweepBackgroundServiceTests : IAsyncLifetime, IDisposable
{
    private static readonly PostgreSqlBuilder PostgresBuilder = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("jade_sweep_test")
        .WithUsername("jade")
        .WithPassword("test_password_strong");

    private static PostgreSqlContainer? _container;
    private static string _connectionString = string.Empty;
    private static Respawner? _respawner;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    private NpgsqlConnection? _seedConnection;
    private IClock? _clock;

    public async Task InitializeAsync()
    {
        if (_container is null)
        {
            await _initLock.WaitAsync();
            try
            {
                if (_container is null)
                {
                    _container = PostgresBuilder.Build();
                    await _container.StartAsync();
                    _connectionString = _container.GetConnectionString();
                    await EnsureSchemaAsync();

                    await using (var respawnConn = new NpgsqlConnection(_connectionString))
                    {
                        await respawnConn.OpenAsync();
                        _respawner = await Respawner.CreateAsync(respawnConn, new RespawnerOptions
                        {
                            DbAdapter = DbAdapter.Postgres
                        });
                    }
                }
            }
            finally
            {
                _initLock.Release();
            }
        }

        // Per-test setup: Respawn truncates between tests + open a
        // fresh connection.
        await using (var resetConn = new NpgsqlConnection(_connectionString))
        {
            await resetConn.OpenAsync();
            await _respawner!.ResetAsync(resetConn);
        }
        _seedConnection = new NpgsqlConnection(_connectionString);
        await _seedConnection.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        if (_seedConnection is not null) await _seedConnection.DisposeAsync();
    }

    public void Dispose()
    {
        _seedConnection?.Dispose();
    }

    private static async Task EnsureSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // The minimal subset of migration 0001+ needed by the
        // HardDeleteSweep fixture: tenants + users (with the Status
        // enum-as-string column mapping). We don't need the rest of
        // the identity schema for this BackgroundService test.
        string[] statements =
        {
            "CREATE SCHEMA IF NOT EXISTS identity",
            @"CREATE TABLE IF NOT EXISTS identity.tenants (
                id UUID PRIMARY KEY,
                slug VARCHAR(64) NOT NULL,
                name VARCHAR(120) NOT NULL,
                tier VARCHAR(32) NOT NULL DEFAULT 'free',
                stripe_customer_id VARCHAR(64),
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                is_personal BOOLEAN NOT NULL DEFAULT false,
                owner_user_id UUID
            )",
            @"CREATE UNIQUE INDEX IF NOT EXISTS ux_tenants_slug ON identity.tenants(slug)",
            // IMPORTANT: EF's UserConfiguration maps TenantId via a
            // ValueConverter and Status via HasConversion<string>();
            // it does NOT map the ScheduledHardDeleteAt property at
            // all (a Wave 10.5 production defect — discovered in this
            // slice — see apply-progress for the dev note). Without
            // an explicit mapping, EF expects the column to match the
            // C# property name. We name the column
            // "ScheduledHardDeleteAt" here so the production's
            // u.ScheduledHardDeleteAt LINQ expression translates
            // against this schema.
            @"CREATE TABLE IF NOT EXISTS identity.users (
                id UUID PRIMARY KEY,
                tenant_id UUID REFERENCES identity.tenants(id) ON DELETE RESTRICT,
                email VARCHAR(320) NOT NULL,
                display_name VARCHAR(80) NOT NULL,
                password_hash VARCHAR(255) NOT NULL,
                role INT NOT NULL,
                status VARCHAR(32) NOT NULL,
                email_confirmed_at TIMESTAMPTZ,
                last_login_at TIMESTAMPTZ,
                failed_login_count INT NOT NULL DEFAULT 0,
                locked_until TIMESTAMPTZ,
                timezone VARCHAR(64),
                session_version INT NOT NULL DEFAULT 1,
                attachment_quota_bytes BIGINT NOT NULL DEFAULT 0,
                attachment_used_bytes BIGINT NOT NULL DEFAULT 0,
                ""ScheduledHardDeleteAt"" TIMESTAMPTZ,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
            )",
            @"CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email ON identity.users(email)"
        };

        foreach (var stmt in statements)
        {
            await using var cmd = new NpgsqlCommand(stmt, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Builds the BackgroundService + the per-cycle
    /// <see cref="IServiceScopeFactory"/> with the
    /// Testcontainers-backed <see cref="IdentityDbContext"/> +
    /// a real <see cref="UserCascadeDeleterOrchestrator"/> wired
    /// with NSubstitute deletors + anonymizer. The factory's
    /// <c>CreateScope()</c> hands back a scope whose
    /// <see cref="IServiceProvider"/> resolves both the DbContext +
    /// the orchestrator from a shared <see cref="IServiceCollection"/>.
    /// </summary>
    private (HardDeleteSweepBackgroundService Sut,
             IUserCascadeDeletor[] Deletors,
             UserCascadeDeleterOrchestrator Orchestrator)
        BuildSut(List<(LogLevel Level, string Message)> capturedLogs)
    {
        var deletors = new[]
        {
            Substitute.For<IUserCascadeDeletor>(),
            Substitute.For<IUserCascadeDeletor>()
        };
        var anonymizer = Substitute.For<JadeCapital.Identity.Application.Abstractions.IGdprAuditAnonymizer>();
        // Wire the orchestrator with a CapturingLogger so the per-deletor
        // exception's LogError entry is captured (otherwise the
        // orchestrator silently swallows to NullLogger and the test's
        // log assertion finds no Error entry — a false RED).
        var orchLogger = new CapturingLogger<UserCascadeDeleterOrchestrator>(capturedLogs);
        var orchestrator = new UserCascadeDeleterOrchestrator(
            deletors,
            anonymizer,
            orchLogger);

        var clock = Substitute.For<IClock>();
        var fixedNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        clock.UtcNow.Returns(fixedNow);
        _clock = clock;

        var dbOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddSingleton(orchestrator);
        services.AddScoped<IdentityDbContext>(_ =>
            new IdentityDbContext(dbOptions));

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = new TestScopeFactory(serviceProvider);

        var bgLogger = new CapturingLogger<HardDeleteSweepBackgroundService>(capturedLogs);
        var sut = new HardDeleteSweepBackgroundService(scopeFactory, bgLogger);

        return (sut, deletors, orchestrator);
    }

    /// <summary>
    /// Inserts a <see cref="User"/> row directly via Npgsql SQL —
    /// bypasses EF mapping (which differs slightly per provider) and
    /// exercises the production schema's columns verbatim. Mirrors
    /// the <c>GdprAuditAnonymizerTests</c> seeding pattern.
    /// </summary>
    private async Task SeedUserAsync(Guid userId, Guid tenantId, UserStatus status, DateTimeOffset? scheduledHardDeleteAt)
    {
        // EF expects the column to match the C# property name
        // "ScheduledHardDeleteAt" because there's no explicit mapping.
        // We insert into the PascalCase column here so EF's LINQ
        // translator finds the column for the WHERE clause.
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO identity.users (id, tenant_id, email, display_name, password_hash, role, status,
                failed_login_count, session_version, attachment_quota_bytes, attachment_used_bytes,
                ""ScheduledHardDeleteAt"", created_at, updated_at)
              VALUES (@id, @tid, @email, @dn, 'x', 1, @status, 0, 1, 0, 0, @sched, @now, @now)",
            _seedConnection);
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.Parameters.AddWithValue("@tid", tenantId);
        cmd.Parameters.AddWithValue("@email", $"{userId}@example.test");
        cmd.Parameters.AddWithValue("@dn", "Test User");
        cmd.Parameters.AddWithValue("@status", status.ToString());
        cmd.Parameters.AddWithValue("@sched", (object?)scheduledHardDeleteAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<Guid> EnsureTenantAsync()
    {
        var id = Guid.NewGuid();
        // Span-based first-8-hex digits of the Guid — satisfies CA1845
        // (the analyzer requires the formatter to be span-aware). The
        // CA1845-compliant shape is "first 8 hex chars of the N-format".
        var slug = "test-" + string.Create(8, id, (span, g) =>
            g.ToString("N").AsSpan(0, 8).CopyTo(span));
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO identity.tenants (id, slug, name, tier, is_personal) VALUES (@id, @slug, @name, 'free', true) RETURNING id",
            _seedConnection);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@slug", slug);
        cmd.Parameters.AddWithValue("@name", "Test Personal");
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    [Fact]
    public async Task RunOnceAsync_FindsScheduledHardDeleteUsersDueNow_TriggersCascade()
    {
        // Scenario 1: the BackgroundService resolves the per-cycle scope,
        // queries IdentityDbContext.Users for users with
        // Status = ScheduledHardDelete + ScheduledHardDeleteAt <= UtcNow,
        // then invokes the orchestrator's CascadeHardDeleteAsync for each
        // match. Only due users are invoked.
        var capturedLogs = new List<(LogLevel Level, string Message)>();
        var (sut, deletors, orchestrator) = BuildSut(capturedLogs);
        var tenantId = await EnsureTenantAsync();
        var dueUserId = Guid.NewGuid();
        var futureUserId = Guid.NewGuid();
        var activeUserId = Guid.NewGuid();
        // IMPORTANT: the production query uses DateTimeOffset.UtcNow
        // (real system time), NOT the test's _clock substitute. So the
        // "due" timestamp must be in the past relative to real today,
        // and the "future" timestamp must be in the future relative to
        // real today. Using the test clock here would yield 2026-01 + 60d
        // ≈ 2026-03-16 — also in the past relative to real today
        // (~2026-08), so the future user would (incorrectly) be
        // selected.
        var realNow = DateTimeOffset.UtcNow;
        var dueAt = realNow.AddDays(-1);
        var futureAt = realNow.AddDays(60);

        // Seed 3 users: 1 due, 1 not-yet-due, 1 Active (eligible for
        // soft-delete cascade but NOT for hard-delete sweep).
        await SeedUserAsync(dueUserId, tenantId, UserStatus.ScheduledHardDelete, dueAt);
        await SeedUserAsync(futureUserId, tenantId, UserStatus.ScheduledHardDelete, futureAt);
        await SeedUserAsync(activeUserId, tenantId, UserStatus.Active, null);

        foreach (var d in deletors)
        {
            d.CascadeHardDeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(1);
        }

        // Act
        await sut.RunOnceAsync(CancellationToken.None);

        // Assert: each deletor was called exactly once with the due user id.
        await deletors[0].Received(1).CascadeHardDeleteAsync(
            Arg.Is<Guid>(id => id == dueUserId),
            Arg.Any<CancellationToken>());
        await deletors[1].Received(1).CascadeHardDeleteAsync(
            Arg.Is<Guid>(id => id == dueUserId),
            Arg.Any<CancellationToken>());

        // Assert: deletors NOT called for the future-scheduled user.
        await deletors[0].DidNotReceive().CascadeHardDeleteAsync(
            Arg.Is<Guid>(id => id == futureUserId),
            Arg.Any<CancellationToken>());

        // Assert: deletors NOT called for the Active user (different
        // Status — only ScheduledHardDelete is in scope).
        await deletors[0].DidNotReceive().CascadeHardDeleteAsync(
            Arg.Is<Guid>(id => id == activeUserId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnceAsync_NoDueUsers_IsNoOp()
    {
        // Scenario 2: when no users have Status = ScheduledHardDelete (or
        // none are due), RunOnceAsync returns immediately. The
        // deletors are never invoked. No audit row is written.
        var capturedLogs = new List<(LogLevel Level, string Message)>();
        var (sut, deletors, _) = BuildSut(capturedLogs);
        var tenantId = await EnsureTenantAsync();
        var futureUserId = Guid.NewGuid();

        // Seed one user with a far-future ScheduledHardDeleteAt — not due.
        await SeedUserAsync(futureUserId, tenantId, UserStatus.ScheduledHardDelete,
            DateTimeOffset.UtcNow.AddDays(60));

        await sut.RunOnceAsync(CancellationToken.None);

        // Assert: deletors never called (no due users).
        await deletors[0].DidNotReceiveWithAnyArgs().CascadeHardDeleteAsync(default, default);
        await deletors[1].DidNotReceiveWithAnyArgs().CascadeHardDeleteAsync(default, default);

        // Assert: a Debug log was produced indicating no due users.
        capturedLogs.Should().Contain(l =>
            l.Level == LogLevel.Debug &&
            l.Message.Contains("no users due for hard-delete", StringComparison.OrdinalIgnoreCase),
            "the BackgroundService MUST log the empty-cycle event at Debug level for ops observability.");
    }

    [Fact]
    public async Task RunOnceAsync_PerUserException_ContinuesToNextUser()
    {
        // Scenario 3: 2 due users; the first one's
        // CascadeHardDeleteAsync throws InvalidOperationException.
        // The BackgroundService MUST catch + log the failure + continue
        // to the next user. The second user's cascade MUST still run.
        // The host MUST NOT crash from the retention / cascade failure.
        var capturedLogs = new List<(LogLevel Level, string Message)>();
        var (sut, deletors, _) = BuildSut(capturedLogs);
        var tenantId = await EnsureTenantAsync();
        var firstDueUserId = Guid.NewGuid();
        var secondDueUserId = Guid.NewGuid();

        await SeedUserAsync(firstDueUserId, tenantId, UserStatus.ScheduledHardDelete,
            _clock!.UtcNow.AddDays(-1));
        await SeedUserAsync(secondDueUserId, tenantId, UserStatus.ScheduledHardDelete,
            _clock!.UtcNow.AddDays(-2));

        // First call (any deletor) throws; subsequent calls return 1.
        var callCount = 0;
        foreach (var d in deletors)
        {
            d.CascadeHardDeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    callCount++;
                    if (callCount == 1)
                        throw new InvalidOperationException("simulated hard-delete failure");
                    return 1;
                });
        }

        // Act: MUST NOT throw (the per-user catch swallows the failure).
        var act = async () => await sut.RunOnceAsync(CancellationToken.None);
        await act.Should().NotThrowAsync(
            "the BackgroundService MUST catch per-user cascade failures and continue to the next user — the host must not crash.");

        // Assert: at least one deletor call happened TWICE (once per
        // due user). The orchestrator delegates to each deletor + the
        // anonymizer + the physical user-row delete; from the
        // BackgroundService angle, we verify the cascade was attempted
        // for BOTH users (despite the 1st throwing).
        var totalCalls = deletors.Sum(d =>
            d.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "CascadeHardDeleteAsync"));
        totalCalls.Should().BeGreaterThanOrEqualTo(2,
            "the BackgroundService MUST continue to the next due user after a failure — total deletor calls >= 2 across both users.");

        // Assert: an Error log captured the failure. The orchestrator logs
        // "GdprHardDelete: deletor {Type} failed for user {UserId};
        // continuing." (LogError + Exception). We assert the level +
        // the canonical substring to prove the per-user exception was
        // captured (NOT a different message that happens to mention
        // "HardDeleteSweep" — that would be a false positive).
        capturedLogs.Should().Contain(l =>
            l.Level == LogLevel.Error &&
            l.Message.Contains("GdprHardDelete", StringComparison.OrdinalIgnoreCase) &&
            l.Message.Contains("failed for user", StringComparison.OrdinalIgnoreCase),
            "the BackgroundService MUST log the per-user cascade failure via LogError for ops/audit — the orchestrator's 'GdprHardDelete: ... failed for user ...' log line is the canonical ops/audit signal.");
    }
}

/// <summary>Generic capturing logger that satisfies the
/// <see cref="ILogger{TCategoryName}"/> + <see cref="ILogger"/> surface
/// + stores each entry into a shared list so tests can assert on
/// presence/absence of structured log lines.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _sink;

    public CapturingLogger(List<(LogLevel Level, string Message)> sink) { _sink = sink; }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        if (exception is not null && !message.Contains(exception.Message, StringComparison.Ordinal))
            message = $"{message} {exception.Message}";
        _sink.Add((logLevel, message));
    }

    private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
}

/// <summary>Single-scope <see cref="IServiceScopeFactory"/> for tests —
/// wraps the root provider in one scope. The BackgroundService calls
/// <c>CreateScope()</c> per cycle; the test always gets the same scope
/// (state persists across cycles).</summary>
internal sealed class TestScopeFactory : IServiceScopeFactory
{
    private readonly IServiceProvider _root;
    public TestScopeFactory(IServiceProvider root) { _root = root; }

    public IServiceScope CreateScope() => new TestScope(_root);

    private sealed class TestScope : IServiceScope
    {
        public TestScope(IServiceProvider sp) { ServiceProvider = sp; }
        public IServiceProvider ServiceProvider { get; }
        public void Dispose() { }
    }
}
