# AI Risk Advisor Specification

## Purpose

A pre-trade risk advisory layer that consults a local Ollama provider with the proposed trade's context and the user's recent trading history. The advisor runs on the `OpenTrade` critical path (before the trade is persisted), returns a structured advisory (`allow` / `warning` / `block` + reason), and persists the advisory alongside the `PreTradeChecklist` row. The trader retains final authority — `block` recommendations return 422 but the FE presents an explicit override modal; `warning` recommendations attach the advisory without blocking; `allow` is silent.

This spec covers the advisory interface, the prompt template, the parser of the structured Ollama response, the hook in `OpenTradeHandler`, and the read/write HTTP surface. It does NOT cover auto-execution (the advisor never opens a trade on its own) or cloud AI providers (Wave 6).

## Requirements

### Requirement: IAIRiskAdvisor abstraction

The system MUST define `IAIRiskAdvisor` in `Trading.Application.Ai` with one method:
- `Task<Result<AIRiskAdvice>> AdviseAsync(AIRiskAdviceRequest request, CancellationToken ct = default)`.

The default impl is `OllamaAIRiskAdvisor` (Infrastructure), targeting `http://localhost:11434`. Wave 6 may add `OpenAiAIRiskAdvisor`. The advisor MUST enforce a 5-second internal timeout (independent of the `IAIProvider` default 30s — the advisor runs on the OpenTrade critical path).

#### Scenario: Advisory happy path

- GIVEN the user has 4 consecutive losing trades in the last hour, all on EURUSD
- WHEN `AdviseAsync` is called with `{ symbol: "EURUSD", direction: "Long", riskRewardAtEntry: 0.8 }`
- THEN the response MUST be `Result.Success(advice)` with `ParsedAction = Warning` or `Block`
- AND `advice.Reason` MUST be a non-empty, PII-free string in the user's locale (Spanish by default — JadeCapital is es-AR first)

#### Scenario: 5s timeout

- GIVEN Ollama takes longer than 5 seconds to respond
- WHEN `AdviseAsync` is called
- THEN the result MUST be `Result.Failure(ai.timeout)`
- AND `OpenTradeHandler` MUST silently fall back to `action = "allow"` + reason = "AI provider unavailable — proceeding without advisory"

#### Scenario: Provider unavailable

- GIVEN Ollama is not running
- WHEN `AdviseAsync` is called
- THEN the result MUST be `Result.Failure(ai.unavailable)`
- AND no exception MUST propagate to OpenTradeHandler

### Requirement: AIRiskAdvisorPrompt safety

`AIRiskAdvisorPrompt.Render(req, ctx)` MUST produce a single string with explicit delimiter sections:
1. **Role definition** (advisory-only AI).
2. **Instructions** (respond ONLY with JSON `{action, reason}`).
3. **User context block** — `--- USER TRADING CONTEXT (last 7 days) ---` + JSON.
4. **Trade block** — `--- PROPOSED TRADE ---` + symbol/direction/volume/entry/RR/setup.
5. **Output spec** — `--- ADVISORY JSON ---` directive.

The user context MUST NOT include PII (no email, no display name, no absolute P&L values). The data block MUST NOT be reformatted by the AI; the explicit delimiters prevent re-interpretation as instructions.

#### Scenario: PII exclusion

- GIVEN a `UserTradingContext` derived from a user with sensitive fields populated
- WHEN the prompt renders
- THEN the serialized context JSON MUST NOT contain absolute P&L amounts (`+$420.50` etc.)
- AND MUST NOT contain user email or display name

#### Scenario: Delimiter structure

- GIVEN a request and context
- WHEN the prompt renders
- THEN the output MUST contain the literal strings `--- USER TRADING CONTEXT (last 7 days) ---`, `--- PROPOSED TRADE ---`, and `--- ADVISORY JSON ---` as line-separated markers

### Requirement: Response parsing

`AIRiskAdvisorResponseParser.Parse(string content)` MUST extract `{action, reason}` JSON, stripping markdown code fences if present (Ollama sometimes returns ` ```json\n...\n``` `). Malformed JSON MUST map to `(Allow, "AI returned unparseable response — proceeding without advisory")`. The `reason` text MUST be truncated to 500 chars maximum.

#### Scenario: Valid JSON

- GIVEN the response content is `{"action": "warning", "reason": "4 trades perdedoras en EURUSD en la última hora"}`
- WHEN `Parse` is called
- THEN the result MUST be `(Warning, "4 trades perdedoras en EURUSD en la última hora")`

#### Scenario: Markdown code fence stripping

- GIVEN the response content is ` ```json\n{"action": "block", "reason": "excessive risk"}\n``` `
- WHEN `Parse` is called
- THEN the result MUST be `(Block, "excessive risk")` (fences stripped first)

#### Scenario: Malformed JSON

- GIVEN the response content is `"I think you should be careful today"`
- WHEN `Parse` is called
- THEN the result MUST be `(Allow, "AI returned unparseable response — proceeding without advisory")`

#### Scenario: Unknown action string

