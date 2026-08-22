using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using JadeCapital.Api.IntegrationTests.Auth;
using JadeCapital.Api.IntegrationTests.Infrastructure;
using JadeCapital.Trading.Infrastructure.Realtime;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JadeCapital.Api.IntegrationTests.Wave4;

/// <summary>
/// Slice 4e — SignalR QuoteHub smoke (Phase 3 — Testcontainers).
///
/// Opens a WebSocket against /hubs/quotes, performs the SignalR JSON
/// handshake (protocol version + record-separator delimiter), subscribes
/// to a known stub symbol, and verifies that at least one
/// <c>{"type":1,"target":"OnQuoteUpdate","arguments":[...]}</c> frame
/// arrives within the broadcast cadence window.
///
/// Failure handling:
///  - If Docker is unavailable (Testcontainers can't start the harness),
///    xUnit reports Failed with the container error — surfaced in the
///    slice summary as an environment limitation, not a slice defect.
///  - If SignalR itself is timing-flaky, the 15s timeout protects CI.
/// </summary>
public class QuoteHubSmokeTests : IClassFixture<JadeApiFactory>
{
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    private readonly JadeApiFactory _factory;

    public QuoteHubSmokeTests(JadeApiFactory factory) { _factory = factory; }

    [Fact]
    public async Task QuoteHub_ConnectSubscribeReceive_AtLeastOneFrameWithin15s()
    {
        // 1) Register a trader and grab a JWT.
        var http = _factory.CreateClient();
        var email = $"signalr{Guid.NewGuid():N}@test.com";
        var reg = await http.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "SignalR Tester", "Passw0rd!Str0ng"));
        reg.StatusCode.Should().Be(HttpStatusCode.Created);
        var tokens = await reg.Content.ReadFromJsonAsync<TokenResponse>(_jsonOpts);
        tokens!.AccessToken.Should().NotBeNullOrEmpty();

        // 2) Connect through TestServer's in-memory WebSocket transport.
        var webSocketClient = _factory.Server.CreateWebSocketClient();
        webSocketClient.SubProtocols.Add("json");
        webSocketClient.ConfigureRequest = request =>
            request.Headers.Authorization = $"Bearer {tokens.AccessToken}";
        var hubUri = new Uri("ws://localhost/hubs/quotes");

        // 3) Open the WebSocket and run the SignalR JSON handshake manually.
        using var ws = await webSocketClient.ConnectAsync(hubUri, CancellationToken.None);

        // Client → server handshake: {"protocol":"json","version":1}\x1e
        await SendSignalRFrameAsync(ws, "{\"protocol\":\"json\",\"version\":1}\x1e", CancellationToken.None);

        // Server → client handshake (also terminated by \x1e).
        var handshake = await ReadSignalRFrameAsync(ws, CancellationToken.None);
        handshake.Should().NotBeNullOrEmpty("server must send the handshake frame");

        // 4) Send Subscribe {"arguments":["EURUSD"],"target":"SubscribeToSymbols","type":1}\x1e
        await SendSignalRFrameAsync(ws,
            "{\"arguments\":[[\"EURUSD\"]],\"invocationId\":\"1\",\"target\":\"SubscribeToSymbols\",\"type\":1}\x1e",
            CancellationToken.None);

        var completion = await ReadSignalRFrameAsync(ws, CancellationToken.None);
        completion.Should().Contain("\"type\":3", "the subscription invocation must complete");

        var broadcaster = _factory.Services.GetServices<IHostedService>()
            .OfType<QuoteBroadcastService>()
            .Single();
        await broadcaster.BroadcastTickAsync(CancellationToken.None);

        // 5) Wait up to 15s for at least one OnQuoteUpdate frame.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        var receivedAnyFrame = false;

        while (DateTimeOffset.UtcNow < deadline && !receivedAnyFrame)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                var frame = await ReadSignalRFrameAsync(ws, cts.Token);
                if (frame.Contains("\"OnQuoteUpdate\"", StringComparison.Ordinal))
                {
                    receivedAnyFrame = true;
                }
            }
            catch (OperationCanceledException)
            {
                // Continue polling until the deadline.
            }
        }

        try
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "smoke done", CancellationToken.None);
        }
        catch
        {
            // Best-effort close.
        }

        receivedAnyFrame.Should().BeTrue(
            "at least one OnQuoteUpdate frame must arrive within 15s of subscribing to a known symbol");
    }

    // ===== SignalR framing helpers (record-separator \x1e delimited) =====

    private const char RecordSeparator = '\x1e';

    private static async Task<string> ReadSignalRFrameAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var ms = new MemoryStream();
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return Encoding.UTF8.GetString(ms.ToArray());
            }

            ms.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            var raw = Encoding.UTF8.GetString(ms.ToArray());
            // Strip the trailing record-separator if present.
            return raw.TrimEnd(RecordSeparator);
        }
        return Encoding.UTF8.GetString(ms.ToArray()).TrimEnd(RecordSeparator);
    }

    private static async Task SendSignalRFrameAsync(WebSocket ws, string frame, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(frame);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }
}
