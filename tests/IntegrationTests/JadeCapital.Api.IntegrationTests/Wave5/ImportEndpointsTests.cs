using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using JadeCapital.Api.IntegrationTests.Auth;
using JadeCapital.Api.IntegrationTests.Infrastructure;
using JadeCapital.Trading.Contracts.AiRiskAdvisor;
using JadeCapital.Trading.Domain.Enums;

namespace JadeCapital.Api.IntegrationTests.Wave5;

/// <summary>
/// Slice 5c.2 — Phase 2: end-to-end integration tests for the Wave 5 import
/// surface (slice 5a.1 CSV + slice 5a.2 MT4 auto-detection).
///
/// These tests exercise the full HTTP path against the real DI graph
/// (MediatR + EF + Testcontainers Postgres) — the same fixture used by
/// Wave 4. Each test creates its own user + account so isolation is
/// guaranteed even though the DB is shared.
/// </summary>
public class ImportEndpointsTests : IClassFixture<JadeApiFactory>
{
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    private readonly JadeApiFactory _factory;

    public ImportEndpointsTests(JadeApiFactory factory) { _factory = factory; }

    private async Task<HttpClient> RegisterTraderAndCreateAccountAsync()
    {
        var client = _factory.CreateClient();
        var email = $"wave5import{Guid.NewGuid():N}@test.com";
        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Wave 5 Importer", "Passw0rd!Str0ng"));
        reg.StatusCode.Should().Be(HttpStatusCode.Created);
        var tokens = await reg.Content.ReadFromJsonAsync<TokenResponse>(_jsonOpts);
        tokens!.AccessToken.Should().NotBeNullOrEmpty();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        // Open an account to attach the import to.
        var acct = await client.PostAsJsonAsync("/api/accounts", new
        {
            name = "Wave5 Test Account",
            broker = "IC Markets",
            marketType = (int)MarketType.Forex,
            currency = "USD",
            initialBalance = 10_000m,
            leverage = (decimal?)100m,
        });
        acct.StatusCode.Should().Be(HttpStatusCode.Created);

        return client;
    }

    [Fact]
    public async Task PostImportsCsv_AcceptsValidUpload_Returns202WithJobId()
    {
        var client = await RegisterTraderAndCreateAccountAsync();
        var accountId = await GetFirstAccountIdAsync(client);

        // Sample 9-row CSV. One row (row 5) is a duplicate that the dedupe
        // path in the parser should drop silently.
        var csv = "Ticket,Open Time,Symbol,Type,Volume,Open Price,SL,TP,Commission\n" +
                  "T1,2026-01-02T09:00:00Z,EURUSD,buy,0.10,1.0800,1.0700,1.0900,0.0\n" +
                  "T2,2026-01-02T10:00:00Z,EURUSD,buy,0.10,1.0810,1.0710,1.0910,0.0\n" +
                  "T3,2026-01-02T11:00:00Z,GBPUSD,sell,0.20,1.2700,1.2800,1.2600,0.0\n" +
                  "T4,2026-01-02T12:00:00Z,USDJPY,buy,0.05,150.10,149.50,151.00,0.0\n" +
                  "T1,2026-01-02T09:00:00Z,EURUSD,buy,0.10,1.0800,1.0700,1.0900,0.0\n" +
                  "T5,2026-01-02T13:00:00Z,AUDUSD,buy,0.15,0.6500,0.6400,0.6600,0.0\n" +
                  "T6,2026-01-02T14:00:00Z,USDCAD,sell,0.10,1.3500,1.3600,1.3400,0.0\n" +
                  "T7,2026-01-02T15:00:00Z,NZDJPY,buy,0.20,90.50,89.50,91.50,0.0\n" +
                  "T8,2026-01-02T16:00:00Z,USDCHF,buy,0.10,0.8800,0.8700,0.8900,0.0\n";
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(fileContent, "file", "wave5-smoke.csv");
        form.Add(new StringContent(accountId.ToString()), "accountId");

        var resp = await client.PostAsync("/api/imports/csv", form);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        var jobId = body.GetProperty("importJobId").GetGuid();
        jobId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task PostImportsMt4_AcceptsMt4Export_Returns202WithJobId()
    {
        var client = await RegisterTraderAndCreateAccountAsync();
        var accountId = await GetFirstAccountIdAsync(client);

        // Minimal canonical MT4 CSV export: SL + TP distinguish it from
        // generic CSV, while the complete header clears the parser threshold.
        var mt4 = "Ticket,Open Time,Type,Volume,Symbol,Open Price,SL,TP,Close Time,Close Price,Commission,Swap,Profit\n" +
                  "1001,2026.01.02 09:00:00,buy,0.10,EURUSD,1.0800,1.0700,1.0900,,,,0.0,0.0\n" +
                  "1002,2026.01.02 10:00:00,sell,0.20,GBPUSD,1.2700,1.2800,1.2600,,,,0.0,0.0\n" +
                  "1003,2026.01.02 11:00:00,buy,0.05,USDJPY,150.10,149.50,151.00,,,,0.0,0.0\n";
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(mt4));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(fileContent, "file", "wave5-smoke.csv");
        form.Add(new StringContent(accountId.ToString()), "accountId");

        var resp = await client.PostAsync("/api/imports/csv", form);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        body.GetProperty("importJobId").GetGuid().Should().NotBe(Guid.Empty);
    }

    private static async Task<Guid> GetFirstAccountIdAsync(HttpClient client)
    {
        var list = await client.GetAsync("/api/accounts");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var arr = await list.Content.ReadFromJsonAsync<JsonElement>(_jsonOpts);
        return arr.EnumerateArray().First().GetProperty("id").GetGuid();
    }
}
