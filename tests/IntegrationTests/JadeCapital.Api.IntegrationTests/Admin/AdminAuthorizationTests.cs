using JadeCapital.Api.IntegrationTests.Auth;
using JadeCapital.Api.IntegrationTests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace JadeCapital.Api.IntegrationTests.Admin;

/// <summary>
/// Slice 0f.1 — integration RED tests for Admin API authorization.
///
/// The Admin API MUST deny every request BEFORE any subscription lookup or
/// mutation side effect. Two distinct denial scenarios are tested:
///   (1) non-Admin authenticated identities (a Trader token).
///   (2) restricted-scope tokens — the JWT carries scope=password_change,
///       which is sufficient for /api/auth/change-password but MUST be denied
///       for /api/admin/subscriptions/* even though the token is authenticated.
/// Plus an Owner projection narrowing test that asserts the contract surface
/// exposes only Email + DisplayName (no role, status, or sensitive fields).
/// </summary>
public class AdminAuthorizationTests : IClassFixture<JadeApiFactory>
{
    private readonly JadeApiFactory _factory;

    public AdminAuthorizationTests(JadeApiFactory factory) { _factory = factory; }

    [Fact]
    public async Task AdminEndpoint_RejectsNonAdmin_BeforeLookup()
    {
        var client = _factory.CreateClient();
        // Register a Trader (the default role).
        var email = $"trader{Guid.NewGuid():N}@test.com";
        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Trader", "Passw0rd!Str0ng"));
        reg.StatusCode.Should().Be(HttpStatusCode.Created);
        var tokens = await reg.Content.ReadFromJsonAsync<TokenResponse>();
        tokens!.Role.Should().Be("Trader");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        // Hit a representative Admin endpoint. MUST be 401/403 BEFORE any lookup
        // or DB query — the policy lives at the endpoint boundary, not in the
        // handler body. Either status is acceptable as long as no 200/404 leaks
        // subscription existence information.
        var resp = await client.GetAsync("/api/admin/subscriptions?status=active&page=1&pageSize=10");

        ((int)resp.StatusCode).Should().BeOneOf(401, 403);
    }

    [Fact]
    public async Task AdminEndpoint_RejectsForcedChangeToken()
    {
        // Use the existing /api/auth/change-password flow to obtain a
        // restricted-scope token (scope=password_change). That token MUST NOT
        // grant Admin access — the AdminOnly policy must demand the Admin role.
        var client = _factory.CreateClient();
        var email = $"restricted{Guid.NewGuid():N}@test.com";
        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "User", "Passw0rd!Str0ng"));
        reg.StatusCode.Should().Be(HttpStatusCode.Created);
        var tokens = await reg.Content.ReadFromJsonAsync<TokenResponse>();

        // Build a JWT with the restricted scope using the same key/issuer/audience
        // as the test factory. We sign it ourselves so we don't have to wait for
        // the actual password-recovery flow to issue one.
        var restrictedJwt = BuildJwtWithScope(
            tokens!.UserId, tokens.Email,
            issuer: "JadeCapitalSuite.Test",
            audience: "JadeCapitalSuite.Test.Client",
            secret: "test_secret_for_integration_tests_must_be_32_chars_xx",
            scope: "password_change");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", restrictedJwt);
        var resp = await client.GetAsync("/api/admin/subscriptions?status=active&page=1&pageSize=10");
        ((int)resp.StatusCode).Should().BeOneOf(401, 403);
    }

    [Fact]
    public void OwnerProjection_ExposesOnlyEmailAndDisplayName()
    {
        // Contract assertion: the projection interface MUST expose only Email +
        // DisplayName. This guards against accidental widening — e.g. role
        // claims, status, or sensitive fields MUST NOT leak to the Admin read
        // surface. Reflection check on the interface's public properties.
        var iface = typeof(JadeCapital.Identity.Contracts.Projections.IUserOwnerProjection);
        var publicProperties = iface.GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        publicProperties.Should().BeEquivalentTo(new[] { "DisplayName", "Email" },
            "the projection MUST expose only Email + DisplayName — no role, status, or other fields");
    }

    /// <summary>Mint a HS256 JWT with the given scope claim. Used by the
    /// restricted-scope test to simulate a forced-change grant token.</summary>
    private static string BuildJwtWithScope(
        Guid userId, string email, string issuer, string audience, string secret, string scope)
    {
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(secret));
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()),
            new System.Security.Claims.Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, userId.ToString()),
            new System.Security.Claims.Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email, email),
            new System.Security.Claims.Claim("scope", scope),
        };
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);
        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }
}
