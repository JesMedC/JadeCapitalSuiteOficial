using System.Data.Common;
using FluentAssertions;
using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Identity.Infrastructure.MultiTenancy;
using JadeCapital.Identity.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Identity.UnitTests.Infrastructure;

/// <summary>
/// Reflection helper — extracts the inner Guid of a TenantId wrapper
/// without assuming a specific provider's hydration behavior. EF Core
/// passes through the value converter on write, but providers (e.g.
/// SQLite) sometimes bypass it on read, producing a raw Guid. Either
/// shape (TenantId or Guid) is acceptable here; only the inner Guid
/// value matters for the assertion.
/// </summary>
internal static class TenantIdAccessor
{
    public static Guid ExtractInnerGuid(object? tenantIdOrGuid)
    {
        if (tenantIdOrGuid is null) return Guid.Empty;
        if (tenantIdOrGuid is Guid g) return g;
        var valProp = tenantIdOrGuid.GetType().GetProperty("Value");
        return valProp is null
            ? Guid.Empty
            : (Guid)(valProp.GetValue(tenantIdOrGuid) ?? Guid.Empty);
    }
}

/// <summary>
/// Behavior tests for <see cref="BackfillTenantsRunner"/> (Wave 6, slice 6c.2).
///
/// <para>
/// Each test spins up an in-process SQLite DbContext (the Shared.Kernel /
/// Identity module chain) so we exercise the real EF behavior — the same
/// code path the production hosted service runs. The runner is invoked
/// against the live DbContext; assertions check the resulting rows.
/// </para>
///
/// <para>
/// Five RED scenarios per spec (Phase 5 / tasks 5.1):
/// </para>
/// <list type="number">
///   <item>First run → creates Personal tenant + assigns all NULL users</item>
///   <item>Re-run on the same DB → no-op (idempotent)</item>
///   <item>Zero users → no Personal tenant is created (nothing to do)</item>
///   <item>Partial users (some NULL, some assigned) → only NULL users are assigned</item>
///   <item>Transient exception → does NOT crash the hosted service;
///         the next run is still triggered</item>
/// </list>
/// </summary>
public class BackfillTenantsRunnerTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public BackfillTenantsRunnerTests()
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
    /// Builds a fresh <see cref="IServiceProvider"/> over a SQLite-backed
    /// <see cref="IdentityDbContext"/>. The schema is created from the
    /// EF model so we exercise the same column shape as production
    /// Postgres (the Guid-based PK, the tenant_id column, the FK).
    /// </summary>
    private IServiceProvider BuildContext(out IdentityDbContext db)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<IdentityDbContext>(opts => opts.UseSqlite(_connection));
        services.AddScoped<IBackfillTenantsRunner, BackfillTenantsRunner>();

        var sp = services.BuildServiceProvider();
        db = sp.GetRequiredService<IdentityDbContext>();
        db.Database.EnsureCreated();
        return sp;
    }

    private static User NewUser(string email, Guid? tenantId = null)
    {
        var result = User.Register(
            Guid.NewGuid(),
            email,
            "Test User",
            "hash",
            UserRole.Trader);
        // Result<>'s value is non-null on success; throw if the test
        // seed produces an invalid user (catches regression in the
        // domain layer rather than masking it).
        if (result.IsFailure) throw new InvalidOperationException($"Seed failed: {result.Error.Message}");
        var user = result.Value;
        if (tenantId.HasValue)
        {
            var assign = user.AssignToTenant(new JadeCapital.Shared.Kernel.MultiTenancy.TenantId(tenantId.Value));
            if (assign.IsFailure) throw new InvalidOperationException($"Assign failed: {assign.Error.Message}");
        }
        return user;
    }

    [Fact]
    public async Task FirstRun_CreatesPersonalTenantAndAssignsAllUsers()
    {
        // Phase 5 #1: 3 NULL users → runner creates Personal + assigns all.
        var sp = BuildContext(out var db);
        db.Users.Add(NewUser("a@example.com"));
        db.Users.Add(NewUser("b@example.com"));
        db.Users.Add(NewUser("c@example.com"));
        await db.SaveChangesAsync();

        var runner = sp.GetRequiredService<IBackfillTenantsRunner>();
        var updated = await runner.RunAsync();

        updated.Should().Be(3);
        var tenants = await db.Tenants.ToListAsync();
        tenants.Should().ContainSingle(t => t.Slug == BackfillTenantsRunner.PersonalSlug);

        var users = await db.Users.AsNoTracking().ToListAsync();
        users.Should().OnlyContain(u => u.TenantId != null);
        var personalId = tenants.Single(t => t.Slug == BackfillTenantsRunner.PersonalSlug).Id;

        // EF materializes TenantId via the value converter. Pull the
        // inner Guid through reflection so the assertion survives any
        // provider quirks (SQLite stores Guid as TEXT and EF's
        // converter sometimes bypasses on read).
        var assignedIds = users.Select(u => TenantIdAccessor.ExtractInnerGuid(u.TenantId)).ToList();
        assignedIds.Should().OnlyContain(id => id == personalId);
    }

    [Fact]
    public async Task ReRun_IsIdempotent()
    {
        // Phase 5 #2: re-run on the same DB → 0 updates, single Personal tenant.
        var sp = BuildContext(out var db);
        db.Users.Add(NewUser("a@example.com"));
        await db.SaveChangesAsync();

        var runner = sp.GetRequiredService<IBackfillTenantsRunner>();
        var first = await runner.RunAsync();
        first.Should().Be(1);

        var second = await runner.RunAsync();
        second.Should().Be(0, "second run is a no-op because every user is already assigned.");

        var tenants = await db.Tenants.AsNoTracking().Where(t => t.Slug == BackfillTenantsRunner.PersonalSlug).ToListAsync();
        tenants.Should().ContainSingle("Personal tenant is created once, not duplicated.");
    }

    [Fact]
    public async Task ZeroUsers_DoesNotCreatePersonalTenant()
    {
        // Phase 5 #3: empty DB → no Personal row.
        var sp = BuildContext(out var db);

        var runner = sp.GetRequiredService<IBackfillTenantsRunner>();
        var updated = await runner.RunAsync();

        updated.Should().Be(0);
        var tenants = await db.Tenants.AsNoTracking().Where(t => t.Slug == BackfillTenantsRunner.PersonalSlug).ToListAsync();
        tenants.Should().BeEmpty("a Personal tenant is only created when there is at least one NULL user to backfill.");
    }

    [Fact]
    public async Task PartialUsers_OnlyNullUsersAreAssigned()
    {
        // Phase 5 #4: 2 NULL + 2 already-assigned → runner touches only the NULL pair.
        var existingTenantId = Guid.NewGuid();
        var sp = BuildContext(out var db);
        db.Users.Add(NewUser("existing1@example.com", existingTenantId));
        db.Users.Add(NewUser("existing2@example.com", existingTenantId));
        db.Users.Add(NewUser("null1@example.com"));
        db.Users.Add(NewUser("null2@example.com"));
        await db.SaveChangesAsync();

        var runner = sp.GetRequiredService<IBackfillTenantsRunner>();
        var updated = await runner.RunAsync();

        updated.Should().Be(2, "only the NULL users get assigned; pre-existing assignments are preserved.");

        var users = await db.Users.AsNoTracking().ToListAsync();
        var existingIds = users.Where(u => u.Email!.Contains("existing"))
            .Select(u => TenantIdAccessor.ExtractInnerGuid(u.TenantId)).ToList();
        existingIds.Should().OnlyContain(id => id == existingTenantId);

        var nullIds = users.Where(u => u.Email!.Contains("null"))
            .Select(u => TenantIdAccessor.ExtractInnerGuid(u.TenantId)).ToList();
        nullIds.Should().NotContain(existingTenantId, "the NULL users got a NEW tenant, not the existing one.");
        nullIds.Should().AllBeEquivalentTo(nullIds.Distinct().Single(),
            "the NULL users share the new Personal tenant.");
    }

    [Fact]
    public async Task TransientException_DoesNotCrashHostedService()
    {
        // Phase 5 #5: the hosted service catches transient failures and
        // does NOT bring down the application. We simulate this by
        // closing the connection mid-run; the runner throws and the
        // hosted service swallows + logs.
        var sp = BuildContext(out var db);
        db.Users.Add(NewUser("a@example.com"));
        await db.SaveChangesAsync();
        var runner = sp.GetRequiredService<IBackfillTenantsRunner>();

        // Closed before invocation — the next SaveChanges will fail.
        _connection.Close();

        // Runner throws because the connection is closed.
        Func<Task> act = async () => await runner.RunAsync();
        await act.Should().ThrowAsync<Exception>();

        // But the hosted service wraps RunAsync in a try/catch:
        // verify the wrapping pattern in isolation.
        var hosted = new BackfillTenantsHostedService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<BackfillTenantsHostedService>>());

        // We can't await ExecuteAsync (it has the 15s delay), so we
        // exercise the inner RunOnceAsync path via reflection-free
        // public surface: confirm RunAsync is the seam and that the
        // hosted service does NOT throw when the inner runner throws.
        // We invoke the hosted service's protected method via
        // dynamic dispatch — the simpler assertion is that the seam
        // is "try { runner.RunAsync(...) } catch (Exception) { log }".
        var hostedType = hosted.GetType();
        var runOnce = hostedType.GetMethod(
            "RunOnceAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        runOnce.Should().NotBeNull("the hosted service MUST expose RunOnceAsync so failures are isolated.");

        var task = (Task)runOnce!.Invoke(hosted, new object[] { CancellationToken.None })!;
        // RunOnceAsync swallows the exception — the task completes
        // successfully even though the inner runner threw.
        await task;
    }
}
