using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using JadeCapital.Host.Configuration;
using JadeCapital.Identity.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Host.UnitTests.Endpoints;

public sealed class ClientIpEndpointTests
{
    [Fact]
    public async Task TrustedProxy_ResolvesForwardedOrigin()
    {
        await using var host = new MinimalHost(new Dictionary<string, string?>
        {
            ["ReverseProxy:KnownProxies:0"] = "10.0.0.10",
            ["ReverseProxy:ForwardLimit"] = "1",
        });

        var response = await host.GetClientIpAsync("10.0.0.10", "203.0.113.9");

        response.Should().Be("203.0.113.9");
    }

    [Fact]
    public async Task TrustedNetwork_ResolvesForwardedOrigin()
    {
        await using var host = new MinimalHost(new Dictionary<string, string?>
        {
            ["ReverseProxy:KnownNetworks:0"] = "10.42.0.0/16",
        });

        var response = await host.GetClientIpAsync("10.42.8.4", "198.51.100.25");

        response.Should().Be("198.51.100.25");
    }

    [Fact]
    public async Task ForwardLimit_StopsAtConfiguredHop()
    {
        await using var host = new MinimalHost(new Dictionary<string, string?>
        {
            ["ReverseProxy:KnownProxies:0"] = "10.0.0.10",
            ["ReverseProxy:KnownProxies:1"] = "10.0.0.20",
            ["ReverseProxy:ForwardLimit"] = "1",
        });

        var response = await host.GetClientIpAsync(
            "10.0.0.10", "203.0.113.9, 10.0.0.20");

        response.Should().Be("10.0.0.20");
    }

    [Fact]
    public async Task UntrustedPeer_CannotSpoofForwardedOrigin()
    {
        await using var host = new MinimalHost(new Dictionary<string, string?>
        {
            ["ReverseProxy:KnownProxies:0"] = "10.0.0.10",
        });

        var response = await host.GetClientIpAsync("198.51.100.7", "203.0.113.9");

        response.Should().Be("198.51.100.7");
    }

    [Fact]
    public async Task EmptyTrustList_IgnoresForwardedOrigin()
    {
        await using var host = new MinimalHost(new Dictionary<string, string?>());

        var response = await host.GetClientIpAsync("192.0.2.44", "203.0.113.9");

        response.Should().Be("192.0.2.44");
    }

    [Fact]
    public async Task DirectRequest_ReturnsTransportPeer()
    {
        await using var host = new MinimalHost(new Dictionary<string, string?>());

        var response = await host.GetClientIpAsync("192.0.2.88");

        response.Should().Be("192.0.2.88");
    }

    [Fact]
    public async Task ClientIpRoute_RemainsAnonymousAndPreservesResponseShape()
    {
        await using var host = new MinimalHost(new Dictionary<string, string?>());
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/util/client-ip");
        request.Headers.Add(MinimalHost.TestPeerHeader, "192.0.2.99");

        using var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<ClientIpEndpoint.ClientIpDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Be(new ClientIpEndpoint.ClientIpDto("192.0.2.99"));
    }

    private sealed class MinimalHost : IAsyncDisposable
    {
        public const string TestPeerHeader = "X-Test-Transport-Peer";
        private readonly WebApplication _app;

        public MinimalHost(IReadOnlyDictionary<string, string?> settings)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Configuration.AddInMemoryCollection(settings);
            builder.AddJadeCapitalReverseProxy();

            _app = builder.Build();
            _app.Use(async (context, next) =>
            {
                if (context.Request.Headers.TryGetValue(TestPeerHeader, out var peer))
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(peer.ToString());
                }

                await next(context);
            });
            _app.UseForwardedHeaders();
            _app.MapClientIpEndpoint();
            _app.Start();
            Client = _app.GetTestServer().CreateClient();
        }

        public HttpClient Client { get; }

        public async Task<string?> GetClientIpAsync(string peer, string? forwardedFor = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/util/client-ip");
            request.Headers.Add(TestPeerHeader, peer);
            if (forwardedFor is not null)
            {
                request.Headers.Add("X-Forwarded-For", forwardedFor);
            }

            using var response = await Client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<ClientIpEndpoint.ClientIpDto>();
            return body?.Ip;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }
}
