using FluentAssertions;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace JadeCapital.Api.IntegrationTests.Audit;

public sealed class AuditPartitionMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("postgres")
        .WithUsername("postgres")
        .WithPassword("audit-partition-test")
        .Build();

    private static readonly string Migration = Locate("infrastructure/postgres/migrations/0040_partition_audit_events.sql");
    private static readonly string Rollback = Locate("infrastructure/postgres/rehearsals/0040_rollback_audit_events.sql");

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task AuditPartition_FreshDatabase_IsWritableAndDeterministic()
    {
        await using var db = await CreateDatabaseAsync();
        await CreateSourceAsync(db);
        await ExecuteFileAsync(db, Migration);

        (await ScalarAsync<string>(db, "SELECT relkind::text FROM pg_class WHERE oid='audit.events'::regclass"))
            .Should().Be("p");
        (await ScalarAsync<long>(db, "SELECT count(*) FROM pg_inherits WHERE inhparent='audit.events'::regclass"))
            .Should().Be(3, "an empty source gets current, next, and DEFAULT partitions");

        var anchor = await ScalarAsync<DateTime>(db, "SELECT partition_anchor FROM audit.migration_0040_state WHERE singleton");
        await InsertAsync(db, Guid.NewGuid(), new DateTimeOffset(anchor, TimeSpan.Zero).AddDays(1));
        await InsertAsync(db, Guid.NewGuid(), new DateTimeOffset(anchor, TimeSpan.Zero).AddMonths(1).AddDays(1));
        await InsertAsync(db, Guid.NewGuid(), new DateTimeOffset(anchor, TimeSpan.Zero).AddYears(10));

        var routes = await QueryAsync<string>(db,
            "SELECT tableoid::regclass::text FROM audit.events ORDER BY occurred_at");
        routes.Should().ContainInOrder(
            $"audit.events_{anchor:yyyyMM}",
            $"audit.events_{anchor.AddMonths(1):yyyyMM}",
            "audit.events_default");
    }

    [Fact]
    public async Task AuditPartition_UpgradeCheckpointAndRerun_PreservesExactRowsAndSource()
    {
        await using var db = await CreateDatabaseAsync();
        await CreateSourceAsync(db);
        var historical = DateTimeOffset.UtcNow.AddMonths(-8);
        await InsertAsync(db, Guid.NewGuid(), historical, "historical");
        await InsertAsync(db, Guid.NewGuid(), DateTimeOffset.UtcNow, "current");

        var checkpoint = async () => await ExecuteFileAsync(db, Migration,
            "SET audit.migration_0040_stop_after_prepare = 'on';");
        await checkpoint.Should().ThrowAsync<PostgresException>();
        (await ScalarAsync<string>(db, "SELECT state FROM audit.migration_0040_state WHERE singleton"))
            .Should().Be("PREPARED");
        (await ScalarAsync<string>(db, "SELECT relkind::text FROM pg_class WHERE oid='audit.events'::regclass"))
            .Should().Be("r");

        await ExecuteFileAsync(db, Migration, "RESET audit.migration_0040_stop_after_prepare;");
        await ExecuteFileAsync(db, Migration);

        (await ScalarAsync<long>(db, "SELECT count(*) FROM audit.events")).Should().Be(2);
        (await ScalarAsync<long>(db, "SELECT count(*) FROM audit.events_unpartitioned_0040")).Should().Be(2);
        (await ScalarAsync<long>(db, ExactDifferenceSql("audit.events", "audit.events_unpartitioned_0040"))).Should().Be(0);
        (await ScalarAsync<string>(db, "SELECT state FROM audit.migration_0040_state WHERE singleton"))
            .Should().Be("ACTIVE");
    }

    [Fact]
    public async Task AuditPartition_EfIdentityAndRetention_UseCompositePairsAcrossPartitions()
    {
        await using var db = await CreateDatabaseAsync();
        await CreateSourceAsync(db);
        await InsertAsync(db, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMonths(-6));
        await ExecuteFileAsync(db, Migration);

        var options = new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(db.ConnectionString).Options;
        await using var context = new AuditDbContext(options);
        var sharedId = Guid.NewGuid();
        var expiredHistorical = Event(sharedId, DateTimeOffset.UtcNow.AddMonths(-5));
        var retainedCurrent = Event(sharedId, DateTimeOffset.UtcNow);
        var expiredDefault = Event(Guid.NewGuid(), DateTimeOffset.UtcNow.AddYears(-20));
        context.AuditEvents.AddRange(expiredHistorical, retainedCurrent, expiredDefault);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        (await context.AuditEvents.CountAsync(e => e.Id == sharedId)).Should().Be(2);

        var deleted = await new AuditRetentionService(context)
            .PurgeOldAsync(DateTimeOffset.UtcNow.AddMonths(-1), 10, CancellationToken.None);
        deleted.Should().Be(3, "the seed plus two expired rows span monthly and DEFAULT partitions");
        (await context.AuditEvents.CountAsync(e => e.Id == sharedId)).Should().Be(1);
        (await context.AuditEvents.SingleAsync(e => e.Id == sharedId)).OccurredAt
            .Should().BeCloseTo(retainedCurrent.OccurredAt, TimeSpan.FromMicroseconds(1));
        await InsertAsync(db, Guid.NewGuid(), DateTimeOffset.UtcNow, "still-writable");
    }

    [Fact]
    public async Task AuditPartition_PostWriteRollback_ReconcilesThenMigrationConverges()
    {
        await using var db = await CreateDatabaseAsync();
        await CreateSourceAsync(db);
        await InsertAsync(db, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMonths(-2), "before");
        await ExecuteFileAsync(db, Migration);
        await InsertAsync(db, Guid.NewGuid(), DateTimeOffset.UtcNow.AddYears(4), "default");

        await ExecuteFileAsync(db, Rollback);
        (await ScalarAsync<string>(db, "SELECT relkind::text FROM pg_class WHERE oid='audit.events'::regclass"))
            .Should().Be("r");
        (await ScalarAsync<long>(db, ExactDifferenceSql("audit.events", "audit.events_partitioned_0040"))).Should().Be(0);
        await InsertAsync(db, Guid.NewGuid(), DateTimeOffset.UtcNow, "rollback-write");

        await ExecuteFileAsync(db, Migration);
        (await ScalarAsync<string>(db, "SELECT relkind::text FROM pg_class WHERE oid='audit.events'::regclass"))
            .Should().Be("p");
        (await ScalarAsync<long>(db, "SELECT count(*) FROM audit.events")).Should().Be(3);
    }

    [Fact]
    public void AuditPartition_RejectedDraftSignatures_FailClosed()
    {
        var migration = File.ReadAllText(Migration);
        var prior = File.ReadAllText(Locate("infrastructure/postgres/migrations/0039_add_existing_user_aggregate_columns.sql"));
        prior.Should().NotContain("PARTITION BY", "0039 is already occupied and must never be promoted as the rejected draft");
        migration.Should().Contain("EXCEPT ALL").And.Contain("ACCESS EXCLUSIVE");
        migration.Should().NotContain("2026-08").And.NotContain("2026-11");
        migration.Should().NotContain("DROP TABLE").And.NotContain("CASCADE");
    }

    private async Task<NpgsqlConnection> CreateDatabaseAsync()
    {
        var name = $"audit_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await create.ExecuteNonQueryAsync();
        }
        var cs = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = name,
            PersistSecurityInfo = true
        }.ConnectionString;
        var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        return connection;
    }

    private static Task CreateSourceAsync(NpgsqlConnection db) => ExecuteAsync(db, """
        CREATE SCHEMA audit;
        CREATE TABLE audit.events (
          id uuid PRIMARY KEY, entity_type varchar(80) NOT NULL, entity_id uuid NOT NULL,
          action smallint NOT NULL CHECK (action IN (0,1,2,3,4,5)), tenant_id uuid, user_id uuid,
          changes_json jsonb, occurred_at timestamptz NOT NULL DEFAULT now());
        CREATE INDEX ix_audit_events_entity ON audit.events(entity_type, entity_id);
        CREATE INDEX ix_audit_events_tenant_time ON audit.events(tenant_id, occurred_at DESC);
        CREATE INDEX ix_audit_events_user ON audit.events(user_id);
        """);

    private static async Task ExecuteFileAsync(NpgsqlConnection db, string path, string prefix = "")
        => await ExecuteAsync(db, prefix + await File.ReadAllTextAsync(path));

    private static async Task ExecuteAsync(NpgsqlConnection db, string sql)
    {
        await using var command = new NpgsqlCommand(sql, db) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    private static Task InsertAsync(NpgsqlConnection db, Guid id, DateTimeOffset occurredAt, string entity = "event")
        => ExecuteAsync(db, $"INSERT INTO audit.events(id,entity_type,entity_id,action,occurred_at) VALUES ('{id}', '{entity}', gen_random_uuid(), 0, '{occurredAt:O}')");

    private static AuditEvent Event(Guid id, DateTimeOffset at) => AuditEvent.FromTrusted(
        id, "retention", Guid.NewGuid(), AuditAction.Updated, null, null, null, at);

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection db, string sql)
    {
        await using var command = new NpgsqlCommand(sql, db);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<List<T>> QueryAsync<T>(NpgsqlConnection db, string sql)
    {
        await using var command = new NpgsqlCommand(sql, db);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<T>();
        while (await reader.ReadAsync()) values.Add(reader.GetFieldValue<T>(0));
        return values;
    }

    private static string ExactDifferenceSql(string left, string right) => $"""
        SELECT count(*) FROM (
          (SELECT id,entity_type,entity_id,action,tenant_id,user_id,changes_json,occurred_at FROM {left}
           EXCEPT ALL
           SELECT id,entity_type,entity_id,action,tenant_id,user_id,changes_json,occurred_at FROM {right})
          UNION ALL
          (SELECT id,entity_type,entity_id,action,tenant_id,user_id,changes_json,occurred_at FROM {right}
           EXCEPT ALL
           SELECT id,entity_type,entity_id,action,tenant_id,user_id,changes_json,occurred_at FROM {left})
        ) difference
        """;

    private static string Locate(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(relative);
    }
}
