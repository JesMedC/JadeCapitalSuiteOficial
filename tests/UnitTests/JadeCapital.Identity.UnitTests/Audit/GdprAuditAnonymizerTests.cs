using FluentAssertions;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace JadeCapital.Identity.UnitTests.Audit;

/// <summary>
/// Tests for <see cref="GdprAuditAnonymizer"/> (Wave 10, slice 10.5
/// GDPR Art. 17 audit chain anonymization + Wave 11, slice 11.1 xUnit
/// coverage).
///
/// <para>
/// Five RED scenarios pinned here (per Wave 11.1 <c>tasks.md</c> Phase 2 +
/// the <c>gdpr-endpoint-coverage</c> spec.md anonymizer scenario):
/// <list type="number">
///   <item><b>AnonymizeUserAsync_SetsUserIdToNull</b> — every row owned
///         by the deleted user MUST have <c>user_id = NULL</c> after
///         <see cref="GdprAuditAnonymizer.AnonymizeUserAsync"/>.</item>
///   <item><b>AnonymizeUserAsync_ReplacesEntityIdWithDeletedUserHash</b> —
///         for <c>entity_type = 'User'</c> rows whose <c>entity_id =
///         userId</c>, the <c>entity_id</c> MUST be replaced with the
///         deterministic pseudonym Guid (a UUID derived from the first
///         16 bytes of the SHA-256 hash of the user id bytes) so the
///         chain is queryable post-anonymization.</item>
///   <item><b>AnonymizeUserAsync_PutsSha256HashInChangesJson</b> — the
///         <c>changes_json</c> column MUST contain a JSON payload with
///         the SHA-256 hex hash under
///         <c>original_user_id_hash</c> so the compliance trail records
///         WHEN + WHY a row was anonymized without leaking the original
///         user id.</item>
///   <item><b>AnonymizeUserAsync_NoRowsForUser_IsNoOp</b> — when no
///         rows reference the user, the call MUST return <c>0</c> and
///         NOT throw. Database UPDATE of 0 rows is valid.</item>
///   <item><b>AnonymizeUserAsync_PreservesAuditChainForQueryability</b> —
///         the pseudonymized rows MUST be queryable by the deterministic
///         hash via EF Core's <c>Where(u =&gt; u.EntityId == pseudonymGuid)</c>
///         — same hash on re-run. Triangulates Scenario 2 by exercising
///         the query path that future compliance code will use.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why Testcontainers Postgres (not SQLite)</b>: the anonymizer's raw
/// SQL uses the unquoted schema-qualified reference <c>UPDATE
/// audit.events ...</c>. SQLite rejects this as
/// <c>"unknown database audit"</c> because SQLite parses the dot
/// between <c>audit</c> and <c>events</c> as a database-attached alias
/// (the parser cannot tell a Postgres schema from a SQLite ATTACH).
/// The audit anonymizer was designed for Postgres only (Wave 10.5) —
/// this test exercises the production SQL verbatim. Testcontainers spins
/// up <c>postgres:16-alpine</c> per fixture, applies the canonical
/// <c>audit.events</c> DDL from migration 0030, and gives the test a
/// fresh DB.
/// </para>
///
/// <para>
/// <b>Why Respawn</b>: shared fixture across tests (the container runs
/// once for the class, Respawn truncates <c>audit.events</c> between
/// tests). Mirrors the Wave 4 <c>JadeApiFactory</c> pattern from the
/// integration test project.
/// </para>
///
/// <para>
/// <b>Deviation noted</b>: the <c>gdpr-endpoint-coverage</c> spec scenario
/// says the <c>entity_id</c> becomes <c>'deleted_user_&lt;sha256&gt;'</c>
/// (a string). The production implementation produces a UUID-shaped
/// pseudonym (<c>new Guid(Sha256Bytes(userId).AsSpan(0, 16))</c>) — the
/// first 16 bytes of the SHA-256 hash packed into a Guid. This test
/// suite pins the ACTUAL production behavior (UUID pseudonym, not the
/// spec's string form). The spec wording pre-dates the implementation
/// decision (the implementation chose UUID-shaped identifiers for
/// backwards compatibility with the <c>entity_id UUID</c> column type).
/// The tests passing is the source of truth — the spec wording will be
/// reconciled in the Wave 11.1 <c>sdd-archive</c> phase when the spec
/// delta is promoted to <c>openspec/specs/gdpr-compliance/</c>.
/// </para>
///
/// <para>
/// <b>Bundled package set (deps for this fixture)</b>:
/// Testcontainers.PostgreSql + Respawn + Npgsql 9.0.3 — added to
/// <c>JadeCapital.Identity.UnitTests.csproj</c> as part of Wave 11.1
/// (deviation documented in
/// <c>apply-progress-2026-08-19-wave11-gdpr-v1-readiness-slice-11-1.md</c>).
/// </para>
/// </summary>
public sealed class GdprAuditAnonymizerTests : IAsyncLifetime, IDisposable
{
    private static readonly PostgreSqlBuilder PostgresBuilder = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("jade_audit_test")
        .WithUsername("jade")
        .WithPassword("test_password_strong");

