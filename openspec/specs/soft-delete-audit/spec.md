# Soft-Delete and Audit Specification

## Purpose

Introduce soft-delete + audit log across all user-owned entities. Every entity that can be deleted MUST implement `ISoftDelete` (with `IsDeleted`, `DeletedAt`, `DeletedBy` properties) and every mutation MUST emit an `AuditEvent` (`entity_type`, `entity_id`, `action`, `tenant_id`, `user_id`, `changes JSONB`, `timestamp`). The audit log is append-only and lives in `audit.events` (separate schema, separate DbContext to prevent accidental UPDATE/DELETE).

This spec covers the `ISoftDelete` interface, the EF global query filter, the `AuditEvent` aggregate, the `IAuditLogger` interface, the `DecoratedRepository<T>` decorator pattern, and the soft-delete exception contract. It does NOT cover audit log retention/auto-purge (Wave 7), audit log query UI (Wave 7), or audit log export (Wave 8).

## Requirements

### Requirement: ISoftDelete interface

The system MUST define `ISoftDelete` in `Shared.Kernel.SoftDelete` with three properties: `bool IsDeleted`, `DateTimeOffset? DeletedAtUtc`, `Guid? DeletedByUserId`. Every entity that can be deleted MUST implement this interface. Each entity's EF configuration MUST declare `b.HasQueryFilter(e => !e.IsDeleted)`.

#### Scenario: Soft-delete sets fields

- GIVEN an entity implementing `ISoftDelete` with `IsDeleted = false`
- WHEN `entity.MarkDeleted(userId, clock)` is called
- THEN `IsDeleted` MUST be `true`
- AND `DeletedAtUtc` MUST be `clock.UtcNow`
- AND `DeletedByUserId` MUST be `userId`

#### Scenario: Re-delete is no-op

- GIVEN an entity already soft-deleted
- WHEN `MarkDeleted(userId, clock)` is called again
- THEN the call MUST be a no-op
- AND the existing `DeletedAtUtc` and `DeletedByUserId` MUST be preserved

#### Scenario: Restore clears fields

- GIVEN an entity that is soft-deleted
- WHEN `entity.Restore()` is called
- THEN `IsDeleted` MUST be `false`
- AND `DeletedAtUtc` MUST be `null`
- AND `DeletedByUserId` MUST be `null`

### Requirement: EF global query filter

Every entity implementing `ISoftDelete` MUST have `b.HasQueryFilter(e => !e.IsDeleted)` in its `IEntityTypeConfiguration`. The filter MUST be applied to all queries (read, count, list) but MUST be omittable via `IgnoreQueryFilters()` in test fixtures.

#### Scenario: Default query excludes soft-deleted

- GIVEN 5 entities, 2 of which are soft-deleted
- WHEN the repository calls `GetByIdAsync(id)` for a soft-deleted entity
- THEN the result MUST be `null` (the filter hides it)

#### Scenario: List query excludes soft-deleted

- GIVEN 10 entities, 3 of which are soft-deleted
- WHEN the repository calls `ListAsync()`
- THEN the result MUST be 7 entities (only non-deleted)

#### Scenario: IgnoreQueryFilters bypasses

- GIVEN 5 entities, 2 of which are soft-deleted
- WHEN a test calls `.IgnoreQueryFilters().ToList()`
- THEN the result MUST be 5 entities (all of them)

#### Scenario: Count excludes soft-deleted

- GIVEN 10 entities, 3 soft-deleted
- WHEN the repository calls `CountAsync()`
- THEN the result MUST be 7

### Requirement: Soft-delete exception

When a user requests a soft-deleted entity via the API, the endpoint MUST return 404 with `error.code = "entity.not_found"`. The audit log MUST record the attempt with `FoundDeleted = true` (in the `changes` JSON) for compliance.

#### Scenario: GET soft-deleted entity

- GIVEN entity E1 is soft-deleted
- WHEN the user calls `GET /api/entities/E1`
- THEN the endpoint MUST return 404
- AND the `audit.events` row MUST include `entity_type = "entity"`, `entity_id = "E1"`, `action = "Deleted"`, `changes = { "IsDeleted": { "before": false, "after": true } }`

#### Scenario: PATCH soft-deleted entity

