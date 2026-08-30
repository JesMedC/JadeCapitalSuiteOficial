using System.Net;
using System.Net.Http.Json;
using JadeCapital.Api.IntegrationTests.Auth;
using JadeCapital.Api.IntegrationTests.Infrastructure;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Api.IntegrationTests.Gdpr;

// ============================================================================
//  DeleteAccountFlowTests — Wave 11 slice 11.2b.
//
//  Hits the real Host via WebApplicationFactory against a Testcontainers
//  Postgres + Redis (JadeApiFactory). Two scenarios per tasks.md §11.2b
//  Phase 3:
//
//    1.1 FullFlow                      — register → DELETE → assert
//        user row anonymized + refresh tokens revoked + audit row written.
//    1.2 AnonymizedUserCannotLogin      — DELETE then attempt login → 401.
//
//  The cross-tenant 403 scenario is covered by the unit test suite (handler
//  tests prove the JWT-driven userId extraction is the only path) — the
//  integration tier exercises the full HTTP + DB round-trip.
// ============================================================================

public class DeleteAccountFlowTests : IClassFixture<JadeApiFactory>
{
    private readonly JadeApiFactory _factory;

    public DeleteAccountFlowTests(JadeApiFactory factory) { _factory = factory; }

    private HttpClient NewClient() => _factory.CreateClient();

    [Fact]
    public async Task DeleteAccountFlow_AuthedUser_TriggersCascade_AnonymizesAuditTrail()
    {
        var client = NewClient();

        // 1. Register a fresh user.
        var email = $"gdpr{Guid.NewGuid():N}@test.com";
        var regResp = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "GDPR User", "Passw0rd!Str0ng"));
        regResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var tokens = await regResp.Content.ReadFromJsonAsync<TokenResponse>();
        tokens.Should().NotBeNull();

        // 2. Authorize the DELETE call with the freshly minted access token.
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        // 3. DELETE /api/users/me/account → 202 + body.
        var deleteResp = await client.DeleteAsync("/api/users/me/account");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await deleteResp.Content.ReadFromJsonAsync<DeleteAccountResponse>();
        body.Should().NotBeNull();
        body!.UserId.Should().Be(tokens.UserId);
        body.Status.Should().Be("SoftDeleted");
        body.ScheduledHardDeleteAt.Should().NotBe(default);

        // 4. Assert DB state: user row is anonymized + status is ScheduledHardDelete.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == tokens.UserId);
        user.Status.Should().Be(UserStatus.ScheduledHardDelete);
        user.SoftDeletedAt.Should().NotBeNull();
        user.ScheduledHardDeleteAt.Should().NotBeNull();
        user.Email.Should().StartWith("deleted-").And.EndWith("@anonymized.local");
        user.DisplayName.Should().Be("Deleted User");
        user.PasswordHash.Should().BeEmpty();

        // 5. Assert cascade: refresh tokens for this user are revoked.
        var activeTokens = await db.RefreshTokens
            .Where(r => r.UserId == tokens.UserId && r.RevokedAt == null)
            .CountAsync();
        activeTokens.Should().Be(0, "the cascade should have revoked every active refresh token");

        // 6. Assert audit row written (action = Deleted, entity_type = User).
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var auditCount = await auditDb.AuditEvents
            .Where(e => e.EntityType == "User" && e.EntityId == tokens.UserId)
            .CountAsync();
        auditCount.Should().BeGreaterThanOrEqualTo(1, "the GDPR cascade must write at least one audit row");
    }

    [Fact]
    public async Task DeleteAccountFlow_AnonymizedUser_CannotLogin_RefreshTokensRevoked()
    {
        var client = NewClient();

        // 1. Register + DELETE.
        var email = $"gdpr-login{Guid.NewGuid():N}@test.com";
        var regResp = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "GDPR Login User", "Passw0rd!Str0ng"));
        regResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var tokens = await regResp.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var deleteResp = await client.DeleteAsync("/api/users/me/account");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // 2. Try to log in with the original credentials — must fail.
        //    A fresh client is used so the bearer header doesn't interfere.
        var loginClient = NewClient();
        var loginResp = await loginClient.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "Passw0rd!Str0ng"));
        loginResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 3. The refresh token issued at registration must NOT work either.
        var refreshClient = NewClient();
        var refreshResp = await refreshClient.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest(tokens.RefreshToken));
        refreshResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteAccountFlow_Unauthenticated_Returns401()
    {
        var client = NewClient();
        var resp = await client.DeleteAsync("/api/users/me/account");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteAccountFlow_SecondDelete_Returns409()
    {
        var client = NewClient();

        var email = $"gdpr-2x{Guid.NewGuid():N}@test.com";
        var regResp = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "GDPR 2x", "Passw0rd!Str0ng"));
        var tokens = await regResp.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        // First delete succeeds.
        var first = await client.DeleteAsync("/api/users/me/account");
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Second delete: user is already in ScheduledHardDelete status → conflict.
        var second = await client.DeleteAsync("/api/users/me/account");
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private sealed record DeleteAccountResponse(
        Guid UserId,
        DateTimeOffset SoftDeletedAt,
        DateTimeOffset ScheduledHardDeleteAt,
        int CascadeSoftDeletedRows,
        string Status);
}
