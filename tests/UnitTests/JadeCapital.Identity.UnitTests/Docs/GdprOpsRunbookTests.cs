using System.Reflection;
using FluentAssertions;

namespace JadeCapital.Identity.UnitTests.Docs;

// ============================================================================
//  GdprOpsRunbookTests — Wave 11 slice 11.4
//
//  Content-sanity check on the canonical DSAR runbook. The test ensures
//  the runbook at `docs/runbooks/gdpr-data-subject-request.md`:
//
//   * contains the expected compliance substrings (GDPR Art. 17 / 20,
//     30-day grace, privacy@, HardDeleteSweep, GdprAuditAnonymizer,
//     psql/curl, 0009-gdpr-right-to-be-forgotten);
//   * does NOT contain "TODO" markers (a future-onboarding smell);
//   * is > 1,500 characters long (the runbook is a multi-section
//     document; a one-page sanity check is too brief to be useful).
//
//  The path resolution walks the binary directory up to the repo root
//  (same trick as `MigrationOrderTests`).
// ============================================================================

public class GdprOpsRunbookTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private const string RunbookPath = "docs/runbooks/gdpr-data-subject-request.md";

    [Fact]
    public void ContentSanity()
    {
        var path = Path.Combine(RepoRoot, RunbookPath);
        File.Exists(path).Should().BeTrue($"the DSAR runbook must exist at {RunbookPath}");

        var content = File.ReadAllText(path);

        // Required compliance substrings (any case — the runbook uses
        // both GDPR-style and operational-style references).
        var required = new[]
        {
            "GDPR Art. 17",
            "GDPR Art. 20",
            "30-day grace",
            "privacy@jadecapital.com",
            "HardDeleteSweep",
            "GdprAuditAnonymizer",
            "psql",
            "curl",
            "0009-gdpr-right-to-be-forgotten",
        };

        foreach (var needle in required)
        {
            content.Should().Contain(
                needle,
                $"the DSAR runbook must reference {needle} (compliance spec coverage).");
        }

        content.Should().NotContain("TODO",
            "the DSAR runbook is a production-facing doc; TODO markers are not allowed.");

        content.Length.Should().BeGreaterThan(1_500,
            "the DSAR runbook is a multi-section document; a one-pager is too brief to be useful.");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "JadeCapital.slnx")))
            dir = dir.Parent;
        if (dir is null)
            throw new DirectoryNotFoundException(
                $"Could not locate repo root (JadeCapital.slnx) starting from {AppContext.BaseDirectory}");
        return dir.FullName;
    }
}
