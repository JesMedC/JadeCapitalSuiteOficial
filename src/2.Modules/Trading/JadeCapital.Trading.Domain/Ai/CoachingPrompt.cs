using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using SharedAi = JadeCapital.Shared.Kernel.Ai;

namespace JadeCapital.Trading.Domain.Ai;

// ============================================================================
//  CoachingPrompt — slice 5b.2 (Wave 5, AI Coaching).
//
//  Aggregate root for an AI-generated coaching prompt. Persisted to
//  <c>trading.coaching_prompts_ai</c> by <c>CoachingPromptRepository</c>;
//  populated by <c>GenerateCoachingPromptHandler</c> (manual + BG service).
//
//  <para>
//  Invariants:
//  <list type="bullet">
//    <item>UserId != Guid.Empty.</item>
//    <item>PromptText non-empty, <= 4000 chars.</item>
//    <item>ContextJson non-empty (caller composes from
//          <c>IUserTradingContextProvider</c> + Wave 3b violations).</item>
//    <item>ProviderResponseText (extracted from <c>PromptResponse.Text</c>) non-empty.</item>
//    <item>Model 1..64 chars.</item>
//    <item>LatencyMs >= 0.</item>
//    <item>Severity byte 0..2.</item>
//    <item>Kind is fixed to <see cref="CoachingPromptKind.Ai"/> — the Rule
//          kind lives on the Wave 3b table and never enters this aggregate.</item>
//    <item>CreatedAt set from the supplied clock — never from UtcNow directly.</item>
//  </list>
//  </para>
//
//  <para>
//  Immutability: the aggregate has no public setters. EF rehydration uses
//  the <see cref="Rehydrate"/> factory which is reserved for the repository
//  and skips the validation guards (the DB already enforced the shape via
//  CHECK constraints).
//  </para>
// ============================================================================

public sealed class CoachingPrompt : AggregateRoot<Guid>
{
    /// <summary>Max length of the rendered prompt text — matches VARCHAR(4000) in the migration.</summary>
    public const int MaxPromptTextLength = 4000;

    /// <summary>Max length of the model identifier — matches VARCHAR(64).</summary>
    public const int MaxModelLength = 64;

    public Guid UserId { get; private set; }
    public string PromptText { get; private set; } = string.Empty;
    public string ContextJson { get; private set; } = string.Empty;
    public string ProviderResponseText { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public int LatencyMs { get; private set; }
    public CoachingPromptSeverity Severity { get; private set; }
    public CoachingPromptKind Kind { get; private set; }
    public new DateTimeOffset CreatedAt { get; private set; }

    // EF Core.
    private CoachingPrompt() { }

    private CoachingPrompt(
        Guid id,
        Guid userId,
        string promptText,
        string contextJson,
        string providerResponseText,
        string model,
        int latencyMs,
        CoachingPromptSeverity severity,
        DateTimeOffset createdAt) : base(id)
    {
        UserId = userId;
        PromptText = promptText;
        ContextJson = contextJson;
        ProviderResponseText = providerResponseText;
        Model = model;
        LatencyMs = latencyMs;
        Severity = severity;
        Kind = CoachingPromptKind.Ai;  // hard-wired — Rule kind never enters this aggregate
        CreatedAt = createdAt;
        // Inherit CreatedAt for the base Entity<TId>.CreatedAt too — keeps
        // EF's CreatedAt column in lockstep with our explicit one.
        SetCreatedAt(createdAt);
    }

    /// <summary>
    /// Factory: validates invariants and builds the aggregate. All errors
    /// are <see cref="Error.Validation"/> with the <c>coaching_prompt.</c>
    /// prefix so the API endpoints can route to 422 (semantic validation).
    /// </summary>
    public static Result<CoachingPrompt> Create(
        Guid userId,
        string promptText,
        string contextJson,
        SharedAi.PromptResponse response,
        CoachingPromptSeverity severity,
        IClock clock)
    {
        if (userId == Guid.Empty)
            return Result.Failure<CoachingPrompt>(CoachingPromptErrors.Errors.UserIdRequired);

        if (string.IsNullOrWhiteSpace(promptText))
            return Result.Failure<CoachingPrompt>(CoachingPromptErrors.Errors.PromptTextRequired);

        var trimmedPrompt = promptText.Trim();
        if (trimmedPrompt.Length > MaxPromptTextLength)
            return Result.Failure<CoachingPrompt>(CoachingPromptErrors.Errors.PromptTextTooLong);

        if (string.IsNullOrWhiteSpace(contextJson))
            return Result.Failure<CoachingPrompt>(CoachingPromptErrors.Errors.ContextJsonRequired);

        if (response is null || string.IsNullOrWhiteSpace(response.Text))
            return Result.Failure<CoachingPrompt>(CoachingPromptErrors.Errors.ResponseRequired);

        var model = response.Model ?? string.Empty;
        if (string.IsNullOrWhiteSpace(model))
            return Result.Failure<CoachingPrompt>(CoachingPromptErrors.Errors.ModelRequired);

        if (model.Length > MaxModelLength)
            return Result.Failure<CoachingPrompt>(CoachingPromptErrors.Errors.ModelTooLong);

        var latencyMs = (int)Math.Max(0, response.Duration.TotalMilliseconds);
        if (response.Duration < TimeSpan.Zero)
            return Result.Failure<CoachingPrompt>(CoachingPromptErrors.Errors.LatencyNegative);

        if ((byte)severity > (byte)CoachingPromptSeverity.High)
            return Result.Failure<CoachingPrompt>(CoachingPromptErrors.Errors.SeverityOutOfRange);

        var createdAt = clock.UtcNow;
        return Result.Success(new CoachingPrompt(
            id: Guid.NewGuid(),
            userId: userId,
            promptText: trimmedPrompt,
            contextJson: contextJson,
            providerResponseText: response.Text.Trim(),
            model: model.Trim(),
            latencyMs: latencyMs,
            severity: severity,
            createdAt: createdAt));
    }

    /// <summary>
    /// EF rehydration path. NO validation (DB CHECK constraints are the
    /// safety net). Reserved for <c>CoachingPromptRepository</c>.
    /// </summary>
    public static CoachingPrompt Rehydrate(
        Guid id,
        Guid userId,
        string promptText,
        string contextJson,
        string providerResponseText,
        string model,
        int latencyMs,
        CoachingPromptSeverity severity,
        CoachingPromptKind kind,
        DateTimeOffset createdAt)
    {
        return new CoachingPrompt(
            id: id,
            userId: userId,
            promptText: promptText,
            contextJson: contextJson,
            providerResponseText: providerResponseText,
            model: model,
            latencyMs: latencyMs,
            severity: severity,
            createdAt: createdAt)
        {
            // Unconventional pattern: assign Kind directly via a private setter-like
            // escape hatch. The aggregate stores whatever the DB says; the Rule
            // kind is allowed for future cross-kind migration but today the
            // AI table never persists it.
            Kind = kind,
        };
    }
}

// ============================================================================
//  CoachingPromptErrors — error catalog for the AI coaching prompt aggregate.
//  Codes carry the `coaching_prompt.` prefix so the API endpoints can map
//  them to RFC 7807 problem responses.
//
//  Invariant mapping (mirrors existing scanner/checklist errors):
//   - validation.* → 400 / 422 (semantic validation failure)
// ============================================================================

public static class CoachingPromptErrors
{
    public static class Errors
    {
        public static readonly Error UserIdRequired =
            Error.Validation("coaching_prompt.user_id_required", "Coaching prompt user id is required.");

