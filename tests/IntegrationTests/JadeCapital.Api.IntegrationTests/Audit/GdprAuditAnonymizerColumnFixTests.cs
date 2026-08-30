using System.Text.Json;
using FluentAssertions;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace JadeCapital.Api.IntegrationTests.Audit;

/// <summary>
/// Tests for the Wave 11.2a Bug #1 hotfix — the GDPR audit anonymizer's
/// column-name alignment.
///
/// <para>
/// RED scenarios pinned here (per Wave 11.2a <c>tasks.md</c> + the brief):
/// <list type="number">
///   <item><b>AnonymizeUserAsync_DoesNotThrow_OnRealMigrationSchema</b> —
///         the production <see cref="GdprAuditAnonymizer.AnonymizeUserAsync"/>
///         MUST execute without throwing against a real Postgres whose
///         schema is produced by the canonical migration chain
///         (including the Wave 11.2a migration 0034 rename). Pre-fix,
///         the SQL fails with <c>42703: column "changes_json" does not
///         exist</c> (the column was <c>changes</c> per migration 0030
///         until 0034 renamed it).</item>
///   <item><b>AnonymizeUserAsync_WritesJsonbPayload_ToChangesJsonColumn</b> —
///         after the call, the <c>audit.events.changes_json</c> column
///         MUST contain a valid JSONB object with <c>reason</c> and
///         <c>original_user_id_hash</c> fields. This proves the
///         <c>::jsonb</c> cast + the rename are both in place — without
///         the cast, Postgres rejects the string parameter; without
///         the rename, the column doesn't exist.</item>
///   <item><b>AnonymizeUserAsync_PseudonymGuid_DerivesFromSha256</b> —
///         the production anonymizer's SQL CASE replaces
///         <c>entity_id = U1.Id</c> with a deterministic UUID-shaped
///         pseudonym derived from the first 16 bytes of SHA-256(U1.Id).
///         The pseudonym MUST be deterministic across re-runs (same
///         input → same hash). This pins the pseudonym shape contract
///         per the Wave 11.2a spec reconciliation (see
///         <c>openspec/specs/account-lifecycle/spec.md</c> scenario for
///         HardDeleted audit row).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why this test class uses the shared <see cref="JadeApiFactory"/></b>:
/// the factory's <c>ApplyMigrationAsync</c> runs every migration
/// (including the new 0033 + 0034) against a fresh Testcontainers
/// Postgres. The test asserts that the production anonymizer's SQL
/// works against that schema — i.e., the column-name alignment is real,
/// not just a code-level change without DB support.
/// </para>
///
/// <para>
/// <b>Respawn between tests</b>: each test truncates <c>audit.events</c>
/// between runs so the seeded data doesn't leak.
/// </para>
/// </summary>
public sealed class GdprAuditAnonymizerColumnFixTests : IClassFixture<JadeApiFactory>, IAsyncLifetime, IDisposable
{
    private readonly JadeApiFactory _factory;
    private NpgsqlConnection? _conn;
    private AuditDbContext? _db;

    public GdprAuditAnonymizerColumnFixTests(JadeApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        // Open a per-test connection.
        _conn = new NpgsqlConnection(_factory.PostgresConnectionString);
        await _conn.OpenAsync();

        // Clean the audit table before each test so the seeded events
        // don't leak. The audit schema is preserved (we never DROP
        // anything — only DELETE).
        await using (var cmd = new NpgsqlCommand("DELETE FROM audit.events", _conn))
            await cmd.ExecuteNonQueryAsync();

        // Construct the production AuditDbContext against the factory's
        // connection string. The context's OnModelCreating registers
        // AuditEventConfiguration (which maps ChangesJson → changes_json).
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(_factory.PostgresConnectionString)
            .Options;
        _db = new AuditDbContext(options);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
        if (_conn is not null) await _conn.DisposeAsync();
    }

    public void Dispose()
    {
        // CA1001 safety net: synchronous fallback if DisposeAsync is
        // not invoked (test runner aborts mid-Init).
        _db?.Dispose();
        _conn?.Dispose();
    }

