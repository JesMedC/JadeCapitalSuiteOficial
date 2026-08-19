-- Migracion 0007 — Recovery Supersession (slice 0c supersession addendum de jade-trader-os-core-portals).
--
-- Anade al schema `identity`:
--   * identity.temporary_credentials.superseded_at — timestamp de cuando una
--     fila Activated fue reemplazada por una nueva generacion. Permite al
--     sweeper (slice fuera de alcance de 0c) purgar filas superseded/expired
--     de forma eficiente.
--   * CHECK constraint ck_temporary_credentials_status ampliado para incluir
--     el nuevo estado 'Superseded' (idempotente: DROP IF EXISTS + ADD).
--   * ix_temporary_credentials_superseded_at — indice btree para el sweep
--     ("WHERE superseded_at IS NOT NULL AND superseded_at < now() - interval '7 days'").
--
-- Reglas de la migracion:
--   - Idempotente: ADD COLUMN IF NOT EXISTS, CREATE INDEX IF NOT EXISTS,
--     DROP CONSTRAINT IF EXISTS + ADD CONSTRAINT.
--   - Solo DDL aditiva: NO DROP TABLE, NO DROP COLUMN, NO DELETE en datos.
--   - El reemplazo del CHECK constraint broadens el allowlist (no destructive).
--
-- Origen de la fecha: 2026-08-12 (sprint Wave 0 / slice 0c).

BEGIN;

-- ============================================
-- identity.temporary_credentials.superseded_at
-- ============================================
ALTER TABLE identity.temporary_credentials
    ADD COLUMN IF NOT EXISTS superseded_at TIMESTAMPTZ;

-- El CHECK original solo permitia 'Pending', 'Activated', 'Consumed'. Ahora
-- 'Superseded' (valor 3 del enum) es valido para la fila en la transaccion
-- atomica que ForgotPasswordHandler ejecuta antes de reservar la nueva Pending.
ALTER TABLE identity.temporary_credentials
    DROP CONSTRAINT IF EXISTS ck_temporary_credentials_status;

ALTER TABLE identity.temporary_credentials
    ADD CONSTRAINT ck_temporary_credentials_status
    CHECK (status IN ('Pending', 'Activated', 'Consumed', 'Superseded'));

-- Sweep index: el background job (fuera de 0c) purga filas superseded/expired
-- despues de 7 dias. La columna se indexa sola porque siempre se filtra con
-- IS NOT NULL + comparacion de fecha.
CREATE INDEX IF NOT EXISTS ix_temporary_credentials_superseded_at
    ON identity.temporary_credentials (superseded_at)
    WHERE superseded_at IS NOT NULL;

-- Comentarios idempotentes: COMMENT ON INDEX/COLUMN no soporta IF EXISTS,
-- asi que los envolvemos en un DO que solo aplica el comentario si la
-- relacion existe. No es destructivo: el comentario solo se setea la
-- primera vez, en re-runs es un no-op silencioso.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'identity' AND table_name = 'temporary_credentials'
          AND column_name = 'superseded_at'
    ) THEN
        EXECUTE 'COMMENT ON COLUMN identity.temporary_credentials.superseded_at IS ''Timestamp de supersession atomica (Forgotten password antes de reservar una nueva Pending). NULL para filas no superseded.''';
    END IF;
    IF EXISTS (
        SELECT 1 FROM pg_indexes
        WHERE schemaname = 'identity' AND tablename = 'temporary_credentials'
          AND indexname = 'ix_temporary_credentials_superseded_at'
    ) THEN
        EXECUTE 'COMMENT ON INDEX identity.ix_temporary_credentials_superseded_at IS ''Sweep index para purga de 7 dias de filas superseded/expired (background job fuera de 0c).''';
    END IF;
END
$$;

COMMIT;