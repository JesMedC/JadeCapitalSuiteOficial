using System.Security.Claims;
using FluentAssertions;
using JadeCapital.Identity.Infrastructure.MultiTenancy;
using JadeCapital.Shared.Kernel.MultiTenancy;
using Microsoft.AspNetCore.Http;

namespace JadeCapital.Identity.UnitTests.Infrastructure;

/// <summary>
/// Behavior tests for <see cref="JadeCapital.Identity.Infrastructure.MultiTenancy.TenantContext"/>
/// (Wave 6, slice 6c.2).
///
/// <para>
/// The implementation reads three values from <c>HttpContext.User</c>:
/// </para>
/// <list type="bullet">
///   <item><c>tenant_id</c> claim → <see cref="ITenantContext.Current"/></item>
///   <item><c>sub</c> (NameIdentifier) claim → <see cref="ITenantContext.CurrentUserId"/></item>
///   <item><c>role</c> claim whose value is <c>"SuperAdmin"</c> →
///         <see cref="ITenantContext.IsSuperAdmin"/></item>
/// </list>
///
/// <para>
/// Five RED scenarios pinned here per the spec (Phase 2 / tasks 2.1):
/// </para>
/// <list type="number">
///   <item>HttpContext-bound <c>Current</c> returns <c>tenant_id</c> from JWT</item>
///   <item><c>CurrentUserId</c> returns <see cref="ClaimsIdentity.NameIdentifier"/></item>
///   <item><c>IsSuperAdmin</c> returns true for <c>SuperAdmin</c> role</item>
///   <item><c>Current</c> returns <c>null</c> for anonymous (no auth)</item>
///   <item>Mocked <see cref="IHttpContextAccessor"/> — covered via NSubstitute
///         in every test; no special case needed beyond parameter pass-through</item>
/// </list>
/// </summary>
public class TenantContextTests
{
    private const string TenantClaim = "tenant_id";

    private static IHttpContextAccessor CtxWith(ClaimsPrincipal? user)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        var http = Substitute.For<HttpContext>();
        http.User.Returns(user ?? new ClaimsPrincipal(new ClaimsIdentity()));
        accessor.HttpContext.Returns(http);
        return accessor;
    }

    private static ClaimsPrincipal AuthenticatedUser(Guid? userId = null, Guid? tenantId = null, string role = "Trader")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, role),
        };
        if (userId.HasValue)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
        if (tenantId.HasValue)
            claims.Add(new Claim(TenantClaim, tenantId.Value.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }

    [Fact]
    public void Current_ReturnsTenantIdClaim_WhenPresent()
    {
        var tenantId = Guid.NewGuid();
        var sut = new TenantContext(CtxWith(AuthenticatedUser(tenantId: tenantId)));

        // Phase 2 #1: HttpContext-bound Current reads tenant_id from JWT.
        sut.Current.Should().Be(new TenantId(tenantId));
    }

    [Fact]
    public void CurrentUserId_ReturnsNameIdentifierClaim_WhenPresent()
    {
        var userId = Guid.NewGuid();
        var sut = new TenantContext(CtxWith(AuthenticatedUser(userId: userId)));

        // Phase 2 #2: CurrentUserId reads the NameIdentifier (sub) claim.
        sut.CurrentUserId.Should().Be(userId);
    }

    [Fact]
    public void IsSuperAdmin_ReturnsTrue_ForSuperAdminRole()
    {
        var sut = new TenantContext(CtxWith(AuthenticatedUser(role: "SuperAdmin")));

        // Phase 2 #3: IsSuperAdmin reads the role claim.
        sut.IsSuperAdmin.Should().BeTrue();
    }

    [Fact]
    public void IsSuperAdmin_ReturnsFalse_ForTraderRole()
    {
        var sut = new TenantContext(CtxWith(AuthenticatedUser(role: "Trader")));

        sut.IsSuperAdmin.Should().BeFalse();
    }

    [Fact]
    public void Current_ReturnsNull_ForAnonymousCaller()
    {
        var sut = new TenantContext(CtxWith(user: new ClaimsPrincipal(new ClaimsIdentity())));

        // Phase 2 #4: anonymous callers (no auth) see null Current.
        sut.Current.Should().BeNull();
    }

    [Fact]
    public void CurrentUserId_ReturnsNull_ForAnonymousCaller()
    {
        var sut = new TenantContext(CtxWith(user: new ClaimsPrincipal(new ClaimsIdentity())));

        sut.CurrentUserId.Should().BeNull();
    }

    [Fact]
    public void Current_ReturnsNull_WhenHttpContextIsNull()
    {
        // Defensive: the middleware has not run (background jobs, manual scope).
        // Treat as anonymous.
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var sut = new TenantContext(accessor);

        sut.Current.Should().BeNull();
        sut.CurrentUserId.Should().BeNull();
        sut.IsSuperAdmin.Should().BeFalse();
    }

    [Fact]
    public void Current_ReturnsNull_WhenTenantClaimIsMalformedGuid()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, "Trader"),
            new(TenantClaim, "not-a-guid")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var sut = new TenantContext(CtxWith(user));

        // Phase 2 hardening: a malformed tenant_id must NOT poison the
        // request — the middleware emits 401 BEFORE TenantContext is
        // resolved; if the malformed claim somehow reaches us (e.g. an
        // admin script that bypassed the middleware), Current is null
        // and the caller falls back to anonymous.
        sut.Current.Should().BeNull();
    }
}