- GIVEN entity E1 is soft-deleted
- WHEN the user calls `PATCH /api/entities/E1` with `{ name: "..." }`
- THEN the endpoint MUST return 404
- AND the audit log MUST record the attempt (with the proposed changes)

### Requirement: AuditEvent aggregate

The system MUST define `AuditEvent` as an `Identity.Domain.Audit` aggregate with `Id, EntityType, EntityId, Action, TenantId, UserId, ChangesJson, OccurredAt`. The aggregate is immutable after `Create` (no mutators). The EF DbContext MUST be configured to prevent UPDATE/DELETE.

#### Scenario: Create audit event

- GIVEN a valid `AuditEventEntry`
- WHEN `AuditEvent.Create(entry, clock)` is called
- THEN the result MUST be `Result.Success`
- AND the aggregate's `OccurredAt` MUST be set to `clock.UtcNow`

#### Scenario: Update rejected

- GIVEN an `AuditEvent` row exists
- WHEN a developer tries to call `db.AuditEvents.Update(event)` and `SaveChangesAsync`
- THEN the call MUST throw `InvalidOperationException("audit.events is append-only")`
- AND the row MUST NOT be modified

#### Scenario: Delete rejected

- GIVEN an `AuditEvent` row exists
- WHEN a developer tries to call `db.AuditEvents.Remove(event)` and `SaveChangesAsync`
- THEN the call MUST throw `InvalidOperationException("audit.events is append-only")`
- AND the row MUST NOT be deleted

### Requirement: AuditLogger fire-and-forget

The `IAuditLogger.LogAsync(entry, ct)` MUST catch all exceptions silently and log a warning to Serilog. The main mutation MUST NOT be rolled back if the audit write fails. The audit log is a defense layer, not a critical path.

#### Scenario: Audit write fails

- GIVEN the audit DB is down
- WHEN the handler calls `IAuditLogger.LogAsync(entry, ct)` after the main mutation
- THEN the call MUST NOT throw
- AND the main mutation MUST remain committed
- AND a Serilog warning MUST be logged with `entity_type`, `entity_id`, and the exception

#### Scenario: Audit write succeeds

- GIVEN the audit DB is up
- WHEN the handler calls `IAuditLogger.LogAsync(entry, ct)`
- THEN a row MUST be inserted in `audit.events`
- AND the call MUST return without throwing

#### Scenario: Audit entry enriched with tenant + user

- GIVEN a user U1 in tenant T1 calls `CreateTenant` (via `DecoratedRepository`)
- WHEN the audit log entry is created
- THEN the entry MUST include `tenant_id = T1`, `user_id = U1` (from `ITenantContext`)
- AND the entry MUST include `entity_type = "Tenant"`, `entity_id = newTenantId`, `action = "Created"`

### Requirement: DecoratedRepository pattern

The system MUST provide `DecoratedRepository<T>` that wraps `IRepository<T>` and emits `AuditEvent` for every Create / Update / Delete. The decorator MUST be applied via `services.Decorate<IRepository<T>, DecoratedRepository<T>>()` (Scrutor pattern). The decorator chain order is: `DecoratedRepository → TenantRepository → DbContext`.

#### Scenario: Create is audited

- GIVEN the handler calls `TenantRepository.AddAsync(newTenant, ct)`
- WHEN the call completes
- THEN a `billing.tenants` row MUST be inserted
- AND a `audit.events` row MUST be inserted with `entity_type = "Tenant"`, `action = "Created"`, `changes = null`

#### Scenario: Update is audited with diff

- GIVEN a tenant T1 with `name = "Old Name"`
- WHEN the handler updates `name = "New Name"` and calls `UpdateAsync`
- THEN the `identity.tenants` row MUST be updated
- AND a `audit.events` row MUST be inserted with `action = "Updated"`, `changes = { "name": { "before": "Old Name", "after": "New Name" } }`

#### Scenario: Multi-field update

- GIVEN a tenant T1 with `name = "A", plan = 0`
- WHEN the handler updates both `name = "B"` and `plan = 1`
- THEN the audit log MUST include both fields in the diff JSON
- AND the diff MUST order fields alphabetically

#### Scenario: Delete is audited

