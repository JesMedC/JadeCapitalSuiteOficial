using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JadeCapital.Api.IntegrationTests.Auth;
using JadeCapital.Api.IntegrationTests.Infrastructure;

namespace JadeCapital.Api.IntegrationTests.Wave4;

/// <summary>
/// Slice 4e — MarketData endpoint smoke (Phase 3 — Testcontainers).
///
/// Covers the read path introduced by 4b:
///  - GET /api/quotes/{symbol} → 200 with QuoteDto (known stub symbol)
///  - GET /api/quotes?symbols=A,B → 200 with array (mix of known + unknown)
///
/// Anonymous requests must return 401 (auth gate verified indirectly by
/// the JWT middleware; the anonymous test for scanner lives there).
/// </summary>
public class MarketDataEndpointsTests : IClassFixture<JadeApiFactory>
{
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    private readonly JadeApiFactory _factory;

    public MarketDataEndpointsTests(JadeApiFactory factory) { _factory = factory; }

    private async Task<HttpClient> RegisterTraderAsync()
    {
        var client = _factory.CreateClient();
        var email = $"marketdata{Guid.NewGuid():N}@test.com";
        var resp = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "MarketData Tester", "Passw0rd!Str0ng"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var tokens = await resp.Content.ReadFromJsonAsync<TokenResponse>(_jsonOpts);
        tokens!.AccessToken.Should().NotBeNullOrEmpty();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    [Fact]
    public async Task GetSingleQuote_KnownSymbol_Returns200WithQuote()
    {
        var client = await RegisterTraderAsync();

        var resp = await client.GetAsync("/api/quotes/EURUSD");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var quote = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        quote.GetProperty("symbol").GetString().Should().Be("EURUSD");
        quote.GetProperty("bid").GetDecimal().Should().BeGreaterThan(0);
        quote.GetProperty("ask").GetDecimal().Should().BeGreaterThan(0);
        quote.GetProperty("spread").GetDecimal().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetBulkQuotes_KnownAndUnknown_Returns200WithArray()
    {
        var client = await RegisterTraderAsync();

        var resp = await client.GetAsync("/api/quotes?symbols=EURUSD,BTCUSD,UNKNOWN123");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var arr = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        arr.ValueKind.Should().Be(JsonValueKind.Array);
        // At least the two known stub symbols must be present.
        var symbols = arr.EnumerateArray()
            .Select(q => q.GetProperty("symbol").GetString())
            .Where(s => s != null)
            .Cast<string>()
            .ToHashSet();
        symbols.Should().Contain("EURUSD");
        symbols.Should().Contain("BTCUSD");
    }
}