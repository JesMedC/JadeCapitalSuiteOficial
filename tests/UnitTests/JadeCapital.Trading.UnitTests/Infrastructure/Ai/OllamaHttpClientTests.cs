using JadeCapital.Shared.Kernel.Ai;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Infrastructure.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace JadeCapital.Trading.UnitTests.Infrastructure.Ai;

/// <summary>
/// Tests for <c>OllamaHttpClient</c> (Wave 5, slice 5b.1).
///
/// <para>
/// All HTTP traffic is mocked through a <see cref="StubHttpMessageHandler"/>
/// so the suite is deterministic — no real Ollama call, no flaky network, no
/// port-binding. The handler exposes a single function that maps request →
/// response so each test can pin the exact wire shape Ollama is asked to
/// produce.
/// </para>
///
/// <para>
/// Failure-code mapping is locked here:
/// <list type="bullet">
///   <item>200 OK + non-empty <c>response</c> → <c>Result.Success(PromptResponse)</c></item>
///   <item>5xx after 3 attempts → <c>ai.unavailable</c></item>
///   <item>timeout / TaskCanceled → <c>ai.timeout</c></item>
///   <item>200 OK + empty <c>response</c> → <c>ai.empty_response</c></item>
///   <item>200 OK + malformed JSON → <c>ai.parse_error</c></item>
///   <item>IsHealthyAsync on 200 → true; connection refused / 5xx → false (no throw)</item>
/// </list>
/// </para>
/// </summary>
public class OllamaHttpClientTests
{
    private static AIProviderOptions DefaultOptions(string baseUrl = "http://localhost:11434/")
        => new()
        {
            BaseUrl = baseUrl,
            Model = "llama3.1:8b",
            Timeout = TimeSpan.FromSeconds(30),
        };

    private static OllamaHttpClient BuildClient(
        StubHttpMessageHandler handler,
        AIProviderOptions? options = null,
        HttpClient? http = null)
    {
        options ??= DefaultOptions();
        var httpClient = http ?? new HttpClient(handler) { Timeout = options.Timeout };
        return new OllamaHttpClient(
            httpClient,
            Options.Create(options),
            NullLogger<OllamaHttpClient>.Instance);
    }

    private static string OllamaBody(string response, string model = "llama3.1:8b", int promptEval = 12, int eval = 24)
        => JsonSerializer.Serialize(new
        {
            model,
            response,
            done = true,
            prompt_eval_count = promptEval,
            eval_count = eval,
        });

    // ========================================================================
    // Happy path
    // ========================================================================

