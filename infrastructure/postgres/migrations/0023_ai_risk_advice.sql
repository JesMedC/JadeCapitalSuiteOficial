-- Migration 0021 — AI Risk Advisor (slice 5c.1, Wave 5).
--
-- Anade al schema `trading`:
--   * trading.ai_risk_advice — bitacora de advisories pre-trade generados
--     por el AI risk advisor (IAIRiskAdvisor). Una fila por advisory
--     (manual u OpenTrade-time). trade_id puede ser null (manual) o
--     poblarse (OpenTrade).
--   * ALTER TABLE trading.pre_trade_checklists ADD COLUMN ai_advisory JSONB
--     — nullable. Persiste el advisory mas reciente que se adjunto en
--     el OpenTrade time (para GET /api/ai/risk-advice/{tradeId}).
--
-- Columnas:
--   - id (UUID PK)
--   - user_id (UUID, FK a identity.users con ON DELETE CASCADE)
--   - trade_id (UUID, FK a trading.trades con ON DELETE SET NULL, nullable)
--   - context_json (JSONB — el user trading context serializado, sin PII)
--   - provider_response (JSONB — la respuesta cruda del provider)
--   - parsed_action (SMALLINT 0..2 — 0=Allow, 1=Warning, 2=Block)
--   - reason (VARCHAR(500) — el reason text, truncado a 500 chars)
--   - model (VARCHAR(64) — ej. "llama3.1:8b")
--   - latency_ms (INTEGER — duracion del call al provider)
--   - created_at (TIMESTAMPTZ default now())
--
-- Reglas idempotente + additive only:
--   - CREATE TABLE IF NOT EXISTS, CREATE INDEX IF NOT EXISTS.
--   - ALTER TABLE ADD COLUMN IF NOT EXISTS.
--   - FK cross-schema a identity.users(id) ON DELETE CASCADE.
--   - FK cross-schema a trading.trades(id) ON DELETE SET NULL.
--   - CHECK parsed_action BETWEEN 0 AND 2.

BEGIN;

CREATE TABLE IF NOT EXISTS trading.ai_risk_advice (
    id                UUID         PRIMARY KEY,
    user_id           UUID         NOT NULL,
    trade_id          UUID,
    context_json      JSONB        NOT NULL,
    provider_response JSONB        NOT NULL,
    parsed_action     SMALLINT     NOT NULL,
    reason            VARCHAR(500),
    model             VARCHAR(64)  NOT NULL,
    latency_ms        INTEGER      NOT NULL,
    created_at        TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT fk_ai_risk_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE CASCADE,

    CONSTRAINT fk_ai_risk_trade
        FOREIGN KEY (trade_id) REFERENCES trading.trades(id) ON DELETE SET NULL,

    CONSTRAINT ck_ai_risk_action
        CHECK (parsed_action BETWEEN 0 AND 2),
    CONSTRAINT ck_ai_risk_latency
        CHECK (latency_ms >= 0)
);

-- Lookup by user + trade — drives GET /api/ai/risk-advice/{tradeId}.
CREATE INDEX IF NOT EXISTS ix_ai_risk_advice_user_trade
    ON trading.ai_risk_advice (user_id, trade_id);

-- Additive column on pre_trade_checklists: stores the advisory that was
-- attached at OpenTrade time. NULL for trades opened before this migration
-- or for trades opened without an advisory (advisor unavailable).
ALTER TABLE trading.pre_trade_checklists
    ADD COLUMN IF NOT EXISTS ai_advisory JSONB;

COMMENT ON TABLE trading.ai_risk_advice IS 'AI-generated pre-trade risk advisories (slice 5c.1, Wave 5). One row per advisory invocation; trade_id is null for manual requests and populated for OpenTrade-time advisories. The advisory attached to a PreTradeChecklist is mirrored in trading.pre_trade_checklists.ai_advisory for fast GET /api/ai/risk-advice/{tradeId} reads.';
COMMENT ON COLUMN trading.ai_risk_advice.user_id IS 'Owner of the advisory. Cross-user isolation enforced at the endpoint (JWT claim).';
COMMENT ON COLUMN trading.ai_risk_advice.trade_id IS 'Optional FK to the trade at OpenTrade time. NULL for manual advisory requests (POST /api/ai/risk-advice). ON DELETE SET NULL preserves the audit trail when a trade is deleted.';
COMMENT ON COLUMN trading.ai_risk_advice.context_json IS 'User trading context that fed the prompt (closed_trades, winners, losers, win_rate, avg_rr, instruments, violations). No PII; aggregated fields only.';
COMMENT ON COLUMN trading.ai_risk_advice.provider_response IS 'Raw Ollama response text (JSONB). The narrative AI output (the JSON {action, reason} contract).';
COMMENT ON COLUMN trading.ai_risk_advice.parsed_action IS 'AIRiskAction: 0=Allow, 1=Warning, 2=Block. The OpenTradeHandler short-circuits with 422 ai_risk.blocked when this is 2.';
COMMENT ON COLUMN trading.ai_risk_advice.reason IS 'One-sentence rationale from the AI. Truncated to 500 chars to match the DB VARCHAR(500).';
COMMENT ON COLUMN trading.ai_risk_advice.model IS 'AI model identifier echoed from the provider (e.g. llama3.1:8b).';
COMMENT ON COLUMN trading.ai_risk_advice.latency_ms IS 'Wall-clock latency of the AI provider call (5s cap enforced by the OllamaAIRiskAdvisor).';

COMMENT ON COLUMN trading.pre_trade_checklists.ai_advisory IS 'AI risk advisor output attached at OpenTrade time (slice 5c.1). NULL when the advisor did not run or returned allow-without-reason. Mirrors the AIRiskAdvice row that triggered the OpenTrade flow.';

COMMIT;
