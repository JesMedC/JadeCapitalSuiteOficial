-- Migracion 0009 — Risk Profile (slice 1a.1a de jade-trader-os-core-portals).
--
-- Anade al schema `identity`:
--   * identity.risk_profiles — perfil de riesgo single-active por usuario.
--     Captura capital (NUMERIC(24,8) + moneda ISO 4217-like), drawdown maximo
--     tolerado, riesgo por operacion y target de riesgo/beneficio. Es la
--     fuente de verdad del position-size calculator y del pre-trade checklist.
--     Es DEL USUARIO: nunca del admin.
--
-- Reglas de la migracion (idempotente, additive only):
--   - CREATE TABLE IF NOT EXISTS, CREATE INDEX IF NOT EXISTS, ALTER ADD COLUMN IF NOT EXISTS.
--   - NO DROP, NO ALTER destructivo (rollback = DROP TABLE manual).
--   - Exactly-one-active invariant se enforce en DB con un UNIQUE INDEX
--     PARTIAL (user_id) WHERE is_active. Sin ese indice, dos requests
--     concurrentes del mismo usuario podrian crear perfiles activos ambos.
--   - El handler CreateOrSupersedeRiskProfileHandler hace el supersede + add
--     en una sola transaccion de EF (SaveChangesAsync una sola vez); el
--     UNIQUE PARTIAL es la red de seguridad para el caso borde de dos
--     requests pisandose en el commit.
--
-- Origen de la fecha: 2026-08-15 (sprint Wave 1 / slice 1a / sub-slice 1a.1a).

BEGIN;

-- ============================================
-- identity.risk_profiles
-- ============================================
CREATE TABLE IF NOT EXISTS identity.risk_profiles (
    id                       UUID            PRIMARY KEY,
    user_id                  UUID            NOT NULL,
    capital_amount           NUMERIC(24,8)   NOT NULL,
    capital_currency         CHAR(3)         NOT NULL,
    max_drawdown_percent     NUMERIC(5,2)    NOT NULL,
    risk_per_trade_percent   NUMERIC(5,2)    NOT NULL,
    risk_reward_target       NUMERIC(6,2)    NOT NULL,
    is_active                BOOLEAN         NOT NULL DEFAULT TRUE,
    superseded_at            TIMESTAMPTZ,
    created_at               TIMESTAMPTZ     NOT NULL DEFAULT now(),
    updated_at               TIMESTAMPTZ     NOT NULL DEFAULT now(),

    CONSTRAINT fk_risk_profiles_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE CASCADE,

    -- Capital debe ser estrictamente positivo. La cota superior es implicita
    -- en NUMERIC(24,8) (max 10^16 - 10^-8). El VO Money.MaxAmount en dominio
    -- enforce el mismo techo al persistir.
    CONSTRAINT ck_risk_profiles_capital_amount_positive
        CHECK (capital_amount > 0),

    -- Moneda canonica de 3 letras ASCII mayusculas (matches CHAR(3)).
    CONSTRAINT ck_risk_profiles_capital_currency_format
        CHECK (capital_currency ~ '^[A-Z]{3}$'),

    -- Drawdown 0-50% inclusive. El agregado enforce el mismo rango en
    -- MaxDrawdownPercent.Create(); el CHECK es la red de seguridad a nivel DB.
    CONSTRAINT ck_risk_profiles_max_drawdown_range
        CHECK (max_drawdown_percent BETWEEN 0 AND 50),

    -- Riesgo por operacion 0.01%-5.00% inclusive.
    CONSTRAINT ck_risk_profiles_risk_per_trade_range
        CHECK (risk_per_trade_percent BETWEEN 0.01 AND 5.00),

    -- RR target >= 1.0 (no aceptamos targets < 1 porque seria absurdo: un
    -- trader no va a configurar un peor-que-1:1 como objetivo).
    CONSTRAINT ck_risk_profiles_risk_reward_target_min
        CHECK (risk_reward_target >= 1.0),

    -- Si is_active es FALSE, superseded_at DEBE estar poblada. Si es TRUE,
    -- superseded_at DEBE ser NULL. Garantiza que el supersede es trazable.
    CONSTRAINT ck_risk_profiles_active_supersede_exclusive
        CHECK (
            (is_active = TRUE  AND superseded_at IS NULL) OR
            (is_active = FALSE AND superseded_at IS NOT NULL)
        )
);

-- Exactly-one-active invariant: el UNIQUE INDEX parcial sobre (user_id)
-- WHERE is_active rechaza un segundo INSERT activo antes de que la UoW
-- pueda siquiera cerrar. Esto se enforce AT THE DB; es imposible evadirlo
-- desde aplicacion saltando checks.
CREATE UNIQUE INDEX IF NOT EXISTS ux_risk_profiles_user_active
    ON identity.risk_profiles (user_id)
    WHERE is_active;

-- Lookup cross-module: el Trading module lee a traves de
-- IIdentityUserRiskProfileReader (proyeccion narrow). El indice NO-UNIQUE
-- sobre (user_id) cubre la consulta por user_id directo sin importar el
-- estado; el UNIQUE ya cubre la lectura del unico activo.
CREATE INDEX IF NOT EXISTS ix_risk_profiles_user
    ON identity.risk_profiles (user_id);

-- Comentarios de tabla + columnas para que el dev que abre psql tenga la
-- narrativa a mano sin abrir el design.md.
COMMENT ON TABLE identity.risk_profiles IS 'Perfil de riesgo single-active por usuario. Fuente de verdad del position-size calculator + pre-trade checklist. El check ck_risk_profiles_active_supersede_exclusive + el UNIQUE INDEX ux_risk_profiles_user_active garantem que cada usuario tiene a lo sumo una fila activa; cualquier supercede crea una nueva fila y marca la previa como superseded_at=NOW().';
COMMENT ON COLUMN identity.risk_profiles.capital_amount IS 'NUMERIC(24,8) — capital declarado por el trader. Check > 0; techo Money.MaxAmount (10^16).';
COMMENT ON COLUMN identity.risk_profiles.capital_currency IS 'CHAR(3) — codigo ISO 4217-like en mayusculas.';
COMMENT ON COLUMN identity.risk_profiles.max_drawdown_percent IS 'NUMERIC(5,2) — drawdown maximo tolerado, rango [0.00, 50.00]. Por encima de 50% es irresponsible por definicion.';
COMMENT ON COLUMN identity.risk_profiles.risk_per_trade_percent IS 'NUMERIC(5,2) — riesgo por operacion, rango [0.01, 5.00]. Por debajo de 0.01% es ruido; por encima de 5% es gambling.';
COMMENT ON COLUMN identity.risk_profiles.risk_reward_target IS 'NUMERIC(6,2) — target minimo de riesgo/beneficio. >= 1.0 (un trader nunca configura 0.8 como target).';
COMMENT ON COLUMN identity.risk_profiles.is_active IS 'TRUE para el perfil activo del usuario; FALSE para perfiles superseded. Invariant: TRUE ⇔ superseded_at IS NULL (ver ck_risk_profiles_active_supersede_exclusive).';
COMMENT ON COLUMN identity.risk_profiles.superseded_at IS 'Timestamp del supersede; poblado si y solo si is_active = FALSE.';

COMMIT;
