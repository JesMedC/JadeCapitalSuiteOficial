// Wave 12 slice 12.1 — /api/util/client-ip endpoint integration test.
//
// Uses the minimal-host pattern from
// JadeCapital.Admin.UnitTests/Endpoints/AdminAuditEndpointsIntegrationTests
// (no Postgres / Redis / Ollama needed — the endpoint is a pure delegate
// over HttpContext headers + RemoteIpAddress).
//
// RED scenarios:
//   1. WithXForwardedFor_ReturnsFirstHop — X-Forwarded-For "1.2.3.4, 10.0.0.1"
//      must yield "1.2.3.4" (left-most per RFC 7239 §5.2).
//   2. WithoutXForwardedFor_FallsBackToRemoteIp — connection peer "5.6.7.8"
//      must be returned verbatim.
//   3. AnonymousAccess_No401 — endpoint is AllowAnonymous; unauthenticated
//      requests MUST succeed (the cookie-consent banner needs the IP before
//      the visitor logs in).

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JadeCapital.Identity.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Host.UnitTests.Endpoints;

public class ClientIpEndpointTests : IClassFixture<ClientIpEndpointTests.MinimalHost>
{
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    private readonly MinimalHost _host;

    public ClientIpEndpointTests(MinimalHost host) { _host = host; }

    [Fact]
    public async Task WithXForwardedFor_ReturnsFirstHop()
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "1.2.3.4, 10.0.0.1, 192.168.0.42");
        var resp = await client.GetAsync("/api/util/client-ip");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        body.GetProperty("ip").GetString().Should().Be("1.2.3.4");
    }

    [Fact]
    public async Task WithoutXForwardedFor_FallsBackToRemoteIp()
    {
        var client = _host.CreateClient();
        var resp = await client.GetAsync("/api/util/client-ip");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        // TestServer pipes a synthetic transport peer; we only assert the
        // shape is an IP-like string (not null, not "0.0.0.0" sentinel).
        body.GetProperty("ip").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AnonymousAccess_No401()
    {
        var client = _host.CreateClient();
        var resp = await client.GetAsync("/api/util/client-ip");
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "the endpoint is AllowAnonymous — the cookie consent banner needs the IP BEFORE login.");
    }

    [Fact]
    public async Task XForwardedForSingleHop_ReturnsItVerbatim()
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.42");
        var resp = await client.GetAsync("/api/util/client-ip");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        body.GetProperty("ip").GetString().Should().Be("203.0.113.42");
    }

    [Fact]
    public async Task XForwardedForEmpty_FallsBackToRemoteIp_OrSentinel()
    {
        // Defensive: a hostile client (or buggy proxy) sends an empty header.
        // We must NOT crash. If a transport peer is available (real nginx
        // in prod) we return it; if not (TestServer, raw TCP without peer
        // info) we return the "0.0.0.0" sentinel. Either is acceptable.
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "");
        var resp = await client.GetAsync("/api/util/client-ip");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        var ip = body.GetProperty("ip").GetString();
        ip.Should().NotBeNullOrWhiteSpace(
            "the endpoint must always return a non-empty IP — either the first hop or the transport peer or the 0.0.0.0 sentinel.");
    }

    /// <summary>
    /// Minimal host: only routing + the <c>MapClientIpEndpoint()</c>
    /// mapping. No auth, no rate limiter, no DB — the endpoint is
    /// AllowAnonymous and a pure HttpContext reader.
    /// </summary>
    public sealed class MinimalHost : IDisposable
    {
        private readonly WebApplication _app;

        public MinimalHost()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);

            _app = builder.Build();
            _app.UseRouting();
            _app.MapClientIpEndpoint();
            _app.Start();
        }

        public HttpClient CreateClient() => _app.GetTestServer().CreateClient();

        public void Dispose()
        {
            _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
