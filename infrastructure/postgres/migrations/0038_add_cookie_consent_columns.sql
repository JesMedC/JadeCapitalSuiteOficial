-- ============================================================================
-- Migration 0038 — GDPR ePrivacy Directive cookie consent (Wave 11 slice 11.4)
--
-- <para>
-- Adds 2 columns to identity.users that record the user's COOKIE consent
-- decision under the ePrivacy Directive (2002/58/EC, art. 5(3)) + GDPR
-- Art. 6(1)(a). The cookie banner lives in the Angular frontend
-- (`shared/cookie-consent/`); the FE writes the choice to localStorage
-- (`jade.consent` key) AND POSTs to `POST /api/auth/consent` so the BE
-- has a durable audit trail. The ConsentHandler (slice 11.4) writes the
-- columns below on every successful POST.
-- </para>
--
-- <para>
-- Column contract:
--   * cookie_consent_accepted_at  TIMESTAMPTZ NULL
--         UTC offset of the most recent consent decision. NULL until the
--         user interacts with the cookie banner. Re-written on every
--         subsequent choice change (banner always shows on subsequent loads
--         until the user selects an option; the FE does NOT assume
--         silent-acceptance).
--   * cookie_consent_choice  VARCHAR(16) NULL
--         The user's chosen level. Constrained by application-layer
--         validation to one of:
--           * 'all'        — analytics + functional + essential cookies
--           * 'essential'  — essential + functional only (GDPR-strict)
--         VARCHAR(16) comfortably fits 'all' (3) + 'essential' (9) + future
--         'functional' / 'none' if needed.
-- </para>
--
-- <para>
-- Idempotency: re-POSTing the same choice is a no-op (handler returns the
-- existing row). Changing the choice ('essential' → 'all' etc.) updates
-- `cookie_consent_accepted_at` to `now()` — the column is the source of
-- truth for "when did the user last interact with the banner?" The
-- cookie banner itself reads `localStorage.jade.consent` first so it does
-- NOT re-show on subsequent loads once the user has chosen.
-- </para>
--
-- <para>
-- Defense-in-depth:
--   * ADD COLUMN IF NOT EXISTS — re-runnable.
--   * No DEFAULT — the application writes the column; legacy users keep
--     NULL until they click the cookie banner.
--   * No FK to a `cookie_consent_options` lookup table — the discriminator
--     is small and stable; the FE service + BE validator both normalise to
--     the canonical set. We could promote to a lookup if the cookie
--     taxonomy grows.
-- </para>
-- ============================================================================

ALTER TABLE identity.users
    ADD COLUMN IF NOT EXISTS cookie_consent_accepted_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS cookie_consent_choice       VARCHAR(16)  NULL;

COMMENT ON COLUMN identity.users.cookie_consent_accepted_at IS
    'GDPR ePrivacy Directive: UTC offset of the most recent cookie-consent '
    'decision. NULL until the user interacts with the cookie banner. '
    'Re-written on every successful POST /api/auth/consent (ConsentHandler, '
    'slice 11.4). Treat as the canonical "last banner interaction" timestamp.';

COMMENT ON COLUMN identity.users.cookie_consent_choice IS
    'GDPR ePrivacy Directive: the user-chosen cookie tier. NULL until the '
    'first banner interaction. Allowed values: ''all'' (analytics + functional '
    '+ essential) or ''essential'' (essential + functional only). Enforced by '
    'application-layer validation (no DB CHECK to keep the schema '
    'forward-compatible if the taxonomy grows).';