- GIVEN the response is `{"action": "abort", "reason": "..."}`
- WHEN `Parse` is called
- THEN the result MUST be `(Allow, "...")` (safe default when action is unknown)

### Requirement: OpenTradeHandler integration

`OpenTradeHandler.Handle(cmd, ct)` MUST invoke `IAIRiskAdvisor.AdviseAsync` only when `cmd.PreTradeChecklist != null`. The advisor dependency MUST be optional (nullable) so callers without the registration skip the call. The flow:
- If advisor returns `Block` → return `Result.Failure(ai_risk.blocked)` mapped to 422 + advisory payload.
- If advisor returns `Warning` → attach advisory to checklist (`AttachAIRiskAdvisory(json)`), continue with legacy trade-open flow.
- If advisor returns `Allow` → attach advisory with empty reason, continue.
- If advisor returns `Result.Failure` OR is not registered → log warning + continue with legacy flow (no advisory attached, no block).
- If `cmd.PreTradeChecklist == null` (legacy OpenTrade without checklist) → advisor MUST NOT be called.

#### Scenario: Warning flow

- GIVEN user submits `OpenTrade(cmd)` with checklist + advisor returns `Warning`
- WHEN the handler runs
- THEN the trade MUST be opened
- AND `pre_trade_checklists.ai_advisory` MUST contain the advisory JSON
- AND the response MUST be 201 + trade + advisory in payload

#### Scenario: Block flow

- GIVEN advisor returns `Block`
- WHEN the handler runs
- THEN the response MUST be 422 with `error.code = "ai_risk.blocked"` and `error.reason` populated
- AND no row MUST be inserted in `trading.trades`
- AND a row MUST be inserted in `trading.ai_risk_advice` (audit trail of the block)

#### Scenario: No checklist → advisor not invoked

- GIVEN user submits `OpenTrade(cmd)` WITHOUT checklist
- WHEN the handler runs
- THEN the advisor dependency MUST NOT be resolved (or its result discarded)
- AND the trade MUST open as in legacy flow (no AI involvement)

#### Scenario: Advisor throws

- GIVEN `IAIProvider.GenerateAsync` throws an unexpected exception (not the documented failures)
- WHEN the handler runs
- THEN the exception MUST be caught internally
- AND the handler MUST log a warning + continue with legacy flow
- AND the trade MUST open

#### Scenario: Legacy DI (no advisor registered)

- GIVEN `IAIRiskAdvisor` is NOT registered in DI (Wave 1c deployments)
- WHEN the handler is constructed via the nullable-ctor overload
- THEN the `IAIRiskAdvisor?` parameter MUST be null
- AND the handler MUST skip the advisor step entirely
- AND no exception MUST propagate from construction or invocation

### Requirement: Manual advisory request

`POST /api/ai/risk-advice` MUST accept a body `{ symbol, direction, volume, volumeCurrency, entryPrice, riskRewardAtEntry, setupQuality }` and return `{ adviceId, action, reason, model, latencyMs }`. The endpoint MUST persist the advisory under `trading.ai_risk_advice` with `trade_id = null` (manual advisory request, no associated trade).

#### Scenario: Manual request happy path

- GIVEN a valid request body for a low-quality setup on a high-volatility instrument
- WHEN `POST /api/ai/risk-advice` is invoked
- THEN the response MUST be 200 + `{ adviceId, action: "warning", reason: "...", model: "llama3.1:8b", latencyMs: 412 }`

#### Scenario: Manual request when Ollama is down

- GIVEN Ollama is unavailable
- WHEN `POST /api/ai/risk-advice` is invoked
- THEN the response MUST be 503 with `error.code = "ai.unavailable"`

### Requirement: Cached advisory by trade id

`GET /api/ai/risk-advice/{tradeId}` MUST return the cached advisory persisted at `OpenTrade` time. The advisory is stored in `pre_trade_checklists.ai_advisory` (associated with the trade via `trade_id`). The endpoint MUST return 404 if no advisory is attached (e.g., trade opened before Wave 5 migration).

#### Scenario: Cached advisory for an AI-blocked-checked trade

- GIVEN trade `id = T1` was opened with checklist + Warning advisory attached
- WHEN `GET /api/ai/risk-advice/T1` is called
- THEN the response MUST be 200 + the stored JSON advisory

#### Scenario: Trade opened before Wave 5

- GIVEN trade `id = T2` was opened without an advisory (pre-Wave 5)
- WHEN `GET /api/ai/risk-advice/T2` is called
- THEN the response MUST be 404 with `error.code = "ai_risk.not_found"`

#### Scenario: Cross-user

- GIVEN user A opened trade `T1`
- WHEN user B calls `GET /api/ai/risk-advice/T1`
- THEN the response MUST be 404 (no cross-user access)

### Requirement: AIRiskAdvice aggregate

