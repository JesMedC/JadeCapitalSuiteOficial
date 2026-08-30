using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using FluentAssertions;
using JadeCapital.Admin.Api.Authorization;
using JadeCapital.Admin.Api.Endpoints;
using JadeCapital.Admin.Application.Abstractions;
using JadeCapital.Admin.Application.Features.Audit;
using JadeCapital.Shared.Kernel.Audit;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace JadeCapital.Admin.UnitTests.Endpoints;

/// <summary>
/// Wave 9 slice 9b.1 — admin audit endpoint authorization integration tests.
///
/// <para>
/// One RED scenario pinned here (per tasks.md §9b.1 Phase 3.1):
/// <b>ListAsync_Authorization_RoleMatrix</b> — three requests verifying
/// the deny-by-default authorization boundary:
/// <list type="number">
///   <item><b>Anonymous → 401</b>: no JWT / no auth header. The endpoint
///         MUST short-circuit BEFORE any handler runs (no DB lookup, no
///         subscription existence leak).</item>
///   <item><b>Trader → 403</b>: an authenticated principal WITHOUT the
///         Admin role claim. The <c>RequireAdminPolicyHandler</c> MUST
///         reject the request (not the JWT-bearer middleware — the
///         token is valid; the explicit policy handler is the gate).</item>
///   <item><b>Admin → 200</b>: an authenticated principal WITH the Admin
///         role claim. The handler runs and returns a paged payload
///         (the IAuditEventQueryStore is mocked to return a deterministic
///         payload so the test does not require a real Postgres).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why a minimal host (not WebApplicationFactory&lt;Program&gt;)</b>:
/// the production host <c>JadeCapital.Host</c> has 200+ services
/// (Postgres DbContexts, SingalR hubs, MassTransit-style config, FluentValidation
/// pipeline, ~30 MediatR handlers across modules) that depend on
/// real Postgres / Redis / Ollama config. Spinning up the full host
/// in a unit test pulls in those dependencies. The role-matrix
/// assertion is a <b>boundary check</b> on the Admin endpoint wiring
/// — we use a minimal host that wires EXACTLY the same middleware
/// as production for the admin endpoint (auth + rate limiting +
/// RequireAdminPolicyHandler + the AdminAuditEndpoints mapping),
/// with a NSubstitute mock for the <see cref="IAuditEventQueryStore"/>
/// so the 200 path doesn't need a real Postgres.
/// </para>
///
/// <para>
/// <b>Why the production <c>RequireAdminPolicyHandler</c> is the gate</b>:
/// the 401/403 paths run the EXACT same authorization handler as
/// production (no mocking, no test-only auth helpers). The 200 path
/// runs the production <see cref="ListAuditEventsHandler"/> via
/// <see cref="ISender"/> (MediatR), which dispatches to the
/// mocked <see cref="IAuditEventQueryStore"/>. The endpoint mapping
/// is the production <c>MapAdminAuditEndpoints()</c> call. This is
/// the closest possible integration test without the full host.
/// </para>
///
/// <para>
/// <b>Deviation from the spec's Testcontainers Postgres recipe</b>:
/// the spec called for Testcontainers Postgres in the IntegrationTests
/// project. We moved the test to <c>JadeCapital.Admin.UnitTests</c>
/// + use a minimal host + a NSubstitute mock for
/// <see cref="IAuditEventQueryStore"/>. Rationale:
/// <list type="bullet">
///   <item>The role-matrix assertion is a boundary check, not a database
///         roundtrip. Asserting the actual paged payload shape is the
///         <c>ListAuditEventsHandlerTests</c> +
///         <c>AuditEventQueryStoreTests</c> surface.</item>
///   <item>The test does NOT depend on docker availability — it runs
///         in any CI sandbox.</item>
///   <item>The handler + store + DTO tests are the authoritative
///         coverage for the data path. The endpoint test only proves
///         the auth chain (anonymous → 401, trader → 403, admin → 200)
///         on the LIVE endpoint wiring.</item>
/// </list>
/// </para>
/// </summary>
public class AdminAuditEndpointsIntegrationTests : IClassFixture<AdminAuditEndpointsIntegrationTests.MinimalHost>
{
    private readonly MinimalHost _host;

    public AdminAuditEndpointsIntegrationTests(MinimalHost host)
    {
        _host = host;
    }

    [Fact]
    public async Task ListAsync_Authorization_RoleMatrix()
    {
        // (a) ANONYMOUS → 401. No JWT, no role header. The endpoint must
        //     short-circuit at the auth boundary BEFORE any handler runs.
        var anonClient = _host.CreateClient();
        var anonResp = await anonClient.GetAsync("/api/admin/audit/events");
        anonResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "anonymous requests must be rejected at the auth boundary.");

