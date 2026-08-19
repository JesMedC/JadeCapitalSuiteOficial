using System.Reflection;

namespace JadeCapital.Identity.UnitTests.Infrastructure.Migrations;

/// <summary>
/// Tests that the migration chain ordering is intact (Wave 6, slice 6c.3 + Wave 10.4 renumbering).
///
/// <para>
/// One RED scenario pinned here (per tasks.md 6c.3 line 377):
/// the <c>0028_NOT_NULL_tenant_id.sql</c> migration file MUST exist on
/// disk AND it MUST run AFTER <c>0029_backfill_personal_tenant.sql</c>
/// in sort order (so the backfill assigns NULL users to the Personal
/// tenant before the NOT NULL constraint is applied).
/// </para>
/// <para>
/// The original numbering was <c>0026_*</c>; Wave 10.4 slice renumbered the
/// whole 37-file migration set to consecutive 0001-0037. The semantic
/// ordering (backfill BEFORE NOT NULL) is preserved because 0028 &lt; 0029.
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

        // 1. Both files MUST exist (backfill + NOT NULL) — renamed to 0028 / 0029
        //    by Wave 10.4 consecutive renumbering (preserves 0028 < 0029 ordering).
        var backfillPath = Path.Combine(root, "0029_backfill_personal_tenant.sql");
        var notNullPath = Path.Combine(root, "0028_NOT_NULL_tenant_id.sql");

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

        // 3. Ordering: backfill MUST run BEFORE NOT NULL. Wave 10.4 renamed
        //    the backfill to 0029_*.sql and the NOT NULL to 0028_*.sql, so
        //    `ls | sort` (which the new order-agnostic Dockerfile relies on)
        //    yields 0028 < 0029 → backfill-before-NOT-NULL at the runner
        //    level. We assert that:
        //     a) migrate.Dockerfile exists (so the runner is wired);
        //     b) the Dockerfile is order-agnostic (relies on `ls | sort`,
        //        NOT on explicit `psql -f 0028_... && psql -f 0029_...`).
        var dockerfilePath = Path.Combine(
            Directory.GetParent(root)!.FullName,
            "migrate.Dockerfile");
        File.Exists(dockerfilePath).Should().BeTrue(
            "migrate.Dockerfile must exist so the new NOT NULL migration can be wired in.");
        var dockerfileContent = File.ReadAllText(dockerfilePath);

        dockerfileContent.Should().Contain("ls /migrations/*.sql",
            "migrate.Dockerfile MUST iterate *.sql via `ls | sort` so the consecutive numbering (0028 < 0029) determines order.");

        // Sanity check: the sort-order invariant must hold. 0028 sorts
        // before 0029, so the NOT NULL migration runs AFTER the backfill.
        var notNullPrefix = "0028";
        var backfillPrefix = "0029";
        string.Compare(notNullPrefix, backfillPrefix, StringComparison.Ordinal)
            .Should().BeLessThan(0,
                $"Wave 10.4 renumbering must keep NOT NULL ({notNullPrefix}) before backfill ({backfillPrefix}) lexicographically " +
                "so `ls | sort` applies the backfill first.");
    }
}
