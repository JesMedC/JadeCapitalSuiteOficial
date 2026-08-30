-- Migracion 0006 — Password Recovery (slice 0a de jade-trader-os-core-portals).
--
-- Anade al schema `identity`:
--   * identity.temporary_credentials — credenciales temporales single-use,
--     con expiracion de 24h desde activacion, y CAS-style latest-generation
--     activation. Solo se conserva el HASH (nunca plaintext).
--   * identity.password_history      — historial de los cinco passwords
--     anteriores, ordenado (user, changed_at DESC). El password actual vive
--     en identity.users.password_hash.
--   * identity.users.session_version — contador monotono que se incrementa
--     en cada cambio de password; los refresh tokens emitidos contra una
--     version anterior quedan invalidados (revocacion por rotacion).
--
-- Reglas de la migracion:
--   - Idempotente: CREATE TABLE / INDEX con IF NOT EXISTS, ALTER con ADD COLUMN IF NOT EXISTS.
--   - Solo DDL aditiva: NO DROP, NO ALTER destructivo.
--   - Sin plaintext: ningun DEFAULT textual que pueda interpretarse como credencial.
--
-- Origen de la fecha: 2026-08-11 (sprint Wave 0 / slice 0a).

BEGIN;

-- ============================================
-- identity.temporary_credentials
-- ============================================
CREATE TABLE IF NOT EXISTS identity.temporary_credentials (
    id              UUID            PRIMARY KEY,
    user_id         UUID            NOT NULL,
    generation      INTEGER         NOT NULL,
    hash            VARCHAR(255)    NOT NULL,
    status          VARCHAR(16)     NOT NULL,
    activated_at    TIMESTAMPTZ,
    expires_at      TIMESTAMPTZ     NOT NULL,
    consumed_at     TIMESTAMPTZ,
    grant_jti       VARCHAR(64),
    created_at      TIMESTAMPTZ     NOT NULL,
    updated_at      TIMESTAMPTZ,

    CONSTRAINT fk_temporary_credentials_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE CASCADE,

    CONSTRAINT ck_temporary_credentials_status
        CHECK (status IN ('Pending', 'Activated', 'Consumed')),

    CONSTRAINT ck_temporary_credentials_generation_positive
        CHECK (generation > 0)
);

-- Una sola fila por (user, generation): la unica candidata a CAS-activate.
CREATE UNIQUE INDEX IF NOT EXISTS ux_temporary_credentials_user_generation
    ON identity.temporary_credentials (user_id, generation);

-- Lookup de la fila mas reciente para un usuario, ordenada por generation DESC.
-- Igual columna set que ux_temporary_credentials_user_generation pero orden inverso.
CREATE INDEX IF NOT EXISTS ix_temporary_credentials_user_generation_desc
    ON identity.temporary_credentials (user_id, generation DESC);

-- Sweep index para purge de 7 dias de credenciales consumidas/expiradas.
CREATE INDEX IF NOT EXISTS ix_temporary_credentials_expires_at
    ON identity.temporary_credentials (expires_at);

-- Latest-only persistence foundation: a lo sumo UNA fila Activated por usuario.
-- Slice 0b supersede cualquier Activated previa dentro de la misma transaccion
-- DB que reserva una nueva Pending. El indice unico parcial es el segundo nivel
-- de defensa contra una carrera entre dos requests de recuperacion concurrentes.
CREATE UNIQUE INDEX IF NOT EXISTS ux_temporary_credentials_user_active
    ON identity.temporary_credentials (user_id)
    WHERE status = 'Activated';

-- Para deployments previos que ya tengan el viejo ix_temporary_credentials_active
-- (no-unique), lo aceptamos: el nuevo ux_temporary_credentials_user_active es
-- la fuente de verdad para nuevos deployments. Un script de cleanup externo
-- puede dropear el viejo si se desea.

-- ============================================
-- identity.password_history
-- ============================================
CREATE TABLE IF NOT EXISTS identity.password_history (
    id              UUID            PRIMARY KEY,
    user_id         UUID            NOT NULL,
    hash            VARCHAR(255)    NOT NULL,
    changed_at      TIMESTAMPTZ     NOT NULL,
    created_at      TIMESTAMPTZ     NOT NULL,
    updated_at      TIMESTAMPTZ,

    CONSTRAINT fk_password_history_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE CASCADE
);

-- Lectura por usuario, mas reciente primero (changed_at DESC, id DESC).
-- El id es el tie-breaker deterministico cuando dos cambios ocurren en el mismo
-- timestamp; replica el orden del helper PasswordHistoryEntry.OrderNewestFirst
-- del dominio y la projection IReadOnlyList<PasswordHistoryEntry> del agregado User.
CREATE INDEX IF NOT EXISTS ix_password_history_user_changed_at_id_desc
    ON identity.password_history (user_id, changed_at DESC, id DESC);

-- ============================================
-- identity.users.session_version
-- ============================================
ALTER TABLE identity.users
    ADD COLUMN IF NOT EXISTS session_version INTEGER NOT NULL DEFAULT 0;

-- Sweep index para "expulsar" cualquier refresh token pre-cambio: los handlers
-- comparan el session_version del token contra el actual del usuario.
CREATE INDEX IF NOT EXISTS ix_users_session_version
    ON identity.users (session_version);

-- Comentarios de tabla
COMMENT ON TABLE identity.temporary_credentials IS 'Credenciales temporales single-use. Solo se persiste el HASH; el plaintext viaja unicamente por el canal de email seleccionado.';
COMMENT ON TABLE identity.password_history IS 'Historial de los cinco passwords anteriores por usuario. Orden deterministico (changed_at DESC, id DESC).';
COMMENT ON COLUMN identity.users.session_version IS 'Contador monotono que se incrementa en cada cambio de password; refresh tokens con session_version anterior quedan revocados.';

COMMIT;
