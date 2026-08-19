-- Migration 0020 — AI Coaching Prompts (slice 5b.2, Wave 5).
--
-- Anade al schema `trading`:
--   * trading.coaching_prompts_ai — bitacora de prompts narrativos generados
--     por el BackgroundService `CoachingPromptService` (daily 03:00 UTC ± 30min).
--     Una fila por prompt; (user_id, created_at DESC) indexado para los
--     queries de FE /api/coaching/ai-prompts?period=... .
--
-- Columnas:
--   - id (UUID PK)
--   - user_id (UUID, FK a identity.users con ON DELETE CASCADE)
--   - prompt_text (TEXT — el prompt completo enviado a Ollama)
--   - context_json (JSONB — el contexto del user serializado, sin PII)
--   - provider_response (JSONB — la respuesta cruda del provider, parseada)
--   - model (VARCHAR(64) — nombre del modelo usado, ej. "llama3.1:8b")
--   - latency_ms (INTEGER — duracion del call al provider, no del BG service)
--   - severity (SMALLINT 0..2 — 0=Low, 1=Medium, 2=High)
--   - kind (SMALLINT 0..1 — 0=Rule legacy, 1=AI; default 1)
--   - created_at (TIMESTAMPTZ default now())
--
-- Reglas idempotente + additive only:
--   - CREATE TABLE IF NOT EXISTS, CREATE INDEX IF NOT EXISTS.
--   - FK cross-schema a identity.users(id) ON DELETE CASCADE.
--   - CHECK severity BETWEEN 0 AND 2.
--   - kind siempre 1 (AI); la columna existe por extension futura.
--
-- Nota: el JSONB provider_response guarda la respuesta cruda de Ollama
-- (campo `response` mas `prompt_eval_count` / `eval_count`). El FE renderiza
-- `providerResponse.text` cuando esta presente.

BEGIN;

CREATE TABLE IF NOT EXISTS trading.coaching_prompts_ai (
    id                UUID         PRIMARY KEY,
    user_id           UUID         NOT NULL,
    prompt_text       VARCHAR(4000) NOT NULL,
    context_json      JSONB        NOT NULL,
    provider_response JSONB        NOT NULL,
    model             VARCHAR(64)  NOT NULL,
    latency_ms        INTEGER      NOT NULL,
    severity          SMALLINT     NOT NULL DEFAULT 0,
    kind              SMALLINT     NOT NULL DEFAULT 1,
    created_at        TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT fk_coaching_ai_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE CASCADE,

    CONSTRAINT ck_coaching_ai_severity
        CHECK (severity BETWEEN 0 AND 2),
    CONSTRAINT ck_coaching_ai_kind
        CHECK (kind IN (0, 1)),
    CONSTRAINT ck_coaching_ai_latency
        CHECK (latency_ms >= 0)
);

-- Lookup by user + created_at DESC — drives GET /api/coaching/ai-prompts.
CREATE INDEX IF NOT EXISTS ix_coaching_ai_user_created
    ON trading.coaching_prompts_ai (user_id, created_at DESC);

COMMENT ON TABLE trading.coaching_prompts_ai IS 'AI-generated coaching prompts (slice 5b.2, Wave 5). One row per prompt; populated by CoachingPromptService daily BG service at 03:00 UTC ± 30min jitter. kind=1 for AI prompts; kind=0 reserved for future Rule-prompt consolidation.';
COMMENT ON COLUMN trading.coaching_prompts_ai.prompt_text IS 'Full rendered prompt sent to the AI provider. No PII (template excludes email/displayName).';
COMMENT ON COLUMN trading.coaching_prompts_ai.context_json IS 'User trading context (closed_trades, winners, losers, win_rate, avg_rr, instruments_traded, violations). No PII; aggregated fields only.';
COMMENT ON COLUMN trading.coaching_prompts_ai.provider_response IS 'Raw Ollama response (JSONB): { response, model, prompt_eval_count, eval_count, done }. The narrative text lives under response.';
COMMENT ON COLUMN trading.coaching_prompts_ai.severity IS 'CoachingPromptSeverity: 0=Low, 1=Medium, 2=High. Derived from violations count + win-rate in the handler.';
COMMENT ON COLUMN trading.coaching_prompts_ai.kind IS 'CoachingPromptKind: 0=Rule (legacy Wave 3b), 1=AI (Wave 5b.2). Always 1 in this table — column exists for the eventual server-side merge on /api/coaching/prompts.';
COMMENT ON COLUMN trading.coaching_prompts_ai.latency_ms IS 'Wall-clock latency of the Ollama /api/generate call (NOT the entire BG tick). 0 when the provider short-circuits without calling Ollama.';

COMMIT;