    [Fact]
    public async Task GenerateAsync_Returns_PromptResponse_On_200_OK_With_NonEmpty_Content()
    {
        var handler = new StubHttpMessageHandler((req, ct) =>
        {
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.AbsolutePath.Should().Be("/api/generate");
            return HttpResponse(HttpStatusCode.OK, OllamaBody("You closed 4 losing trades today. Review your setup."));
        });
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(new PromptRequest(User: "Summarize today."));

        result.IsSuccess.Should().BeTrue();
        var resp = result.Value;
        resp.Text.Should().Be("You closed 4 losing trades today. Review your setup.");
        resp.Model.Should().Be("llama3.1:8b");
        resp.TokensUsed.Should().Be(36);  // 12 + 24
        resp.Duration.Should().BeGreaterOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task GenerateAsync_Trims_Trailing_Slash_From_BaseUrl_Before_Requesting()
    {
        // AIProviderOptions.BaseUrl defaults to "http://localhost:11434/" — the
        // impl MUST normalize so /api/generate is appended WITHOUT a double
        // slash. The handler inspects RequestUri to catch regressions.
        var handler = new StubHttpMessageHandler((req, ct) =>
        {
            req.RequestUri!.ToString().Should().Be("http://localhost:11434/api/generate");
            return HttpResponse(HttpStatusCode.OK, OllamaBody("ok"));
        });
        var client = BuildClient(handler, DefaultOptions("http://localhost:11434/"));

        var result = await client.GenerateAsync(new PromptRequest(User: "hi"));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateAsync_Prepends_SystemContext_To_Prompt_In_Request_Body()
    {
        // Ollama's /api/generate has no separate system role. The contract is
        // that the impl concatenates System + "\n\n" + User into the
        // `prompt` field of the wire body.
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(async (req, ct) =>
        {
            capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
            return await HttpResponse(HttpStatusCode.OK, OllamaBody("ok"));
        });
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(new PromptRequest(
            User: "Generate advice.",
            System: "{\"closed_trades\":8}"));

        result.IsSuccess.Should().BeTrue();
        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("model").GetString().Should().Be("llama3.1:8b");
        doc.RootElement.GetProperty("prompt").GetString()
            .Should().Be("{\"closed_trades\":8}\n\nGenerate advice.");
        doc.RootElement.GetProperty("stream").GetBoolean().Should().BeFalse();
        var opts = doc.RootElement.GetProperty("options");
        opts.GetProperty("temperature").GetDouble().Should().BeApproximately(0.3, 0.001);
        opts.GetProperty("num_predict").GetInt32().Should().Be(512);
    }

    // ========================================================================
    // Failure modes
    // ========================================================================

    [Fact]
    public async Task GenerateAsync_Returns_ai_unavailable_After_3_Attempts_On_503()
    {
        // Polly retry policy would normally hide this — but our slice does
        // manual retry to keep the test surface small (no extra NuGet).
        // The contract is the SAME: 3 attempts, then failure.
        var attempts = 0;
        var handler = new StubHttpMessageHandler((req, ct) =>
        {
            attempts++;
            return HttpResponse(HttpStatusCode.ServiceUnavailable, "ollama down");
        });
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(new PromptRequest(User: "hi"));

        attempts.Should().Be(3);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("failure.ai.unavailable");
    }

    [Fact]
    public async Task GenerateAsync_Returns_ai_unavailable_On_500()
    {
        var attempts = 0;
        var handler = new StubHttpMessageHandler((req, ct) =>
        {
            attempts++;
            return HttpResponse(HttpStatusCode.InternalServerError, "boom");
        });
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(new PromptRequest(User: "hi"));

        attempts.Should().Be(3);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("failure.ai.unavailable");
    }

    [Fact]
    public async Task GenerateAsync_Returns_ai_timeout_On_TaskCanceledException()
    {
        // When the HttpClient timeout fires, the inner send throws
        // TaskCanceledException. The impl must NOT bubble it — it must
        // translate it into ai.timeout so callers don't need try/catch.
        var handler = new StubHttpMessageHandler((req, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            throw new TaskCanceledException("simulated timeout");
        });
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(new PromptRequest(User: "hi"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("failure.ai.timeout");
    }

    [Fact]
    public async Task GenerateAsync_Returns_ai_empty_response_On_Empty_Text()
    {
        // Ollama returns 200 + `response: ""` when the model produces no
        // tokens (e.g. prompt was filtered). The caller treats empty as
        // failure so the BG service can skip silently.
        var handler = new StubHttpMessageHandler((req, ct) =>
            HttpResponse(HttpStatusCode.OK, OllamaBody("")));

        var client = BuildClient(handler);

        var result = await client.GenerateAsync(new PromptRequest(User: "hi"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("failure.ai.empty_response");
    }

    [Fact]
    public async Task GenerateAsync_Returns_ai_parse_error_On_Malformed_Json()
    {
        var handler = new StubHttpMessageHandler((req, ct) =>
            HttpResponse(HttpStatusCode.OK, "this is not json {"));

        var client = BuildClient(handler);

        var result = await client.GenerateAsync(new PromptRequest(User: "hi"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("failure.ai.parse_error");
    }

    // ========================================================================
    // Health probe
    // ========================================================================

    [Fact]
    public async Task IsHealthyAsync_Returns_True_On_200_OK()
    {
        var handler = new StubHttpMessageHandler((req, ct) =>
        {
            req.Method.Should().Be(HttpMethod.Get);
            req.RequestUri!.AbsolutePath.Should().Be("/api/tags");
            return HttpResponse(HttpStatusCode.OK, "{\"models\":[]}");
        });
        var client = BuildClient(handler);

        var ok = await client.IsHealthyAsync();

        ok.Should().BeTrue();
    }

    [Fact]
    public async Task IsHealthyAsync_Returns_False_On_Connection_Refused_Without_Throwing()
    {
        // HttpClient throws HttpRequestException on connection refused. The
        // contract is to swallow + return false so /api/ai/health always
        // produces a useful 503 instead of a 500.
        var handler = new StubHttpMessageHandler((req, ct) =>
            throw new HttpRequestException("connection refused"));

        var client = BuildClient(handler);

        var ok = await client.IsHealthyAsync();

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task IsHealthyAsync_Returns_False_On_503_Without_Throwing()
    {
        var handler = new StubHttpMessageHandler((req, ct) =>
            HttpResponse(HttpStatusCode.ServiceUnavailable, "down"));

        var client = BuildClient(handler);

        var ok = await client.IsHealthyAsync();

        ok.Should().BeFalse();
    }

    // ========================================================================
    // Configuration wiring
    // ========================================================================

    [Fact]
    public async Task GenerateAsync_Uses_Model_From_AIProviderOptions()
    {
        string? capturedModel = null;
        var handler = new StubHttpMessageHandler(async (req, ct) =>
        {
            var body = await req.Content!.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            capturedModel = doc.RootElement.GetProperty("model").GetString();
            return await HttpResponse(HttpStatusCode.OK, OllamaBody("ok"));
        });
        var opts = DefaultOptions();
        opts.Model = "mistral:7b";
        var client = BuildClient(handler, opts);

        await client.GenerateAsync(new PromptRequest(User: "hi"));

        capturedModel.Should().Be("mistral:7b");
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static Task<HttpResponseMessage> HttpResponse(HttpStatusCode status, string body)
    {
        var msg = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(msg);
    }

    /// <summary>
    /// Minimal HttpMessageHandler double. The handler exposes a single Func
    /// that maps request → response so each test pins the exact wire shape
    /// Ollama is asked to produce. The class is private to this test file —
    /// production code never sees it (DI uses IHttpClientFactory).
    /// </summary>
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return _handler(request, cancellationToken);
        }
    }
}
