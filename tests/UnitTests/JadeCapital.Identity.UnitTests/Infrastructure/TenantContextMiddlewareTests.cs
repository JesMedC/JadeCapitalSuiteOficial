using System.Security.Claims;
using FluentAssertions;
using JadeCapital.Identity.Infrastructure.MultiTenancy;
using JadeCapital.Shared.Kernel.MultiTenancy;
using Microsoft.AspNetCore.Http;

namespace JadeCapital.Identity.UnitTests.Infrastructure;

/// <summary>
/// Behavior tests for <see cref="JadeCapital.Identity.Infrastructure.MultiTenancy.TenantContextMiddleware"/>
/// (Wave 6, slice 6c.2).
///
/// <para>
/// The middleware sits in the pipeline right after <c>UseAuthentication</c>
/// + <c>UseAuthorization</c> and reads the <c>tenant_id</c> claim from the
/// <see cref="HttpContext.User"/> principal. It does NOT itself set the
/// <see cref="ITenantContext"/> — the real implementation reads the
/// principal at access-time via <see cref="IHttpContextAccessor"/>, so
/// middleware responsibility is purely the 401 gate for missing / malformed
/// <c>tenant_id</c> claims on authenticated requests.
/// </para>
///
/// <para>
/// Six RED scenarios per spec (Phase 1 / tasks 1.1):
/// </para>
/// <list type="number">
///   <item>Authenticated user <b>with</b> <c>tenant_id</c> → next() (request continues)</item>
///   <item>Authenticated user <b>without</b> <c>tenant_id</c> → 401
///         <c>auth.tenant_missing</c></item>
///   <item>Anonymous user (no auth claim) → next() (public endpoints keep working)</item>
///   <item>Malformed <c>tenant_id</c> claim → 401 (defense-in-depth: we never
///         parse a Guid in the middleware; any non-empty string the
///         validator rejects is a malformed token)</item>
///   <item>Multiple requests → fresh resolution per request (the middleware
///         stores nothing; the per-request <see cref="HttpContext.User"/>
///         is independent)</item>
///   <item>Exception in <c>next()</c> propagates (the middleware does not
///         swallow downstream failures)</item>
/// </list>
/// </summary>
public class TenantContextMiddlewareTests
{
    private const string TenantClaim = "tenant_id";

    private static HttpContext MakeContext(ClaimsPrincipal? user, bool captureBody = false)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = user ?? new ClaimsPrincipal(new ClaimsIdentity());
        // Ensure the response body can be read back when the middleware
        // short-circuits with a 401. DefaultHttpContext returns a no-op
        // NullStream that does not preserve written bytes; the helper
        // below swaps a real MemoryStream in for the failure-path tests.
        if (captureBody)
            ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static string ReadBody(HttpContext ctx)
    {
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        using var sr = new StreamReader(ctx.Response.Body, leaveOpen: true);
        return sr.ReadToEnd();
    }

