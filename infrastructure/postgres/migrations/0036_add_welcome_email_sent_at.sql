-- ============================================================================
-- Migration 0036 — Add `welcome_email_sent_at` column (Wave 11 slice 11.3)
--
-- <para>
-- GDPR Art. 7 consent + idempotent welcome-email flow preparation.
-- The column records the UTC timestamp at which the registration
-- flow sent the post-signup welcome email. Nullable because:
--   • Existing rows from before slice 11.3 have NULL → "not sent yet".
--   • RegisterUserHandler (Wave 11 slice 11.4) reads/writes this column
--     to enforce idempotent send (only send if NULL + 7-day suppression
--     on retry to absorb the "user clicked register twice" foot-gun).
-- </para>
--
-- <para>
-- Schema pinning (re-runnable via `IF NOT EXISTS`):
--   • schema: identity  (mirrors migration 0001+ convention)
--   • column: identity.users.welcome_email_sent_at TIMESTAMPTZ NULL
--   • inherits the migration history table at `identity.__ef_migrations`
-- </para>
--
-- <para>
-- Companion EF mapping: <c>UserConfiguration.WelcomeEmailSentAt</c>
-- is added in slice 11.4 (alongside the RegisterUserHandler change).
-- This migration intentionally has NO EF counterpart — the EF-side
-- wiring is bound to the slice that introduces the behavioral change
-- (sending the email), not to the schema-only addition here. Until
-- then, EF ignores the column (Postgres allows extra columns that
-- EF doesn't know about; reads are unaffected, writes only via raw SQL).
-- </para>
-- ============================================================================

ALTER TABLE identity.users
    ADD COLUMN IF NOT EXISTS welcome_email_sent_at TIMESTAMPTZ NULL;

COMMENT ON COLUMN identity.users.welcome_email_sent_at IS
    'GDPR Art. 7 consent: timestamp of the post-signup welcome email. '
    'NULL = not sent yet. Populated by RegisterUserHandler (slice 11.4) '
    'on the first successful send; the handler enforces a 7-day '
    'suppression window so accidental double-clicks do not double-send '
    'the transactional email. Nullable so legacy accounts (pre-slice '
    '11.4) remain valid.';
