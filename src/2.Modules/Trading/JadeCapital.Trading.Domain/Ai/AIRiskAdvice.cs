using JadeCapital.Shared.Kernel.Ai;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Trading.Domain.Ai;

// ============================================================================
//  AIRiskAdvice — slice 5c.1 (Wave 5, AI Risk Advisor Pre-Trade).
//
//  Aggregate root for an AI-generated pre-trade risk advisory. Persisted to
//  <c>trading.ai_risk_advice</c> by <c>AIRiskAdviceRepository</c>; populated
//  by:
//   - <c>GetPreTradeAdviceHandler</c> (manual advisory endpoint).
//   - <c>OllamaAIRiskAdvisor</c> invoked from <c>OpenTradeHandler</c>.
//
//  <para>
//  Invariants:
//  <list type="bullet">
//    <item>UserId != Guid.Empty.</item>
//    <item>ContextJson non-empty (the user trading context that fed the prompt).</item>
//    <item>ProviderResponseText (the raw Ollama body, trimmed) non-empty.</item>
//    <item>Reason length <= 500 chars (truncated by parser before Create).</item>
//    <item>ParsedAction byte 0..2.</item>
//    <item>Model 1..64 chars (matches DB column).</item>
//    <item>LatencyMs >= 0.</item>
//    <item>TradeId is OPTIONAL — null for manual advisory, populated for OpenTrade.</item>
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

public sealed class AIRiskAdvice : AggregateRoot<Guid>
{
    /// <summary>Max length of the reason — matches VARCHAR(500) in the migration.</summary>
    public const int MaxReasonLength = 500;

    /// <summary>Max length of the model identifier — matches VARCHAR(64).</summary>
    public const int MaxModelLength = 64;

