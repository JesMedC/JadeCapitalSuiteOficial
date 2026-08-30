-- Migracion 0011 — Pre-Trade Checklist (slice 1c.1 de jade-trader-os-core-portals).
--
-- Anade al schema `trading`:
--   * trading.pre_trade_checklists — checklist obligatorio-optativo por trade.
--     Captura el estado emocional, calidad del setup, RR al ingreso, target
--     de RR usado en ese trade puntual, y cantidad de confluencias.
--     Es DEL TRADER (no del admin): siempre se evalua user-scoped.
--
-- Reglas de la migracion (idempotente, additive only):
--   - CREATE TABLE IF NOT EXISTS, CREATE INDEX IF NOT EXISTS.
--   - NO DROP, NO ALTER destructivo (rollback = DROP TABLE manual).
--   - Exactly-one-checklist-per-trade invariant: UNIQUE INDEX sobre trade_id
--     en la tabla + UNIQUE constraint en la columna (column-level UNIQUE).
--     Red de seguridad ante requests concurrentes que intenten persistir
--     dos checklists para el mismo trade.
--   - El FK a trading.trades es ON DELETE CASCADE: borrar un trade borra
--     su checklist (orphan rows serian basura). El FK a identity.users
--     es ON DELETE RESTRICT: borrar un usuario con checklists es una
--     operacion sensible que debe fallar ruidosamente.
--   - Los CHECK constraints redundan con la validacion a nivel dominio
--     (PreTradeChecklist.Create + PreTradeChecklistSubmission). La DB es
--     la red de seguridad final contra bugos en aplicacion.
--
-- Origen de la fecha: 2026-08-15 (sprint Wave 1 / slice 1c / sub-slice 1c.1).

BEGIN;

-- ============================================
-- trading.pre_trade_checklists
-- ============================================
CREATE TABLE IF NOT EXISTS trading.pre_trade_checklists (
    id                          UUID            PRIMARY KEY,
    trade_id                    UUID            NOT NULL UNIQUE,
    user_id                     UUID            NOT NULL,
    submitted_at                TIMESTAMPTZ     NOT NULL DEFAULT now(),
    emotionality                SMALLINT        NOT NULL,
    setup_quality               SMALLINT        NOT NULL,
    risk_reward_at_entry        NUMERIC(6,2)    NOT NULL,
    risk_reward_target_used     NUMERIC(6,2)    NOT NULL,
    confluences_count           SMALLINT        NOT NULL,

    CONSTRAINT fk_pre_trade_checklists_trade
        FOREIGN KEY (trade_id) REFERENCES trading.trades(id) ON DELETE CASCADE,

    CONSTRAINT fk_pre_trade_checklists_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE RESTRICT,

    -- Emotionality / SetupQuality son SMALLINT 1..5 siguiendo la escala
    -- de psicologia del trader: 1 = peor (fearful / poor), 5 = mejor
    -- (euphoric / excellent). La validacion a nivel dominio enforce el
    -- mismo rango en el VO; el CHECK es la red de seguridad a nivel DB.
    CONSTRAINT ck_pre_trade_checklists_emotionality_range
        CHECK (emotionality BETWEEN 1 AND 5),

    CONSTRAINT ck_pre_trade_checklists_setup_quality_range
        CHECK (setup_quality BETWEEN 1 AND 5),

    -- RR al ingreso >= 1.0 (mismo piso que el RiskRewardRatio VO del
    -- RiskProfile). Un RR < 1 es absurdo para un trade serio.
    CONSTRAINT ck_pre_trade_checklists_rr_at_entry_min
        CHECK (risk_reward_at_entry >= 1.0),

    -- RR target usado >= 1.0. Cuando no hay perfil activo, el handler
    -- usa 1.0 como default (ver spec scenario "No active risk profile").
    CONSTRAINT ck_pre_trade_checklists_rr_target_used_min
        CHECK (risk_reward_target_used >= 1.0),

    -- Confluencias entre 1 y 10 inclusive. Por debajo de 1 es cero-trabajo;
    -- por encima de 10 es ruido inflado.
    CONSTRAINT ck_pre_trade_checklists_confluences_range
        CHECK (confluences_count BETWEEN 1 AND 10)
);

-- Index sobre user_id: el handler de metricas futuras (Sprint 1.5B)
-- podria querer "cuantos checklists tuvo este usuario en el periodo".
-- Por ahora tambien cubre el escenario "cross-module audit" donde un
-- admin deberia poder leer el historial de checklists de un usuario.
-- (NOTA: la lectura admin NO esta expuesta en esta entrega; el indice
-- existe para no tener que recrearlo cuando llegue.)
CREATE INDEX IF NOT EXISTS ix_pre_trade_checklists_user
    ON trading.pre_trade_checklists (user_id);

-- Comentarios para el dev que abre psql.
COMMENT ON TABLE trading.pre_trade_checklists IS 'Checklist pre-trade (slice 1c.1). Una fila por trade: emotionality, setup quality, RR al ingreso, RR target usado, confluencias. El UNIQUE sobre trade_id garantiza exactly-one-checklist-per-trade; el FK a trading.trades con ON DELETE CASCADE evita orphans si se borra el trade.';
COMMENT ON COLUMN trading.pre_trade_checklists.trade_id IS 'FK a trading.trades(id) ON DELETE CASCADE. UNIQUE: cada trade tiene a lo sumo un checklist.';
COMMENT ON COLUMN trading.pre_trade_checklists.user_id IS 'FK a identity.users(id) ON DELETE RESTRICT. Borrar un usuario con checklists es operacion sensible y debe fallar ruidosamente.';
COMMENT ON COLUMN trading.pre_trade_checklists.submitted_at IS 'Timestamp de envio; default now() al persistir.';
COMMENT ON COLUMN trading.pre_trade_checklists.emotionality IS 'SMALLINT 1..5: 1=Fearful, 2=Anxious, 3=Neutral, 4=Confident, 5=Euphoric (escala conventional trader psychology, 1=peor).';
COMMENT ON COLUMN trading.pre_trade_checklists.setup_quality IS 'SMALLINT 1..5: 1=Poor, 2=BelowAverage, 3=Average, 4=Good, 5=Excellent.';
COMMENT ON COLUMN trading.pre_trade_checklists.risk_reward_at_entry IS 'NUMERIC(6,2) >= 1.0. RR esperado al ingreso del trade. Debe ser >= risk_reward_target_used (la validacion semantica la hace el dominio; este CHECK enforce solo el piso).';
COMMENT ON COLUMN trading.pre_trade_checklists.risk_reward_target_used IS 'NUMERIC(6,2) >= 1.0. Target de RR usado en este trade: snapshot del perfil activo al momento del OpenTrade, o 1.0 si no hay perfil activo.';
COMMENT ON COLUMN trading.pre_trade_checklists.confluences_count IS 'SMALLINT 1..10. Numero de confluencias que el trader identifico antes de abrir el trade.';

COMMIT;
