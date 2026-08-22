using FluentAssertions;
using Xunit;

namespace JadeCapital.Api.IntegrationTests.Migrations;

/// <summary>
/// Tests that the migration files for Wave 11.2a hotfixes exist on disk
/// with the correct shape — sentinel user INSERT in 0029, lifecycle
/// state machine in 0033, audit column rename in 0034.
///
/// <para>
/// These tests are static-file checks (no DB / docker required) so they
/// run quickly + don't depend on the Testcontainers infrastructure.
/// The actual fresh-DB apply is verified by the existing
/// <c>scripts/verify-migration-order.sh</c> (Wave 10.4) + the Jade
/// integration test factory's <c>ApplyMigrationAsync</c> path.
/// </para>
///
/// <para>
/// RED scenarios pinned here (per Wave 11.2a <c>tasks.md</c> Phase 3):
/// <list type="number">
///   <item><b>Migration_0029_ContainsSentinelUserInsert</b> — the
///         Wave 11.2a FK fix MUST insert the sentinel user
///         (id <c>00000000-0000-0000-0000-000000000002</c>) BEFORE
///         the Personal tenant INSERT — so the FK resolves on fresh
///         DBs. Pre-fix the migration referenced the sentinel but
///         never created it, breaking fresh-DB apply.</item>
///   <item><b>Migration_0033_AddsScheduledHardDeleteAt</b> — the
///         Wave 11.2a Bug #2 hotfix MUST add
///         <c>scheduled_for_hard_delete_at TIMESTAMPTZ</c> to
///         <c>identity.users</c> + widen <c>ck_users_status</c> to
///         include the new Wave 10.5 enum values. Without these, the
///         <c>HardDeleteSweepBackgroundService</c> LINQ throws at
///         runtime + the GDPR cascade's <c>status = 'SoftDeleted'</c>
///         writes are rejected by the CHECK constraint.</item>
///   <item><b>Migration_0034_RenamesAuditColumn</b> — the Wave 11.2a
///         Bug #1 hotfix MUST rename <c>audit.events.changes</c> to
///         <c>audit.events.changes_json</c> so the production
///         anonymizer SQL + spec canon + EF mapping all agree.
///         Without this rename, the production anonymizer fails
///         with <c>42703: column "changes_json" does not exist</c>.</item>
///   <item><b>Migration_0030_CreatesAuditSchema</b> — the Wave 11.2a
///         hotfix to migration 0030 MUST add
///         <c>CREATE SCHEMA IF NOT EXISTS audit;</c> before
///         referencing <c>audit.events</c>. The original 0030 did
///         NOT create the schema; Postgres doesn't auto-create
///         schemas on <c>CREATE TABLE schema.table</c> references,
///         so fresh-DB apply failed at 0030.</item>
///   <item><b>Migration_FilenamesAreConsecutivelyNumbered</b> — every
///         <c>*.sql</c> file under <c>infrastructure/postgres/migrations/</c>
///         follows the canonical <c>NNNN_description.sql</c> naming
///         with no gaps or duplicates (sort order invariant).</item>
/// </list>
/// </para>
/// </summary>
public sealed class MigrationOrderApplyTests
{
    private static string LocateMigrationsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "infrastructure", "postgres", "migrations");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate infrastructure/postgres/migrations relative to " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Migration_0029_ContainsSentinelUserInsert()
    {
        var root = LocateMigrationsRoot();
        var migrationPath = Path.Combine(root, "0029_backfill_personal_tenant.sql");
        File.Exists(migrationPath).Should().BeTrue("the 0029 migration must exist.");
        var content = File.ReadAllText(migrationPath);

        // 1. Sentinel user INSERT must happen BEFORE Personal tenant INSERT
        //    so the FK (fk_tenants_owner_user_id → identity.users.id) is
        //    satisfied when the tenant is created.
        var sentinelInsertPos = content.IndexOf("00000000-0000-0000-0000-000000000002", StringComparison.Ordinal);
        var personalTenantInsertPos = content.IndexOf("personal-default", StringComparison.Ordinal);
        sentinelInsertPos.Should().BeGreaterThan(-1,
            "the sentinel id ...0002 must appear in the 0029 migration (FK target for the Personal tenant).");
        personalTenantInsertPos.Should().BeGreaterThan(-1,
            "the personal-default tenant must be created by the 0029 migration.");
        sentinelInsertPos.Should().BeLessThan(personalTenantInsertPos,
            "the sentinel INSERT must happen BEFORE the Personal tenant INSERT so the FK resolves on fresh DBs.");

        // 2. Sentinel user must be inserted into identity.users (not just
        //    referenced — pre-fix the migration referenced the id but
        //    never created the row).
        content.Should().Contain("INSERT INTO identity.users",
            "0029 MUST INSERT the sentinel user row (not just reference the FK).");
        content.Should().Contain("ON CONFLICT (id) DO NOTHING",
            "the sentinel INSERT must be idempotent (ON CONFLICT DO NOTHING) so re-runs are no-ops.");

        // 3. NOT NULL constraint must be temporarily dropped + re-applied
        //    (the sentinel has tenant_id = NULL initially; the Personal
        //    tenant assigns it back).
        content.Should().Contain("ALTER COLUMN tenant_id DROP NOT NULL",
            "0029 must drop the NOT NULL constraint on tenant_id to allow the sentinel to be inserted with NULL tenant_id.");
        content.Should().Contain("ALTER COLUMN tenant_id SET NOT NULL",
            "0029 must re-apply the NOT NULL constraint after the sentinel + all NULL users are assigned to the Personal tenant.");
    }

    [Fact]
    public void Migration_0033_AddsScheduledHardDeleteAt()
    {
        var root = LocateMigrationsRoot();
        var migrationPath = Path.Combine(root, "0033_user_lifecycle_state_machine.sql");
        File.Exists(migrationPath).Should().BeTrue("the 0033 migration must exist.");
        var content = File.ReadAllText(migrationPath);

        content.Should().Contain("ADD COLUMN IF NOT EXISTS scheduled_for_hard_delete_at",
            "0033 must add the scheduled_for_hard_delete_at column to identity.users.");
        content.Should().Contain("'ScheduledHardDelete'",
            "0033 must widen ck_users_status to include the ScheduledHardDelete status.");
        content.Should().Contain("'SoftDeleted'",
            "0033 must widen ck_users_status to include the SoftDeleted status.");
        content.Should().Contain("'HardDeleted'",
            "0033 must widen ck_users_status to include the HardDeleted status.");

        // Partial index for the BackgroundService hot path.
        content.Should().Contain("ix_users_scheduled_hard_delete_at",
            "0033 must add the partial index used by the BackgroundService's WHERE clause.");
    }

    [Fact]
    public void Migration_0034_RenamesAuditColumn()
    {
        var root = LocateMigrationsRoot();
        var migrationPath = Path.Combine(root, "0034_audit_events_changes_to_changes_json.sql");
        File.Exists(migrationPath).Should().BeTrue("the 0034 migration must exist.");
        var content = File.ReadAllText(migrationPath);

        content.Should().Contain("RENAME COLUMN changes TO changes_json",
            "0034 must rename the audit payload column from `changes` to `changes_json`.");

        // Idempotent guard.
        content.Should().Contain("IF EXISTS",
            "0034 must guard the rename with an IF EXISTS check so re-runs are no-ops.");
    }

    [Fact]
    public void Migration_0039_AddsExistingUserAggregateColumns()
    {
        var root = LocateMigrationsRoot();
        var migrationPath = Path.Combine(root, "0039_add_existing_user_aggregate_columns.sql");
        File.Exists(migrationPath).Should().BeTrue(
            "the migration schema must include every column already mapped by the shipped User aggregate");
        var content = File.ReadAllText(migrationPath);

        foreach (var column in new[]
        {
            "soft_deleted_at",
            "accepted_terms_version",
            "accepted_privacy_version",
            "accepted_at"
        })
        {
            content.Should().Contain($"ADD COLUMN IF NOT EXISTS {column}");
        }
    }

    [Fact]
    public void Migration_0030_CreatesAuditSchema()
    {
        var root = LocateMigrationsRoot();
        var migrationPath = Path.Combine(root, "0030_audit_events.sql");
        File.Exists(migrationPath).Should().BeTrue("the 0030 migration must exist.");
        var content = File.ReadAllText(migrationPath);

        content.Should().Contain("CREATE SCHEMA IF NOT EXISTS audit",
            "0030 must create the audit schema BEFORE referencing audit.events — Postgres doesn't auto-create schemas on CREATE TABLE schema.table references, so the original 0030 (missing this CREATE) broke fresh-DB apply.");
    }

    [Fact]
    public void Migration_FilenamesAreConsecutivelyNumbered()
    {
        var root = LocateMigrationsRoot();
        var files = Directory.GetFiles(root, "*.sql")
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToArray();

        files.Should().NotBeEmpty("the repo must contain at least one migration file");

        // Pull the 4-digit prefix off each filename. Wave 11.2a added 3
        // migrations (0033 + 0034 + 0035) → 35 total. Wave 11.3
        // slice 11.3 adds 0036_add_welcome_email_sent_at for a total of
        // 36. Wave 11.4 adds 0037_add_consent_columns +
        // 0038_add_cookie_consent_columns for a total of 38. The
        // canonical tasks.md spec referenced 0039 + 0040 file numbers —
        // those are forward-only renames: the canonical SQL body in the
        // spec was preserved verbatim (idempotent
        // `ADD COLUMN IF NOT EXISTS`) but the file numbers were
        // rebased so the sequence stays consecutive (the
        // MigrationOrderTests.EnsureConsecutiveNumbering contract).
        const int ExpectedCount = 39;
        files.Length.Should().Be(ExpectedCount,
            $"Wave 11.2a (3 migrations) + Wave 11.3 (1 migration) + Wave 11.4 (2 migrations = 0037 + 0038) + schema-alignment migration 0039: "
            + $"{ExpectedCount} total.");

        for (int i = 0; i < files.Length; i++)
        {
            var expected = $"{(i + 1):D4}";
            var actual = Path.GetFileName(files[i]).Substring(0, 4);
            actual.Should().Be(expected,
                $"file #{i + 1} should be numbered {expected}, got {actual} in {Path.GetFileName(files[i])}");
        }
    }
}
