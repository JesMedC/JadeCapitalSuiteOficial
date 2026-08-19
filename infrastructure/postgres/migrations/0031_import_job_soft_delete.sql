-- Migration 0028 — ImportJob soft-delete columns (Wave 6, slice 6d.1).
--
-- Anade 3 columnas a trading.import_jobs para soportar ISoftDelete:
--   - is_deleted (BOOLEAN NOT NULL DEFAULT false) — gate para el EF global
--     query filter (`b.HasQueryFilter(j => !j.IsDeleted)`).
--   - deleted_at (TIMESTAMPTZ NULL) — UTC timestamp de MarkDeleted.
--   - deleted_by_user_id (UUID NULL) — actor (FK logica a identity.users.id).
--
-- Las 3 columnas son NULLABLE / DEFAULT-false, por lo que la migration es
-- ADITIVA PURA — cero impacto en filas existentes. La columna is_deleted
-- arranca en `false` para todas las filas, que es el comportamiento por
-- defecto de la entidad en memoria (un ImportJob recien creado no esta
-- soft-deleted).
--
-- No se crea indice sobre is_deleted en este slice porque:
--   1. La mayoria de queries filtran por user_id + status (ya indexado).
--   2. La proporcion de filas soft-deleted se espera baja (< 5%).
--   3. Postgres puede usar un index parcial condicional si la proporcion
--      crece — Wave 7 anadira `CREATE INDEX IF NOT EXISTS
--      ix_import_jobs_live ON trading.import_jobs (user_id) WHERE NOT is_deleted`
--      si las metricas lo justifican.
--
-- FK a identity.users(id): NO se declara como FK formal porque
-- identity.users puede borrarse en cascada y queremos conservar la
-- pista de auditoria (deleted_by_user_id apunta a un user que podria
-- haber sido borrado). La integridad referencial la enforce el
-- DecoratedRepository (6d.2) en lugar del DB.
--
-- Idempotente + additive only:
--   - ALTER TABLE ... ADD COLUMN IF NOT EXISTS.
--   - Re-correr la migration en una DB que ya tiene las columnas es no-op.

BEGIN;

ALTER TABLE trading.import_jobs
    ADD COLUMN IF NOT EXISTS is_deleted BOOLEAN NOT NULL DEFAULT false;

ALTER TABLE trading.import_jobs
    ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ;

ALTER TABLE trading.import_jobs
    ADD COLUMN IF NOT EXISTS deleted_by_user_id UUID;

COMMENT ON COLUMN trading.import_jobs.is_deleted IS
    'Soft-delete gate (Wave 6, slice 6d.1). EF global query filter excludes rows where is_deleted = true.';

COMMENT ON COLUMN trading.import_jobs.deleted_at IS
    'UTC timestamp at which the ImportJob was soft-deleted (MarkDeleted). NULL when is_deleted = false.';

COMMENT ON COLUMN trading.import_jobs.deleted_by_user_id IS
    'Actor user id (FK to identity.users.id) that performed the soft-delete. NULL when is_deleted = false. No FK declared — preserved across user deletion for audit trail.';

COMMIT;
