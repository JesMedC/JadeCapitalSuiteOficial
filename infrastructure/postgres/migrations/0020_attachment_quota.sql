-- Migration 0018 — Attachment Quota + Lifecycle (slice 4d, Wave 4).
--
-- Anade al schema `identity` + `trading`:
--   * identity.users.attachment_quota_bytes BIGINT NOT NULL DEFAULT 104857600
--     Per-user quota total. Default 100 MB (104857600 = 100 * 1024 * 1024).
--     Aplica a todos los attachments uploaded del user (sum across reviews).
--   * identity.users.attachment_used_bytes BIGINT NOT NULL DEFAULT 0
--     Tracking agregado del total consumido. Wave 4d lo mantiene simple
--     (no se recompone desde trade_attachments en cada request — el
--     AttachmentLifecycleService o el ConfirmAttachmentUploadedHandler lo
--     actualizan). Si la fila llega corrupta, el handler compensa en
--     runtime con SELECT SUM(size_bytes).
--   * trading.trade_attachments — 5 columnas additive para lifecycle +
--     virus scan + thumbnail key. CHECK / DEFAULT los mantiene additive.
--   * trading.attachments_quota_audit — bitacora del daily sweep. Nullable
--     campos para distinguir skip vs error vs success.
--
-- Reglas idempotente + additive only:
--   - ADD COLUMN IF NOT EXISTS, CREATE TABLE IF NOT EXISTS.
--   - CREATE INDEX IF NOT EXISTS.
--   - Las columnas de trade_attachments son NULL / DEFAULT 0, sin backfill:
--     las rows existentes tienen expires_at NULL (nunca se vencen) hasta
--     que un Confirm las popule con confirmed_at + 90 days.
--   - El sweep index cubre WHERE is_active = true AND expires_at IS NOT NULL
--     (range scan por expires_at).

BEGIN;

-- ============================================
-- identity.users — quota columns
-- ============================================
ALTER TABLE identity.users
    ADD COLUMN IF NOT EXISTS attachment_quota_bytes BIGINT NOT NULL DEFAULT 104857600,
    ADD COLUMN IF NOT EXISTS attachment_used_bytes BIGINT NOT NULL DEFAULT 0;

-- Constrain the quota to a sane range (>= 1 MB, <= 1 GB). The application
-- also bounds it but a DB-side CHECK protects against bad hand-rolled SQL.
ALTER TABLE identity.users
    DROP CONSTRAINT IF EXISTS ck_users_attachment_quota_bounds;
ALTER TABLE identity.users
    ADD CONSTRAINT ck_users_attachment_quota_bounds
        CHECK (attachment_quota_bytes BETWEEN 1048576 AND 1073741824);

COMMENT ON COLUMN identity.users.attachment_quota_bytes IS 'Per-user attachment quota in bytes (default 100 MiB = 104857600). Set by admin or self-service tier upgrade.';
COMMENT ON COLUMN identity.users.attachment_used_bytes IS 'Aggregate bytes consumed by all uploaded attachments (is_active = true AND status = uploaded). Maintained by ConfirmAttachmentUploadedHandler + AttachmentLifecycleService.';

-- ============================================
-- trading.trade_attachments — lifecycle + scan + thumbnail + soft-delete
-- ============================================
ALTER TABLE trading.trade_attachments
    ADD COLUMN IF NOT EXISTS is_active            BOOLEAN      NOT NULL DEFAULT TRUE,
    ADD COLUMN IF NOT EXISTS thumbnail_object_key VARCHAR(255) NULL,
    ADD COLUMN IF NOT EXISTS bytes                BIGINT       NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS expires_at           TIMESTAMPTZ  NULL,
    ADD COLUMN IF NOT EXISTS virus_scanned_at     TIMESTAMPTZ  NULL,
    ADD COLUMN IF NOT EXISTS scan_result          SMALLINT     NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS swept_at             TIMESTAMPTZ  NULL;

-- Sweep index: range scan on expires_at for the daily BackgroundService.
CREATE INDEX IF NOT EXISTS ix_trade_attachments_expires_sweep
    ON trading.trade_attachments (expires_at)
    WHERE is_active = true AND expires_at IS NOT NULL;

