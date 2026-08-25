-- Align the fresh-database schema with fields already shipped by the User aggregate.
-- All columns are nullable so existing accounts are not assigned implied lifecycle
-- or consent history. Re-applying this migration is safe.

ALTER TABLE identity.users
    ADD COLUMN IF NOT EXISTS soft_deleted_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS accepted_terms_version VARCHAR(64) NULL,
    ADD COLUMN IF NOT EXISTS accepted_privacy_version VARCHAR(64) NULL,
    ADD COLUMN IF NOT EXISTS accepted_at TIMESTAMPTZ NULL;

COMMENT ON COLUMN identity.users.soft_deleted_at IS
    'UTC timestamp at which the GDPR soft-delete cascade completed.';
COMMENT ON COLUMN identity.users.accepted_terms_version IS
    'Terms-of-Service version accepted during registration.';
COMMENT ON COLUMN identity.users.accepted_privacy_version IS
    'Privacy Policy version accepted during registration.';
COMMENT ON COLUMN identity.users.accepted_at IS
    'UTC timestamp at which the versioned registration policies were accepted.';
