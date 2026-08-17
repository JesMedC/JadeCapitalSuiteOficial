namespace JadeCapital.Shared.Kernel.Ai;

/// <summary>
/// Result of a successful <see cref="IAIProvider.GenerateAsync"/> call.
///
/// <para>
/// <b>Text</b> is the raw model output (no markdown stripping — that's the
/// caller's responsibility, e.g. <c>AIRiskAdvisorResponseParser</c> in slice
/// 5c.1). <b>Model</b> is the actual model name used (may differ from the
/// requested model if the provider picks a fallback).
/// </para>
///
/// <para>
/// Explicit-ctor record so <see cref="Duration"/> and <see cref="TokensUsed"/>
/// invariants can be enforced at construction. <c>System.Text.Json</c>
/// deserialization works through the public parameterless ctor + <c>init</c>
/// property setters.
/// </para>
/// </summary>
public sealed record PromptResponse
{
    /// <summary>The raw generated text. Empty string is treated as a failure by callers.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>The model identifier used (echoed back from the provider).</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>Total tokens consumed (prompt + completion). 0 if the provider doesn't report.</summary>
    public int TokensUsed { get; init; }

    /// <summary>Wall-clock latency. Must be non-negative.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Parameterless ctor — required for <c>System.Text.Json</c> deserialization.</summary>
    public PromptResponse() { }

    /// <summary>Validating ctor — the canonical entry point for hand-written callers.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration"/> or <paramref name="tokensUsed"/> is negative.</exception>
    public PromptResponse(string Text, string Model, int TokensUsed, TimeSpan Duration)
    {
        if (Duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(Duration), Duration,
                $"Duration must be >= TimeSpan.Zero (got {Duration}).");

        if (TokensUsed < 0)
            throw new ArgumentOutOfRangeException(nameof(TokensUsed), TokensUsed,
                $"TokensUsed must be >= 0 (got {TokensUsed}).");

        this.Text = Text;
        this.Model = Model;
        this.TokensUsed = TokensUsed;
        this.Duration = Duration;
    }
}
