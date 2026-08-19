-- ============================================================================
-- Migration 0037 — GDPR Art. 7 consent ledger (Wave 11 slice 11.4)
--
-- <para>
-- Adds 3 audit columns to identity.users that record the TOA ("tickling")
-- piece of GDPR Art. 7: the controller (Jade Capital) MUST demonstrate that
-- the data subject has given unambiguous consent to processing. The columns
-- answer "when did the user accept the ToS / Privacy Policy + from which IP?"
-- so the DSAR intake runbook can answer compliance questions during an audit.
-- </para>
--
-- <para>
-- Why NOT fold into 0036 (welcome_email_sent_at):
--   * 0036 is GDPR Art. 7 consent WHILE preparing an idempotent transactional
--     email — single column, single concern.
--   * 0037 is the "ledger" capture — ToS/Privacy/IP at registration time. The
--     5-arg User.Register factory (slice 10.5) already records the version
--     strings (`accepted_terms_version` + `accepted_privacy_version`) so this
--     migration only adds the timestamp + IP columns that the slice 11.4
--     RegisterUserCommand persists via the new AcceptTerms/AcceptPrivacy/
--     ConsentIp fields.
-- </para>
--
-- <para>
-- Column contract:
--   * terms_accepted_at    TIMESTAMPTZ NULL  — UTC offset of ToS acceptance.
--   * privacy_accepted_at  TIMESTAMPTZ NULL  — UTC offset of Privacy Policy.
--   * consent_ip           VARCHAR(45) NULL  — IPv4/IPv6 of the client at
--                                              acceptance. NULL for legacy
--                                              users (pre-slice 11.4) and
--                                              for users who accepted ToS/
--                                              Privacy before this column
--                                              existed.
-- </para>
--
-- <para>
-- Schema pinning (idempotent):
--   * schema: identity.users
--   * column types match 0036 (`welcome_email_sent_at`) so a single ALTER
--     can introduce all three columns without a backfill.
--   * defense-in-depth: ADD COLUMN IF NOT EXISTS — re-runnable against an
--     already-migrated DB.
-- </para>
--
-- <para>
-- Why no DEFAULT / NOT NULL: every existing user has NULL forever; the
-- registration flow from slice 11.4 onward writes the timestamp + IP.
-- A DEFAULT would imply a silent backfill (false consent — disallowed by
-- GDPR Art. 7).
-- </para>
-- ============================================================================

ALTER TABLE identity.users
    ADD COLUMN IF NOT EXISTS terms_accepted_at   TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS privacy_accepted_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS consent_ip          VARCHAR(45) NULL;

COMMENT ON COLUMN identity.users.terms_accepted_at IS
    'GDPR Art. 7 consent ledger: UTC offset at which the user accepted the '
    'Terms of Service during registration. NULL for legacy users (pre-slice '
    '11.4). Populated by RegisterUserHandler when AcceptTerms=true.';

COMMENT ON COLUMN identity.users.privacy_accepted_at IS
    'GDPR Art. 7 consent ledger: UTC offset at which the user accepted the '
    'Privacy Policy during registration. NULL for legacy users. Populated by '
    'RegisterUserHandler when AcceptPrivacy=true (see slice 11.4).';

COMMENT ON COLUMN identity.users.consent_ip IS
    'GDPR Art. 7 consent ledger: client IP (IPv4 or IPv6) recorded at the '
    'time the user accepted the ToS + Privacy Policy. The IP is captured '
    'from the registration POST so a DSAR can correlate a consent decision '
    'with the network identity. VARCHAR(45) covers IPv6 in canonical form '
    '(39 chars for the address + colon separators).';
