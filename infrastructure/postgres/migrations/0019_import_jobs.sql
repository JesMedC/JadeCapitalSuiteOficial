-- Migration 0019 — Trader Import Jobs (slice 5a.1, Wave 5).
--
-- Anade al schema `trading`:
--   * trading.import_jobs — bitacora de uploads de historial de trades.
--     Una fila por intento de import (POST /api/imports/csv). El aggregate
--     ImportJob expone estado observable (Pending/InProgress/Completed/Failed)
--     + counters (rows_imported, rows_skipped, rows_errored).
--   * SHA-256 del body completo (file_sha256) — clave de idempotencia:
--     si el trader sube el mismo archivo dos veces, el segundo BeginImport
--     devuelve 409 conflict con el existingJobId.
--
-- Reglas idempotente + additive only:
--   - CREATE TABLE IF NOT EXISTS, CREATE INDEX IF NOT EXISTS.
--   - FK cross-schema a identity.users(id) ON DELETE CASCADE (borrar el user
--     borra sus import jobs).
--   - FK a trading.accounts(id) ON DELETE RESTRICT (no se permite borrar una
--     account que tenga imports referenciandola — historicidad).
--   - CHECK file_size_bytes > 0 AND <= 10 MiB (10 * 1024 * 1024 = 10485760).
--   - CHECK format IN (0,1,2,255) — matchea ImportFormat enum.
--   - CHECK status BETWEEN 0 AND 4 — matchea ImportJobStatus enum.

BEGIN;

CREATE TABLE IF NOT EXISTS trading.import_jobs (
    id               UUID         PRIMARY KEY,
    user_id          UUID         NOT NULL,
    account_id       UUID         NOT NULL,
    format           SMALLINT     NOT NULL,
    file_name        VARCHAR(255) NOT NULL,
    file_size_bytes  BIGINT       NOT NULL,
    file_sha256      CHAR(64)     NOT NULL,
    status           SMALLINT     NOT NULL DEFAULT 0,
    rows_total       INTEGER      NOT NULL DEFAULT 0,
    rows_imported    INTEGER      NOT NULL DEFAULT 0,
    rows_skipped     INTEGER      NOT NULL DEFAULT 0,
    rows_errored     INTEGER      NOT NULL DEFAULT 0,
    error_message    VARCHAR(2000),
    started_at       TIMESTAMPTZ  NOT NULL DEFAULT now(),
    finished_at      TIMESTAMPTZ,

    CONSTRAINT fk_import_jobs_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE CASCADE,
    CONSTRAINT fk_import_jobs_account
        FOREIGN KEY (account_id) REFERENCES trading.accounts(id) ON DELETE RESTRICT,

    CONSTRAINT ck_import_jobs_size
        CHECK (file_size_bytes > 0 AND file_size_bytes <= 10485760),
    CONSTRAINT ck_import_jobs_format
        CHECK (format IN (0, 1, 2, 255)),
    CONSTRAINT ck_import_jobs_status
        CHECK (status BETWEEN 0 AND 4),
    CONSTRAINT ck_import_jobs_rows_nonneg
        CHECK (rows_total >= 0 AND rows_imported >= 0
               AND rows_skipped >= 0 AND rows_errored >= 0)
);

-- Lookup by user + status (drives the GetImportStatus / list-by-user queries).
CREATE INDEX IF NOT EXISTS ix_import_jobs_user_status
    ON trading.import_jobs (user_id, status);

-- SHA idempotency lookup — only meaningful for active jobs (Pending/InProgress/Completed).
-- Failed jobs are excluded so the user can re-upload the same file after a failure.
CREATE INDEX IF NOT EXISTS ix_import_jobs_sha
    ON trading.import_jobs (file_sha256)
    WHERE status IN (0, 1, 2);

COMMENT ON TABLE trading.import_jobs IS 'Trade history import jobs (slice 5a.1, Wave 5). One row per import attempt; counters track rows_imported + rows_skipped + rows_errored vs rows_total.';
COMMENT ON COLUMN trading.import_jobs.format IS 'ImportFormat enum: 0=Csv, 1=Mt4, 2=Mt5, 255=Unknown.';
COMMENT ON COLUMN trading.import_jobs.status IS 'ImportJobStatus enum: 0=Pending, 1=InProgress, 2=Completed, 3=Failed, 4=Cancelled.';
COMMENT ON COLUMN trading.import_jobs.file_sha256 IS 'SHA-256 hex del body completo. Idempotency key — re-uploads con el mismo sha256 devuelven 409 conflict.';
COMMENT ON COLUMN trading.import_jobs.error_message IS 'Populated when status = Failed (3). NULL para Pending/InProgress/Completed.';
COMMENT ON COLUMN trading.import_jobs.finished_at IS 'Populated when status transitions to a terminal state (Completed/Failed/Cancelled). NULL mientras Pending/InProgress.';

COMMIT;