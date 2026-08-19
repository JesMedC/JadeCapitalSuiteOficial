using FluentAssertions;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Identity.Infrastructure.BackgroundServices;
using JadeCapital.Identity.Infrastructure.Cascade;
using JadeCapital.Identity.Infrastructure.Configuration;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace JadeCapital.Identity.UnitTests.BackgroundServices;

// ============================================================================
//  HardDeleteSweepBackgroundServiceOptionsTests — Wave 11 slice 11.3
//
//  RED-first TDD coverage for the Wave 10 W-04 closure: the
//  BackgroundService MUST pick up its tuning values from
//  `HardDeleteSweepOptions` via `IOptionsMonitor` so per-env cadence
//  changes do not require a recompile.
//
//  Two scenarios:
//    1. RunOnceAsync_UsesOptionsIntervalAndBatchLimit_NotHardcoded
//       — non-default `BatchLimit` flows through to the per-cycle query
//         (proves the BackgroundService reads `_options.CurrentValue`,
//         not the Wave 10.5 hardcoded value).
//    2. RunOnceAsync_PicksUpHotReloadOfOptions
//       — mutating the `TestOptionsMonitor.CurrentValue` between cycles
//         changes the next cycle's behavior (proves the BackgroundService
//         re-reads per cycle, not at startup).
// ============================================================================

public sealed class HardDeleteSweepBackgroundServiceOptionsTests : IAsyncLifetime, IDisposable
{
    private static readonly PostgreSqlBuilder PostgresBuilder = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("jade_sweep_options_test")
        .WithUsername("jade")
        .WithPassword("test_password_strong");

    private static PostgreSqlContainer? _container;
    private static string _connectionString = string.Empty;
    private static Respawner? _respawner;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    private NpgsqlConnection? _seedConnection;

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

