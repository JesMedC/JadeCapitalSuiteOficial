using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JadeCapital.Api.IntegrationTests.Auth;
using JadeCapital.Api.IntegrationTests.Infrastructure;

namespace JadeCapital.Api.IntegrationTests.Wave5;

/// <summary>
/// Slice 5c.2 — Phase 2: integration tests for the AI risk-advisor
/// surface (slice 5c.1 endpoints).
///
/// Defense-in-depth: these tests deliberately stay hermetic — they do
/// not require a live Ollama instance. The auth + 404 + health-shape
/// assertions prove the endpoint wiring is correct; the deeper
/// advisory round-trip is covered by the unit tests in
/// <c>JadeCapital.Trading.UnitTests/Application/AiRiskAdvisor</c>
/// (which use HttpMessageHandler mocks to avoid network).
/// </summary>
public class AiRiskAdviceEndpointsTests : IClassFixture<JadeApiFactory>
{
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    private readonly JadeApiFactory _factory;

    public AiRiskAdviceEndpointsTests(JadeApiFactory factory) { _factory = factory; }

    private async Task<HttpClient> RegisterTraderAsync()
    {
        var client = _factory.CreateClient();
        var email = $"wave5ai{Guid.NewGuid():N}@test.com";
        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Wave 5 AI Tester", "Passw0rd!Str0ng"));
        reg.StatusCode.Should().Be(HttpStatusCode.Created);
        var tokens = await reg.Content.ReadFromJsonAsync<TokenResponse>(_jsonOpts);
        tokens!.AccessToken.Should().NotBeNullOrEmpty();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    [Fact]
    public async Task PostRiskAdvice_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/ai/risk-advice", new
        {
            symbol = "EURUSD",
            direction = "buy",
            volume = 0.1m,
            volumeCurrency = "USD",
            entryPrice = 1.08m,
            stopLoss = 1.07m,
            riskRewardAtEntry = 2.0m,
            setupQuality = "high",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetRiskAdvice_ForUnknownTrade_Returns404()
    {
        var client = await RegisterTraderAsync();
        var resp = await client.GetAsync($"/api/ai/risk-advice/{Guid.NewGuid()}");
        // 404 = no advisory persisted for that (random) trade id. This
        // proves the GET endpoint is wired and the cross-user isolation
        // path returns notfound (not 200/empty).
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAiHealth_Authed_ReturnsExpectedShape()
    {
        // The endpoint returns {status, model}. status is 'ok' when
        // Ollama answers /api/tags and 'down' otherwise. Either is
        // acceptable here — we only assert the contract is honored.
        var client = await RegisterTraderAsync();
        var resp = await client.GetAsync("/api/ai/health");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        body.TryGetProperty("status", out var statusProp).Should().BeTrue();
        statusProp.GetString().Should().BeOneOf("ok", "down");
    }

    [Fact]
    public async Task GetAiHealth_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/ai/health");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
