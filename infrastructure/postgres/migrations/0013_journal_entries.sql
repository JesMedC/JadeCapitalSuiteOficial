-- Migracion 0013 — Journal Daily entries (slice 2a.1 de
-- 2026-08-17-trader-journal-core).
--
-- Anade al schema `trading`:
--   * trading.journal_entries — un entry por user por local_date. Captura
--     mood_pre / mood_during / mood_post (SMALLINT 1..5), premarket_plan
--     (<= 2000 chars), postmarket_reflection (<= 5000 chars), tags
--     (TEXT[] hasta 10 items, cada uno <= 32 chars), y create/update audit.
--     Es DEL TRADER: todo el scope de operaciones se hace por user_id y
--     local_date derivado del timezone del header `X-User-Timezone`.
--
-- Reglas de la migracion (idempotente, additive only):
--   - CREATE TABLE IF NOT EXISTS, CREATE INDEX IF NOT EXISTS.
--   - NO DROP, NO ALTER destructivo (rollback = DROP TABLE manual).
--   - Exactly-one-entry-per-user-per-day invariant: UNIQUE INDEX sobre
--     (user_id, local_date) en trading.journal_entries.
--   - El FK a identity.users es ON DELETE CASCADE: borrar el usuario
--     borra todos sus journals (orphan rows serian basura). El cross-schema
--     FK vive en la migration SQL — EF no lo puede crear limpiamente.
--   - Los CHECK constraints redundan con la validacion a nivel dominio
--     (JournalEntry.CreateOrUpdate). La DB es la red de seguridad final
--     contra bugs en aplicacion.
--
-- Origen de la fecha: 2026-08-16 (sprint Wave 2 / slice 2a / sub-slice 2a.1).

BEGIN;

-- ============================================
-- trading.journal_entries
-- ============================================
CREATE TABLE IF NOT EXISTS trading.journal_entries (
    id                      UUID            PRIMARY KEY,
    user_id                 UUID            NOT NULL,
    local_date              DATE            NOT NULL,
    timezone                VARCHAR(64)     NOT NULL DEFAULT 'UTC',
    mood_pre                SMALLINT,
    mood_during             SMALLINT,
    mood_post               SMALLINT,
    premarket_plan          TEXT,
    postmarket_reflection   TEXT,
    tags                    TEXT[],
    created_at              TIMESTAMPTZ     NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ     NOT NULL DEFAULT now(),

    CONSTRAINT fk_journal_entries_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE CASCADE,

    -- Mood dimensions SMALLINT 1..5 (misma escala que
    -- pre_trade_checklist.emotionality y trade_review.emotionality:
    -- 1=Fearful, 2=Anxious, 3=Neutral, 4=Confident, 5=Euphoric).
    -- El dominio enforce el set exacto; el CHECK enforce el piso/techo.
    CONSTRAINT ck_journal_entries_mood_pre_range
        CHECK (mood_pre IS NULL OR mood_pre BETWEEN 1 AND 5),
    CONSTRAINT ck_journal_entries_mood_during_range
        CHECK (mood_during IS NULL OR mood_during BETWEEN 1 AND 5),
    CONSTRAINT ck_journal_entries_mood_post_range
        CHECK (mood_post IS NULL OR mood_post BETWEEN 1 AND 5),

    -- Plan pre-mercado <= 2000 chars (watchlist, scenarios, bias).
    CONSTRAINT ck_journal_entries_premarket_plan_max_length
        CHECK (premarket_plan IS NULL OR length(premarket_plan) <= 2000),

    -- Reflexion post-mercado <= 5000 chars (what worked, lessons).
    CONSTRAINT ck_journal_entries_postmarket_reflection_max_length
        CHECK (postmarket_reflection IS NULL OR length(postmarket_reflection) <= 5000),

    -- Tags: hasta 10 tags por entry (array_length NULL-safe).
    CONSTRAINT ck_journal_entries_tags_max_count
        CHECK (tags IS NULL OR array_length(tags, 1) <= 10)
);

-- Exactly-one-entry-per-user-per-day. El lookup principal del FE es
-- `(user_id, local_date)` (range query por mes), asi que UNIQUE es la
-- forma natural y evita la condicion de carrera entre dos POST
-- concurrentes del mismo usuario en el mismo dia.
CREATE UNIQUE INDEX IF NOT EXISTS ux_journal_user_date
    ON trading.journal_entries (user_id, local_date);

-- Indice secundario sobre (user_id, created_at DESC) para queries cross-module
-- futuras (e.g. "ultimo journal del usuario" sin filtrar por local_date).
CREATE INDEX IF NOT EXISTS ix_journal_user_created
    ON trading.journal_entries (user_id, created_at DESC);

-- Comentarios.
COMMENT ON TABLE trading.journal_entries IS 'Daily trading journal entries (slice 2a.1). Exactly-one-entry-per-user-per-day via UNIQUE(user_id, local_date). Mood 1..5 en tres dimensiones temporales; plan/反思 free-text; tags free-text.';
COMMENT ON COLUMN trading.journal_entries.id IS 'PK UUID.';
COMMENT ON COLUMN trading.journal_entries.user_id IS 'FK a identity.users(id) ON DELETE CASCADE. Borrar el usuario borra sus journals.';
COMMENT ON COLUMN trading.journal_entries.local_date IS 'Fecha local del journal (YYYY-MM-DD). Deriva del timezone del header X-User-Timezone; UTC fallback.';
COMMENT ON COLUMN trading.journal_entries.timezone IS 'IANA TZ name con el que el usuario definio la local_date (e.g. America/Argentina/Buenos_Aires). Default UTC.';
COMMENT ON COLUMN trading.journal_entries.mood_pre IS 'Mood pre-mercado: SMALLINT 1..5 (1=Fearful..5=Euphoric). NULL permitido.';
COMMENT ON COLUMN trading.journal_entries.mood_during IS 'Mood durante-mercado: SMALLINT 1..5. NULL permitido.';
COMMENT ON COLUMN trading.journal_entries.mood_post IS 'Mood post-mercado: SMALLINT 1..5. NULL permitido.';
COMMENT ON COLUMN trading.journal_entries.premarket_plan IS 'Plan pre-mercado en texto libre (watchlist, scenarios, bias). <= 2000 chars.';
COMMENT ON COLUMN trading.journal_entries.postmarket_reflection IS 'Reflexion post-mercado en texto libre (what worked, lessons). <= 5000 chars.';
COMMENT ON COLUMN trading.journal_entries.tags IS 'Array de tags free-text (e.g. "fomo", "revenge", "good-execution"). Hasta 10 items, cada uno <= 32 chars enforced por aplicacion.';
COMMENT ON COLUMN trading.journal_entries.created_at IS 'Timestamp de creacion del entry. Updated on each upsert.';
COMMENT ON COLUMN trading.journal_entries.updated_at IS 'Timestamp de la ultima modificacion. Default = created_at en INSERT.';

COMMIT;
