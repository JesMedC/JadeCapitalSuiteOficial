-- Migracion 0012 — Post-Trade Review + Attachments (slice 1d.1 de jade-trader-os-core-portals).
--
-- Anade al schema `trading`:
--   * trading.trade_reviews — un review por trade cerrado. Captura
--     emotionality post-trade, setup tag (free-text, <= 80 chars), lessons
--     (<= 5000 chars), rating opcional (1..5), y create/update audit fields.
--     Es DEL TRADER: todo el scope de operaciones se hace por user_id.
--   * trading.trade_attachments — referencias a objetos en MinIO subidos por
--     el browser via presigned URL. Persiste object_key, content_type,
--     size_bytes (max 10MB), sha256 opcional, y status transitorio
--     (pending -> uploaded | failed).
--
-- Reglas de la migracion (idempotente, additive only):
--   - CREATE TABLE IF NOT EXISTS, CREATE INDEX IF NOT EXISTS.
--   - NO DROP, NO ALTER destructivo (rollback = DROP TABLE manual).
--   - Exactly-one-review-per-trade invariant: UNIQUE INDEX sobre trade_id
--     en trading.trade_reviews.
--   - Bucket isolation: cada object_key es UNIQUE GLOBAL (el path completo
--     incluye user_id/trade_id/attachment_id/filename, ver spec scenario
--     "Cross-user prefix attempt"). UNIQUE INDEX sobre object_key.
--   - El FK a trading.trades es ON DELETE CASCADE: borrar un trade borra
--     sus reviews y attachments (orphan rows serian basura). El FK a
--     identity.users es ON DELETE RESTRICT: borrar un usuario con reviews
--     y/o attachments es una operacion sensible que debe fallar ruidosamente.
--   - Los CHECK constraints redundan con la validacion a nivel dominio
--     (TradeReview.Create + TradeAttachment.RequestSlot). La DB es la red
--     de seguridad final contra bugos en aplicacion.
--
-- Origen de la fecha: 2026-08-15 (sprint Wave 1 / slice 1d / sub-slice 1d.1).

BEGIN;

-- ============================================
-- trading.trade_reviews
-- ============================================
CREATE TABLE IF NOT EXISTS trading.trade_reviews (
    id                  UUID            PRIMARY KEY,
    trade_id            UUID            NOT NULL UNIQUE,
    user_id             UUID            NOT NULL,
    emotionality        SMALLINT        NOT NULL,
    setup_used          VARCHAR(64),
    lessons             TEXT,
    rating              SMALLINT,
    created_at          TIMESTAMPTZ     NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ     NOT NULL DEFAULT now(),

    CONSTRAINT fk_trade_reviews_trade
        FOREIGN KEY (trade_id) REFERENCES trading.trades(id) ON DELETE CASCADE,

    CONSTRAINT fk_trade_reviews_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE RESTRICT,

    -- Emotionality del review post-trade: SMALLINT 1..5 (misma escala
    -- que el checklist pre-trade pero con valores nominales distintos:
    -- Confident | Calm | Anxious | Neutral | Tilted | Frustrated). El
    -- dominio enforce el set exacto; el CHECK enforce el piso/techo.
    CONSTRAINT ck_trade_reviews_emotionality_range
        CHECK (emotionality BETWEEN 1 AND 5),

    -- Rating opcional 1..5; NULL permitido (el trader puede dejar el review
    -- sin rating durante una sesion y volver a editarlo).
    CONSTRAINT ck_trade_reviews_rating_range
        CHECK (rating IS NULL OR rating BETWEEN 1 AND 5),

    -- Setup <= 64 chars (mas generoso que el Strategy del trade porque
    -- es un tag libre, no un formato estricto). Verificado tambien en el
    -- dominio (TradeReview.Create).
    CONSTRAINT ck_trade_reviews_setup_used_max_length
        CHECK (setup_used IS NULL OR length(setup_used) <= 64),

    -- Lessons <= 5000 chars. Texto libre, max razonable para una reflexion
    -- post-trade (mas permisivo que Notes del trade porque el review es
    -- una reflexion, no una nota rapida).
    CONSTRAINT ck_trade_reviews_lessons_max_length
        CHECK (lessons IS NULL OR length(lessons) <= 5000)
);

-- Index sobre user_id para lecturas cross-module futuras
-- (e.g. "todos los reviews de este usuario en el ultimo mes").
CREATE INDEX IF NOT EXISTS ix_trade_reviews_user
    ON trading.trade_reviews (user_id);

