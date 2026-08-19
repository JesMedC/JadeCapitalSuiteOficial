using System.Reflection;
using FluentAssertions;

namespace JadeCapital.Identity.UnitTests.Docs;

// ============================================================================
//  EmailDeliverabilityDocsTests — Wave 11 slice 11.4
//
//  Content-sanity check on the canonical email-deliverability docs:
//
//   * `docs/runbooks/email-deliverability.md` — the full DNS + provider
//     env-var runbook;
//   * `docs/email-deliverability.md` — the executive summary.
//
//  Both must contain:
//   * `v=spf1`      (SPF record include directive);
//   * `_dmarc`       (DMARC record hostname);
//   * `selector1._domainkey`  (DKIM record hostname, SES convention);
//   * `p=quarantine` (DMARC policy).
//
//  Both must NOT contain "TODO" markers; a future-onboarding smell.
//  Both must be > 1,500 characters long.
// ============================================================================

public class EmailDeliverabilityDocsTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void RunbookContentSanity()
    {
        var path = Path.Combine(RepoRoot, "docs/runbooks/email-deliverability.md");
        File.Exists(path).Should().BeTrue("docs/runbooks/email-deliverability.md must exist");
        var content = File.ReadAllText(path);

        AssertRequiredSubstrings(content, runbookPath: path);
    }

    [Fact]
    public void SummaryContentSanity()
    {
        var path = Path.Combine(RepoRoot, "docs/email-deliverability.md");
        File.Exists(path).Should().BeTrue("docs/email-deliverability.md must exist");
        var content = File.ReadAllText(path);

        AssertRequiredSubstrings(content, runbookPath: path);
    }

    private static void AssertRequiredSubstrings(string content, string runbookPath)
    {
        var required = new[]
        {
            "v=spf1",
            "_dmarc",
            "selector1._domainkey",
            "p=quarantine",
        };

        foreach (var needle in required)
        {
            content.Should().Contain(needle,
                $"email-deliverability doc must contain {needle} (DNS record spec).");
        }

        content.Should().NotContain("TODO",
            $"email-deliverability doc ({runbookPath}) is production-facing; TODO markers not allowed.");

        content.Length.Should().BeGreaterThan(1_500,
            $"email-deliverability doc ({runbookPath}) is a multi-section document.");
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