        public static readonly Error PromptTextRequired =
            Error.Validation("coaching_prompt.prompt_text_required", "Coaching prompt text is required.");

        public static readonly Error PromptTextTooLong =
            Error.Validation("coaching_prompt.prompt_text_too_long",
                $"Coaching prompt text must be at most {CoachingPrompt.MaxPromptTextLength} characters.");

        public static readonly Error ContextJsonRequired =
            Error.Validation("coaching_prompt.context_json_required", "Coaching prompt context json is required.");

        public static readonly Error ResponseRequired =
            Error.Validation("coaching_prompt.response_required", "Coaching prompt provider response text is required.");

        public static readonly Error ModelRequired =
            Error.Validation("coaching_prompt.model_required", "Coaching prompt model identifier is required.");

        public static readonly Error ModelTooLong =
            Error.Validation("coaching_prompt.model_too_long",
                $"Coaching prompt model must be at most {CoachingPrompt.MaxModelLength} characters.");

        public static readonly Error LatencyNegative =
            Error.Validation("coaching_prompt.latency_negative",
                "Coaching prompt latency must be non-negative.");

        public static readonly Error SeverityOutOfRange =
            Error.Validation("coaching_prompt.severity_out_of_range",
                "Coaching prompt severity must be one of Low (0), Medium (1), High (2).");

        public static readonly Error NotFound =
            Error.NotFound("coaching_prompt.not_found", "Coaching prompt not found.");
    }
}

// ============================================================================
//  CoachingPromptEvents — reserved event namespace.
//
//  Per design.md, the CoachingPrompt aggregate is intentionally event-free:
//  the BG service emits Prometheus counters instead, and the FE renders
//  prompts on its next polling tick. We keep this section to document the
//  decision and to give future slices a stable place to add events (e.g.
//  CoachingPromptRatedDomainEvent when the user feedback loop ships in
//  Wave 6+).
// ============================================================================

/// <summary>
/// Marker interface — implement when a coaching-prompt lifecycle event
/// needs to cross a bounded-context boundary.
/// </summary>
public interface ICoachingPromptDomainEvent : JadeCapital.Shared.Kernel.Primitives.IDomainEvent
{
    Guid PromptId { get; }
    Guid UserId { get; }
}