`AIRiskAdvice` MUST be a `Trading.Domain.Ai` aggregate with `Id, UserId, TradeId?, ContextJson, ProviderResponse, ParsedAction, Reason, CreatedAt`. Invariants:
- `ParsedAction` MUST be one of `Allow=0, Warning=1, Block=2`.
- `Reason` length MUST be <= 500 chars (truncated by parser).
- `CreatedAt` MUST be set by `IClock` at create (NOT by PostgreSQL `now()`) — the aggregate is constructed in-process before persistence.

## Data Model

```
trading.ai_risk_advice
  id                UUID PK
  user_id           UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE
  trade_id          UUID,
  context_json      JSONB NOT NULL
  provider_response JSONB NOT NULL
  parsed_action     SMALLINT NOT NULL  -- 0=Allow, 1=Warning, 2=Block
  reason            VARCHAR(500)
  created_at        TIMESTAMPTZ NOT NULL DEFAULT now()

ALTER TABLE trading.pre_trade_checklists
  ADD COLUMN ai_advisory JSONB

Constraints:
  ck_ai_risk_action   CHECK (parsed_action BETWEEN 0 AND 2)
  fk_ai_risk_trade    FOREIGN KEY (trade_id) REFERENCES trading.trades(id) ON DELETE SET NULL

Indexes:
  ix_ai_risk_advice_user_trade  (user_id, trade_id)
```

## Endpoints

- `POST /api/ai/risk-advice` — manual advisory request. Returns 200 + `{ adviceId, action, reason, model, latencyMs }`. Returns 503 if Ollama is down. Rate limit: 60/hour/user.
- `GET /api/ai/risk-advice/{tradeId}` — cached advisory for a trade. Returns 200 + stored JSON. Returns 404 if not found. Rate limit: 60/hour/user.
- (Internal) `OpenTradeHandler` calls `IAIRiskAdvisor.AdviseAsync` directly — no HTTP endpoint; the trigger is the trade-open command itself.

Both HTTP endpoints require `RequireAuthorization`.

## Output Shape

```json
{
  "adviceId": "...",
  "action": "warning",
  "reason": "4 operaciones perdedoras consecutivas en EURUSD en la última hora. RR objetivo 2.0, actual 0.8. Recomendamos un break de 30 minutos antes de continuar.",
  "model": "llama3.1:8b",
  "latencyMs": 412,
  "createdAt": "2026-08-19T14:32:00Z"
}
```

For cached advisory (the same fields + `contextJson` and `providerResponse` for transparency):

```json
{
  "adviceId": "...",
  "tradeId": "...",
  "action": "warning",
  "reason": "...",
  "model": "llama3.1:8b",
  "latencyMs": 412,
  "contextJson": { /* anonymized */ },
  "providerResponse": { /* raw Ollama */ },
  "createdAt": "..."
}
```

## Architecture

- **Trading.Domain/Ai** — `AIRiskAdvice` aggregate, `AIRiskAction` enum.
- **Trading.Application/Ai** — `IAIRiskAdvisor` interface, `AIRiskAdviceRequest` record, `AIRiskAdvisorPrompt` (static renderer), `AIRiskAdvisorResponseParser` (static parser), `GetPreTradeAdviceHandler`.
- **Trading.Application/Abstractions** — `IUserTradingContextProvider` (full impl, already in 5b.2), `IAIRiskAdviceRepository`.
- **Trading.Infrastructure/Ai** — `OllamaAIRiskAdvisor`, `AIRiskAdviceRepository`, `AIRiskAdviceConfiguration`.
- **Trading.Domain/PreTradeChecklists** (existing) — extended with `AIRiskAdvisoryJson` + `AttachAIRiskAdvisory` method.
- **Trading.Infrastructure/Persistence/Configurations/PreTradeChecklistConfiguration** (existing) — extended to map `ai_advisory JSONB`.
- **Trading.Application/Features/TradeOpen/OpenTradeHandler** (existing) — modified to accept `IAIRiskAdvisor?` (optional) + invoke advisor before trade persist.
- **Trading.Api/Endpoints/AiEndpoints.cs** (5b.1 partial, extended here) — `POST /api/ai/risk-advice` + `GET /api/ai/risk-advice/{tradeId}`.
- **Frontend** — `trader/checklist/pre-trade-checklist-page.ts` extended with AI advisory section, `risk-advice.service.ts`, override modal for Block.

## Out of Scope

- Auto-execution (the advisor never opens a trade on its own behalf) — Phase 7+ (probably never).
- Multi-tenant AI rate limits — Fase 6.
- Streaming token delivery — Wave 7 PWA observability.
- Cloud AI providers (OpenAI, Claude) — Wave 6.
- Per-user tuning (tone, aggressiveness of advice) — Wave 6+.
- Adversarial prompt-injection fuzzing suite — Wave 6+.
- A/B testing different prompt templates — Wave 6+.
- Advisory for non-trade events (e.g., "should I scale up?") — Wave 6+.
- Advisor-triggered cancellation (auto-cancel a trade if Block is returned after the fact) — Wave 6+ (and only if `block` becomes more reliable).
- Real-time alert push via SignalR for high-severity advisories — Wave 6+.
