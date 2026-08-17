namespace JadeCapital.Trading.Contracts.AiRiskAdvisor;

// ============================================================================
//  AiRiskAdvisorDtos — slice 5c.1 (Wave 5, AI Risk Advisor Pre-Trade).
//
//  Consolidated wire shapes for the AI risk advisor endpoints. Mirrors
//  the JadeCapital.Trading.Domain.Ai.AIRiskAdvice aggregate's externally
//  visible fields. The action string uses lowercase to match the
//  precedent (severity is "low"/"medium"/"high").
//
//  Two DTOs in one file: AIRiskAdviceDto (response) and
//  AiRiskAdvisorRequestDto (manual request). Both are wire payloads that
//  flow into the endpoints; neither carries invariant logic.
// ============================================================================

public sealed record AiRiskAdvisorRequestDto(
    string Symbol,
    string Direction,
    decimal Volume,
    string VolumeCurrency,
    decimal EntryPrice,
    decimal? StopLoss,
    decimal RiskRewardAtEntry,
    string SetupQuality);

public sealed record AIRiskAdviceDto(
    Guid Id,
    Guid UserId,
    Guid? TradeId,
    string Action,
    string Reason,
    string Model,
    int LatencyMs,
    DateTimeOffset CreatedAt);
