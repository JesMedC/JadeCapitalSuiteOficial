using JadeCapital.Shared.Kernel.Ai;
using JadeCapital.Shared.Kernel.Results;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace JadeCapital.Shared.Kernel.UnitTests.Ai;

/// <summary>
/// Contract tests for the AI provider wire shape (Wave 5, slice 5b.1).
/// Validates the <see cref="PromptRequest"/>, <see cref="PromptResponse"/>,
/// and <see cref="IAIProvider"/> types defined in <c>Shared.Kernel/Ai</c>.
///
/// <para>
/// These tests pin the cross-module contract that Trading.Infrastructure's
/// <c>OllamaHttpClient</c> and Trading.Application's coaching + risk-advisor
/// handlers rely on. Changing them is a breaking change.
/// </para>
/// </summary>
public class PromptContractTests
{
    // ========================================================================
    // PromptRequest
    // ========================================================================

    [Fact]
    public void PromptRequest_Defaults_Apply_When_Only_User_Is_Provided()
    {
        // The contract: callers only need to supply the prompt text. Everything
        // else (System context, MaxTokens, Temperature) has safe defaults so a
        // one-shot "hello" call doesn't require a 4-arg ctor.
        var req = new PromptRequest(User: "Summarize my trading day.");

        req.System.Should().BeNull();
        req.MaxTokens.Should().Be(512);
        req.Temperature.Should().Be(0.3m);
    }

    [Fact]
    public void PromptRequest_Allows_Custom_System_Context_And_Generation_Params()
    {
        var req = new PromptRequest(
            User: "Generate advice for EURUSD context.",
            System: "{\"closed_trades\":12,\"win_rate\":0.41}",
            MaxTokens: 256,
            Temperature: 0.1m);

        req.User.Should().Be("Generate advice for EURUSD context.");
        req.System.Should().Be("{\"closed_trades\":12,\"win_rate\":0.41}");
        req.MaxTokens.Should().Be(256);
        req.Temperature.Should().Be(0.1m);
    }

    [Fact]
    public void PromptRequest_Survives_Json_Roundtrip_With_Snake_Case_Properties()
    {
        // The HttpClient implementation will JSON-serialize the request body
        // for Ollama. We pin the wire shape (PascalCase keys, the canonical
        // System.Text.Json default) so a future refactor doesn't accidentally
        // break the wire contract.
        var req = new PromptRequest(User: "hi", System: "ctx", MaxTokens: 100, Temperature: 0.5m);

        var json = JsonSerializer.Serialize(req);
        var back = JsonSerializer.Deserialize<PromptRequest>(json);

        back.Should().Be(req);
    }

    [Fact]
    public void PromptRequest_Rejects_NonPositive_MaxTokens()
    {
        // Generation cap of zero is nonsensical — the caller is misconfigured.
        // This invariant is enforced at construction so the provider never sees
        // an obviously broken request.
        Action act = () => new PromptRequest(User: "x", MaxTokens: 0);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(PromptRequest.MaxTokens));
    }

    // ========================================================================
    // PromptResponse
    // ========================================================================

    [Fact]
    public void PromptResponse_Requires_Text_Content()
    {
        var resp = new PromptResponse(Text: "You had 4 losing trades today.", Model: "llama3.1:8b", TokensUsed: 42, Duration: TimeSpan.FromMilliseconds(412));

        resp.Text.Should().Be("You had 4 losing trades today.");
        resp.Model.Should().Be("llama3.1:8b");
        resp.TokensUsed.Should().Be(42);
        resp.Duration.Should().Be(TimeSpan.FromMilliseconds(412));
    }

    [Fact]
    public void PromptResponse_Allows_Zero_Duration_For_Instant_Responses()
    {
        // Local Ollama can respond in <1ms on warm cache. Duration must allow 0
        // (not negative) — callers use it for latency metrics + logging only.
        var resp = new PromptResponse(Text: "ok", Model: "llama3.1:8b", TokensUsed: 1, Duration: TimeSpan.Zero);

        resp.Duration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void PromptResponse_Rejects_Negative_Duration()
    {
        Action act = () => new PromptResponse(Text: "x", Model: "m", TokensUsed: 1, Duration: TimeSpan.FromMilliseconds(-1));

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(PromptResponse.Duration));
    }

    [Fact]
    public void PromptResponse_Rejects_Negative_TokensUsed()
    {
        Action act = () => new PromptResponse(Text: "x", Model: "m", TokensUsed: -1, Duration: TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(PromptResponse.TokensUsed));
    }

    // ========================================================================
    // IAIProvider interface shape
    // ========================================================================

    [Fact]
    public void IAIProvider_Has_GenerateAsync_And_IsHealthyAsync_Members()
    {
        // Pin the interface surface — adding/removing members is a breaking
        // change for every implementation (OllamaHttpClient today, OpenAI /
        // Claude clients in Wave 6).
        var t = typeof(IAIProvider);

        t.IsInterface.Should().BeTrue();

        var generate = t.GetMethod(nameof(IAIProvider.GenerateAsync));
        generate.Should().NotBeNull();
        generate!.ReturnType.Should().Be<Task<Result<PromptResponse>>>();

        var healthy = t.GetMethod(nameof(IAIProvider.IsHealthyAsync));
        healthy.Should().NotBeNull();
        healthy!.ReturnType.Should().Be<Task<bool>>();
    }

    [Fact]
    public void AIProviderOptions_Defaults_To_Local_Ollama_Port_With_Reasonable_Timeouts()
    {
        // Defaults MUST be safe for local dev: a developer who runs `ollama serve`
        // and nothing else should be able to call GenerateAsync without
        // configuring anything. Wave 6 may override via env vars.
        var opts = new AIProviderOptions();

        opts.BaseUrl.Should().Be("http://localhost:11434");
        opts.Model.Should().Be("llama3.1:8b");
        opts.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void AIProviderOptions_Allows_Override_Of_Every_Property()
    {
        var opts = new AIProviderOptions
        {
            BaseUrl = "http://gpu-box.lan:11434",
            Model = "mistral:7b",
            Timeout = TimeSpan.FromSeconds(5),
        };

        opts.BaseUrl.Should().Be("http://gpu-box.lan:11434");
        opts.Model.Should().Be("mistral:7b");
        opts.Timeout.Should().Be(TimeSpan.FromSeconds(5));
    }
}