- GIVEN the handler calls `TenantRepository.DeleteAsync(tenant, ct)`
- WHEN the call completes
- THEN the entity MUST be soft-deleted (if `ISoftDelete`) OR hard-deleted (if not)
- AND a `audit.events` row MUST be inserted with `action = "Deleted"`

#### Scenario: GetById is NOT audited

- GIVEN the handler calls `GetByIdAsync(id, ct)`
- WHEN the call completes
- THEN NO `audit.events` row MUST be inserted (read operations are not audited)

#### Scenario: Audit failure does not roll back main mutation

- GIVEN the audit DB is down
- WHEN the handler calls `UpdateAsync(entity, ct)` (which triggers audit)
- THEN the main entity update MUST be committed
- AND a Serilog warning MUST be logged

### Requirement: Diff JSON format

The `changes` JSON in `AuditEvent.ChangesJson` MUST follow this format:

```json
{
  "fieldName": {
    "before": <oldValue>,
    "after": <newValue>
  }
}
```

Fields are sorted alphabetically. Fields that did not change MUST NOT appear in the diff. Fields with `null` before/after MUST be serialized as `null`, not omitted.

#### Scenario: Single field changed

- GIVEN a tenant's `name` changes from "A" to "B"
- WHEN the audit event is written
- THEN `changes = { "name": { "before": "A", "after": "B" } }`

#### Scenario: Field changed from null to value

- GIVEN a tenant's `display_name` was `null` and becomes "Acme"
- WHEN the audit event is written
- THEN `changes = { "display_name": { "before": null, "after": "Acme" } }`

#### Scenario: Multiple fields changed

- GIVEN a tenant's `name = "A"` and `plan = 0` both change
- WHEN the audit event is written
- THEN `changes = { "name": { "before": "A", "after": "B" }, "plan": { "before": 0, "after": 1 } }`

#### Scenario: No fields changed

- GIVEN an Update with no actual changes
- WHEN the audit event is written
- THEN the entry MUST be omitted (no audit event row inserted)

#### Scenario: Diff serialization failure

- GIVEN a field value that cannot be JSON-serialized (e.g., a circular reference)
- WHEN the diff generator runs
- THEN the diff MUST fall back to `{ "snapshot": <full entity> }`
- AND the audit event MUST still be written

### Requirement: Bulking audit events

Bulk operations (e.g., importing 1000 trades) MUST emit ONE audit event with `entity_count = 1000` and `entity_ids = [...]` (the array of 1000 ids), NOT 1000 separate audit events. This is the only deviation from the "1 event per mutation" rule.

#### Scenario: Bulk import

- GIVEN 1000 trades are imported via `TradeRepository.AddRangeAsync(trades, ct)`
- WHEN the bulk operation completes
- THEN ONE `audit.events` row MUST be inserted with `entity_type = "Trade"`, `action = "Created"`, `entity_count = 1000`, `entity_ids = [...]`

#### Scenario: Single insert

- GIVEN a single trade is added via `TradeRepository.AddAsync(trade, ct)`
- WHEN the call completes
- THEN ONE `audit.events` row MUST be inserted with `entity_type = "Trade"`, `action = "Created"`, `entity_id = tradeId`, `entity_count = 1`

### Requirement: Audit log is queryable

The audit log MUST be queryable by `entity_type + entity_id`, `tenant_id + occurred_at`, and `user_id`. The query is Admin-only (no User-facing endpoint in Wave 6).

#### Scenario: Query by entity

- GIVEN an admin queries `audit.events WHERE entity_type = 'Tenant' AND entity_id = 'T1'`
- THEN the result MUST include all mutations on T1 (Creates, Updates, Deletes), newest first

#### Scenario: Query by tenant

- GIVEN an admin queries `audit.events WHERE tenant_id = 'T1' AND occurred_at >= '2026-08-01'`
- THEN the result MUST include all mutations in tenant T1 since 2026-08-01

#### Scenario: Query by user

- GIVEN an admin queries `audit.events WHERE user_id = 'U1'`
- THEN the result MUST include all mutations performed by user U1

### Requirement: Apply decorator to existing repositories

The `DecoratedRepository<T>` MUST be applied to the following repositories in Wave 6:
- `TenantRepository`
- `ImportJobRepository`
- `SubscriptionRepository`

Other repositories (e.g., `TradeRepository`, `JournalEntryRepository`) come in Wave 7 when the migration is complete.

