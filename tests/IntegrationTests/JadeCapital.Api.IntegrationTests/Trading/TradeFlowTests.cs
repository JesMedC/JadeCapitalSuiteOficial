using JadeCapital.Api.IntegrationTests.Auth;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JadeCapital.Api.IntegrationTests.Trading;

/// <summary>
/// Slice 1e — Integration coverage for the 8 endpoints under /api/trades/*.
/// Closes the Sprint 1F follow-up (Trading had no integration tests).
/// Each test creates its own user via /api/auth/register + a default
/// account + instrument, so tests do NOT depend on shared state.
/// </summary>
public class TradeFlowTests : IClassFixture<JadeApiFactory>
{
    private readonly JadeApiFactory _factory;

    public TradeFlowTests(JadeApiFactory factory) { _factory = factory; }

    // ===== Helpers =====

    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    private async Task<HttpClient> RegisterTraderAsync()
    {
        var client = _factory.CreateClient();
        var email = $"trade{Guid.NewGuid():N}@test.com";
        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Trader", "Passw0rd!Str0ng"));
        reg.StatusCode.Should().Be(HttpStatusCode.Created);
        var tokens = await reg.Content.ReadFromJsonAsync<TokenResponse>(_jsonOpts);
        tokens!.AccessToken.Should().NotBeNullOrEmpty();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private static async Task<Guid> OpenAccountAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/api/accounts", new
        {
            name = "Test Account",
            broker = "TestBroker",
            marketType = 1,            // Forex
            currency = "USD",
            initialBalance = 10000m,
            leverage = 100m
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        return doc.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> OpenInstrumentAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/api/instruments", new
        {
            symbol = "EURUSD",
            assetClasses = 1,          // Forex
            contractSize = 100000m,
            decimalPlaces = 5,
            pipValue = 10m,
            payoutPercent = 0m
        });
        if (resp.StatusCode == HttpStatusCode.Conflict)
        {
            // Global catalog — fetch by symbol.
            var list = await client.GetAsync("/api/instruments");
            list.StatusCode.Should().Be(HttpStatusCode.OK);
            var arr = await list.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
            foreach (var item in arr.EnumerateArray())
            {
                if (item.GetProperty("symbol").GetString() == "EURUSD")
                    return item.GetProperty("id").GetGuid();
            }
            throw new InvalidOperationException("EURUSD instrument not found after 409");
        }
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        return doc.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> OpenTradeAsync(HttpClient client, Guid accountId, Guid instrumentId, string symbol = "EURUSD")
    {
        var resp = await client.PostAsJsonAsync("/api/trades", new
        {
            accountId,
            instrumentId,
            symbol,
            assetClass = 1,            // Forex
            direction = 1,             // Long
            volume = 1m,
            volumeCurrency = "USD",
            entryPrice = 1.10000m,
            entryPriceCurrency = "USD",
            strategy = "test-strategy",
            notes = "integration test trade"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        return doc.GetProperty("id").GetGuid();
    }

    // ===== Tests =====

    [Fact]
    public async Task OpenTrade_WithValidPayload_Returns201_WithTradeId()
    {
        var client = await RegisterTraderAsync();
        var accountId = await OpenAccountAsync(client);
        var instrumentId = await OpenInstrumentAsync(client);

        var resp = await client.PostAsJsonAsync("/api/trades", new
        {
            accountId,
            instrumentId,
            symbol = "EURUSD",
            assetClass = 1,
            direction = 1,
            volume = 1m,
            volumeCurrency = "USD",
            entryPrice = 1.10000m,
            entryPriceCurrency = "USD",
            strategy = "trend-follow",
            notes = "open test"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        doc.GetProperty("id").GetGuid().Should().NotBe(Guid.Empty);
        doc.GetProperty("status").GetInt16().Should().Be((short)1); // Open
        doc.GetProperty("symbol").GetString().Should().Be("EURUSD");
    }

    [Fact]
    public async Task GetTrades_WithPagination_ReturnsPagedTrades()
    {
        var client = await RegisterTraderAsync();
        var accountId = await OpenAccountAsync(client);
        var instrumentId = await OpenInstrumentAsync(client);
        await OpenTradeAsync(client, accountId, instrumentId);
        await OpenTradeAsync(client, accountId, instrumentId, "GBPUSD");

        var resp = await client.GetAsync("/api/trades?page=1&pageSize=20");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        doc.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        doc.GetProperty("page").GetInt32().Should().Be(1);
        doc.GetProperty("pageSize").GetInt32().Should().Be(20);
        var items = doc.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetDashboard_ReturnsSummary_WithZeroStateForNewUser()
    {
        var client = await RegisterTraderAsync();

        var resp = await client.GetAsync("/api/trades/dashboard");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        doc.GetProperty("totalCount").GetInt32().Should().Be(0);
        doc.GetProperty("openCount").GetInt32().Should().Be(0);
        doc.GetProperty("closedCount").GetInt32().Should().Be(0);
        doc.GetProperty("winRate").GetDecimal().Should().Be(0m);
        doc.GetProperty("totalPnL").GetDecimal().Should().Be(0m);
    }

    [Fact]
    public async Task GetCalendar_ReturnsCalendarForYearMonth()
    {
        var client = await RegisterTraderAsync();
        var now = DateTimeOffset.UtcNow;

        var resp = await client.GetAsync($"/api/trades/calendar?year={now.Year}&month={now.Month}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        doc.GetProperty("year").GetInt32().Should().Be(now.Year);
        doc.GetProperty("month").GetInt32().Should().Be(now.Month);
        doc.GetProperty("days").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetTradeById_ReturnsTrade_WithSameId()
    {
        var client = await RegisterTraderAsync();
        var accountId = await OpenAccountAsync(client);
        var instrumentId = await OpenInstrumentAsync(client);
        var tradeId = await OpenTradeAsync(client, accountId, instrumentId);

        var resp = await client.GetAsync($"/api/trades/{tradeId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        doc.GetProperty("id").GetGuid().Should().Be(tradeId);
        doc.GetProperty("userId").GetGuid().Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task CloseTrade_WithExitPrice_ReturnsTradeWithPnlComputed()
    {
        var client = await RegisterTraderAsync();
        var accountId = await OpenAccountAsync(client);
        var instrumentId = await OpenInstrumentAsync(client);
        var tradeId = await OpenTradeAsync(client, accountId, instrumentId);

        var resp = await client.PutAsJsonAsync($"/api/trades/{tradeId}/close", new
        {
            exitPrice = 1.10500m,
            exitPriceCurrency = "USD"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        // Status 2 = Closed per TradeStatus enum (Open=1, Closed=2, ...).
        doc.GetProperty("status").GetInt16().Should().Be((short)2);
        doc.GetProperty("exitPrice").GetDecimal().Should().Be(1.10500m);
        doc.GetProperty("closedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task UpdateNotes_WithValidNotes_Returns200()
    {
        var client = await RegisterTraderAsync();
        var accountId = await OpenAccountAsync(client);
        var instrumentId = await OpenInstrumentAsync(client);
        var tradeId = await OpenTradeAsync(client, accountId, instrumentId);

        var resp = await client.PatchAsync($"/api/trades/{tradeId}",
            JsonContent.Create(new { strategy = "updated-strategy", notes = "updated notes" }));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        doc.GetProperty("strategy").GetString().Should().Be("updated-strategy");
        doc.GetProperty("notes").GetString().Should().Be("updated notes");
    }

    [Fact]
    public async Task DeleteTrade_OfOpenTrade_Returns204()
    {
        var client = await RegisterTraderAsync();
        var accountId = await OpenAccountAsync(client);
        var instrumentId = await OpenInstrumentAsync(client);
        var tradeId = await OpenTradeAsync(client, accountId, instrumentId);

        var resp = await client.DeleteAsync($"/api/trades/{tradeId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Confirm gone
        var follow = await client.GetAsync($"/api/trades/{tradeId}");
        follow.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}