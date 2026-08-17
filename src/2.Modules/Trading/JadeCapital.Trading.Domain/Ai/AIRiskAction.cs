namespace JadeCapital.Trading.Domain.Ai;

// ============================================================================
//  AIRiskAction — slice 5c.1 (Wave 5, AI Risk Advisor Pre-Trade).
//
//  Parsed action classification from the AI risk advisor. The raw Ollama
//  response is a JSON object { action: "allow"|"warning"|"block", reason: "..." }
//  parsed by AIRiskAdvisorResponseParser (Application layer). The byte
//  representation mirrors the DB CHECK constraint (parsed_action BETWEEN 0 AND 2).
//
//  Semantics:
//   - Allow   = 0 — no objection, advisory is silent (allow-with-reason).
//   - Warning = 1 — persist on the checklist, do not block the trade.
//   - Block   = 2 — OpenTradeHandler returns 422 with error.code = "ai_risk.blocked".
//
//  Per JadeCapital style, byte values match the DB SMALLINT column 1-a-1.
// ============================================================================

public enum AIRiskAction : byte
{
    Allow = 0,
    Warning = 1,
    Block = 2,
}
