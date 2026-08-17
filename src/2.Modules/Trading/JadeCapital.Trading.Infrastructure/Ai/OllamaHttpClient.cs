using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JadeCapital.Shared.Kernel.Ai;
using JadeCapital.Shared.Kernel.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JadeCapital.Trading.Infrastructure.Ai;

/// <summary>
/// Ollama-backed implementation of <see cref="IAIProvider"/> (Wave 5, slice 5b.1).
///
/// <para>
/// Talks to a local Ollama REST endpoint (default <c>http://localhost:11434</c>).
/// The provider is the <em>only</em> HTTP touchpoint for AI traffic — every
/// other component (coaching prompt BG, risk advisor) talks to
/// <see cref="IAIProvider"/>, never to Ollama directly. Wave 6 swaps this
/// registration for OpenAI / Claude implementations without touching
/// consumers.
/// </para>
///
/// <para>
/// <b>Failure semantics</b>: this class never throws on transient provider
/// failures. <see cref="GenerateAsync"/> retries up to 3 times on 5xx, then
/// returns <c>Result.Failure</c> with one of:
/// <list type="bullet">
///   <item><c>ai.unavailable</c> — Ollama unreachable or returned 5xx after 3 attempts</item>
///   <item><c>ai.timeout</c> — request cancelled (HttpClient timeout / linked CTS)</item>
///   <item><c>ai.empty_response</c> — Ollama returned 200 but <c>response</c> was empty/whitespace</item>
///   <item><c>ai.parse_error</c> — response body was not valid JSON for the expected shape</item>
///   <item><c>ai.internal_error</c> — anything else (logged + wrapped)</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Wire shape</b>: <c>POST /api/generate</c> with body
/// <c>{ model, prompt, stream:false, options:{ temperature, num_predict } }</c>.
/// Ollama's endpoint has no separate system role, so <see cref="PromptRequest.System"/>
/// is prepended to <see cref="PromptRequest.User"/> with a blank-line separator.
/// Cloud providers in Wave 6 will have their own mapping.
/// </para>
/// </summary>
public sealed class OllamaHttpClient : IAIProvider
{
    /// <summary>Number of attempts on transient 5xx (the spec scenario "after Polly retries 3 attempts").</summary>
    private const int MaxAttempts = 3;

    private readonly HttpClient _http;
    private readonly AIProviderOptions _options;
    private readonly ILogger<OllamaHttpClient> _logger;

    public OllamaHttpClient(
        HttpClient http,
        IOptions<AIProviderOptions> options,
        ILogger<OllamaHttpClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        // Defense-in-depth: normalize the BaseUrl once at construction so the
        // HttpClient doesn't accidentally double-slash the path. The factory
        // (DI) already sets BaseAddress + Timeout, but this is a belt-and-
        // braces for tests that construct the client with a raw HttpClient.
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = _options.Timeout;
    }