    private static ClaimsPrincipal AuthenticatedUser(Guid? tenantId = null, bool skipTenant = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, "Trader") };
        if (!skipTenant && tenantId.HasValue)
            claims.Add(new Claim(TenantClaim, tenantId.Value.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }

    /// <summary>Harness: middleware + a stub downstream pipeline.</summary>
    private sealed class MiddlewareHarness
    {
        public List<HttpContext> Seen { get; } = new();
        public bool NextCalled { get; set; }
        public Exception? NextThrows { get; set; }

        public RequestDelegate Next => ctx =>
        {
            NextCalled = true;
            Seen.Add(ctx);
            if (NextThrows is not null) throw NextThrows;
            return Task.CompletedTask;
        };

        public async Task Invoke(HttpContext ctx)
        {
            var middleware = new JadeCapital.Identity.Infrastructure.MultiTenancy.TenantContextMiddleware(Next);
            await middleware.InvokeAsync(ctx);
        }
    }

    [Fact]
    public async Task AuthenticatedUserWithTenant_CallsNext()
    {
        var harness = new MiddlewareHarness();
        var ctx = MakeContext(AuthenticatedUser(tenantId: Guid.NewGuid()));

        await harness.Invoke(ctx);

        // Phase 1 #1: authenticated user WITH a valid tenant_id → request continues.
        harness.NextCalled.Should().BeTrue();
        harness.Seen.Should().ContainSingle();
    }

    [Fact]
    public async Task AuthenticatedUserWithoutTenant_Returns401()
    {
        var harness = new MiddlewareHarness();
        // Authenticated but no tenant_id claim (pre-Wave-6 JWT, e.g. one
        // minted before slice 6c.2 deployed, or a service token).
        var ctx = MakeContext(AuthenticatedUser(skipTenant: true), captureBody: true);

        await harness.Invoke(ctx);

        // Phase 1 #2: authenticated user WITHOUT tenant_id → 401 with our code.
        harness.NextCalled.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        ctx.Response.ContentType.Should().StartWith("application/problem+json");
        ReadBody(ctx).Should().Contain("auth.tenant_missing");
    }

    [Fact]
    public async Task AnonymousUser_CallsNext()
    {
        var harness = new MiddlewareHarness();
        var ctx = MakeContext(user: new ClaimsPrincipal(new ClaimsIdentity()));

        await harness.Invoke(ctx);

        // Phase 1 #3: anonymous (public endpoints must keep working — /auth/login,
        // /auth/register, health checks, billing public catalog, etc. all
        // run without a JWT).
        harness.NextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task MalformedTenant_Returns401()
    {
        var harness = new MiddlewareHarness();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, "Trader"),
            new(TenantClaim, "not-a-guid")
        };
        var ctx = MakeContext(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), captureBody: true);

        await harness.Invoke(ctx);

        // Phase 1 #4: malformed tenant_id → 401. We don't try to parse
        // a Guid in the middleware; we check Guid.TryParse and reject
        // anything that isn't a valid Guid-shaped value. This is
        // defense-in-depth: a misconfigured issuer can't smuggle a
        // bad tenant_id and have downstream code pretend it's a valid
        // tenant.
        harness.NextCalled.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        ReadBody(ctx).Should().Contain("auth.tenant_malformed");
    }

    [Fact]
    public async Task MultipleRequests_AreIndependent()
    {
        // Phase 1 #5: two requests in a row, each with its own
        // HttpContext + User. The middleware stores no state; the
        // per-request principal is read fresh each time. A request
        // with a missing claim is rejected; a subsequent request
        // with a valid claim passes.
        var noTenantHarness = new MiddlewareHarness();
        var badCtx = MakeContext(AuthenticatedUser(skipTenant: true));
        await noTenantHarness.Invoke(badCtx);
        noTenantHarness.NextCalled.Should().BeFalse();
        badCtx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        var goodHarness = new MiddlewareHarness();
        var goodCtx = MakeContext(AuthenticatedUser(tenantId: Guid.NewGuid()));
        await goodHarness.Invoke(goodCtx);
        goodHarness.NextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task ExceptionInNext_Propagates()
    {
        var harness = new MiddlewareHarness { NextThrows = new InvalidOperationException("boom") };
        var ctx = MakeContext(AuthenticatedUser(tenantId: Guid.NewGuid()));

        // Phase 1 #6: the middleware does not swallow downstream failures.
        // We let the exception bubble so the global exception handler
        // in Program.cs maps it to 500 (or whatever shape the caller
        // expects). Catching here would hide bugs and break correlation
        // with Serilog request logs.
        Func<Task> act = async () => await harness.Invoke(ctx);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("boom");
    }

    [Fact]
    public async Task AuthenticatedUserWithTenant_NextSeesItInClaims()
    {
        // Extras beyond the 6 RED: confirm the middleware does NOT
        // mutate the principal. The downstream pipeline sees the
        // same claims the middleware resolved — there's no shadowing
        // or rewriting.
        var harness = new MiddlewareHarness();
        var tenantId = Guid.NewGuid();
        var ctx = MakeContext(AuthenticatedUser(tenantId: tenantId));

        await harness.Invoke(ctx);

        harness.NextCalled.Should().BeTrue();
        ctx.User.FindFirst(TenantClaim)!.Value.Should().Be(tenantId.ToString());
    }
}