#### Scenario: Tenant is audited

- GIVEN `ITenantRepository` is decorated with `DecoratedRepository<Tenant>`
- WHEN the handler calls `AddAsync(tenant, ct)`
- THEN a `audit.events` row MUST be written

#### Scenario: ImportJob is audited

- GIVEN `IImportJobRepository` is decorated with `DecoratedRepository<ImportJob>`
- WHEN the handler calls `AddAsync(job, ct)`
- THEN a `audit.events` row MUST be written

#### Scenario: Subscription is audited

- GIVEN `ISubscriptionRepository` is decorated with `DecoratedRepository<Subscription>`
- WHEN the webhook handler calls `UpdateAsync(subscription, ct)`
- THEN a `audit.events` row MUST be written with `actor = "stripe-webhook"`

#### Scenario: Trade is NOT audited in Wave 6

- GIVEN `ITradeRepository` is not yet decorated
- WHEN the handler calls `AddAsync(trade, ct)`
- THEN NO audit event MUST be written (Wave 7)

## Data Model

```
audit.events
  id              UUID PK
  entity_type     VARCHAR(80) NOT NULL
  entity_id       UUID NOT NULL
  action          SMALLINT NOT NULL  -- 0=Created, 1=Updated, 2=Deleted, 3=Restored
  tenant_id       UUID
  user_id         UUID
  changes         JSONB  -- { "field": { "before": ..., "after": ... } } or null
  occurred_at     TIMESTAMPTZ NOT NULL DEFAULT now()

Constraints:
  ck_audit_events_action  CHECK (action IN (0, 1, 2, 3))

Indexes:
  ix_audit_events_entity       (entity_type, entity_id)
  ix_audit_events_tenant_time  (tenant_id, occurred_at DESC)
  ix_audit_events_user         (user_id)
```

The `audit.events` table MUST be in the `audit` schema (separate from `identity`, `billing`, `trading`). The `AuditDbContext` MUST be separate from `IdentityDbContext` to isolate the schema and prevent accidental cross-DbContext mutations.

The `AuditDbContext` MUST register a custom `SaveChangesInterceptor` that rejects UPDATE and DELETE on `AuditEvent` rows.

## Endpoints

No new endpoints in Wave 6. The audit log is write-only from the application side. Admin query endpoints are Wave 7.

## Architecture

- **Shared.Kernel/SoftDelete** — `ISoftDelete` interface.
- **Shared.Kernel/Audit** — `IAuditLogger` interface + `AuditEventEntry` record + `AuditAction` enum.
- **Identity.Domain/Audit** — `AuditEvent` aggregate + `AuditEventErrors`.
- **Identity.Infrastructure/Audit** — `AuditLogger` (uses `AuditDbContext`).
- **Identity.Infrastructure/Persistence** — `AuditDbContext` (separate DbContext) + `DecoratedRepository<T>` (generic decorator).
- **Identity.Infrastructure/Persistence/Configurations** — `AuditEventConfiguration` (EF mapping).
- **Identity.Infrastructure/SoftDelete** — `SoftDeleteCommand` + `SoftDeleteHandler<T>` (generic over `ISoftDelete`).

## Out of Scope

- Audit log retention policy (90-day auto-purge) — Wave 7.
- Audit log query UI for admins — Wave 7.
- Audit log export to S3 / Glacier — Wave 8.
- Audit log search by content (full-text on `changes` JSONB) — Wave 8.
- Audit log immutability proofs (hash chain, Merkle root) — Wave 8+.
- Restore endpoint for soft-deleted entities — Wave 7.
- Bulk-restore (Admin-level) — Wave 8.
- User-facing action history (`GET /api/audit/me`) — Wave 7.
- Audit log real-time stream (SSE/WebSocket) — Wave 8.
- Audit log GDPR compliance (right-to-be-forgotten: scrub PII from old audit events) — Wave 8.
- Audit log per-actor banning (e.g., disable audit for system actors) — Wave 8+.
- Middleware-level audit (capture every HTTP request, not just mutations) — Wave 8+.
- Database-level audit (PostgreSQL `pgaudit` extension) — Wave 8+.
- Audit log analytics (dashboard of mutations per tenant) — Wave 8+.
