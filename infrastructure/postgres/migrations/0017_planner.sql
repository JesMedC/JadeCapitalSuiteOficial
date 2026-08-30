-- Migracion 0015c — Trader Planner (slice 3c de 2026-08-18-trader-strategies-alerts-planner).
--
-- Anade al schema `trading`:
--   * trading.planner_sessions — una fila por sesion planeada por el trader.
--     Representa el "cuando pienso operar" (date + planned_start_time +
--     planned_end_time + symbol opcional + notas). El handler de GET por
--     semana computa el `comparison` (closedTrades on that date + followsPlan)
--     via JOIN con trading.trades; NO hay columnas adicionales en
--     planner_sessions para eso (compare on read).
--
-- Reglas de la migracion (idempotente, additive only):
--   - CREATE TABLE IF NOT EXISTS, CREATE INDEX IF NOT EXISTS.
--   - NO DROP, NO ALTER destructivo (rollback = DROP TABLE manual).
--   - Exactly-one-session-per-(user, date) via UNIQUE INDEX. El design dice
--     "move semantics" — si el trader cambia la date se crea una nueva sesion,
--     no se actualiza. Esto enforce el shape "una sesion por dia".
--   - Cross-schema FK a identity.users(id) con ON DELETE CASCADE: borrar el
--     usuario borra todas sus sesiones planeadas (orphan rows serian basura).
--   - Cross-schema FK soft a trading.instruments(code): ON DELETE SET NULL
--     porque un instrumento es un recurso global (no por user). Si Wave 4+
--     borra un instrumento, las sesiones planeadas con ese symbol pierden el
--     link pero conservan la fila.
--   - Status SMALLINT 1..4 (Planned=1, Completed=2, Skipped=3, Cancelled=4).
--     La spec inicial mencionaba 0..3; alineamos el rango con el enum
--     PlannerStatus del dominio (1..4) para evitar confusion entre wire y DB.
--   - planned_start_time y planned_end_time son TIME (sin TZ — el trader
--     significa "10:00 hora local").
--
-- Origen de la fecha: 2026-08-18 (sprint Wave 3 / slice 3c).

BEGIN;

-- ============================================
-- trading.planner_sessions
-- ============================================
CREATE TABLE IF NOT EXISTS trading.planner_sessions (
    id                   UUID         PRIMARY KEY,
    user_id              UUID         NOT NULL,
    session_date         DATE         NOT NULL,
    planned_start_time   TIME         NULL,
    planned_end_time     TIME         NULL,
    symbol               VARCHAR(20)  NULL,
    status               SMALLINT     NOT NULL DEFAULT 1,
    notes                VARCHAR(500) NULL,
    created_at           TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at           TIMESTAMPTZ  NOT NULL DEFAULT now(),

    -- FK cross-schema a identity.users. Borrar el usuario borra sus planner sessions.
    CONSTRAINT fk_planner_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE CASCADE,

    -- Status 1..4 (Planned, Completed, Skipped, Cancelled — match con PlannerStatus del dominio).
    CONSTRAINT ck_planner_status_range
        CHECK (status BETWEEN 1 AND 4),

    -- Defense in depth: end > start si ambos presentes. CHECK constraints en
    -- Postgres pueden referenciar multiples columnas; el aggregate tambien lo
    -- enforce, pero la DB es la red final.
    CONSTRAINT ck_planner_end_after_start
        CHECK (
            planned_start_time IS NULL
            OR planned_end_time IS NULL
            OR planned_end_time > planned_start_time
        ),

    -- Longitudes maximas (defense in depth).
    CONSTRAINT ck_planner_notes_max_length
        CHECK (notes IS NULL OR length(notes) <= 500)
);

-- Exactly-one-planner-session-per-(user, date). El "move semantics" del spec
-- se traduce a: si cambia la date, es una sesion nueva (no UPDATE de este row).
CREATE UNIQUE INDEX IF NOT EXISTS ux_planner_user_date
    ON trading.planner_sessions (user_id, session_date);

-- Indice secundario sobre (user_id, session_date) para queries por semana.
-- (Redundante con el UNIQUE pero el planner repo filtra por rango de fechas
-- y queremos que el index sea eficiente para BETWEEN.)
CREATE INDEX IF NOT EXISTS ix_planner_user_date
    ON trading.planner_sessions (user_id, session_date);

-- FK soft a trading.instruments(symbol) para validar el symbol. La migration
-- se ejecuta DESPUES de 20260806_0002_TradingSchema.sql que crea la tabla
-- trading.instruments(symbol). ON DELETE SET NULL: si el instrumento se borra,
-- las sesiones planeadas con ese symbol pierden el link pero conservan la fila.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_planner_instrument'
          AND conrelid = 'trading.planner_sessions'::regclass
    ) THEN
        ALTER TABLE trading.planner_sessions
            ADD CONSTRAINT fk_planner_instrument
                FOREIGN KEY (symbol) REFERENCES trading.instruments(symbol) ON DELETE SET NULL;
    END IF;
END $$;

-- Comentarios.
COMMENT ON TABLE trading.planner_sessions IS 'Trader planner sessions (slice 3c). Una sesion planeada por user por dia. Status: 1=Planned, 2=Completed, 3=Skipped, 4=Cancelled. El handler GET /api/planner/week computa comparison (closedTrades on date + followsPlan) on read via JOIN con trading.trades.';
COMMENT ON COLUMN trading.planner_sessions.id IS 'PK UUID.';
COMMENT ON COLUMN trading.planner_sessions.user_id IS 'FK a identity.users(id) ON DELETE CASCADE. Borrar el usuario borra sus planner sessions.';
COMMENT ON COLUMN trading.planner_sessions.session_date IS 'DATE de la sesion planeada (sin TZ). Match con LocalDate del aggregate. UNIQUE per (user_id, session_date) — move semantics: cambiar date = crear nueva sesion.';
COMMENT ON COLUMN trading.planner_sessions.planned_start_time IS 'Hora planeada de inicio (TIME, sin TZ). NULL permitido = sesion abierta sin hora definida.';
COMMENT ON COLUMN trading.planner_sessions.planned_end_time IS 'Hora planeada de fin (TIME, sin TZ). NULL permitido. CHECK constraint: si ambos presentes, end > start.';
COMMENT ON COLUMN trading.planner_sessions.symbol IS 'Simbolo del instrumento planeado (e.g. EUR/USD). FK soft a trading.instruments(symbol) ON DELETE SET NULL. NULL = sesion general (cualquier instrumento).';
COMMENT ON COLUMN trading.planner_sessions.status IS 'Status 1..4: 1=Planned, 2=Completed, 3=Skipped, 4=Cancelled. Default 1 (Planned).';
COMMENT ON COLUMN trading.planner_sessions.notes IS 'Notas libres del trader. NULL permitido. <= 500 chars.';
COMMENT ON COLUMN trading.planner_sessions.created_at IS 'Timestamp de creacion.';
COMMENT ON COLUMN trading.planner_sessions.updated_at IS 'Timestamp de la ultima modificacion.';

COMMIT;