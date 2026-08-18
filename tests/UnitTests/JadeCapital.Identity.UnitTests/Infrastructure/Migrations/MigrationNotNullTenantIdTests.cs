using System.Reflection;

namespace JadeCapital.Identity.UnitTests.Infrastructure.Migrations;

/// <summary>
/// Tests that the migration chain ordering is intact (Wave 6, slice 6c.3).
///
/// <para>
/// One RED scenario pinned here (per tasks.md 6c.3 line 377):
/// the <c>0026_NOT_NULL_tenant_id.sql</c> migration file MUST exist on
/// disk AND it MUST run AFTER <c>0026_backfill_personal_tenant.sql</c>
/// alphabetically (so the backfill assigns NULL users to the Personal
/// tenant before the NOT NULL constraint is applied).
/// </para>
///
/// <para>
/// We assert the migration files exist relative to the repo root. The
/// runtime DB-level check (running the migration against a real Postgres)
/// is exercised in integration tests; here we just pin the file ordering
/// and idempotency primitives so a developer who deletes or renames the
/// file gets a fast RED.
/// </para>
/// </summary>
public class MigrationNotNullTenantIdTests
{
    private static string LocateMigrationsRoot()
    {
        // Tests run from the test project's bin/ output; walk up until we
        // find the solution's infrastructure/postgres/migrations folder.
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
    public void NotNullMigration_FileExistsAndIsIdempotent()
    {
        var root = LocateMigrationsRoot();

        // 1. Both 0026_ files MUST exist (backfill + NOT NULL).
        var backfillPath = Path.Combine(root, "0026_backfill_personal_tenant.sql");
        var notNullPath = Path.Combine(root, "0026_NOT_NULL_tenant_id.sql");

        File.Exists(backfillPath).Should().BeTrue(
            "the 6c.2 backfill migration must exist so the 6c.3 NOT NULL can rely on it.");
        File.Exists(notNullPath).Should().BeTrue(
            "the 6c.3 NOT NULL migration must exist on disk; this slice ships it.");

        var notNullContent = File.ReadAllText(notNullPath);

        // 2. Idempotency contract: a DO $$ ... IF NOT isdefined NOT NULL THEN ALTER ...
        //    block MUST be present — without it, re-running after a partial failure
        //    would raise "column is already NOT NULL".
        notNullContent.Should().Contain("DO $$",
            "the migration must wrap the ALTER in a DO $$ block to be idempotent.");
        notNullContent.Should().Contain("ALTER COLUMN",
            "the migration must use ALTER COLUMN to flip NULL → NOT NULL.");
        notNullContent.Should().Contain("SET NOT NULL",
            "the migration must explicitly request NOT NULL on the column.");
        notNullContent.Should().Contain("tenant_id",
            "the migration must target the tenant_id column on identity.users.");
        notNullContent.Should().Contain("identity.users",
            "the migration must target the identity.users table.");

        // 3. Ordering: backfill MUST run BEFORE NOT NULL. The two files
        //    share the 0026_ prefix by spec, so alphabetical sort is not
        //    reliable. The Dockerfile's psql command list is the source
        //    of truth — assert the backfill command precedes the NOT NULL
        //    command in the Dockerfile's happy-path psql chain.
        //    root = infrastructure/postgres/migrations/ → parent = infrastructure/postgres/
        var dockerfilePath = Path.Combine(
            Directory.GetParent(root)!.FullName,
            "migrate.Dockerfile");
        File.Exists(dockerfilePath).Should().BeTrue(
            "migrate.Dockerfile must exist so the new NOT NULL migration can be wired in.");
        var dockerfileContent = File.ReadAllText(dockerfilePath);

        var backfillIdx = dockerfileContent.IndexOf(
            "0026_backfill_personal_tenant.sql", StringComparison.Ordinal);
        var notNullIdx = dockerfileContent.IndexOf(
            "0026_NOT_NULL_tenant_id.sql", StringComparison.Ordinal);

        backfillIdx.Should().BeGreaterThan(-1,
            "the backfill migration MUST be referenced in migrate.Dockerfile (happy path).");
        notNullIdx.Should().BeGreaterThan(-1,
            "the NOT NULL migration MUST be referenced in migrate.Dockerfile (happy path).");
        (backfillIdx < notNullIdx).Should().BeTrue(
            $"migrate.Dockerfile must list the backfill BEFORE the NOT NULL migration " +
            $"(backfill idx={backfillIdx}, NOT NULL idx={notNullIdx}); " +
            "otherwise the NOT NULL fails because tenant_id still has NULL rows.");
    }
}