    public Guid UserId { get; private set; }
    public Guid? TradeId { get; private set; }
    public string ContextJson { get; private set; } = string.Empty;
    public string ProviderResponseText { get; private set; } = string.Empty;
    public AIRiskAction ParsedAction { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public int LatencyMs { get; private set; }
    public new DateTimeOffset CreatedAt { get; private set; }

    // EF Core.
    private AIRiskAdvice() { }

    private AIRiskAdvice(
        Guid id,
        Guid userId,
        Guid? tradeId,
        string contextJson,
        string providerResponseText,
        AIRiskAction parsedAction,
        string reason,
        string model,
        int latencyMs,
        DateTimeOffset createdAt) : base(id)
    {
        UserId = userId;
        TradeId = tradeId;
        ContextJson = contextJson;
        ProviderResponseText = providerResponseText;
        ParsedAction = parsedAction;
        Reason = reason;
        Model = model;
        LatencyMs = latencyMs;
        CreatedAt = createdAt;
        SetCreatedAt(createdAt);
    }

    /// <summary>
    /// Factory: validates invariants and builds the aggregate. The
    /// <paramref name="parsedAction"/> defaults to <see cref="AIRiskAction.Warning"/>
    /// when the caller does not override it — this is the load-bearing
    /// default the parser uses when the raw action string is unknown (safe
    /// default per design.md §"Response parsing").
    /// </summary>
    public static Result<AIRiskAdvice> Create(
        Guid userId,
        Guid? tradeId,
        string contextJson,
        PromptResponse response,
        string reason,
        IClock clock)
        => Create(userId, tradeId, contextJson, response, reason, AIRiskAction.Warning, clock);

    /// <summary>
    /// Factory overload that takes the parsed action explicitly. Used by
    /// <c>GetPreTradeAdviceHandler</c> after the parser has already classified
    /// the response.
    /// </summary>
    public static Result<AIRiskAdvice> Create(
        Guid userId,
        Guid? tradeId,
        string contextJson,
        PromptResponse response,
        string reason,
        AIRiskAction parsedAction,
        IClock clock)
    {
        if (userId == Guid.Empty)
            return Result.Failure<AIRiskAdvice>(AIRiskAdviceErrors.Errors.UserIdRequired);

        if (string.IsNullOrWhiteSpace(contextJson))
            return Result.Failure<AIRiskAdvice>(AIRiskAdviceErrors.Errors.ContextJsonRequired);

        if (response is null || string.IsNullOrWhiteSpace(response.Text))
            return Result.Failure<AIRiskAdvice>(AIRiskAdviceErrors.Errors.ResponseRequired);

        var trimmedReason = (reason ?? string.Empty).Trim();
        if (trimmedReason.Length > MaxReasonLength)
            return Result.Failure<AIRiskAdvice>(AIRiskAdviceErrors.Errors.ReasonTooLong);

        var model = (response.Model ?? string.Empty).Trim();
        if (model.Length > MaxModelLength)
            return Result.Failure<AIRiskAdvice>(AIRiskAdviceErrors.Errors.ModelTooLong);

        var latencyMs = (int)Math.Max(0, response.Duration.TotalMilliseconds);

        if ((byte)parsedAction > (byte)AIRiskAction.Block)
            return Result.Failure<AIRiskAdvice>(AIRiskAdviceErrors.Errors.ActionOutOfRange);

        var createdAt = clock.UtcNow;
        return Result.Success(new AIRiskAdvice(
            id: Guid.NewGuid(),
            userId: userId,
            tradeId: tradeId,
            contextJson: contextJson,
            providerResponseText: response.Text.Trim(),
            parsedAction: parsedAction,
            reason: trimmedReason,
            model: model,
            latencyMs: latencyMs,
            createdAt: createdAt));
    }

    /// <summary>
    /// EF rehydration path. NO validation (DB CHECK constraints are the
    /// safety net). Reserved for <c>AIRiskAdviceRepository</c>.
    /// </summary>
    public static AIRiskAdvice Rehydrate(
        Guid id,
        Guid userId,
        Guid? tradeId,
        string contextJson,
        string providerResponseText,
        AIRiskAction parsedAction,
        string reason,
        string model,
        int latencyMs,
        DateTimeOffset createdAt)
    {
        return new AIRiskAdvice(
            id: id,
            userId: userId,
            tradeId: tradeId,
            contextJson: contextJson,
            providerResponseText: providerResponseText,
            parsedAction: parsedAction,
            reason: reason,
            model: model,
            latencyMs: latencyMs,
            createdAt: createdAt);
    }
}

// ============================================================================
//  AIRiskAdviceErrors — error catalog for the AIRiskAdvice aggregate.
//  Codes carry the `ai_risk_advice.` prefix so the API endpoints can map
//  them to RFC 7807 problem responses.
//
//  Invariant mapping (mirrors CoachingPromptErrors / CoachingPromptErrors):
//   - validation.* → 400 / 422 (semantic validation failure)
// ============================================================================

public static class AIRiskAdviceErrors
{
    public static class Errors
    {
        public static readonly Error UserIdRequired =
            Error.Validation("ai_risk_advice.user_id_required", "AI risk advice user id is required.");

        public static readonly Error ContextJsonRequired =
            Error.Validation("ai_risk_advice.context_json_required",
                "AI risk advice context json is required.");

        public static readonly Error ResponseRequired =
            Error.Validation("ai_risk_advice.response_required",
                "AI risk advice provider response text is required.");

        public static readonly Error ReasonTooLong =
            Error.Validation("ai_risk_advice.reason_too_long",
                $"AI risk advice reason must be at most {AIRiskAdvice.MaxReasonLength} characters.");

        public static readonly Error ModelTooLong =
            Error.Validation("ai_risk_advice.model_too_long",
                $"AI risk advice model must be at most {AIRiskAdvice.MaxModelLength} characters.");

        public static readonly Error ActionOutOfRange =
            Error.Validation("ai_risk_advice.action_out_of_range",
                "AI risk advice action must be one of Allow (0), Warning (1), Block (2).");

        public static readonly Error NotFound =
            Error.NotFound("ai_risk_advice.not_found", "AI risk advice not found.");
    }
}
