// Wave 10 slice 10.3 — frontend index.html meta-CSP-fallback tests.
//
// RED scenario (matches tasks.md Phase 3.1 + design.md §2.3 + spec/security-headers/spec.md
// §"Out of scope: Strict nonce-only CSP" + proposal.md §7.2 decision #4):
//   1. IndexHtml_ContainsMetaContentSecurityPolicy (CSP fallback when nginx is bypassed
//      — e.g. dev `ng serve`, Storybook, or any local static serve scenario)
//
// Why this exists: nginx emits a `Content-Security-Policy` HTTP header for prod traffic,
// but Angular's dev server (`ng serve`) and any static-server scenario skip nginx entirely.
// The HTML <meta http-equiv="Content-Security-Policy"> tag is the in-document fallback so
// the browser enforces CSP even when no edge proxy sits in front.
//
// Phase 3.1 RED: the meta tag is absent before slice 10.3 lands the fallback.

using System.IO;
using FluentAssertions;

namespace JadeCapital.Host.UnitTests.Nginx;

public class IndexHtmlMetaCspTests
{
    // Anchor: find the directory that contains JadeCapital.slnx (robust to bin/<config>/<tfm>/ depth).
    private static readonly string _RepoRoot = FindRepoRoot(AppContext.BaseDirectory);

    private static readonly string IndexHtmlPath =
        Path.Combine(_RepoRoot, "frontend", "src", "index.html");

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

    private static string ReadIndexHtml()
    {
        File.Exists(IndexHtmlPath)
            .Should()
            .BeTrue($"index.html must exist at {IndexHtmlPath} (Angular project baseline)");
        return File.ReadAllText(IndexHtmlPath);
    }

    [Fact]
    public void IndexHtml_ContainsMetaContentSecurityPolicy()
    {
        var html = ReadIndexHtml();

        // The bootstrap script creates the development fallback dynamically. In
        // production, nginx injects its request nonce into document.currentScript.
        html.Should().Contain(
            "document.createElement('meta')",
            "frontend index.html MUST create a meta CSP fallback when nginx is bypassed");

        html.Should().Contain(
            "document.currentScript.nonce",
            "the meta policy must reuse the nonce injected into the executable bootstrap script");

        html.Should().Contain(
            "meta.httpEquiv = 'Content-Security-Policy'",
            "the generated meta element must enforce Content-Security-Policy");

        html.Should().Contain(
            "default-src 'self'",
            "meta CSP must default-deny (no wildcard default-src)");

        html.Should().Contain(
            "frame-ancestors 'none'",
            "meta CSP must deny framing (defense in depth with nginx header)");
    }
}