    public async Task<Result<PromptResponse>> GenerateAsync(PromptRequest request, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await GenerateInternalAsync(request, stopwatch, ct);
        }
        catch (Exception ex)
        {
            // Defense-in-depth: the IAIProvider contract guarantees no throw.
            // Anything that escapes the inner loop (e.g. InvalidOperationException
            // from a misconfigured HttpClient) becomes ai.internal_error so the
            // BG service + advisor can keep running.
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected exception in Ollama GenerateAsync.");
            return Result<PromptResponse>.Failure(Error.Failure("ai.internal_error",
                $"Unexpected exception: {ex.Message}"));
        }
    }

    private async Task<Result<PromptResponse>> GenerateInternalAsync(PromptRequest request, Stopwatch stopwatch, CancellationToken ct)
    {
        // Compose the wire body. Ollama has no separate system role, so the
        // System context is prepended to the User prompt. This matches the
        // 5b.1 spec scenario "GenerateAsync request JSON shape".
        var composedPrompt = string.IsNullOrWhiteSpace(request.System)
            ? request.User
            : request.System + "\n\n" + request.User;

        var body = new OllamaGenerateRequest(
            Model: _options.Model,
            Prompt: composedPrompt,
            Stream: false,
            Options: new OllamaGenerateOptions(
                Temperature: (double)request.Temperature,
                NumPredict: request.MaxTokens));

        HttpResponseMessage? response = null;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                response = await _http.PostAsJsonAsync("/api/generate", body, ct);
            }
            catch (TaskCanceledException ex) when (ct.IsCancellationRequested || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                stopwatch.Stop();
                _logger.LogWarning(ex, "Ollama request cancelled (timeout) on attempt {Attempt}", attempt);
                return Result<PromptResponse>.Failure(Error.Failure("ai.timeout",
                    $"Ollama request timed out after {stopwatch.ElapsedMilliseconds}ms."));
            }
            catch (HttpRequestException ex)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Ollama unreachable on attempt {Attempt}/{Max}", attempt, MaxAttempts);
                response?.Dispose();
                response = null;
                // Don't burn the budget — connection refused is unlikely to
                // succeed on retry; we still retry per the spec.
                if (attempt < MaxAttempts) continue;
                stopwatch.Stop();
                return Result<PromptResponse>.Failure(Error.Failure("ai.unavailable",
                    $"Ollama unreachable after {MaxAttempts} attempts: {ex.Message}"));
            }

            if (response.IsSuccessStatusCode)
                break;

            var statusCode = (int)response.StatusCode;
            var bodyText = await SafeReadAsync(response, ct);
            response.Dispose();
            response = null;

            // Retry transient 5xx; surface anything else immediately.
            if (statusCode >= 500 && statusCode < 600 && attempt < MaxAttempts)
            {
                _logger.LogWarning("Ollama returned {Status} on attempt {Attempt}/{Max}; retrying. Body: {Body}",
                    statusCode, attempt, MaxAttempts, bodyText);
                continue;
            }

            stopwatch.Stop();
            _logger.LogWarning("Ollama returned {Status} after {Attempt} attempts. Body: {Body}",
                statusCode, attempt, bodyText);
            return Result<PromptResponse>.Failure(Error.Failure("ai.unavailable",
                $"Ollama returned HTTP {statusCode}: {Truncate(bodyText, 200)}"));
        }

        // All attempts exhausted without success — defensive guard.
        if (response is null)
        {
            stopwatch.Stop();
            return Result<PromptResponse>.Failure(Error.Failure("ai.unavailable",
                lastError is null
                    ? $"Ollama unreachable after {MaxAttempts} attempts."
                    : $"Ollama unreachable after {MaxAttempts} attempts: {lastError.Message}"));
        }

        try
        {
            var parsed = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: ct);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Response))
            {
                stopwatch.Stop();
                return Result<PromptResponse>.Failure(Error.Failure("ai.empty_response",
                    "Ollama returned 200 with empty `response` field."));
            }

            stopwatch.Stop();
            var tokens = (parsed.PromptEvalCount ?? 0) + (parsed.EvalCount ?? 0);
            return Result<PromptResponse>.Success(new PromptResponse(
                Text: parsed.Response,
                Model: parsed.Model ?? _options.Model,
                TokensUsed: tokens,
                Duration: stopwatch.Elapsed));
        }
        catch (System.Text.Json.JsonException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Failed to parse Ollama response body.");
            return Result<PromptResponse>.Failure(Error.Failure("ai.parse_error",
                $"Ollama returned malformed JSON: {ex.Message}"));
        }
        finally
        {
            response.Dispose();
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync("/api/tags", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            // Contract: must NOT throw. Connection refused / timeout / DNS
            // errors all collapse to false so /api/ai/health can return a
            // useful 503 instead of a 500.
            _logger.LogDebug(ex, "Ollama health probe failed (returning false).");
            return false;
        }
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return "<body read failed>";
        }
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : string.Concat(s.AsSpan(0, max), "…");

    // ===== Wire DTOs (private — not exposed outside this file) =====

    private sealed record OllamaGenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("options")] OllamaGenerateOptions Options);

    private sealed record OllamaGenerateOptions(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("num_predict")] int NumPredict);

    private sealed record OllamaGenerateResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("response")] string Response,
        [property: JsonPropertyName("done")] bool Done,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount,
        [property: JsonPropertyName("eval_count")] int? EvalCount);
}