-- ============================================
-- trading.trade_attachments
-- ============================================
CREATE TABLE IF NOT EXISTS trading.trade_attachments (
    id                  UUID            PRIMARY KEY,
    review_id           UUID            NOT NULL,
    user_id             UUID            NOT NULL,
    object_key          VARCHAR(512)    NOT NULL UNIQUE,
    content_type        VARCHAR(127)    NOT NULL,
    size_bytes          BIGINT          NOT NULL,
    sha256              VARCHAR(64),
    status              VARCHAR(16)     NOT NULL DEFAULT 'pending',
    created_at          TIMESTAMPTZ     NOT NULL DEFAULT now(),
    uploaded_at         TIMESTAMPTZ,

    CONSTRAINT fk_trade_attachments_review
        FOREIGN KEY (review_id) REFERENCES trading.trade_reviews(id) ON DELETE CASCADE,

    CONSTRAINT fk_trade_attachments_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE RESTRICT,

    -- Status finito: pending (slot creado pero bytes no subidos aun),
    -- uploaded (bytes confirmados por Complete), failed (cliente pudo subir
    -- pero el backend rechazo por size mismatch, sha mismatch, etc.).
    CONSTRAINT ck_trade_attachments_status_enum
        CHECK (status IN ('pending', 'uploaded', 'failed')),

    -- Tamano 1 byte .. 10 MB inclusive. Limite del producto: attachments
    -- son screenshots/PDFs cortos, no backups ni videos.
    CONSTRAINT ck_trade_attachments_size_bounds
        CHECK (size_bytes > 0 AND size_bytes <= 10485760),

    -- sha256 es hex de 64 chars (256 bits). NULL permitido mientras
    -- status = 'pending'; si llega a 'uploaded' el cliente puede o no
    -- haberlo computado (opcional en el contrato).
    CONSTRAINT ck_trade_attachments_sha256_shape
        CHECK (sha256 IS NULL OR sha256 ~ '^[a-fA-F0-9]{64}$')
);

-- UNIQUE explicito sobre object_key como red de seguridad adicional a la
-- declaracion de columna UNIQUE (idempotente). El path completo
-- trading/attachments/{userId}/{tradeId}/{reviewId}/{attachmentId}/{filename}
-- es globalmente unico, asi que un INSERT duplicado fallara ruidosamente.
CREATE UNIQUE INDEX IF NOT EXISTS ux_trade_attachments_object_key
    ON trading.trade_attachments (object_key);

-- Lookup por review para listar los attachments de un review dado.
CREATE INDEX IF NOT EXISTS ix_trade_attachments_review
    ON trading.trade_attachments (review_id);

-- Comentarios.
COMMENT ON TABLE trading.trade_reviews IS 'Post-trade review (slice 1d.1). Una fila por trade; UNIQUE sobre trade_id. emotionality/setup/lessons/rating opcionales.';
COMMENT ON COLUMN trading.trade_reviews.trade_id IS 'FK a trading.trades(id) ON DELETE CASCADE. UNIQUE: exactly-one-review-per-trade.';
COMMENT ON COLUMN trading.trade_reviews.user_id IS 'FK a identity.users(id) ON DELETE RESTRICT. Borrar un usuario con reviews es operacion sensible.';
COMMENT ON COLUMN trading.trade_reviews.emotionality IS 'SMALLINT 1..5: Confident (1), Calm (2), Anxious (3), Neutral (4), Tilted (5). Ver domain enum ReviewEmotionality.';
COMMENT ON COLUMN trading.trade_reviews.setup_used IS 'Tag libre del setup (e.g. "London breakout", "NY reversal"). Trimmed, <= 64 chars.';
COMMENT ON COLUMN trading.trade_reviews.lessons IS 'Reflexion post-trade en texto libre, <= 5000 chars.';
COMMENT ON COLUMN trading.trade_reviews.rating IS 'Rating 1..5 opcional. NULL permitido — el trader puede editarlo despues.';
COMMENT ON COLUMN trading.trade_reviews.created_at IS 'Timestamp de creacion del review. UN review por trade, entonces este es el comienzo del review.';
COMMENT ON COLUMN trading.trade_reviews.updated_at IS 'Timestamp de la ultima edicion (Update). Default = created_at en INSERT.';

COMMENT ON TABLE trading.trade_attachments IS 'Attachment references para un post-trade review (slice 1d.1). Bytes viven en MinIO; este row solo persiste object_key + content_type + size + sha256.';
COMMENT ON COLUMN trading.trade_attachments.review_id IS 'FK a trading.trade_reviews(id) ON DELETE CASCADE. Borrar el review borra sus attachments.';
COMMENT ON COLUMN trading.trade_attachments.user_id IS 'FK a identity.users(id) ON DELETE RESTRICT.';
COMMENT ON COLUMN trading.trade_attachments.object_key IS 'Path completo en MinIO: trading/attachments/{userId}/{tradeId}/{reviewId}/{attachmentId}/{filename}. UNIQUE global — colision detectada al INSERT.';
COMMENT ON COLUMN trading.trade_attachments.content_type IS 'MIME type del archivo (image/png, image/jpeg, image/webp, application/pdf). Validado en el handler.';
COMMENT ON COLUMN trading.trade_attachments.size_bytes IS 'Tamano en bytes, 1..10485760 (10 MB). CHECK en DB; tamano se compara contra stat-object durante Complete.';
COMMENT ON COLUMN trading.trade_attachments.sha256 IS 'Hex SHA-256 de 64 chars. Opcional — el cliente puede computarlo o no; si llega, se verifica contra el archivo en MinIO durante Complete.';
COMMENT ON COLUMN trading.trade_attachments.status IS 'pending | uploaded | failed. Transicion pending -> uploaded es exitosa (Complete); pending -> failed ocurre si el tamano/sha no coincide.';
COMMENT ON COLUMN trading.trade_attachments.uploaded_at IS 'Timestamp del Complete exitoso. NULL mientras status = pending.';

COMMIT;