-- Lookup by user_id for usage query (sum + count per user).
CREATE INDEX IF NOT EXISTS ix_trade_attachments_user_uploaded
    ON trading.trade_attachments (user_id)
    WHERE is_active = true AND status = 'uploaded';

COMMENT ON COLUMN trading.trade_attachments.is_active            IS 'Soft-delete flag. TRUE = attachment live; FALSE = swept by AttachmentLifecycleService or manually deleted. Default TRUE for legacy rows.';
COMMENT ON COLUMN trading.trade_attachments.thumbnail_object_key IS 'MinIO key for the cached thumbnail of image attachments (nullable; only populated for image/png|jpeg|webp).';
COMMENT ON COLUMN trading.trade_attachments.bytes                IS 'Real size in bytes of the uploaded file (snapped during Complete). Default 0 for legacy rows.';
COMMENT ON COLUMN trading.trade_attachments.expires_at           IS 'Expiration timestamp (confirmed_at + 90 days by default). NULL for legacy rows — never swept until a future Complete re-stamps them.';
COMMENT ON COLUMN trading.trade_attachments.virus_scanned_at     IS 'Timestamp of the last virus scan (always set by ConfirmAttachmentUploadedHandler via IVirusScanner).';
COMMENT ON COLUMN trading.trade_attachments.scan_result          IS 'VirusScanResult enum: 0=NotScanned, 1=Clean, 2=Infected, 3=Error.';
COMMENT ON COLUMN trading.trade_attachments.swept_at             IS 'Timestamp del soft-delete por el AttachmentLifecycleService daily sweep. NULL si nunca fue swept.';

-- ============================================
-- trading.attachments_quota_audit
-- ============================================
CREATE TABLE IF NOT EXISTS trading.attachments_quota_audit (
    id                   UUID         PRIMARY KEY,
    user_id              UUID         NOT NULL,
    ran_at               TIMESTAMPTZ  NOT NULL DEFAULT now(),
    cleaned_count        INTEGER      NOT NULL DEFAULT 0,
    cleaned_bytes        BIGINT       NOT NULL DEFAULT 0,
    remaining_count      INTEGER      NOT NULL DEFAULT 0,
    remaining_bytes      BIGINT       NOT NULL DEFAULT 0,
    skipped_reason       VARCHAR(64)  NULL,
    error_message        TEXT         NULL,

    CONSTRAINT fk_attachments_quota_audit_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE CASCADE,

    CONSTRAINT ck_attachments_quota_audit_counts_nonneg
        CHECK (cleaned_count >= 0 AND remaining_count >= 0),
    CONSTRAINT ck_attachments_quota_audit_bytes_nonneg
        CHECK (cleaned_bytes >= 0 AND remaining_bytes >= 0)
);

-- Lookup by ran_at descending (debug + smoke queries).
CREATE INDEX IF NOT EXISTS ix_attachments_quota_audit_ran_at
    ON trading.attachments_quota_audit (ran_at DESC);

CREATE INDEX IF NOT EXISTS ix_attachments_quota_audit_user
    ON trading.attachments_quota_audit (user_id);

COMMENT ON TABLE trading.attachments_quota_audit IS 'Bitacora del AttachmentLifecycleService daily sweep. Una fila por user (los users sin attachments quedan sin row). skipped_reason NULL = sweep exitoso; populated = ran pero no encontro expired; error_message solo en fallos.';
COMMENT ON COLUMN trading.attachments_quota_audit.cleaned_count IS 'Cantidad de attachments soft-deleted en este sweep (expirados + MinIO objects borrados).';
COMMENT ON COLUMN trading.attachments_quota_audit.cleaned_bytes IS 'Bytes liberados en este sweep (suma del SizeBytes de las rows cleaned).';
COMMENT ON COLUMN trading.attachments_quota_audit.remaining_count IS 'Cantidad de attachments uploaded activos al final del sweep.';
COMMENT ON COLUMN trading.attachments_quota_audit.remaining_bytes IS 'Bytes usados al final del sweep.';
COMMENT ON COLUMN trading.attachments_quota_audit.skipped_reason IS 'Razon textual del skip (e.g. "no_expired", "minio_transient"). NULL cuando el sweep proceso expired items.';
COMMENT ON COLUMN trading.attachments_quota_audit.error_message IS 'Mensaje de error cuando el sweep fallo (Transient MinIO error, DB timeout, etc.).';

COMMIT;