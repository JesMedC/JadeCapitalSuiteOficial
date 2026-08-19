-- Migration 0027 — Audit events table (Wave 6, slice 6d.1).
--
-- Tabla audit.events: bitacora append-only de mutaciones sobre entidades
-- soft-deleteable. Cada fila registra una operacion Create / Update /
-- Delete / Restore contra un aggregate (EntityType + EntityId). La tabla
-- vive en un schema separado (`audit`, NO `identity`) para que el modelo
-- de seguridad la pueda tratar independientemente y para que el
-- AuditDbContext dedicado (Wave 6d.1) la pueda manipular sin contaminar
-- el IdentityDbContext principal.
--
-- El aggregate AuditEvent (Identity.Domain/Audit/AuditEvent.cs) es
-- append-only: la tabla NO tiene trigger UPDATE / DELETE en Postgres
-- porque el diseno es defense-in-depth en el lado de la aplicacion
-- (AuditDbContext es write-only, separado de IdentityDbContext).
--
-- Columnas:
--   - id (UUID PK) — Guid del aggregate AuditEvent
--   - entity_type (VARCHAR(80) NOT NULL) — nombre del tipo (ej. "ImportJob")
--   - entity_id (UUID NOT NULL) — Guid de la entidad afectada
--   - action (SMALLINT NOT NULL) — AuditAction enum: 0=Created, 1=Updated, 2=Deleted, 3=Restored
--   - tenant_id (UUID NULL) — alcance del tenant (nullable para admin cross-tenant)
--   - user_id (UUID NULL) — actor (nullable para system actors / webhooks)
--   - changes (JSONB NULL) — diff JSON {field: {before, after}} (NULL para Created)
--   - occurred_at (TIMESTAMPTZ NOT NULL DEFAULT now())
--
-- Indice ix_audit_events_entity (entity_type, entity_id) — soporta
-- "show me every audit event for this entity" (admin tooling, 6d.2).
-- Indice ix_audit_events_tenant_time (tenant_id, occurred_at DESC) —
-- soporta "list audit events by tenant, newest first" (admin view).
-- Indice ix_audit_events_user (user_id) — soporta "what did user X do".
--
-- CHECK constraint ck_audit_events_action enforces action IN (0,1,2,3) —
-- mismo rango que el enum AuditAction. Defense-in-depth: si un futuro
-- contribuidor agrega un quinto valor al enum, la migracion que ampla
-- el constraint y este CHECK van en lock-step.
--
-- Idempotente + additive only:
--   - CREATE TABLE IF NOT EXISTS, CREATE INDEX IF NOT EXISTS.
--   - Sin ALTER sobre tablas existentes; el schema audit es nuevo.
--   - Re-correr la migration en una DB que ya tiene la tabla es no-op.

BEGIN;

-- Wave 11.2a hotfix: create the audit schema BEFORE referencing
-- `audit.events` in the CREATE TABLE below. The original 0030 did NOT
-- include `CREATE SCHEMA IF NOT EXISTS audit;` — Postgres does NOT
-- implicitly create schemas on `CREATE TABLE schema.table` references.
-- The result: fresh-DB apply fails at 0030 with
-- `3F000: schema "audit" does not exist`, blocking every subsequent
-- migration that touches audit.events (0031, 0032, 0033, 0034, 0035)
-- AND every GDPR anonymizer sweep in production (the
-- `GdprAuditAnonymizer` SQL references `audit.events`).
--
-- This is a forward-only fix: existing DBs that already have the
-- schema (if any) see IF NOT EXISTS as a no-op. New DBs see the
-- schema created.
CREATE SCHEMA IF NOT EXISTS audit;

CREATE TABLE IF NOT EXISTS audit.events (
    id              UUID PRIMARY KEY,
    entity_type     VARCHAR(80) NOT NULL,
    entity_id       UUID NOT NULL,
    action          SMALLINT NOT NULL,
    tenant_id       UUID,
    user_id         UUID,
    changes         JSONB,
    occurred_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ck_audit_events_action CHECK (action IN (0, 1, 2, 3))
);

CREATE INDEX IF NOT EXISTS ix_audit_events_entity
    ON audit.events (entity_type, entity_id);

CREATE INDEX IF NOT EXISTS ix_audit_events_tenant_time
    ON audit.events (tenant_id, occurred_at DESC);

CREATE INDEX IF NOT EXISTS ix_audit_events_user
    ON audit.events (user_id);

COMMENT ON TABLE audit.events IS
    'Audit event log (Wave 6, slice 6d.1). Append-only; AuditDbContext is write-only + isolated from IdentityDbContext.';

COMMENT ON COLUMN audit.events.entity_type IS
    'Aggregate type name (e.g. "ImportJob", "Tenant"). VARCHAR(80) — matches AuditEvent.MaxEntityTypeLength.';

COMMENT ON COLUMN audit.events.entity_id IS
    'Aggregate id (Guid) being audited. Combined with entity_type, this is the audit lookup key.';

COMMENT ON COLUMN audit.events.action IS
    'AuditAction enum: 0=Created, 1=Updated, 2=Deleted, 3=Restored. CHECK constraint enforces the range.';

COMMENT ON COLUMN audit.events.tenant_id IS
    'Tenant scope. NULL for cross-tenant admin operations; the 6d.2 AuditLogger enriches from ITenantContext.Current when null.';

COMMENT ON COLUMN audit.events.user_id IS
    'Actor user id. NULL for system actors (webhook handlers, scheduled jobs).';

COMMENT ON COLUMN audit.events.changes IS
    'JSONB diff: { "field": { "before": ..., "after": ... } }. NULL for Created events.';

COMMENT ON COLUMN audit.events.occurred_at IS
    'UTC timestamp at which the aggregate created the AuditEvent row. Pinned from IClock.UtcNow in the aggregate.';

COMMIT;
