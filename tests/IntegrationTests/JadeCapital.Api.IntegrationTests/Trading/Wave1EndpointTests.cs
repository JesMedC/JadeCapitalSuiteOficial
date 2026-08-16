using JadeCapital.Api.IntegrationTests.Auth;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JadeCapital.Api.IntegrationTests.Trading;

/// <summary>
/// Slice 1e — Integration coverage for the 4 Wave 1 endpoints added on top
/// of /api/trades: risk-profile (1a), position-size (1b), metrics (1f),
/// trade-review (1d). MinIO presigned flows are intentionally excluded —
/// they require Testcontainers MinIO and would blow the line budget.
/// </summary>
public class Wave1EndpointTests : IClassFixture<JadeApiFactory>
{
    private readonly JadeApiFactory _factory;

    public Wave1EndpointTests(JadeApiFactory factory) { _factory = factory; }

    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    private async Task<HttpClient> RegisterTraderAsync()
    {
        var client = _factory.CreateClient();
        var email = $"wave1{Guid.NewGuid():N}@test.com";
        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Trader", "Passw0rd!Str0ng"));
        reg.StatusCode.Should().Be(HttpStatusCode.Created);
        var tokens = await reg.Content.ReadFromJsonAsync<TokenResponse>(_jsonOpts);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    // ===== Risk Profile (1a) =====

    [Fact]
    public async Task RiskProfile_NoAuth_Returns401()
    {
        var anon = _factory.CreateClient();
        var resp = await anon.GetAsync("/api/risk-profile");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PutRiskProfile_WithCapitalAndRisk_CreatesProfile_ThenGetReturnsIt()
    {
        var client = await RegisterTraderAsync();

        var put = await client.PutAsJsonAsync("/api/risk-profile", new
        {
            capitalAmount = 10000m,
            capitalCurrency = "USD",
            maxDrawdownPercent = 10m,
            riskPerTradePercent = 1m,
            riskRewardTarget = 2m
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var putBody = await put.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        putBody.GetProperty("capitalAmount").GetDecimal().Should().Be(10000m);
        putBody.GetProperty("riskRewardTarget").GetDecimal().Should().Be(2m);
        putBody.GetProperty("isActive").GetBoolean().Should().BeTrue();

        var get = await client.GetAsync("/api/risk-profile");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var getBody = await get.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        getBody.GetProperty("id").GetGuid().Should().Be(putBody.GetProperty("id").GetGuid());
    }

    // ===== Position Size (1b) =====

    [Fact]
    public async Task CalculatePositionSize_WithoutActiveProfile_Returns404()
    {
        var client = await RegisterTraderAsync();

        var resp = await client.PostAsJsonAsync("/api/trades/position-size/calculate", new
        {
            stopLossDistance = 0.00500m,
            riskPerTradeOverride = (decimal?)null,
            currency = "USD"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ===== Metrics (1f) =====

    [Fact]
    public async Task GetMetrics_WithoutTrades_ReturnsZeros()
    {
        var client = await RegisterTraderAsync();

        var resp = await client.GetAsync("/api/trades/metrics?period=30d");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        doc.GetProperty("period").GetString().Should().Be("30d");
        doc.GetProperty("totalTrades").GetInt32().Should().Be(0);
        doc.GetProperty("totalClosedTrades").GetInt32().Should().Be(0);
        doc.GetProperty("winRate").GetDecimal().Should().Be(0m);
        doc.GetProperty("expectancy").GetDecimal().Should().Be(0m);
        doc.GetProperty("equityCurve").GetArrayLength().Should().Be(0);
        doc.GetProperty("symbolStats").GetArrayLength().Should().Be(0);
    }

    // ===== Trade Review (1d) =====

    [Fact]
    public async Task GetTradeReview_WithoutTrade_Returns404()
    {
        var client = await RegisterTraderAsync();
        var fakeTradeId = Guid.NewGuid();

        var resp = await client.GetAsync($"/api/trades/{fakeTradeId}/review");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RequestAttachment_WithoutReview_Returns404()
    {
        var client = await RegisterTraderAsync();
        var fakeTradeId = Guid.NewGuid();

        var resp = await client.PostAsJsonAsync(
            $"/api/trades/{fakeTradeId}/review/attachments",
            new { contentType = "image/png", sizeBytes = 1024L, filename = "screenshot.png" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}