                    await using var respawnConn = new NpgsqlConnection(_connectionString);
                    await respawnConn.OpenAsync();
                    _respawner = await Respawner.CreateAsync(respawnConn, new RespawnerOptions
                    {
                        DbAdapter = DbAdapter.Postgres,
                    });
                }
            }
            finally
            {
                _initLock.Release();
            }
        }

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

    public void Dispose() => _seedConnection?.Dispose();

    private static async Task EnsureSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

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
            // Mirrors the HardDeleteSweepBackgroundServiceTests schema —
            // the production query selects `ScheduledHardDeleteAt` so
            // the column must exist (mapped via EF; Production drops
            // the explicit mapping once 11.3 ships, but the column
            // name persists — see UserConfiguration.cs).
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
                scheduled_hard_delete_at TIMESTAMPTZ,
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

    private static (HardDeleteSweepBackgroundService Sut,
                    IUserCascadeDeletor[] Deletors,
                    SettableOptionsMonitor<HardDeleteSweepOptions> Monitor)
        BuildSut(string connectionString, HardDeleteSweepOptions initialOptions)
    {
        var deletors = new IUserCascadeDeletor[]
        {
            Substitute.For<IUserCascadeDeletor>(),
            Substitute.For<IUserCascadeDeletor>()
        };
        var anonymizer = Substitute.For<JadeCapital.Identity.Application.Abstractions.IGdprAuditAnonymizer>();
        var orchestrator = new UserCascadeDeleterOrchestrator(
            deletors,
            anonymizer,
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<UserCascadeDeleterOrchestrator>());

        var dbOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var services = new ServiceCollection();
        services.AddSingleton(orchestrator);
        services.AddScoped<IdentityDbContext>(_ => new IdentityDbContext(dbOptions));

        var sp = services.BuildServiceProvider();
        var scopeFactory = new TestScopeFactory(sp);

        var monitor = new SettableOptionsMonitor<HardDeleteSweepOptions>(initialOptions);

        var bgLogger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<HardDeleteSweepBackgroundService>();

        var sut = new HardDeleteSweepBackgroundService(scopeFactory, monitor, bgLogger);
        return (sut, deletors, monitor);
    }

    private async Task<Guid> EnsureTenantAsync()
    {
        var id = Guid.NewGuid();
        var slug = "test-" + string.Create(8, id, (span, g) =>
            g.ToString("N").AsSpan(0, 8).CopyTo(span));
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO identity.tenants (id, slug, name, tier, is_personal) VALUES (@id, @slug, @name, 'free', true)",
            _seedConnection);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@slug", slug);
        cmd.Parameters.AddWithValue("@name", "Test Personal");
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task SeedUserAsync(Guid userId, Guid tenantId, UserStatus status, DateTimeOffset? scheduledHardDeleteAt)
    {
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO identity.users (id, tenant_id, email, display_name, password_hash, role, status,
                failed_login_count, session_version, attachment_quota_bytes, attachment_used_bytes,
                scheduled_hard_delete_at, created_at, updated_at)
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

    [Fact]
    public async Task RunOnceAsync_UsesOptionsIntervalAndBatchLimit_NotHardcoded()
    {
        // Slice 11.3 RED: with `BatchLimit = 2` configured, a single cycle
        // must cascade only the FIRST 2 due users (sorted by EF) and
        // leave the third one for the next cycle. Wave 10.5 had NO
        // batch limit so this scenario proves the BG service reads the
        // option instead of the legacy hardcoded unbounded query.
        var opts = new HardDeleteSweepOptions
        {
            BatchLimit = 2,
            InitialDelaySeconds = 60,
            IntervalHours = 24,
            GracePeriodDays = 30,
            MaxJitterMinutes = 0,
        };
        var (sut, deletors, _) = BuildSut(_connectionString, opts);

        var tenantId = await EnsureTenantAsync();
        var dueUser1 = Guid.NewGuid();
        var dueUser2 = Guid.NewGuid();
        var dueUser3 = Guid.NewGuid();
        var realNow = DateTimeOffset.UtcNow;
        var dueAt = realNow.AddDays(-1);

        await SeedUserAsync(dueUser1, tenantId, UserStatus.ScheduledHardDelete, dueAt);
        await SeedUserAsync(dueUser2, tenantId, UserStatus.ScheduledHardDelete, dueAt);
        await SeedUserAsync(dueUser3, tenantId, UserStatus.ScheduledHardDelete, dueAt);

        foreach (var d in deletors)
            d.CascadeHardDeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(1);

        await sut.RunOnceAsync(CancellationToken.None);

        // Count ALL deletor invocations across the wired registrants.
        // The orchestrator calls every deletor per user (slice 10.5
        // contract). With 2 users cascaded × 2 deletors = 4 calls.
        var totalCalls = deletors.Sum(d =>
            d.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "CascadeHardDeleteAsync"));
        totalCalls.Should().Be(4,
            "the BatchLimit option (2) MUST cap the cycle to 2 users × 2 wired deletors (orchestrator fan-out). "
            + "A result of 6 would mean the Wave 10.5 hardcoded unbounded query is still active.");

        // Sanity: the third user is still in the DB.
        var remainingCount = 0;
        await using var probe = new NpgsqlCommand(
            "SELECT COUNT(*) FROM identity.users WHERE id = @id",
            _seedConnection);
        probe.Parameters.AddWithValue("@id", dueUser3);
        var c = (long)(await probe.ExecuteScalarAsync())!;
        remainingCount = (int)c;
        remainingCount.Should().Be(1,
            "the third due user MUST remain in the DB after the BatchLimit-truncated cycle");
    }

    [Fact]
    public async Task RunOnceAsync_PicksUpHotReloadOfOptions()
    {
        // Slice 11.3 RED: IOptionsMonitor hot-reload. First cycle runs
        // with the initial BatchLimit (3); we then swap the option to
        // BatchLimit = 0 to provoke a no-op second cycle. We assert the
        // second cycle processes 0 rows because the new value is read
        // — if the BackgroundService cached the options at startup, the
        // second cycle would still process the full set.
        var initial = new HardDeleteSweepOptions
        {
            BatchLimit = 3,
            InitialDelaySeconds = 60,
            IntervalHours = 24,
            GracePeriodDays = 30,
            MaxJitterMinutes = 0,
        };
        var (sut, deletors, monitor) = BuildSut(_connectionString, initial);

        var tenantId = await EnsureTenantAsync();
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        await SeedUserAsync(u1, tenantId, UserStatus.ScheduledHardDelete, DateTimeOffset.UtcNow.AddDays(-1));
        await SeedUserAsync(u2, tenantId, UserStatus.ScheduledHardDelete, DateTimeOffset.UtcNow.AddDays(-1));

        foreach (var d in deletors)
            d.CascadeHardDeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(1);

        await sut.RunOnceAsync(CancellationToken.None);
        var firstCycleCalls = deletors.Sum(d =>
            d.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "CascadeHardDeleteAsync"));
        firstCycleCalls.Should().Be(4,
            "first cycle with BatchLimit=3 handles 2 due users × 2 deletors = 4 calls");

        // Hot-reload: BatchLimit dropped to 0 (invalid for production,
        // valid for the test — we're proving the BG service re-reads
        // the option, not that the config is sane).
        monitor.Set(new HardDeleteSweepOptions
        {
            BatchLimit = 0,
            InitialDelaySeconds = 60,
            IntervalHours = 24,
            GracePeriodDays = 30,
            MaxJitterMinutes = 0,
        });

        await sut.RunOnceAsync(CancellationToken.None);
        var secondCycleCalls = deletors.Sum(d =>
            d.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "CascadeHardDeleteAsync"))
            - firstCycleCalls;
        secondCycleCalls.Should().Be(0,
            "second cycle must NOT invoke any deletor because the new BatchLimit=0 caps the cycle to zero rows — "
            + "this proves `RunOnceAsync` reads `_options.CurrentValue` per cycle, not at startup");
    }

    /// <summary>IServiceScopeFactory for tests — wraps the root provider
    /// in one scope so the per-cycle CreateScope() returns the same
    /// provider (state persists across cycles, which is the simplest
    /// deterministic fixture).</summary>
    private sealed class TestScopeFactory : IServiceScopeFactory
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

    /// <summary>Test-only <see cref="IOptionsMonitor{T}"/> whose value
    /// can be re-set after construction. Used to prove the BG service
    /// re-reads on every cycle (hot-reload).</summary>
    private sealed class SettableOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public SettableOptionsMonitor(T value) { CurrentValue = value; }
        public T CurrentValue { get; private set; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
        public void Set(T value) => CurrentValue = value;
    }
}
