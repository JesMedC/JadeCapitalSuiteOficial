using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JadeCapital.Api.IntegrationTests.Auth;
using JadeCapital.Api.IntegrationTests.Infrastructure;

namespace JadeCapital.Api.IntegrationTests.Wave4;

/// <summary>
/// Slice 4e — Scanner endpoint smoke (Phase 3 — Testcontainers).
///
/// Covers the full HTTP path:
///  - CRUD lifecycle (POST filters, GET list, DELETE)
///  - Anonymous request rejected with 401
///
/// Each test creates its own user so the fixture stays isolated.
/// </summary>
public class ScannerEndpointsTests : IClassFixture<JadeApiFactory>
{
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    private readonly JadeApiFactory _factory;

    public ScannerEndpointsTests(JadeApiFactory factory) { _factory = factory; }

    private async Task<HttpClient> RegisterTraderAsync()
    {
        var client = _factory.CreateClient();
        var email = $"scanner{Guid.NewGuid():N}@test.com";
        var resp = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Scanner Tester", "Passw0rd!Str0ng"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var tokens = await resp.Content.ReadFromJsonAsync<TokenResponse>(_jsonOpts);
        tokens!.AccessToken.Should().NotBeNullOrEmpty();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    [Fact]
    public async Task ScannerFilter_FullCrudLifecycle_ReturnsExpectedStatuses()
    {
        var client = await RegisterTraderAsync();

        var createBody = new
        {
            name = $"Wave4 Scanner {Guid.NewGuid():N}".Substring(0, 32),
            minSpread = 0.5m,
            maxSpread = 5.0m,
            minVolume = 1000m,
            minRiskReward = 1.5m,
            volatilityWindow = 7,            // VolatilityWindow.D1
            activeHours = Array.Empty<object>(),
            isActive = true
        };

        var create = await client.PostAsJsonAsync("/api/scanner/filters", createBody);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        var filterId = created.GetProperty("id").GetGuid();

        var list = await client.GetAsync("/api/scanner/filters");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var arr = await list.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        arr.GetArrayLength().Should().BeGreaterThan(0);

        var single = await client.GetAsync($"/api/scanner/filters/{filterId}");
        single.StatusCode.Should().Be(HttpStatusCode.OK);

        var delete = await client.DeleteAsync($"/api/scanner/filters/{filterId}");
        delete.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.OK);
    }

    [Fact]
    public async Task ScannerFilter_AnonymousRequest_Returns401()
    {
        var client = _factory.CreateClient();   // no auth header
        var resp = await client.GetAsync("/api/scanner/filters");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}