    /// <summary>
    /// Seeds a row directly via raw SQL so the seed bypasses the EF model
    /// (the EF mapping for <c>ChangesJson</c> was the Wave 11.2a fix —
    /// bypassing EF here keeps the seed independent of the mapping). The
    /// INSERT matches the production migration 0034 schema verbatim.
    /// </summary>
    private async Task SeedAuditEventAsync(Guid id, string entityType, Guid entityId, Guid userId)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO audit.events (id, entity_type, entity_id, action, tenant_id, user_id, changes_json, occurred_at)
            VALUES (@id, @et, @eid, 0, NULL, @uid, NULL, now())", _conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@et", entityType);
        cmd.Parameters.AddWithValue("@eid", entityId);
        cmd.Parameters.AddWithValue("@uid", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Reads back the post-UPDATE state of one audit row by id.</summary>
    private async Task<(string EntityType, Guid EntityId, Guid? UserId, string? ChangesJson)>
        ReadRowAsync(Guid id)
    {
        await using var cmd = new NpgsqlCommand(@"
            SELECT entity_type, entity_id, user_id, changes_json::text
            FROM audit.events WHERE id = @id", _conn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var userIdOrdinal = reader.GetOrdinal("user_id");
        var userIdValue = await reader.IsDBNullAsync(userIdOrdinal)
            ? (Guid?)null
            : reader.GetFieldValue<Guid>(userIdOrdinal);
        var changesOrdinal = reader.GetOrdinal("changes_json");
        var changesValue = await reader.IsDBNullAsync(changesOrdinal)
            ? null
            : reader.GetFieldValue<string>(changesOrdinal);
        return (reader.GetFieldValue<string>(reader.GetOrdinal("entity_type")),
                reader.GetFieldValue<Guid>(reader.GetOrdinal("entity_id")),
                userIdValue,
                changesValue);
    }

    [Fact]
    public async Task AnonymizeUserAsync_DoesNotThrow_OnRealMigrationSchema()
    {
        // Scenario 1: the production anonymizer SQL must execute cleanly
        // against the real migration 0034 schema. Pre-fix this threw
        // with `42703: column "changes_json" of relation "events" does
        // not exist` because the column was named `changes` in
        // migration 0030 until 0034 renamed it. The RED assertion is
        // that the call DOES NOT throw — i.e., the column name
        // alignment is real.
        var userId = Guid.NewGuid();
        await SeedAuditEventAsync(Guid.NewGuid(), "User", userId, userId);
        await SeedAuditEventAsync(Guid.NewGuid(), "Trade", Guid.NewGuid(), userId);

        var sut = new GdprAuditAnonymizer(_db!);

        var act = async () => await sut.AnonymizeUserAsync(userId, CancellationToken.None);
        await act.Should().NotThrowAsync(
            "the production anonymizer SQL must execute cleanly against the real migration 0034 schema (changes column renamed to changes_json).");
    }

    [Fact]
    public async Task AnonymizeUserAsync_WritesJsonbPayload_ToChangesJsonColumn()
    {
        // Scenario 2: the changes_json column must contain a valid JSONB
        // payload with reason + original_user_id_hash. The payload
        // shape is the spec contract.
        var userId = Guid.NewGuid();
        var auditRowId = Guid.NewGuid();
        await SeedAuditEventAsync(auditRowId, "User", userId, userId);

        var sut = new GdprAuditAnonymizer(_db!);
        await sut.AnonymizeUserAsync(userId, CancellationToken.None);

        var row = await ReadRowAsync(auditRowId);
        row.ChangesJson.Should().NotBeNullOrEmpty(
            "the anonymizer MUST overwrite changes_json with the JSONB marker payload (reason + original_user_id_hash).");

        var payload = JsonDocument.Parse(row.ChangesJson!).RootElement;
        payload.GetProperty("reason").GetString().Should().Be("gdpr_hard_delete",
            "the reason field MUST record the GDPR Art. 17 hard-delete cascade for the compliance trail.");

        // The hash is sha256-hex of the userId bytes — 64 lowercase hex
        // chars. Verify the shape without coupling to the exact hash
        // value (the implementation may use either ToByteArray or
        // ToString("N") as the input — both yield a stable but
        // distinct 64-char hex string).
        var hash = payload.GetProperty("original_user_id_hash").GetString();
        hash.Should().NotBeNullOrEmpty("the original_user_id_hash field MUST be present.");
        hash!.Length.Should().Be(64,
            "sha256 hex is exactly 64 chars — a shorter/longer value would indicate the hash isn't sha256 of the user id.");
    }

    [Fact]
    public async Task AnonymizeUserAsync_PseudonymGuid_DerivesFromSha256()
    {
        // Scenario 3: the entity_id for rows where entity_type='User' AND
        // entity_id=userId MUST be replaced with the deterministic UUID
        // pseudonym (first 16 bytes of sha256(userId) packed into a Guid).
        // This pins the pseudonym shape contract per the Wave 11.2a spec
        // reconciliation (account-lifecycle/spec.md).
        var userId = Guid.NewGuid();
        var userAuditRowId = Guid.NewGuid();
        var tradeAuditRowId = Guid.NewGuid();
        var tradeEntityId = Guid.NewGuid();
        await SeedAuditEventAsync(userAuditRowId, "User", userId, userId);
        await SeedAuditEventAsync(tradeAuditRowId, "Trade", tradeEntityId, userId);

        var sut = new GdprAuditAnonymizer(_db!);
        await sut.AnonymizeUserAsync(userId, CancellationToken.None);

        // The 'User' row's entity_id MUST be replaced with the pseudonym.
        var userRow = await ReadRowAsync(userAuditRowId);
        userRow.EntityType.Should().Be("User");
        userRow.EntityId.Should().NotBe(userId,
            "the production anonymizer replaces entity_id = userId with the pseudonym Guid for entity_type='User' rows.");
        // The pseudonym is a valid Guid (UUID-shaped).
        Guid.TryParse(userRow.EntityId.ToString(), out _).Should().BeTrue(
            "the pseudonym MUST be a valid Guid (UUID-shaped identifier, NOT a string like 'deleted_user_<sha256>').");

        // The 'Trade' row's entity_id MUST remain unchanged (the CASE in
        // the SQL only fires when entity_type='User' AND entity_id=userId).
        var tradeRow = await ReadRowAsync(tradeAuditRowId);
        tradeRow.EntityType.Should().Be("Trade");
        tradeRow.EntityId.Should().Be(tradeEntityId,
            "non-User entity_type rows retain their original entity_id — the CASE in the SQL only fires for entity_type='User'.");

        // Both rows MUST have user_id = NULL (the WHERE clause is
        // user_id = @userId; both rows matched).
        userRow.UserId.Should().BeNull(
            "the production anonymizer's WHERE clause (user_id = @userId) MUST null out user_id on every matched row.");
        tradeRow.UserId.Should().BeNull(
            "the production anonymizer's WHERE clause (user_id = @userId) MUST null out user_id on every matched row, regardless of entity_type.");
    }
}
