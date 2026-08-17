namespace JadeCapital.Shared.Kernel.Ai;

/// <summary>
/// A single completion request to an <see cref="IAIProvider"/>.
///
/// <para>
/// <b>User</b> is the actual user prompt. <b>System</b> is an optional
/// prepended context (instructions, role definition, structured data) that
/// the Ollama impl concatenates as <c>System + "\n\n" + User</c> because
/// Ollama's <c>/api/generate</c> endpoint doesn't expose a separate system
/// role. Cloud providers (Wave 6) will map these onto their native roles.
/// </para>
///
/// <para>
/// <b>MaxTokens</b> and <b>Temperature</b> are mirrored onto Ollama's
/// <c>options.num_predict</c> and <c>options.temperature</c> fields.
/// </para>
///
/// <para>
/// Implemented as an explicit-ctor record (rather than a positional record)
/// so the <see cref="MaxTokens"/> invariant can be enforced at construction.
/// System.Text.Json deserialization works through the public parameterless
/// ctor + <c>init</c> property setters.
/// </para>
/// </summary>
public sealed record PromptRequest
{
    /// <summary>The user-facing prompt (required, non-empty).</summary>
    public string User { get; init; } = string.Empty;

    /// <summary>Optional system context prepended to <see cref="User"/>.</summary>
    public string? System { get; init; }

    /// <summary>Generation cap. Must be &gt; 0. Default 512 — enough for a coaching paragraph.</summary>
    public int MaxTokens { get; init; } = 512;

    /// <summary>Sampling temperature. Default 0.3 — favors deterministic output.</summary>
    public decimal Temperature { get; init; } = 0.3m;

    /// <summary>Parameterless ctor — required for <c>System.Text.Json</c> deserialization.</summary>
    public PromptRequest() { }

    /// <summary>Validating ctor — the canonical entry point for hand-written callers.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxTokens"/> is &lt;= 0.</exception>
    public PromptRequest(string User, string? System = null, int MaxTokens = 512, decimal Temperature = 0.3m)
    {
        if (MaxTokens <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxTokens), MaxTokens,
                $"MaxTokens must be > 0 (got {MaxTokens}).");

        this.User = User;
        this.System = System;
        this.MaxTokens = MaxTokens;
        this.Temperature = Temperature;
    }
}
