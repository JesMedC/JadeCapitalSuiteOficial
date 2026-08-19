using FluentAssertions;
using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Identity.Infrastructure.BackgroundServices;
using JadeCapital.Identity.Infrastructure.Cascade;
using JadeCapital.Identity.Infrastructure.Configuration;
using JadeCapital.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace JadeCapital.Api.IntegrationTests.BackgroundServices;

/// <summary>
/// Tests for the Wave 11.2a Bug #2 hotfix — the
/// <c>HardDeleteSweepBackgroundService</c>'s EF mapping for
/// <c>User.ScheduledHardDeleteAt</c>.
///
/// <para>
/// RED scenarios pinned here (per Wave 11.2a <c>tasks.md</c> + the Wave
/// 11.1 apply-progress "Bug #2" deviation):
/// <list type="number">
///   <item><b>RunOnceAsync_LinqDoesNotThrow_OnRealMigrationSchema</b> —
///         the production <see
///         cref="HardDeleteSweepBackgroundService.RunOnceAsync"/>'s LINQ
///         (<c>db.Users.Where(u =&gt; u.ScheduledHardDeleteAt &lt;= cutoff)</c>)
///         MUST execute without throwing against the real migration 0033
///         schema. Pre-fix, the LINQ throws at translation/execution
///         with <c>42703: column u.ScheduledHardDeleteAt does not exist</c>
///         because migration 0033 hadn't added the column AND the EF
///         mapping was missing. This RED pins the schema-side fix.</item>
///   <item><b>RunOnceAsync_SelectsOnlyDueUsers</b> —
///         the BackgroundService MUST invoke the cascade orchestrator
///         exactly ONCE for users in <c>ScheduledHardDelete</c> status
///         with <c>scheduled_hard_delete_at &lt;= UtcNow</c>. Users with
///         future scheduled dates or Active status MUST NOT be selected.
///         This pins the EF mapping + column-side fix together — the
///         LINQ must not only NOT throw but also filter correctly.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why this test class uses the shared <see cref="JadeApiFactory"/></b>:
/// the factory's <c>ApplyMigrationAsync</c> applies the canonical
/// migration chain (including the new 0033 that adds the
/// <c>scheduled_hard_delete_at</c> column + widens
/// <c>ck_users_status</c>). The test seeds users via raw SQL + verifies
/// the BackgroundService's LINQ works against the real schema. This
/// proves the column + EF mapping alignment is end-to-end real, not
/// just code-level without DB support.
/// </para>
///
/// <para>
/// <b>Why we use a NSubstitute orchestrator</b>: the BackgroundService
/// is the unit under test. The orchestrator + per-module deletors are
/// separate concerns (covered by their own test classes). NSubstitute
/// keeps the test focused on the BackgroundService's per-cycle LINQ +
/// selection logic. The substitute is configured to return 1 (success)
/// so the per-user cascade "completes" and the user-row delete
/// delegate executes.
/// </para>
///
/// <para>
/// <b>Respawn between tests</b>: each test truncates
/// <c>identity.users</c> + <c>identity.tenants</c> between runs so the
/// seeded data doesn't leak. The Personal tenant (created by 0029) is
/// preserved — we DELETE only seeded test users.
/// </para>
/// </summary>
public sealed class HardDeleteSweepBackgroundServiceEfMappingTests
    : IClassFixture<JadeApiFactory>, IAsyncLifetime, IDisposable
{
    private readonly JadeApiFactory _factory;
    private NpgsqlConnection? _seedConn;

    public HardDeleteSweepBackgroundServiceEfMappingTests(JadeApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _seedConn = new NpgsqlConnection(_factory.PostgresConnectionString);
        await _seedConn.OpenAsync();

        // DEBUG: verify the column exists — throw if not so we get a
        // clear error instead of failing at the INSERT line.
        await using (var cmd = new NpgsqlCommand(@"
            SELECT column_name FROM information_schema.columns
            WHERE table_schema = 'identity' AND table_name = 'users' AND column_name = 'scheduled_for_hard_delete_at'", _seedConn))
        {
            var result = await cmd.ExecuteScalarAsync();
            if (result == null)
            {
                throw new InvalidOperationException(
                    $"DEBUG: scheduled_for_hard_delete_at column MISSING. Connection: {_factory.PostgresConnectionString}");
            }
        }

        // Clean only the seeded test users — preserve the sentinel
        // (id ...0002) and the Personal tenant created by 0029. We use a
        // WHERE id != sentinel pattern so the FK reference stays valid.
        await using (var cmd = new NpgsqlCommand(
            "DELETE FROM identity.users WHERE id != '00000000-0000-0000-0000-000000000002'::uuid", _seedConn))
        {
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_seedConn is not null) await _seedConn.DisposeAsync();
    }

    public void Dispose()
    {
        // CA1001 safety net: synchronous fallback if DisposeAsync is
        // not invoked (test runner aborts mid-Init).
        _seedConn?.Dispose();
    }

    /// <summary>
    /// Seeds a user row directly via raw SQL. Uses the canonical column
    /// names from migration 0033 — bypassing EF here means the seed is
    /// independent of the EF mapping (which is the unit under test).
    /// </summary>
    private async Task SeedUserAsync(Guid userId, string status, DateTimeOffset? scheduledHardDeleteAt)
    {
        // Resolve a Personal tenant id (created by 0029). Use the
        // canonical id ...1111 so we don't have to query.
        var personalTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // Ensure the Personal tenant exists (idempotent — 0029 created it,
        // but tests may have been run after a partial truncate).
        await using (var ensureTenant = new NpgsqlCommand(@"
            INSERT INTO identity.tenants (id, name, slug, owner_user_id, plan, status, created_at)
            VALUES (@id, 'Personal', 'personal-default', '00000000-0000-0000-0000-000000000002'::uuid, 0, 0, now())
            ON CONFLICT (slug) DO NOTHING", _seedConn))
        {
            ensureTenant.Parameters.AddWithValue("@id", personalTenantId);
            await ensureTenant.ExecuteNonQueryAsync();
        }

        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO identity.users (
                id, tenant_id, email, display_name, password_hash, role, status,
                email_confirmed_at, failed_login_count, session_version,
                attachment_quota_bytes, attachment_used_bytes,
                scheduled_hard_delete_at, created_at, updated_at
            ) VALUES (
                @id, @tid, @email, @dn, '!', 1, @status,
                now(), 0, 1, 0, 0,
                @sched, now(), now()
            )", _seedConn);
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.Parameters.AddWithValue("@tid", personalTenantId);
        cmd.Parameters.AddWithValue("@email", $"{userId}@example.test");
        cmd.Parameters.AddWithValue("@dn", "Test User");
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@sched", (object?)scheduledHardDeleteAt ?? DBNull.Value);
        // DEBUG: check columns RIGHT BEFORE the user INSERT.
        await using (var check = new NpgsqlCommand(@"
            SELECT string_agg(column_name, ', ' ORDER BY ordinal_position)
            FROM information_schema.columns
            WHERE table_schema = 'identity' AND table_name = 'users'", _seedConn))
        {
            var columns = (string?)await check.ExecuteScalarAsync();
            throw new InvalidOperationException(
                $"DEBUG columns BEFORE insert: {columns ?? "NONE"}");
        }
    }

    /// <summary>
    /// Builds a BackgroundService instance wired to the factory's
    /// Postgres + a stub orchestrator that observes which user ids are
    /// passed in (and returns 1 to simulate a successful cascade).
    /// </summary>
    private (HardDeleteSweepBackgroundService Sut, RecordingDeletor[] Deletors)
        BuildSut()
    {
        var deletors = new[]
        {
            new RecordingDeletor(),
            new RecordingDeletor()
        };
        var anonymizer = new NoOpAnonymizer();
        var orchestrator = new UserCascadeDeleterOrchestrator(
            deletors, anonymizer, NullLogger<UserCascadeDeleterOrchestrator>.Instance);

        var dbOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(_factory.PostgresConnectionString)
            .Options;

        var services = new ServiceCollection();
        services.AddSingleton(orchestrator);
        services.AddScoped<IdentityDbContext>(_ => new IdentityDbContext(dbOptions));

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = new TestScopeFactory(serviceProvider);

        // Wave 11 slice 11.3 — constructor now takes
        // `IOptionsMonitor<HardDeleteSweepOptions>` so the per-cycle
        // cadence can be tuned via appsettings. Tests drive a default
        // options instance whose values match Wave 10.5's hardcoded
        // values so this assertion (column-mapping LINQ vs real
        // migration 0033) continues to hold without behavioural drift.
        var optionsMonitor = new TestOptionsMonitor<HardDeleteSweepOptions>(
            new HardDeleteSweepOptions());

        var sut = new HardDeleteSweepBackgroundService(
            scopeFactory,
            optionsMonitor,
            NullLogger<HardDeleteSweepBackgroundService>.Instance);

        return (sut, deletors);
    }

    [Fact]
    public async Task RunOnceAsync_LinqDoesNotThrow_OnRealMigrationSchema()
    {
        // Scenario 1: the LINQ `u.ScheduledHardDeleteAt <= cutoff` must
        // compile + execute against the real migration 0033 schema. Pre-fix
        // this throws with `42703: column u.ScheduledHardDeleteAt does not
        // exist`. The RED assertion is that the call DOES NOT throw — i.e.,
        // the column + EF mapping alignment is real.
        var (sut, _) = BuildSut();

        // DEBUG: verify the column right before seeding
        await using (var check = new NpgsqlCommand(@"
            SELECT column_name FROM information_schema.columns
            WHERE table_schema = 'identity' AND table_name = 'users' AND column_name = 'scheduled_for_hard_delete_at'", _seedConn))
        {
            var result = await check.ExecuteScalarAsync();
            if (result == null)
            {
                throw new InvalidOperationException(
                    $"DEBUG: scheduled_hard_delete_at column MISSING in DB. " +
                    $"Connection: {_factory.PostgresConnectionString}. " +
                    $"Schema state: {await GetTableInfoAsync()}");
            }
        }

        // Seed a single user so the LINQ has at least one row to evaluate.
        await SeedUserAsync(Guid.NewGuid(), "ScheduledHardDelete", DateTimeOffset.UtcNow.AddDays(-1));

        var act = async () => await sut.RunOnceAsync(CancellationToken.None);
        await act.Should().NotThrowAsync(
            "the BackgroundService's LINQ (`u.ScheduledHardDeleteAt <= cutoff`) must compile + execute against the real migration 0033 schema — column exists + EF mapping is in place.");
    }

    private async Task<string> GetTableInfoAsync()
    {
        await using var cmd = new NpgsqlCommand(@"
            SELECT column_name FROM information_schema.columns
            WHERE table_schema = 'identity' AND table_name = 'users'
            ORDER BY ordinal_position", _seedConn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var cols = new List<string>();
        while (await reader.ReadAsync())
            cols.Add(reader.GetString(0));
        return string.Join(", ", cols);
    }

    [Fact]
    public async Task RunOnceAsync_SelectsOnlyDueUsers()
    {
        // Scenario 2: the BackgroundService must invoke the cascade
        // orchestrator exactly ONCE for users with status =
        // 'ScheduledHardDelete' AND scheduled_hard_delete_at <= UtcNow.
        // Future-scheduled + Active users MUST NOT be selected.
        var (sut, deletors) = BuildSut();
        var dueUserId = Guid.NewGuid();
        var futureUserId = Guid.NewGuid();
        var activeUserId = Guid.NewGuid();

        var realNow = DateTimeOffset.UtcNow;
        await SeedUserAsync(dueUserId, "ScheduledHardDelete", realNow.AddDays(-1));
        await SeedUserAsync(futureUserId, "ScheduledHardDelete", realNow.AddDays(60));
        await SeedUserAsync(activeUserId, "Active", null);

        await sut.RunOnceAsync(CancellationToken.None);

        // The orchestrator calls each deletor per selected user. Verify
        // the only userId received was dueUserId.
        deletors[0].ReceivedUserIds.Should().ContainSingle(id => id == dueUserId,
            "the due user MUST be selected by the BackgroundService's LINQ.");
        deletors[1].ReceivedUserIds.Should().ContainSingle(id => id == dueUserId,
            "the due user MUST be passed to BOTH deletors by the orchestrator.");

        deletors[0].ReceivedUserIds.Should().NotContain(id => id == futureUserId,
            "the future-scheduled user MUST NOT be selected (scheduled_hard_delete_at > UtcNow).");
        deletors[0].ReceivedUserIds.Should().NotContain(id => id == activeUserId,
            "the Active user MUST NOT be selected (different status).");
    }

    /// <summary>
    /// Single-scope <see cref="IServiceScopeFactory"/> for tests — wraps
    /// the root provider in one scope. The BackgroundService calls
    /// <c>CreateScope()</c> per cycle; the test always gets the same
    /// scope.
    /// </summary>
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

    /// <summary>
    /// Test stub <see cref="IUserCascadeDeletor"/> — records every userId
    /// passed to <c>CascadeHardDeleteAsync</c> + always returns 1 (success).
    /// </summary>
    private sealed class RecordingDeletor : IUserCascadeDeletor
    {
        public List<Guid> ReceivedUserIds { get; } = new();

        public Task<int> CascadeSoftDeleteAsync(Guid userId, CancellationToken ct)
            => Task.FromResult(0);

        public Task<int> CascadeHardDeleteAsync(Guid userId, CancellationToken ct)
        {
            ReceivedUserIds.Add(userId);
            return Task.FromResult(1);
        }
    }

    /// <summary>
    /// Test stub <see cref="IGdprAuditAnonymizer"/> — no-op. The
    /// BackgroundService doesn't care about the anonymizer's result; the
    /// cascade orchestrator is the unit under test.
    /// </summary>
    private sealed class NoOpAnonymizer : IGdprAuditAnonymizer
    {
        public Task<int> AnonymizeUserAsync(Guid userId, CancellationToken ct)
            => Task.FromResult(0);
    }

    /// <summary>
    /// Test-only <see cref="IOptionsMonitor{T}"/> for the Wave 11
    /// slice 11.3 options integration. Returns a fixed
    /// <c>CurrentValue</c>; the on-change hook is not exercised by
    /// this fixture.
    /// </summary>
    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T value) { CurrentValue = value; }
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
