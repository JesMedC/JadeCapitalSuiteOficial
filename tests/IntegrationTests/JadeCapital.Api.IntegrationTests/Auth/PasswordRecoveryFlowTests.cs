using System.Collections.Concurrent;
using JadeCapital.Api.IntegrationTests.Infrastructure;
using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace JadeCapital.Api.IntegrationTests.Auth;

/// <summary>
/// Integration tests for the slice-0c password-recovery flow
/// (jade-trader-os-core-portals). Hits the real Host via WebApplicationFactory,
/// a Testcontainers Postgres for the schema, and the
/// <see cref="JadeCapital.Shared.Infrastructure.Email.InMemoryCapturingEmailSender"/>
/// so the tests can assert on the captured message and the captured logs.
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

    private HttpClient NewClientWithHasher(IPasswordHasher hasher) => _factory.WithWebHostBuilder(b => b.ConfigureServices(services => { services.RemoveAll<IPasswordHasher>(); services.AddSingleton(hasher); })).CreateClient();

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
    public async Task KnownRequest_CommitsActivatedCredential_AndCapturedPlaintextVerifies()
    {
        var setupClient = NewClient(recoveryPermitOverride: 100);
        var email = $"pii{Guid.NewGuid():N}@test.com";
        var reg = await setupClient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Pii", "Passw0rd!Str0ng"));
        reg.StatusCode.Should().Be(HttpStatusCode.Created);
        var userId = await UserIdAsync(email);
        var sender = new CommitProbeSender(async () =>
            (await CredentialsAsync(userId)).Count(t => t.Status == TemporaryCredentialStatus.Activated));
        ResetCaptures();
        var client = NewClient(100, sender);
        var resp = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var message = sender.Captured.Should().ContainSingle().Which;
        var credential = (await CredentialsAsync(userId)).Should()
            .ContainSingle(t => t.Status == TemporaryCredentialStatus.Activated).Which;
        sender.Observed.Should().Be(1);
        credential.Hash.Should().NotBe(message.TemporaryPassword);
        _factory.Services.GetRequiredService<IPasswordHasher>().Verify(message.TemporaryPassword, credential.Hash).Should().BeTrue();
        credential.ExpiresAt.Should().Be(credential.ActivatedAt!.Value.AddHours(TemporaryCredential.LifetimeHours));
        AssertLogsSafe(email, message.TemporaryPassword, credential.Hash);
    }

    [Fact]
    public async Task PersistenceFailureAfterSupersession_RollsBackAndDoesNotSend()
    {
        var client = NewClient(100); var (email, userId) = await RegisterAsync(client, "rollback");
        await client.PostAsJsonAsync("/api/auth/forgot-password", new { email }); var prior = (await CredentialsAsync(userId)).Should().ContainSingle().Which;
        var hasher = new ConstraintFailingHasher(); ResetCaptures();
        var response = await NewClientWithHasher(hasher).PostAsJsonAsync("/api/auth/forgot-password", new { email });
        response.StatusCode.Should().Be(HttpStatusCode.OK); (await response.Content.ReadFromJsonAsync<ForgotResp>())!.Accepted.Should().BeTrue();
        var remaining = (await CredentialsAsync(userId)).Should().ContainSingle().Which;
        remaining.Id.Should().Be(prior.Id); remaining.Status.Should().Be(TemporaryCredentialStatus.Activated);
        _factory.EmailSender.Captured.Should().BeEmpty(); AssertLogsSafe(email, hasher.Plaintext, hasher.HashValue);
    }

    [Fact]
    public async Task SmtpFailure_LeavesCommittedCredentialActive()
    {
        // Swap the in-memory sender for a faulting one for this test only.
        var faultingSender = new FaultingEmailSender();
        var client = NewClient(recoveryPermitOverride: 100, emailSenderOverride: faultingSender);

        var email = $"smtp{Guid.NewGuid():N}@test.com";
        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "SmtpFail", "Passw0rd!Str0ng"));
        reg.StatusCode.Should().Be(HttpStatusCode.Created);
        ResetCaptures();
        var resp = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        // Uniform 200 generic — SMTP failure must NOT be observable.
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadFromJsonAsync<ForgotResp>())!.Accepted.Should().BeTrue();
        var message = faultingSender.Captured.Should().ContainSingle().Which;

        // A post-accept transport failure cannot compensate the committed row.
        var userId = await UserIdAsync(email);
        var first = (await CredentialsAsync(userId)).Should()
            .ContainSingle(t => t.Status == TemporaryCredentialStatus.Activated).Which;
        AssertLogsSafe(email, message.TemporaryPassword, first.Hash);

        _factory.EmailSender.Reset();
        await NewClient(100).PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var rows = await CredentialsAsync(userId);
        rows.Select(t => (t.Generation, t.Status)).Should().Equal(
            (1, TemporaryCredentialStatus.Superseded), (2, TemporaryCredentialStatus.Activated));
    }

    [Fact]
    public async Task ConcurrentRequests_AllocateMonotonicGenerations_WithOneActiveRow()
    {
        var client = NewClient(recoveryPermitOverride: 100); var (email, userId) = await RegisterAsync(client, "concurrent");
        ResetCaptures();
        var responses = await RunLockedAsync(userId, "SELECT id FROM identity.users WHERE id = @id FOR UPDATE",
            () => client.PostAsJsonAsync("/api/auth/forgot-password", new { email }),
            () => client.PostAsJsonAsync("/api/auth/forgot-password", new { email }));
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK); var rows = await CredentialsAsync(userId);
        rows.Select(t => t.Generation).Should().Equal(1, 2); rows.Should().ContainSingle(t => t.Status == TemporaryCredentialStatus.Activated).Which.Generation.Should().Be(2);
        _factory.EmailSender.Captured.Should().HaveCount(2);
        AssertLogsSafe([email, .. _factory.EmailSender.Captured.Select(m => m.TemporaryPassword), .. rows.Select(t => t.Hash)]);
    }

    [Fact]
    public async Task EligibilityIsFreshUnderLock_AndSentinelIsExcluded()
    {
        var client = NewClient(100); var (email, userId) = await RegisterAsync(client, "locked");
        ResetCaptures();
        var locked = await RunLockedAsync(userId,
            "UPDATE identity.users SET locked_until = now() + interval '1 hour' WHERE id = @id RETURNING id",
            () => client.PostAsJsonAsync("/api/auth/forgot-password", new { email }));
        locked.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK); var sentinelEmail = "system@anonymized.local";
        (await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = sentinelEmail })).StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.EmailSender.Captured.Should().BeEmpty(); (await CredentialsAsync(userId)).Should().BeEmpty();
        (await CredentialsAsync(User.NonHumanSentinelId)).Should().BeEmpty();
        AssertLogsSafe(email, sentinelEmail);
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

    private async Task<(string Email, Guid UserId)> RegisterAsync(HttpClient client, string prefix)
    {
        var email = $"{prefix}{Guid.NewGuid():N}@test.com";
        (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, prefix, "Passw0rd!Str0ng"))).StatusCode.Should().Be(HttpStatusCode.Created);
        return (email, await UserIdAsync(email));
    }
    private async Task<T> QueryAsync<T>(Func<IdentityDbContext, Task<T>> query)
    {
        await using var scope = _factory.Services.CreateAsyncScope(); return await query(scope.ServiceProvider.GetRequiredService<IdentityDbContext>());
    }
    private Task<Guid> UserIdAsync(string email) => QueryAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
    private Task<List<TemporaryCredential>> CredentialsAsync(Guid userId) => QueryAsync(db => db.TemporaryCredentials.AsNoTracking()
        .Where(t => t.UserId == userId).OrderBy(t => t.Generation).ToListAsync());
    private async Task<HttpResponseMessage[]> RunLockedAsync(Guid userId, string sql, params Func<Task<HttpResponseMessage>>[] start)
    {
        await using var connection = new NpgsqlConnection(_factory.PostgresConnectionString); await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", userId); await command.ExecuteScalarAsync();
        var requests = start.Select(run => run()).ToArray();
        await WaitForBlockedRequestsAsync(requests.Length); await transaction.CommitAsync();
        return await Task.WhenAll(requests);
    }
    private async Task WaitForBlockedRequestsAsync(int expected)
    {
        await using var connection = new NpgsqlConnection(_factory.PostgresConnectionString); await connection.OpenAsync();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND query ILIKE '%identity.users%FOR UPDATE%'", connection);
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) >= expected) return; await Task.Delay(50);
        }
        throw new TimeoutException($"Expected {expected} blocked recovery request(s).");
    }
    private void ResetCaptures() { _factory.EmailSender.Reset(); _factory.CapturedLogs.Clear(); }
    private void AssertLogsSafe(params string[] values)
    {
        foreach (var line in _factory.CapturedLogs) foreach (var value in values.Where(v => !string.IsNullOrEmpty(v))) line.Should().NotContain(value);
    }
    private sealed class CommitProbeSender(Func<Task<int>> inspect) : IEmailSender
    {
        private int _observed = -1; public ConcurrentQueue<RecoveryEmailMessage> Captured { get; } = new(); public int Observed => Volatile.Read(ref _observed);
        public async Task SendRecoveryEmailAsync(RecoveryEmailMessage message, CancellationToken ct = default)
        { Captured.Enqueue(message); Interlocked.Exchange(ref _observed, await inspect()); }
        public Task SendTenantInviteAsync(TenantInviteEmailMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendWelcomeEmailAsync(WelcomeEmailMessage message, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FaultingEmailSender : JadeCapital.Shared.Infrastructure.Email.IEmailSender
    {
        public ConcurrentQueue<RecoveryEmailMessage> Captured { get; } = new();
        public Task SendRecoveryEmailAsync(JadeCapital.Shared.Infrastructure.Email.RecoveryEmailMessage message, CancellationToken ct = default)
        {
            Captured.Enqueue(message);
            throw new InvalidOperationException("SMTP transport failure (simulated).");
        }

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

    private sealed class ConstraintFailingHasher : IPasswordHasher
    {
        private string _plaintext = string.Empty; public string Plaintext => Volatile.Read(ref _plaintext); public string HashValue { get; } = new('x', 256);
        public string Hash(string password) { Interlocked.Exchange(ref _plaintext, password); return HashValue; }
        public bool Verify(string password, string hash) => false;
    }
}
