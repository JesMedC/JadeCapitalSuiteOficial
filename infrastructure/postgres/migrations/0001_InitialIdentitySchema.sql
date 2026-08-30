-- Migración inicial manual para Identity.
-- Generada manualmente porque el entorno actual solo tiene runtime .NET 10
-- y dotnet-ef requiere runtime .NET 8.
--
-- Tablas: identity.users, identity.refresh_tokens
-- Schema dedicado para extraer el modulo a su propio servicio en el futuro.

BEGIN;

CREATE SCHEMA IF NOT EXISTS identity;

-- ============================================
-- identity.users
-- ============================================
CREATE TABLE IF NOT EXISTS identity.users (
    id                  UUID            PRIMARY KEY,
    email               VARCHAR(320)    NOT NULL,
    display_name        VARCHAR(80)     NOT NULL,
    password_hash       VARCHAR(255)    NOT NULL,
    role                INTEGER         NOT NULL,
    status              VARCHAR(32)     NOT NULL,
    email_confirmed_at  TIMESTAMPTZ,
    last_login_at       TIMESTAMPTZ,
    failed_login_count  INTEGER         NOT NULL DEFAULT 0,
    locked_until        TIMESTAMPTZ,
    timezone            VARCHAR(64),
    created_at          TIMESTAMPTZ      NOT NULL,
    updated_at          TIMESTAMPTZ,

    CONSTRAINT ck_users_role CHECK (role IN (1, 2)),
    CONSTRAINT ck_users_status CHECK (status IN ('Active', 'Suspended', 'Cancelled', 'LockedOut'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email ON identity.users (email);
CREATE INDEX IF NOT EXISTS ix_users_status ON identity.users (status) WHERE status IN ('Active', 'LockedOut');

-- ============================================
-- identity.refresh_tokens
-- ============================================
CREATE TABLE IF NOT EXISTS identity.refresh_tokens (
    id                  UUID            PRIMARY KEY,
    user_id             UUID            NOT NULL,
    token_hash          VARCHAR(128)    NOT NULL,
    issued_at           TIMESTAMPTZ      NOT NULL,
    expires_at          TIMESTAMPTZ      NOT NULL,
    revoked_at          TIMESTAMPTZ,
    replaced_by_token_id UUID,
    created_by_ip       VARCHAR(45),
    user_agent          VARCHAR(500),
    created_at          TIMESTAMPTZ      NOT NULL,
    updated_at          TIMESTAMPTZ,

    CONSTRAINT fk_refresh_tokens_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_refresh_tokens_hash ON identity.refresh_tokens (token_hash);
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_user_active
    ON identity.refresh_tokens (user_id, revoked_at)
    WHERE revoked_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_expires
    ON identity.refresh_tokens (expires_at)
    WHERE revoked_at IS NULL;

-- Comentarios de tabla
COMMENT ON TABLE identity.users IS 'Cuentas humanas de la plataforma. Una fila por usuario.';
COMMENT ON TABLE identity.refresh_tokens IS 'Refresh tokens opacos (hasheados SHA-256). Rotacion encadenada.';

COMMIT;