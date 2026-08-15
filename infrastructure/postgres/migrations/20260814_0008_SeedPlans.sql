-- Migracion 0008 — Seed plan catalog (slice 0g housekeeping).
--
-- Slice 0e (20260813_0007_BillingSubscriptions.sql) created the plan table
-- but shipped empty. Without a seeder, an Admin cannot create any
-- subscription because fk_subscriptions_plan rejects unknown codes.
--
-- This migration inserts the three public-facing plans (starter, pro, elite)
-- at canonical price points. ON CONFLICT (code) DO NOTHING makes the
-- migration idempotent: re-running it does not blow away price edits an
-- Admin made through the catalog UI (slice 0f+).
--
-- Origen de la fecha: 2026-08-14 (slice 0g housekeeping).

BEGIN;

INSERT INTO billing.plans (id, code, name, monthly_price, monthly_price_currency, is_eligible_for_self_service, is_deprecated, created_at)
VALUES
    (gen_random_uuid(), 'starter', 'Starter',       9.99,  'USD', true, false, NOW()),
    (gen_random_uuid(), 'pro',     'Pro',          29.99,  'USD', true, false, NOW()),
    (gen_random_uuid(), 'elite',   'Elite',        99.99,  'USD', true, false, NOW())
ON CONFLICT (code) DO NOTHING;

COMMIT;
