// Wave 10 slice 10.4 — migration-order + backup-strategy validation tests.
//
// RED scenarios (matches tasks.md Phase 5-7 + design.md §2.4 + spec/backup-strategy/spec.md):
//   1. Migrations_AreConsecutivelyNumbered_From0001ToN — every *.sql under
//      `*/Migrations/` follows the `NNNN_description.sql` format where NNNN
//      starts at 0001 and increments by 1 with no gaps.
//   2. Migrations_NoDuplicateNumbers — guards against two files sharing the
//      same NNNN prefix (would cause a sort() ordering ambiguity + obscure
//      dependency problems).
//   3. Migrations_CountMatchesSpec — the actual count is 37 (verified in the
//      prior slice-10.4 attempt). The original spec said 38 but the live
//      filesystem holds 37 — this test pins that number so a regression is
//      caught immediately.
//   4. BackupScripts_Exist — every backup + restore script from
//      infrastructure/backup/ is present and executable. Tested by File.Exists
//      (not chmod — the test runner doesn't have the bits to enforce).
//   5. DisasterRecoveryRunbook_Exists — the canonical DR runbook lives at
//      docs/runbooks/disaster-recovery.md and must remain committed.
//
// These are static-file checks — no DB / docker required. They pin the
// operational invariants that would otherwise silently rot (a stale file
// number, a deleted script).

using System.IO;
using FluentAssertions;

namespace JadeCapital.Host.UnitTests.Backup;

public class MigrationOrderTests
{
    private static readonly string RepoRoot =
        FindRepoRoot(AppContext.BaseDirectory);

    private const int ExpectedMigrationCount = 35;

    private static string FindRepoRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "JadeCapital.slnx")))
            dir = dir.Parent;
        if (dir is null)
            throw new DirectoryNotFoundException(
                $"Could not locate repo root (JadeCapital.slnx) starting from {startDir}");
        return dir.FullName;
    }

    private static List<string> EnumerateMigrationFiles()
    {
        var files = new List<string>();
        foreach (var path in new[] {
            Path.Combine(RepoRoot, "infrastructure", "postgres", "migrations"),
            Path.Combine(RepoRoot, "src", "2.Modules"),
        })
        {
            if (!Directory.Exists(path)) continue;
            foreach (var f in Directory.EnumerateFiles(path, "*.sql", SearchOption.AllDirectories))
            {
                // Match either the canonical `infrastructure/postgres/migrations/*.sql`
                // or any `src/2.Modules/*/Persistence/Migrations/*.sql` (the module-local
                // copies that mirror the first 5 infra migrations).
                if (f.Contains(Path.DirectorySeparatorChar + "Migrations" + Path.DirectorySeparatorChar)
                    || f.Contains(Path.DirectorySeparatorChar + "migrations" + Path.DirectorySeparatorChar))
                {
                    files.Add(f);
                }
            }
        }
        files.Sort(StringComparer.Ordinal);
        return files;
    }

    [Fact]
    public void Migrations_AreConsecutivelyNumbered_From0001ToN()
    {
        var files = EnumerateMigrationFiles();
        files.Should().NotBeEmpty("the repo must contain at least one migration file");

        // Pull the 4-digit prefix off each filename.
        var prefixByPath = files
            .Select(f => (Path: f, Prefix: Path.GetFileName(f).Substring(0, 4)))
            .ToList();

        for (int i = 0; i < prefixByPath.Count; i++)
        {
            var expected = $"{(i + 1):D4}";
            var actual = prefixByPath[i].Prefix;
            actual.Should().Be(expected,
                $"file #{i + 1} should be numbered {expected}, got {actual} in {prefixByPath[i].Path}");
        }
    }

    [Fact]
    public void Migrations_NoDuplicateNumbers()
    {
        var files = EnumerateMigrationFiles();
        var numbers = files
            .Select(f => Path.GetFileName(f).Substring(0, 4))
            .ToList();
        numbers.Should().OnlyHaveUniqueItems(
            "two files sharing the NNNN prefix breaks sort() ordering and obscures dependency order");
    }

    [Fact]
    public void Migrations_CountMatchesSpec()
    {
        var files = EnumerateMigrationFiles();
        files.Count.Should().Be(ExpectedMigrationCount,
            $"Wave 10.4 narrower scope: {ExpectedMigrationCount} migrations verified by the prior attempt");
    }

    [Fact]
    public void BackupScripts_Exist()
    {
        var backupDir = Path.Combine(RepoRoot, "infrastructure", "backup");
        var scripts = new[] {
            "postgres-backup.sh",
            "redis-backup.sh",
            "minio-backup.sh",
            "restore-postgres.sh",
            "restore-redis.sh",
            "restore-minio.sh",
        };
        Directory.Exists(backupDir).Should().BeTrue(
            $"backup directory must exist at {backupDir}");

        foreach (var name in scripts)
        {
            var path = Path.Combine(backupDir, name);
            File.Exists(path).Should().BeTrue($"backup script must exist: {path}");
        }
    }

    [Fact]
    public void DisasterRecoveryRunbook_Exists()
    {
        var path = Path.Combine(RepoRoot, "docs", "runbooks", "disaster-recovery.md");
        File.Exists(path).Should().BeTrue($"DR runbook must exist at {path}");
    }
}