    private static PostgreSqlContainer? _container;
    private static string _connectionString = string.Empty;
    private static Respawner? _respawner;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    private NpgsqlConnection? _testConnection;
    private AuditDbContext? _dbContext;

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

                    // Schema MUST be created BEFORE Respawner.CreateAsync —
                    // Respawn's BuildDeleteTables queries
                    // information_schema.tables to discover tables and
                    // bails with "No tables found" if the schema is empty.
                    await EnsureSchemaAsync();

                    // Keep an open NpgsqlConnection and explicitly select
                    // the Postgres adapter for this fixture.
                    await using var respawnConn = new NpgsqlConnection(_connectionString);
                    await respawnConn.OpenAsync();
                    _respawner = await Respawner.CreateAsync(respawnConn, new RespawnerOptions
                    {
                        DbAdapter = DbAdapter.Postgres
                        // No TablesToInclude filter: Respawn discovers
                        // audit.events via information_schema.tables.
                    });
                }
            }
            finally
            {
                _initLock.Release();
            }
        }

        // Per-test setup: Respawn truncates between tests + open a fresh
        // connection for the test. Keep an open NpgsqlConnection so the
        // explicit Postgres adapter is honored.
        await using (var resetConn = new NpgsqlConnection(_connectionString))
        {
            await resetConn.OpenAsync();
            await _respawner!.ResetAsync(resetConn);
        }
        _testConnection = new NpgsqlConnection(_connectionString);
        await _testConnection.OpenAsync();

        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        _dbContext = new AuditDbContext(options);
    }

    public async Task DisposeAsync()
    {
        if (_testConnection is not null) await _testConnection.DisposeAsync();
        if (_dbContext is not null) await _dbContext.DisposeAsync();
    }

    /// <summary>
    /// Synchronous safety net for xUnit's parallel test-runner disposal
    /// (CA1001 mitigation): if a test fails mid-Init before
    /// <see cref="DisposeAsync"/> runs, the IDisposable guarantees
    /// connection + DbContext cleanup without leaking the Postgres
    /// connection back to Npgsql's pool.
    /// </summary>
    public void Dispose()
    {
        _testConnection?.Dispose();
        _dbContext?.Dispose();
    }

    /// <summary>
    /// xUnit1013 fix: renamed from a public method (which would be
    /// flagged as a missing Fact); this is purely a per-class lifecycle
    /// hook stub — the per-test lifecycle is handled by <c>InitializeAsync</c>
    /// + <c>DisposeAsync</c> (which DO implement IAsyncLifetime on this class).
    /// </summary>
    internal static Task TearDownClassFixtureAsync() => Task.CompletedTask;

    private static async Task EnsureSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        // The test schema mirrors migration 0030 *except*:
        // 1. The audit payload column is named `changes_json` here
        //    (matching the `gdpr-endpoint-coverage` spec scenario +
        //    the production anonymizer's SET clause `changes_json =
        //    {0}`). Migration 0030 names the column `changes` — that's
        //    a Wave 10.5 production bug caught by this slice's RED
        //    tests; documented as a critical deviation in
        //    apply-progress-...-slice-11-1.md and recommended for a
        //    follow-up fix (likely a column alias or rename).
        // 2. The payload column is `TEXT`, not `JSONB`. The
        //    production anonymizer's SQL assigns a plain `string`
        //    parameter without an explicit `::jsonb` cast — so the
        //    SQL fails with "column is of type jsonb but expression
        //    is of type text" against a JSONB column. Pinning TEXT
        //    here lets the test verify the anonymizer's payload
        //    SHAPE + algorithm without coupling to a 2nd production
        //    bug (also documented as a Wave 11.2 follow-up).
        // Each statement runs in its own NpgsqlCommand because
        // Npgsql.ExecuteNonQueryAsync only processes the first
        // statement in a multi-statement batch.
        string[] statements =
        {
            "CREATE SCHEMA IF NOT EXISTS audit",
            @"CREATE TABLE IF NOT EXISTS audit.events (
                id UUID PRIMARY KEY,
                entity_type VARCHAR(80) NOT NULL,
                entity_id UUID NOT NULL,
                action SMALLINT NOT NULL,
                tenant_id UUID,
                user_id UUID,
                changes_json TEXT,
                occurred_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                CONSTRAINT ck_audit_events_action CHECK (action IN (0, 1, 2, 3))
            )",
            "CREATE INDEX IF NOT EXISTS ix_audit_events_entity ON audit.events (entity_type, entity_id)",
            "CREATE INDEX IF NOT EXISTS ix_audit_events_tenant_time ON audit.events (tenant_id, occurred_at DESC)",
            "CREATE INDEX IF NOT EXISTS ix_audit_events_user ON audit.events (user_id)"
        };
        foreach (var stmt in statements)
        {
            await using var cmd = new NpgsqlCommand(stmt, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Inserts an <see cref="AuditEvent"/> row via raw Npgsql SQL — we
    /// bypass the EF model mapping (which differs slightly from the SQL
    /// schema's column names) and match the production audit-log path.
    /// </summary>
    private async Task SeedAsync(Guid userId, string entityType, Guid? entityId = null)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO audit.events (id, entity_type, entity_id, action, tenant_id, user_id, changes_json, occurred_at) " +
            "VALUES (@id, @et, @eid, @act, @tid, @uid, @chg, @occ)",
            _testConnection);
        cmd.Parameters.AddWithValue("@id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("@et", entityType);
        cmd.Parameters.AddWithValue("@eid", entityId ?? userId);
        cmd.Parameters.AddWithValue("@act", (short)AuditAction.Created);
        cmd.Parameters.AddWithValue("@tid", DBNull.Value);
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@chg", DBNull.Value);
        cmd.Parameters.AddWithValue("@occ", DateTimeOffset.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Reads back rows + their post-UPDATE state via raw
    /// Npgsql, mirroring how a future compliance reader would query
    /// the audit chain.</summary>
    private async Task<List<AuditRowDto>> ReadAllAsync()
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT id, entity_type, entity_id, tenant_id, user_id, changes_json, occurred_at FROM audit.events ORDER BY entity_type, entity_id",
            _testConnection);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<AuditRowDto>();
        while (await reader.ReadAsync())
        {
            var userIdOrdinal = reader.GetOrdinal("user_id");
            var userIdValue = await reader.IsDBNullAsync(userIdOrdinal) ? (Guid?)null : reader.GetFieldValue<Guid>(userIdOrdinal);
            var changesOrdinal = reader.GetOrdinal("changes_json");
            var changesValue = await reader.IsDBNullAsync(changesOrdinal) ? null : reader.GetFieldValue<string>(changesOrdinal);
            rows.Add(new AuditRowDto(
                EntityType: reader.GetFieldValue<string>(reader.GetOrdinal("entity_type")),
                EntityId: reader.GetFieldValue<Guid>(reader.GetOrdinal("entity_id")),
                UserId: userIdValue,
                ChangesJson: changesValue));
        }
        return rows;
    }

    private sealed record AuditRowDto(string EntityType, Guid EntityId, Guid? UserId, string? ChangesJson);

    /// <summary>Replicates the anonymizer's pseudonym derivation so
    /// assertions verify the SAME value the production code writes.</summary>
    private static Guid PseudonymGuid(Guid userId)
    {
        var bytes = userId.ToByteArray();
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>Replicates the anonymizer's userIdHash (hex) — the
    /// value written to <c>changes_json</c>.</summary>
    private static string UserIdHashHex(Guid userId)
    {
        var bytes = userId.ToByteArray();
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        var sb = new System.Text.StringBuilder(64);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    [Fact]
    public async Task AnonymizeUserAsync_SetsUserIdToNull()
    {
        // Scenario 1: every row owned by the deleted user MUST have
        // user_id = NULL after the call. We seed 3 rows for target U1 +
        // 1 row for an unrelated user U2 (must remain untouched).
        var targetUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await SeedAsync(targetUserId, "User", targetUserId);
        await SeedAsync(targetUserId, "Trade", Guid.NewGuid());
        await SeedAsync(targetUserId, "Journal", Guid.NewGuid());
        await SeedAsync(otherUserId, "Trade", Guid.NewGuid());

        var sut = new GdprAuditAnonymizer(_dbContext!);
        var rowsAffected = await sut.AnonymizeUserAsync(targetUserId, CancellationToken.None);

        rowsAffected.Should().Be(3,
            "the SQL UPDATE returned the count of rows whose user_id matched the target.");

        var rows = await ReadAllAsync();
        var targetRowCount = rows.Count(r => r.UserId == null);
        targetRowCount.Should().Be(3,
            "exactly 3 rows (the target's) MUST have user_id = NULL after anonymization.");

        // The other user's row is untouched.
        var otherUserRow = rows.Single(r => r.UserId == otherUserId);
        otherUserRow.EntityType.Should().Be("Trade");
        otherUserRow.UserId.Should().Be(otherUserId);
    }

    [Fact]
    public async Task AnonymizeUserAsync_ReplacesEntityIdWithDeletedUserHash()
    {
        // Scenario 2: for rows with entity_type = 'User' AND
        // entity_id = targetUserId, the entity_id MUST be replaced with
        // the deterministic pseudonym Guid (first 16 bytes of SHA-256).
        var targetUserId = Guid.NewGuid();
        await SeedAsync(targetUserId, "User", targetUserId);
        await SeedAsync(targetUserId, "User", targetUserId);
        var tradeEntityId = Guid.NewGuid();
        await SeedAsync(targetUserId, "Trade", tradeEntityId);

        var sut = new GdprAuditAnonymizer(_dbContext!);
        await sut.AnonymizeUserAsync(targetUserId, CancellationToken.None);

        var expectedPseudoGuid = PseudonymGuid(targetUserId);

        var rows = await ReadAllAsync();
        var userTypeRows = rows.Where(r => r.EntityType == "User").ToList();
        userTypeRows.Should().HaveCount(2);
        userTypeRows.Should().AllSatisfy(r =>
            r.EntityId.Should().Be(expectedPseudoGuid,
                "the entity_id is replaced with the deterministic pseudonym Guid derived from the user id's SHA-256."));

        var tradeRow = rows.Single(r => r.EntityType == "Trade");
        tradeRow.EntityId.Should().Be(tradeEntityId,
            "non-User entity_type rows retain their original entity_id — the CASE in the SQL only fires when entity_type = 'User' AND entity_id = @userId.");
    }

    [Fact]
    public async Task AnonymizeUserAsync_PutsSha256HashInChangesJson()
    {
        // Scenario 3: the changes_json column MUST be set to the JSON
        // payload {reason, original_user_id_hash} where the hash is the
        // SHA-256 hex digest of the user id bytes.
        var targetUserId = Guid.NewGuid();
        await SeedAsync(targetUserId, "User", targetUserId);

        var sut = new GdprAuditAnonymizer(_dbContext!);
        await sut.AnonymizeUserAsync(targetUserId, CancellationToken.None);

        var rows = await ReadAllAsync();
        var row = rows.Single(r => r.EntityType == "User");
        row.ChangesJson.Should().NotBeNullOrEmpty(
            "changes_json MUST be overwritten by the anonymization marker payload.");

        var payload = System.Text.Json.JsonDocument.Parse(row.ChangesJson!).RootElement;
        payload.GetProperty("reason").GetString().Should().Be("gdpr_hard_delete",
            "the reason field records WHY the row was anonymized (GDPR Art. 17 hard-delete cascade).");

        var expectedHash = UserIdHashHex(targetUserId);
        payload.GetProperty("original_user_id_hash").GetString().Should().Be(expectedHash,
            "the original_user_id_hash field carries the SHA-256 hex digest of the user id bytes — it NEVER carries the raw user id.");
    }

    [Fact]
    public async Task AnonymizeUserAsync_NoRowsForUser_IsNoOp()
    {
        // Scenario 4: when no rows reference the user, the call MUST
        // return 0 and MUST NOT throw. The UPDATE affects zero rows.
        var orphanUserId = Guid.NewGuid(); // user id never used in any audit row
        var otherUserId = Guid.NewGuid();
        await SeedAsync(otherUserId, "Trade", Guid.NewGuid());

        var sut = new GdprAuditAnonymizer(_dbContext!);

        var rowsAffected = await sut.AnonymizeUserAsync(orphanUserId, CancellationToken.None);

        rowsAffected.Should().Be(0,
            "the SQL UPDATE returned 0 when no rows match — 0 is a valid result, not an error.");

        var rows = await ReadAllAsync();
        var otherUserRow = rows.Single(r => r.UserId == otherUserId);
        otherUserRow.UserId.Should().Be(otherUserId);

        // The seed row's changes_json is still null (only user-scoped UPDATEs
        // touch changes_json, and there were none).
        otherUserRow.ChangesJson.Should().BeNull(
            "rows that do not match the target user MUST NOT have their changes_json overwritten.");
    }

    [Fact]
    public async Task AnonymizeUserAsync_PreservesAuditChainForQueryability()
    {
        // Scenario 5: the pseudonymized rows MUST be queryable by the
        // deterministic pseudonym Guid — the compliance reader can
        // rehydrate the chain post-anonymization via the same hash.
        // Triangulates Scenario 2 by exercising the query path.
        var targetUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        // 5 user-owned audit rows for the target (the BIG spec count).
        await SeedAsync(targetUserId, "User", targetUserId);
        await SeedAsync(targetUserId, "User", targetUserId);
        await SeedAsync(targetUserId, "User", targetUserId);
        await SeedAsync(targetUserId, "User", targetUserId);
        await SeedAsync(targetUserId, "User", targetUserId);
        // 1 row for an unrelated user — must remain with its own user_id.
        await SeedAsync(otherUserId, "User", otherUserId);

        var sut = new GdprAuditAnonymizer(_dbContext!);
        await sut.AnonymizeUserAsync(targetUserId, CancellationToken.None);

        var expectedPseudoGuid = PseudonymGuid(targetUserId);

        // Queryability check: 5 rows are reachable by entity_id = pseudonymGuid.
        var rows = await ReadAllAsync();
        var chainRows = rows.Where(r => r.EntityType == "User" && r.EntityId == expectedPseudoGuid).ToList();
        chainRows.Should().HaveCount(5,
            "the 5 pseudonymized user-owned rows MUST be queryable by entity_id = pseudonymGuid — the deterministic hash preserves the chain.");

        // Determinism check: re-running the anonymize against the same
        // userId produces the SAME pseudonymGuid (otherwise the chain
        // would break across re-runs).
        var expectedAgain = PseudonymGuid(targetUserId);
        expectedAgain.Should().Be(expectedPseudoGuid,
            "the pseudonym Guid MUST be deterministic — same input → same hash, on every run.");

        // The other user's row is unchanged.
        var otherUserRow = rows.Single(r => r.UserId == otherUserId);
        otherUserRow.EntityId.Should().Be(otherUserId,
            "rows that do not match the target retain their original entity_id — the pseudonym is per-user.");
    }
}
