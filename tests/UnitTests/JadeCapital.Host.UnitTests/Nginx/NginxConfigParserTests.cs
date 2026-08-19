// Wave 10 slice 10.3 — security headers config-validation tests.
//
// RED scenarios (matches tasks.md Phase 1.1 + design.md §2.3 + spec/security-headers/spec.md):
//   1. NginxConfig_ContainsContentSecurityPolicyHeader (spec §Requirement:CSP)
//   2. NginxConfig_ContainsStrictTransportSecurityHeader (spec §Requirement:HSTS)
//   3. NginxConfig_ContainsPermissionsPolicyHeader (spec §Requirement:Permissions-Policy)
//   4. NginxConfig_PreservesExistingSecurityHeaders (X-Frame-Options, X-Content-Type-Options,
//      Referrer-Policy, server_tokens off — must NOT regress when adding the new headers)
//   5. NginxConfig_DisablesServerTokens (server_tokens off — required so nginx doesn't
//      leak the version banner in 404 responses)
//
// These tests parse the nginx config as text. They confirm the static shape of
// the file (the directives we expect). Live HTTP verification is done via
// scripts/verify-headers.py + scripts/verify-security-headers.sh once nginx is
// running; here we only assert the source-of-truth is correct.
//
// Phase 1.1 RED: all assertions fail before nginx.conf is updated to ship
// `add_header Content-Security-Policy` + `add_header Strict-Transport-Security` +
// `add_header Permissions-Policy`.

using System.IO;
using FluentAssertions;

namespace JadeCapital.Host.UnitTests.Nginx;

public class NginxConfigParserTests
{
    private static readonly string RepoRoot =
        // Walk up from the test bin/Debug/net10.0/.../ folder to the repo root
        // by following the directory chain until we find JadeCapital.slnx
        // (works regardless of how many bin/<config>/<tfm>/ levels exist).
        FindRepoRoot(AppContext.BaseDirectory);

    private static readonly string NginxConfPath =
        Path.Combine(RepoRoot, "infrastructure", "nginx", "nginx.conf");

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

    private static string ReadConfig()
    {
        File.Exists(NginxConfPath)
            .Should()
            .BeTrue($"nginx.conf must exist at {NginxConfPath} (slice 10.2 baseline)");
        return File.ReadAllText(NginxConfPath);
    }

    [Fact]
    public void NginxConfig_ContainsContentSecurityPolicyHeader()
    {
        var config = ReadConfig();

        // Per spec/security-headers/spec.md §Requirement:Content-Security-Policy:
        // `script-src 'self' 'nonce-{per-request}'; style-src 'self' 'unsafe-inline'`
        // must be present. We assert on a stable substring of the directive (the
        // directive name + the start of the value) so the test doesn't break if
        // a nonce token placeholder style changes between releases.
        config.Should().Contain(
            "add_header Content-Security-Policy",
            "nginx must emit a Content-Security-Policy header on every response (spec §Requirement:CSP)");

        config.Should().Contain(
            "script-src 'self'",
            "CSP must lock down scripts to 'self' (no CDN scripts in v1.0.0-rc1)");

        config.Should().Contain(
            "frame-ancestors 'none'",
            "CSP must deny framing (clickjacking defense; spec §Scenario:frame-ancestors 'none')");

        config.Should().Contain(
            "object-src 'none'",
            "CSP must block <object>/<embed>/<applet> tags (Flash/legacy plugin defense)");

        config.Should().NotContain(
            "default-src *",
            "CSP must NOT use wildcard default-src — that's effectively no CSP");
    }

    [Fact]
    public void NginxConfig_ContainsStrictTransportSecurityHeader()
    {
        var config = ReadConfig();

        // Per spec/security-headers/spec.md §Requirement:HSTS:
        // `Strict-Transport-Security: max-age=63072000; includeSubDomains; preload`
        config.Should().Contain(
            "add_header Strict-Transport-Security",
            "nginx must emit Strict-Transport-Security on every HTTPS response (spec §Requirement:HSTS)");

        config.Should().Contain(
            "max-age=63072000",
            "HSTS max-age must be 2 years (63072000 seconds) — the value that qualifies for the Chromium preload list");

        config.Should().Contain(
            "includeSubDomains",
            "HSTS must include subdomains (otherwise an attacker can downgrade a subdomain)");

        config.Should().Contain(
            "preload",
            "HSTS must declare preload intent (ops will submit to hstspreload.org at deploy time)");
    }

    [Fact]
    public void NginxConfig_ContainsPermissionsPolicyHeader()
    {
        var config = ReadConfig();

        // Per spec/security-headers/spec.md §Requirement:Permissions-Policy:
        // `camera=(), microphone=(), geolocation=(), payment=(), usb=(),
        //  magnetometer=(), gyroscope=(), accelerometer=()`
        config.Should().Contain(
            "add_header Permissions-Policy",
            "nginx must emit Permissions-Policy to disable unused browser features");

        config.Should().Contain("camera=()",      "camera API must be denied");
        config.Should().Contain("microphone=()",   "microphone API must be denied");
        config.Should().Contain("geolocation=()",  "geolocation API must be denied");
        config.Should().Contain("payment=()",      "payment API must be denied (Stripe uses redirect, not Payment Request API)");
        config.Should().Contain("usb=()",          "USB API must be denied");
        config.Should().Contain("magnetometer=()", "magnetometer API must be denied");
        config.Should().Contain("gyroscope=()",    "gyroscope API must be denied");
        config.Should().Contain("accelerometer=()","accelerometer API must be denied");
    }

    [Fact]
    public void NginxConfig_PreservesExistingSecurityHeaders()
    {
        var config = ReadConfig();

        // Regression guard: the Wave 9 baseline set these 3 headers + disabled
        // server_tokens. The slice 10.3 addition of CSP/HSTS/Permissions-Policy
        // must NOT remove them.
        config.Should().Contain(
            "add_header X-Content-Type-Options nosniff",
            "X-Content-Type-Options nosniff must remain (MIME-sniffing defense)");

        config.Should().Contain(
            "add_header X-Frame-Options DENY",
            "X-Frame-Options DENY must remain (clickjacking defense; redundant with frame-ancestors but defense in depth)");

        config.Should().Contain(
            "add_header Referrer-Policy strict-origin-when-cross-origin",
            "Referrer-Policy must remain (referer leakage defense)");
    }

    [Fact]
    public void NginxConfig_DisablesServerTokens()
    {
        var config = ReadConfig();

        // server_tokens off prevents nginx from leaking its version in error
        // pages + Server response header. Required for v1 readiness.
        config.Should().Contain(
            "server_tokens off",
            "server_tokens must be off so nginx doesn't leak its version banner");
    }
}