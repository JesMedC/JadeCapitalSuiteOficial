-- Migracion 0015b — Trader Alerts (slice 3b de 2026-08-18-trader-strategies-alerts-planner).
--
-- Anade al schema `trading`:
--   * trading.alerts — una fila por alerta persistida. Las alertas las emite
--     el AlertEvaluationBackgroundService (cada 5 min +/- 30s de jitter).
--     La tabla es append-mostly: el background service inserta, el trader
--     acka (UPDATE acknowledged_at = now()), y la API filtra por
--     acknowledged_at IS NULL + expires_at IS NULL OR expires_at > now().
--
-- Reglas de la migracion (idempotente, additive only):
--   - CREATE TABLE IF NOT EXISTS, CREATE INDEX IF NOT EXISTS.
--   - NO DROP, NO ALTER destructivo.
--   - Exactly-one-active-per-(user, rule, day) via PARTIAL UNIQUE INDEX
--     sobre ((created_at AT TIME ZONE 'UTC')::date). Esto enforce el dedup
--     que el spec requiere (un BackgroundService que corre cada 5 min no
--     debe duplicar la misma alerta el mismo dia para el mismo user).
--   - Cross-schema FK a identity.users(id) con ON DELETE CASCADE: borrar
--     el usuario borra todas sus alertas (las orphan rows serian basura).
--
-- Decisiones de shape (alineadas con el spec / spec.md):
--   - rule_id VARCHAR(64): el nombre de la regla (e.g. "NoTradesInDaysRule").
--   - severity SMALLINT 1..3: 1=Low, 2=Medium, 3=High. Match con el enum
--     Coaching.Severity (mismo ordinal).
--   - title VARCHAR(120), body VARCHAR(500): caps alineados con el
--     aggregate Alert.MaxTitleLength / Alert.MaxBodyLength.
--   - cta_route VARCHAR(255), cta_label VARCHAR(64): la CTA renderizada
--     por el FE (ruta interna + label corto en espanol).
--   - acknowledged_at TIMESTAMPTZ NULL: NULL = activa. UPDATE al ack.
--   - expires_at TIMESTAMPTZ NULL: NULL = sin expiry. Wave 4+ podria
--     populary auto-expirar (>30d) para bounded el tamano de la tabla.
--   - created_at TIMESTAMPTZ NOT NULL DEFAULT now(): la dedup UNIQUE INDEX
--     extrae (created_at AT TIME ZONE 'UTC')::date para el match diario.
--
-- Origen de la fecha: 2026-08-18 (sprint Wave 3 / slice 3b).

BEGIN;

-- ============================================
-- trading.alerts
-- ============================================
CREATE TABLE IF NOT EXISTS trading.alerts (
    id              UUID            PRIMARY KEY,
    user_id         UUID            NOT NULL,
    rule_id         VARCHAR(64)     NOT NULL,
    severity        SMALLINT        NOT NULL,
    title           VARCHAR(120)    NOT NULL,
    body            VARCHAR(500)    NOT NULL,
    cta_route       VARCHAR(255)    NOT NULL,
    cta_label       VARCHAR(64)     NOT NULL,
    acknowledged_at TIMESTAMPTZ     NULL,
    expires_at      TIMESTAMPTZ     NULL,
    created_at      TIMESTAMPTZ     NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ     NULL,

    -- FK cross-schema a identity.users. Borrar el usuario borra sus alerts.
    CONSTRAINT fk_alerts_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE CASCADE,

    -- Severity rango 1..3 (Low, Medium, High — match con Coaching.Severity).
    CONSTRAINT ck_alerts_severity_range
        CHECK (severity BETWEEN 1 AND 3),

    -- Longitudes maximas (defense in depth: el aggregate Alert.Create los
    -- enforce tambien, pero la DB es la red final).
    CONSTRAINT ck_alerts_title_max_length
        CHECK (length(title) <= 120),
    CONSTRAINT ck_alerts_body_max_length
        CHECK (length(body) <= 500),
    CONSTRAINT ck_alerts_cta_label_max_length
        CHECK (length(cta_label) <= 64)
);

-- Dedup: una alerta activa por (user, rule, UTC-date). Si el BackgroundService
-- dispara la misma regla dos veces en el mismo dia, la segunda insercion
-- es un no-op (el ON CONFLICT en el AlertRepository.AddAsync lo traduce a
-- "return false" para que el contador de inserts no incluya duplicados).
CREATE UNIQUE INDEX IF NOT EXISTS ux_alerts_user_rule_day
    ON trading.alerts (user_id, rule_id, ((created_at AT TIME ZONE 'UTC')::date));

-- Indice secundario para el listado principal del FE (?activeOnly=true).
-- El WHERE acknowledged_at IS NULL matchea con la regla de negocio
-- "acked alerts fuera de la lista activa".
CREATE INDEX IF NOT EXISTS ix_alerts_user_active
    ON trading.alerts (user_id) WHERE acknowledged_at IS NULL;

-- Indice secundario para la lista completa del usuario (audit trail).
CREATE INDEX IF NOT EXISTS ix_alerts_user_created
    ON trading.alerts (user_id, created_at DESC);

-- Comentarios.
COMMENT ON TABLE trading.alerts IS 'Trader alerts (slice 3b). BackgroundService inserta cada 5 min; el trader acka via PATCH /api/alerts/{id}/ack. Dedup via ux_alerts_user_rule_day sobre UTC-date.';
COMMENT ON COLUMN trading.alerts.id IS 'PK UUID.';
COMMENT ON COLUMN trading.alerts.user_id IS 'FK a identity.users(id) ON DELETE CASCADE. Borrar el usuario borra sus alerts.';
COMMENT ON COLUMN trading.alerts.rule_id IS 'RuleId del IAlertRule (e.g. NoTradesInDaysRule, DrawdownExceededRule). 1..64 chars.';
COMMENT ON COLUMN trading.alerts.severity IS 'Severity 1..3: 1=Low, 2=Medium, 3=High. Match ordinal con Coaching.Severity.';
COMMENT ON COLUMN trading.alerts.title IS 'Titulo de la alerta. <= 120 chars.';
COMMENT ON COLUMN trading.alerts.body IS 'Cuerpo de la alerta (copy PII-safe, sin amounts). <= 500 chars.';
COMMENT ON COLUMN trading.alerts.cta_route IS 'Ruta del FE para el CTA (e.g. /app/journal). <= 255 chars.';
COMMENT ON COLUMN trading.alerts.cta_label IS 'Label del CTA en espanol (e.g. Revisar). <= 64 chars.';
COMMENT ON COLUMN trading.alerts.acknowledged_at IS 'Timestamp del ack. NULL = alerta activa (visible en GET ?activeOnly=true).';
COMMENT ON COLUMN trading.alerts.expires_at IS 'Timestamp de expiracion. NULL = sin expiry. La API filtra out expired de ?activeOnly=true.';
COMMENT ON COLUMN trading.alerts.created_at IS 'Timestamp de insercion. Default now(). Usado por el dedup UNIQUE INDEX.';
COMMENT ON COLUMN trading.alerts.updated_at IS 'Timestamp de la ultima modificacion (e.g. ack). NULL para alertas nunca modificadas.';

COMMIT;