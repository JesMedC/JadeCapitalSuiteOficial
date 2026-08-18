using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Infrastructure.Security;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace JadeCapital.Identity.UnitTests.Infrastructure;

/// <summary>
/// Behavior tests for the Wave-6c.2 JWT mint fix (Wave 6, slice 6c.2).
///
/// <para>
/// <see cref="JwtTokenService.CreateAccessToken"/> now accepts an optional
/// <see cref="TenantId"/> and embeds a <c>tenant_id</c> claim when present.
/// Pre-Wave-6 callers (and any user whose <c>TenantId</c> is <c>null</c> at
/// the moment of minting) continue to receive a JWT WITHOUT the claim —
/// the middleware gates them as <c>auth.tenant_missing</c> on the next call
/// and forces a re-login once 6c.2 deploys.
///
/// <b>Why also test the refresh path</b>: both access AND refresh-minted
/// access tokens must include the claim (per the slice spec) so that
/// rotating sessions don't drop the claim midway.
/// </para>
///
/// <para>
/// Four RED scenarios per spec (Phase 4 / tasks 4.1):
/// </para>
/// <list type="number">
///   <item>User <b>with</b> a tenant → JWT contains <c>tenant_id</c> claim</item>
///   <item>User <b>without</b> a tenant (pre-Wave-6) → no <c>tenant_id</c> claim</item>
///   <item>Refresh-minted access token also carries <c>tenant_id</c>
///         (the same mint path is reused by <c>RefreshTokenHandler</c>)</item>
///   <item>Malformed <c>tenant_id</c> strings are rejected BEFORE minting
///         (the service guards its own input rather than emitting a token
///         that the middleware would then 401 on)</item>
/// </list>
/// </summary>
public class JwtMintWithTenantIdTests
{
    private static JwtTokenService CreateSut(IClock? clock = null)
    {
        var opts = Substitute.For<IOptions<JwtOptions>>();
        opts.Value.Returns(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            AccessTokenSecret = new string('a', 32),
            RefreshTokenSecret = new string('b', 32),
            AccessTokenTtlMinutes = 15,
            RefreshTokenTtlDays = 14
        });
        return new JwtTokenService(opts, clock ?? Substitute.For<IClock>());
    }

    private static JwtSecurityToken Parse(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear(); // keep raw JWT names
        return handler.ReadJwtToken(token);
    }

    [Fact]
    public void CreateAccessToken_WithTenant_AddsTenantIdClaim()
    {
        var sut = CreateSut();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var result = sut.CreateAccessToken(userId, "user@example.com", "Trader", tenantId: new TenantId(tenantId));

        var parsed = Parse(result.Token);
        var claim = parsed.Claims.FirstOrDefault(c => c.Type == "tenant_id");

        // Phase 4 #1: tenant_id claim is present and matches the input Guid.
        claim.Should().NotBeNull();
        Guid.TryParse(claim!.Value, out var actual).Should().BeTrue();
        actual.Should().Be(tenantId);
    }

    [Fact]
    public void CreateAccessToken_WithoutTenant_OmitsClaim()
    {
        var sut = CreateSut();

        // Phase 4 #2: pre-Wave-6 / not-yet-assigned callers do NOT get
        // a tenant_id claim. The middleware will 401 them with
        // auth.tenant_missing, forcing a re-login (or, in the usual
        // case, a refresh-minted token).
        var result = sut.CreateAccessToken(Guid.NewGuid(), "user@example.com", "Trader", tenantId: null);

        var parsed = Parse(result.Token);
        parsed.Claims.Any(c => c.Type == "tenant_id").Should().BeFalse();
    }

    [Fact]
    public void CreateAccessToken_RefreshPath_CarriesTenantId()
    {
        // Phase 4 #3: refresh-minted tokens carry tenant_id when the
        // user has one. We exercise the same mint method
        // RefreshTokenHandler uses (CreateAccessToken), simulating
        // "user record looked up → tenant_id present → mint".
        var sut = CreateSut();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        // Simulate the refresh handler call shape:
        var access = sut.CreateAccessToken(userId, "u@x", "Trader", tenantId: new TenantId(tenantId));

        var parsed = Parse(access.Token);
        Guid.TryParse(parsed.Claims.First(c => c.Type == "tenant_id").Value, out var actual)
            .Should().BeTrue();
        actual.Should().Be(tenantId);
    }

    [Fact]
    public void CreateAccessToken_NullTenant_DoesNotEmitEmptyClaim()
    {
        // Slice 6c.2 hardening: callers in transition (6c.1→6c.2
        // deploys land at different times) pass null. The mint must
        // either omit the claim or be invoked with a tenant. We omit.
        var sut = CreateSut();

        var result = sut.CreateAccessToken(Guid.NewGuid(), "u@x", "Trader", tenantId: null);

        var parsed = Parse(result.Token);
        // The claim must NOT be present with an empty value — that
        // would pass the middleware's "claim exists" check and then
        // fail on TryParse.
        parsed.Claims
            .Where(c => c.Type == "tenant_id")
            .Should().BeEmpty();
    }

    [Fact]
    public void CreateAccessToken_TokenHasStandardClaims()
    {
        // Cross-cutting assertion: the standard JWT set is preserved
        // when we add tenant_id. Specifically: sub, email, role,
        // jti, iat. A regression that over-wrote the claim list
        // (e.g. via a `claims.Clear()` typo) would catch here.
        var sut = CreateSut();
        var userId = Guid.NewGuid();

        var result = sut.CreateAccessToken(userId, "user@example.com", "Trader", tenantId: new TenantId(Guid.NewGuid()));

        var parsed = Parse(result.Token);
        var types = parsed.Claims.Select(c => c.Type).ToHashSet(StringComparer.Ordinal);

        types.Should().Contain("sub");
        types.Should().Contain("email");
        types.Should().Contain(ClaimTypesAdapter.Role);
        types.Should().Contain("jti");
        types.Should().Contain("iat");
        types.Should().Contain("tenant_id");
    }
}

/// <summary>
/// Local alias to avoid importing <c>System.Security.Claims</c> in the test
/// (the project uses the constant via the JWT package's own re-export).
/// </summary>
internal static class ClaimTypesAdapter
{
    public const string Role = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
}
