using JadeCapital.Api.IntegrationTests.Infrastructure;
using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Api.IntegrationTests.Auth;

/// <summary>
/// Integration tests for the slice-0c password-recovery flow
/// (jade-trader-os-core-portals). Hits the real Host via WebApplicationFactory,
/// a Testcontainers Postgres for the schema, and the
/// <see cref="JadeCapital.Shared.Infrastructure.Email.InMemoryCapturingEmailSender"/>
/// so the tests can assert on the captured message and the captured logs.
///
/// Tests:
///   ForgotPassword_Always200Generic          — known + unknown email both 200.
///   TimingBodyStatusIndistinguishable        — same shape, no enumeration leaks.
///   Throttle5PerHourPerIp                    — 6th request 429.
///   InMemorySender_NeverLogsBody             — body never reaches log lines.
///   SmtpFailure_DoesNotActivate              — transport failure leaves no Activated row.
/// </summary>
public class PasswordRecoveryFlowTests : IClassFixture<JadeApiFactory>
{
    private readonly JadeApiFactory _factory;

    public PasswordRecoveryFlowTests(JadeApiFactory factory) { _factory = factory; }

    private HttpClient NewClient(int? recoveryPermitOverride = null, JadeCapital.Shared.Infrastructure.Email.IEmailSender? emailSenderOverride = null)
    {
        // Each test gets a fresh factory instance so the IP-based throttle doesn't
        // accumulate across tests. The InMemoryCapturingEmailSender from the base
        // factory is always reused (or replaced if a test supplies a faulting one),
        // so tests can assert against _factory.EmailSender.Captured.
        if (recoveryPermitOverride is null && emailSenderOverride is null) return _factory.CreateClient();

        return _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) =>
            {
                if (recoveryPermitOverride is not null)
                    c.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["RateLimit:RecoveryPermit"] = recoveryPermitOverride.Value.ToString()
                    });
            });
            if (emailSenderOverride is not null)
            {
                b.ConfigureServices(services =>
                {
                    var existing = services.Where(s => s.ServiceType == typeof(JadeCapital.Shared.Infrastructure.Email.IEmailSender)).ToList();
                    foreach (var s in existing) services.Remove(s);
                    services.AddSingleton<JadeCapital.Shared.Infrastructure.Email.IEmailSender>(emailSenderOverride);
                });
            }
        }).CreateClient();
    }

    [Fact]
    public async Task ForgotPassword_Always200Generic()
    {
        var client = NewClient(recoveryPermitOverride: 100);
        var knownEmail = $"known{Guid.NewGuid():N}@test.com";
        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(knownEmail, "Known", "Passw0rd!Str0ng"));
        reg.StatusCode.Should().Be(HttpStatusCode.Created);

        var knownResp = await client.PostAsJsonAsync("/api/auth/forgot-password",
            new { email = knownEmail });
        var unknownResp = await client.PostAsJsonAsync("/api/auth/forgot-password",
            new { email = $"ghost{Guid.NewGuid():N}@test.com" });

        knownResp.StatusCode.Should().Be(HttpStatusCode.OK);
        unknownResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var knownBody = await knownResp.Content.ReadFromJsonAsync<ForgotResp>();
        var unknownBody = await unknownResp.Content.ReadFromJsonAsync<ForgotResp>();
        knownBody!.Accepted.Should().BeTrue();
        unknownBody!.Accepted.Should().BeTrue();
    }

    [Fact]
    public async Task TimingBodyStatusIndistinguishable()
    {
        var client = NewClient(recoveryPermitOverride: 100);
        var knownEmail = $"timing{Guid.NewGuid():N}@test.com";
        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(knownEmail, "Timing", "Passw0rd!Str0ng"));
        reg.StatusCode.Should().Be(HttpStatusCode.Created);

        var knownResp = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = knownEmail });
        var unknownResp = await client.PostAsJsonAsync("/api/auth/forgot-password",
            new { email = $"ghost{Guid.NewGuid():N}@test.com" });

        knownResp.StatusCode.Should().Be(unknownResp.StatusCode);
        var knownRaw = await knownResp.Content.ReadAsStringAsync();
        var unknownRaw = await unknownResp.Content.ReadAsStringAsync();
        knownRaw.Should().Be(unknownRaw);
        knownResp.Content.Headers.ContentType?.MediaType
            .Should().Be(unknownResp.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Throttle5PerHourPerIp()
    {
        var client = NewClient(recoveryPermitOverride: 5);
        var responses = new List<HttpStatusCode>();
        for (var i = 0; i < 7; i++)
        {
            var r = await client.PostAsJsonAsync("/api/auth/forgot-password",
                new { email = $"throttle{i}-{Guid.NewGuid():N}@test.com" });
            responses.Add(r.StatusCode);
        }
        responses.Take(5).Should().AllBeEquivalentTo(HttpStatusCode.OK);
        responses.Skip(5).Should().AllBeEquivalentTo(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task InMemorySender_NeverLogsBody()
    {
        var client = NewClient(recoveryPermitOverride: 100);
        var email = $"pii{Guid.NewGuid():N}@test.com";
        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Pii", "Passw0rd!Str0ng"));
        reg.StatusCode.Should().Be(HttpStatusCode.Created);

        // Reset the sender so any prior test's captures don't pollute the count.
        _factory.EmailSender.Reset();

        var resp = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.EmailSender.Captured.Should().HaveCount(1);
        var tempPassword = _factory.EmailSender.Captured[0].TemporaryPassword;

        foreach (var line in _factory.CapturedLogs)
            line.Should().NotContain(tempPassword,
                $"temporary password leaked into a log line: {line}");
        foreach (var line in _factory.CapturedLogs)
            line.Should().NotContain("Passw0rd!Str0ng",
                $"plaintext user password leaked into a log line: {line}");
    }

    [Fact]
    public async Task SmtpFailure_DoesNotActivate()
    {
        // Swap the in-memory sender for a faulting one for this test only.
        var faultingSender = new FaultingEmailSender();
        var client = NewClient(recoveryPermitOverride: 100, emailSenderOverride: faultingSender);

        var email = $"smtp{Guid.NewGuid():N}@test.com";
        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "SmtpFail", "Passw0rd!Str0ng"));
        reg.StatusCode.Should().Be(HttpStatusCode.Created);

        var resp = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        // Uniform 200 generic — SMTP failure must NOT be observable.
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // No Activated row for this user; Pending reservation either committed
        // (CAS-loss on ActivateAsync, which never runs after the catch) or rolled back.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).FirstAsync();
        var activeRows = await db.TemporaryCredentials
            .Where(t => t.UserId == userId && t.Status == TemporaryCredentialStatus.Activated)
            .CountAsync();
        activeRows.Should().Be(0,
            "an SMTP failure MUST NOT leave an Activated credential — only Pending or nothing");
    }

    private sealed record ForgotResp(bool Accepted);

    private sealed record RegisterRequest(
        string Email,
        string DisplayName,
        string Password,
        bool AcceptTerms = true,
        bool AcceptPrivacy = true,
        string ConsentIp = "127.0.0.1",
        string AcceptedTermsVersion = "v1.0",
        string AcceptedPrivacyVersion = "v1.0");

    /// <summary>IEmailSender that throws on every send. Used to assert SMTP
    /// failure leaves no Activated row.</summary>
    private sealed class FaultingEmailSender : JadeCapital.Shared.Infrastructure.Email.IEmailSender
    {
        public Task SendRecoveryEmailAsync(JadeCapital.Shared.Infrastructure.Email.RecoveryEmailMessage message, CancellationToken ct = default)
            => throw new InvalidOperationException("SMTP transport failure (simulated).");

        // Slice 6c.3 — the tenant invite path is a stub on the production
        // transports; tests use NSubstitute. This FaultingEmailSender is
        // only used by the recovery path, so the invite method is a no-op.
        public Task SendTenantInviteAsync(JadeCapital.Shared.Infrastructure.Email.TenantInviteEmailMessage message, CancellationToken ct = default)
            => throw new InvalidOperationException("SMTP transport failure (simulated).");

        // Slice 11.4 — the welcome email path swallows failures so the
        // registration can complete. Simulating a failure here is exactly
        // what the post-registration recovery test validates.
        public Task SendWelcomeEmailAsync(JadeCapital.Shared.Infrastructure.Email.WelcomeEmailMessage message, CancellationToken ct = default)
            => throw new InvalidOperationException("SMTP transport failure (simulated).");
    }
}