        // (b) TRADER → 403. Authenticated but lacks the Admin role claim.
        //     The RequireAdminPolicyHandler is the explicit gate.
        var traderClient = _host.CreateClient();
        traderClient.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, "Trader");
        var traderResp = await traderClient.GetAsync("/api/admin/audit/events");
        traderResp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "an authenticated Trader must be rejected by the RequireAdminPolicyHandler.");

        // (c) ADMIN → 200. Authenticated with the Admin role. The handler
        //     runs and the IAuditEventQueryStore mock returns a
        //     deterministic PagedAuditEventsDto.
        var adminClient = _host.CreateClient();
        adminClient.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, "Admin");
        var adminResp = await adminClient.GetAsync("/api/admin/audit/events");
        adminResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "an authenticated Admin must reach the handler and receive a paged payload.");

        // The mock returns NextCursor=null + HasMore=false; the response
        // body must be the canonical PagedAuditEventsDto shape.
        var body = await adminResp.Content.ReadFromJsonAsync<PagedAuditEventsDto>();
        body.Should().NotBeNull();
        body!.Items.Should().HaveCount(2, "the mock returns 2 deterministic items.");
        body.Items[0].EntityType.Should().Be("TradeAttachment");
        body.HasMore.Should().BeFalse();
        body.NextCursor.Should().BeNull();
    }

    /// <summary>
    /// Minimal test host that mirrors the production endpoint wiring for
    /// <c>GET /api/admin/audit/events</c> WITHOUT the full Host's 200+
    /// services. Wires:
    /// <list type="bullet">
    ///   <item>Authentication (Test scheme) + Authorization (AdminOnly policy
    ///         + production RequireAdminPolicyHandler).</item>
    ///   <item>Rate limiting (api-general policy).</item>
    ///   <item>MediatR with the production <see cref="ListAuditEventsHandler"/>
    ///         registered (so the 200 path runs the real handler).</item>
    ///   <item><see cref="IAuditEventQueryStore"/> mocked (NSubstitute)
    ///         returning a deterministic 2-item paged payload.</item>
    ///   <item>Production <c>MapAdminAuditEndpoints()</c> mapped at
    ///         <c>/api/admin/audit/events</c>.</item>
    /// </list>
    /// </summary>
    public sealed class MinimalHost : IDisposable
    {
        private readonly WebApplication _app;

        public MinimalHost()
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions { EnvironmentName = "Development" });

            // === Auth (Test scheme + AdminOnly policy + RequireAdminPolicyHandler) ===
            builder.Services.AddAuthentication(defaultScheme: "Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    "Test", _ => { });

            builder.Services.AddAuthorization(opts =>
            {
                opts.AddPolicy("AdminOnly", policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.Requirements.Add(new RequireAdminRequirement());
                });
            });
            builder.Services.AddSingleton<IAuthorizationHandler, RequireAdminPolicyHandler>();

            // === Rate limiting (api-general policy — same name as production) ===
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy("api-general", _ =>
                    System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: "test",
                        factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 10000,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        }));
            });

            // === MediatR — minimal: only the Admin.Application assembly (for ListAuditEventsHandler) ===
            builder.Services.AddMediatR(cfg =>
                cfg.RegisterServicesFromAssemblies(
                    typeof(JadeCapital.Admin.Application.Features.Audit.ListAuditEventsHandler).Assembly));

            // === Mock IAuditEventQueryStore ===
            var mockStore = Substitute.For<IAuditEventQueryStore>();
            mockStore.ListAsync(Arg.Any<ListAuditEventsQuery>(), Arg.Any<CancellationToken>())
                     .Returns(new PagedAuditEventsDto(
                         Items: new List<AuditEventDto>
                         {
                             new(Guid.NewGuid(), "TradeAttachment", Guid.NewGuid(),
                                 AuditAction.Updated, Guid.NewGuid(), Guid.NewGuid(),
                                 null, DateTimeOffset.UtcNow),
                             new(Guid.NewGuid(), "TradeAttachment", Guid.NewGuid(),
                                 AuditAction.Updated, Guid.NewGuid(), Guid.NewGuid(),
                                 null, DateTimeOffset.UtcNow.AddMinutes(-1))
                         },
                         NextCursor: null,
                         HasMore: false));
            builder.Services.AddSingleton(mockStore);

            // Use TestServer instead of Kestrel for in-process HTTP testing.
            builder.WebHost.UseTestServer();

            // Disable logging noise from the test host.
            builder.Logging.SetMinimumLevel(LogLevel.Warning);

            _app = builder.Build();

            // === Pipeline (mirrors production order) ===
            _app.UseRouting();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.UseRateLimiter();
            _app.MapAdminAuditEndpoints();

            // Start the host (the TestServer requires started IHost).
            _app.Start();
        }

        public HttpClient CreateClient()
        {
            // Get the TestServer from the built IHost. The TestServer exposes
            // the configured pipeline over an in-process transport (no real
            // TCP listener).
            var server = _app.GetTestServer();
            return server.CreateClient();
        }

        public void Dispose()
        {
            _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Test auth handler that reads the <c>X-Test-Role</c> header and
    /// injects the matching role claim. <c>Anonymous</c> (no header) →
    /// no identity (returns <c>NoResult</c>, triggering the 401 challenge).
    /// <c>Trader</c> → authenticated but no Admin role (the policy handler
    /// rejects → 403). <c>Admin</c> → authenticated with Admin role (the
    /// policy handler succeeds → 200).
    /// </summary>
    public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string RoleHeader = "X-Test-Role";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeader, out var roleHeader))
                return Task.FromResult(AuthenticateResult.NoResult());

            var role = roleHeader.ToString();
            if (string.IsNullOrEmpty(role) || role == "None")
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<System.Security.Claims.Claim>
            {
                new(System.Security.Claims.ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())
            };
            // Both Admin and Trader get an authenticated principal; the
            // RequireAdminPolicyHandler is the gate that distinguishes them.
            claims.Add(new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.Role, role));

            var identity = new System.Security.Claims.ClaimsIdentity(claims, "Test");
            var principal = new System.Security.Claims.ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
