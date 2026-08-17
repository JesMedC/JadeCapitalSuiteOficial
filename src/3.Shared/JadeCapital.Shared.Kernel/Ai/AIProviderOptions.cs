namespace JadeCapital.Shared.Kernel.Ai;

/// <summary>
/// Configuration for the local AI provider (Wave 5, slice 5b.1).
///
/// <para>
/// Bound from the <c>Ollama</c> configuration section (or the
/// <c>Ollama__BaseUrl</c> / <c>Ollama__Model</c> / <c>Ollama__Timeout</c>
/// env vars, matching the project's env-var convention). Defaults target a
/// stock local Ollama install so a developer with <c>ollama serve</c> running
/// on the standard port gets a working provider without any config.
/// </para>
///
/// <para>
/// In Wave 6 the same shape backs the OpenAI / Claude clients; only the
/// <see cref="BaseUrl"/> changes. The class lives in <c>Shared.Kernel</c>
/// so future cross-module consumers (Billing invoice-narratives, Identity
/// password-recovery summaries) can reuse the same options binding.
/// </para>
/// </summary>
public sealed class AIProviderOptions
{
    /// <summary>Ollama REST endpoint. Default matches the official Docker image port.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Model tag passed in the request body (e.g. <c>llama3.1:8b</c>, <c>mistral:7b</c>).</summary>
    public string Model { get; set; } = "llama3.1:8b";

    /// <summary>Per-request HTTP timeout. Default 30s — generous for a single 512-token completion.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
