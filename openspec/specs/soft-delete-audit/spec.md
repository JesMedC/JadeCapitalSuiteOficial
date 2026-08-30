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

The system MUST provide a typed audit decorator per `IXxxRepository` that mutates state on a user-owned aggregate, registered in its module's `*ModuleRegistration` via `services.Decorate<IXxxRepository, XxxAuditDecorator>()` (Scrutor). The decorator MUST forward `AddAsync` / `UpdateAsync` / `DeleteAsync` (or the aggregate-specific mutation method like `MarkSupersededAsync`, `SoftDeleteBatchAsync`, or `DeleteAsync(Guid)`) to a `DecoratedRepository<T>` instance and emit an `AuditEvent` with `EntityType = typeof(T).Name` (or `EntityType = "TradeAttachment"` for `AttachmentSweep` since the aggregate is the child, not the sweep), `Action` matching the mutation, `TenantId` + `UserId` from `ITenantContext`, and (for non-Create actions) a JSONB diff. Cross-tenant access MUST emit `AuditAction.Denied` and throw `UnauthorizedAccessException`. Failed `DeleteAsync` calls on repos whose aggregates have no delete path MUST emit `AuditAction.Failed` and re-throw `NotSupportedException`. Batch mutations (`SoftDeleteBatchAsync`) MUST emit one `AuditEvent` per entity in the batch, not one per batch.

Covered aggregates across all waves:
- **Wave 6 (6d.2)**: `Tenant`, `ImportJob`, `Subscription`.
- **Wave 7 (7a.1 / 7b.1 / 7b.2)**: `User`, `RiskProfile`, `Strategy`, `Trade`, `JournalEntry`.
- **Wave 8 (8a.1 / 8a.2 / 8a.3 / 8b.1)**: `Account`, `Instrument`, `Alert`, `TradeReview`, `PlannerSession`, `PreTradeChecklist`, `StripeCustomer`.
- **Wave 9 (this delta — 9a.1 / 9a.2 / 9a.3)**: `AIRiskAdvice`, `CoachingPrompt`, `ScannerFilter`, `TradeAttachment` (via `AttachmentSweep.SoftDeleteBatchAsync`). The 4 newly-decorated aggregates are user-owned; `ScannerFilter` uses `IsActive = false` via `Deactivate(IClock)` (captured as `AuditAction.Updated` with `IsActive: {before:true, after:false}` diff); `AIRiskAdvice` + `CoachingPrompt` are write-once (no `UpdateAsync` / `DeleteAsync` on the interface); `AttachmentSweep` is a NEW batch soft-delete pattern (1 audit row per id).
- **Wave 10+**: `RiskConfiguration`, `PlanVersion`, `JournalDaily`, `BacktestRun`, `StrategyVersion`, `TradeTag`, `Note`, `Mood`, `BehavioralMetric`, and any other user-owned aggregate.

(Previously: Wave 8 extended coverage to 15 user-owned aggregates and documented 2 explicit SKIPs (`ISubscriptionAdminRepository`, `IStripeWebhookEventRepository`). Wave 9 widens to 18 aggregates (15 + 4 Wave 9 + adds `TradeAttachment` via `AttachmentSweep` = 19 entity types in `audit.events`) and documents 1 explicit SKIP (`ITradeAttachmentUsageRepository` — read-only, no mutations). `ScannerFilter` is decorated via `IRepository<ScannerFilter>` extension that adds a defensive `DeleteAsync` stub (Wave 9 9a.2 — mirrors Wave 7 7a.1 `UserAuditDecorator` precedent). `AttachmentSweepAuditDecorator` is the first batch-soft-delete decorator in the codebase; the 1-call-many-audit-rows pattern is documented in `design.md`.)

#### Scenario: User is audited

- GIVEN `IUserRepository` is decorated with `UserAuditDecorator` via `services.Decorate<IUserRepository, UserAuditDecorator>()`
- WHEN the handler calls `AddAsync(user, ct)` or `UpdateAsync(user, ct)`
- THEN a `audit.events` row MUST be written with `entity_type = "User"`, `action = "Created"` or `"Updated"`, `tenant_id` + `user_id` from `ITenantContext`
- AND the diff (for `Updated`) MUST include the changed fields with `before` + `after`

#### Scenario: RiskProfile supersession is audited as Deleted

- GIVEN `IRiskProfileRepository` is decorated with `RiskProfileAuditDecorator`
- WHEN the handler calls `MarkSupersededAsync(supersededByGuid, clock, ct)`
- THEN a `audit.events` row MUST be written with `entity_type = "RiskProfile"`, `action = "Deleted"`, `changes = { "SupersededBy": { "before": null, "after": "<guid>" }, "SupersededAtUtc": { "before": null, "after": "<utcNow>" } }`

#### Scenario: Strategy deactivation is audited as Updated (not Deleted)

- GIVEN `IStrategyRepository` is decorated with `StrategyAuditDecorator`
- WHEN the handler calls `UpdateAsync(strategy, ct)` after `strategy.Deactivate()` flipped `IsActive` from `true` to `false`
- THEN a `audit.events` row MUST be written with `entity_type = "Strategy"`, `action = "Updated"`, `changes = { "IsActive": { "before": true, "after": false } }`
- AND the row MUST NOT be `action = "Deleted"` (the `DecoratedRepository<T>.IsTerminated` reflection check only upgrades on `IsDeleted == true` or `Status ∈ {Cancelled, Terminated, Expired}`)

#### Scenario: Trade deletion is audited

- GIVEN `ITradeRepository` is decorated with `TradeAuditDecorator`
- WHEN the handler calls `DeleteAsync(trade, ct)` (renamed from `RemoveAsync(Trade)`)
- THEN a `audit.events` row MUST be written with `entity_type = "Trade"`, `action = "Deleted"`, `changes = { "Status": { "before": "Open", "after": null } }` (or whichever fields the diff captures)

#### Scenario: JournalEntry deletion by Guid is audited

- GIVEN `IJournalEntryRepository` is decorated with `JournalEntryAuditDecorator`
- WHEN the handler calls `DeleteAsync(JournalEntry, ct)` (the decorator-friendly overload that internally calls `DeleteAsync(Guid, ct)`)
- THEN a `audit.events` row MUST be written with `entity_type = "JournalEntry"`, `action = "Deleted"`

#### Scenario: Cross-tenant mutation is audited as Denied

- GIVEN the typed decorator's `IsOwner` check compares `entity.UserId` (or `entity.Id` for User) to `ITenantContext.CurrentUserId`
- WHEN a handler in tenant T2 attempts to mutate an entity owned by tenant T1
- THEN a `audit.events` row MUST be written with `entity_type = "<T>"`, `action = "Denied"`, `changes = { "reason": { "before": null, "after": "cross-tenant mutation attempt" } }`
- AND the decorator MUST throw `UnauthorizedAccessException`

#### Scenario: Delete on a non-deletable aggregate is audited as Failed

- GIVEN the typed decorator's `DeleteAsync(T, ct)` method implements the full interface
- WHEN a handler calls `IUserRepository.DeleteAsync(user, ct)` (or `IStrategyRepository.DeleteAsync(strategy, ct)` or `IScannerFilterRepository.DeleteAsync(filter, ct)`)
- THEN a `audit.events` row MUST be written with `entity_type = "<T>"`, `action = "Failed"`, `changes = { "reason": { "before": null, "after": "User/Strategy/ScannerFilter deletion is not supported — use Tenant reassignment / Deactivation / IsActive=false" } }`
- AND the decorator MUST re-throw `NotSupportedException`

#### Scenario: GetById is NOT audited

- GIVEN any of the typed decorators (Wave 6 / Wave 7 / Wave 8 / Wave 9)
- WHEN the handler calls any read-only method (`GetByIdAsync`, `FindByXxxAsync`, `ListByXxxAsync`)
- THEN NO `audit.events` row MUST be inserted (read operations are not audited)

#### Scenario: Batch soft-delete emits N audit rows per id

- GIVEN `IAttachmentSweepRepository` is decorated with `AttachmentSweepAuditDecorator`
- WHEN the handler calls `SoftDeleteBatchAsync([id1, id2, id3], ct)` on 3 user-owned attachments
- THEN 3 `audit.events` rows MUST be written (one per id), each with `entity_type = "TradeAttachment"`, `action = "Deleted"`, `changes = { "IsActive": { "before": true, "after": false } }`
- AND the decorator MUST NOT collapse them into a single batch row

### Requirement: Audit decorator for User aggregate

The system MUST provide `UserAuditDecorator` at `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/UserAuditDecorator.cs` that implements `IUserRepository` and emits `AuditEvent` for every mutation. The decorator MUST be wired via `services.Decorate<IUserRepository, UserAuditDecorator>()` in `IdentityModuleRegistration`. The decorator MUST emit `AuditAction.Created` on `AddAsync`, `AuditAction.Updated` on `UpdateAsync` (with a JSONB diff of changed fields), and `AuditAction.Failed` + `NotSupportedException` on `DeleteAsync`. Read-only methods MUST forward to `_inner` without auditing. Cross-tenant access MUST emit `AuditAction.Denied` and throw `UnauthorizedAccessException`. `IUserRepository` MUST extend `IRepository<User>` (gaining `AddAsync` + `UpdateAsync` + `DeleteAsync`); the `DeleteAsync(User, ct)` STUB MUST throw `NotSupportedException` with the message `"User deletion happens via Tenant reassignment or Deactivation, not direct delete"`.

#### Scenario: User creation writes Created audit event

- GIVEN a handler calls `IUserRepository.AddAsync(newUser, ct)` with `newUser.Id` set
- WHEN the call completes
- THEN a `audit.events` row MUST be inserted with `entity_type = "User"`, `entity_id = newUser.Id`, `action = "Created"`, `tenant_id = ITenantContext.Current`, `user_id = ITenantContext.CurrentUserId`, `changes = null`
- AND the row's `occurred_at` MUST be `clock.UtcNow` at the moment of the call

#### Scenario: User update writes Updated audit event with diff

- GIVEN a user U1 with `displayName = "Old Name"` persisted in the database
- WHEN the handler updates `displayName = "New Name"` and calls `IUserRepository.UpdateAsync(u1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "User"`, `entity_id = U1.Id`, `action = "Updated"`, `changes = { "DisplayName": { "before": "Old Name", "after": "New Name" } }`
- AND the diff MUST order fields alphabetically

#### Scenario: User Cancel domain op writes Updated audit event

- GIVEN a user U1 with `status = Active`
- WHEN the handler calls `Cancel(reason, clock)` on the aggregate followed by `IUserRepository.UpdateAsync(u1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "User"`, `action = "Updated"`, `changes` reflecting the `Status` change
- AND the action MUST be `"Updated"`, NOT `"Deleted"` (User does NOT implement `ISoftDelete`)

#### Scenario: User delete attempt writes Failed audit event with NotSupported message

- GIVEN a handler calls `IUserRepository.DeleteAsync(user, ct)` despite the interface XML doc warning against it
- WHEN the call enters the decorator
- THEN a `audit.events` row MUST be inserted with `entity_type = "User"`, `action = "Failed"`, `changes = { "reason": { "before": null, "after": "User deletion is not supported — use Tenant reassignment or Deactivation" } }`
- AND the decorator MUST re-throw `NotSupportedException` with the same message

#### Scenario: User cross-tenant update is denied

- GIVEN a user U1 belongs to tenant T1, but `ITenantContext.Current = T2` and `CurrentUserId = T2User`
- WHEN the handler calls `IUserRepository.UpdateAsync(u1, ct)` from tenant T2
- THEN a `audit.events` row MUST be inserted with `entity_type = "User"`, `action = "Denied"`, `changes = { "reason": { "before": null, "after": "cross-tenant update attempt" } }`
- AND the decorator MUST throw `UnauthorizedAccessException` (NO main mutation commit)

#### Scenario: User GetById is not audited

- GIVEN a handler calls `IUserRepository.GetByIdAsync(userId, ct)` for an existing user
- WHEN the call completes
- THEN NO `audit.events` row MUST be inserted (read operations are not audited)
- AND the user MUST be returned with the persisted state

### Requirement: Audit decorator for RiskProfile aggregate

The system MUST provide `RiskProfileAuditDecorator` at `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/RiskProfileAuditDecorator.cs` that implements `IRiskProfileRepository`. The decorator MUST be wired via `services.Decorate<IRiskProfileRepository, RiskProfileAuditDecorator>()` in `IdentityModuleRegistration`. The decorator MUST emit `AuditAction.Created` on `AddAsync`, `AuditAction.Deleted` on `MarkSupersededAsync` (the supersede IS the termination — diff is `{ "SupersededBy": { "before": null, "after": "<guid>" }, "SupersededAtUtc": { "before": null, "after": "<utcNow>" } }`), and `AuditAction.Failed` + `NotSupportedException` on `DeleteAsync`. `IRiskProfileRepository` MUST extend `IRepository<RiskProfile>`; the `DeleteAsync(RiskProfile, ct)` overload is a real implementation that calls `MarkSupersededAsync(supersededByGuid, clock, ct)` internally (the canonical mutation). Cross-tenant access MUST emit `AuditAction.Denied` and throw `UnauthorizedAccessException`. The decorator wraps `MarkSupersededAsync` DIRECTLY (not through `UpdateAsync`) because `RiskProfile` has no `UpdateAsync` in its canonical mutation surface.

#### Scenario: RiskProfile creation writes Created audit event

- GIVEN a handler calls `IRiskProfileRepository.AddAsync(newProfile, ct)` with `newProfile.UserId` set to the current user
- WHEN the call completes
- THEN a `audit.events` row MUST be inserted with `entity_type = "RiskProfile"`, `entity_id = newProfile.Id`, `action = "Created"`, `tenant_id = ITenantContext.Current`, `user_id = ITenantContext.CurrentUserId`

#### Scenario: RiskProfile supersession writes Deleted audit event with supersession diff

- GIVEN a risk profile RP1 in state `IsActive = true`, `SupersededBy = null`, `SupersededAtUtc = null`
- WHEN the handler calls `IRiskProfileRepository.MarkSupersededAsync(newSupersededByGuid, clock, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "RiskProfile"`, `entity_id = RP1.Id`, `action = "Deleted"`, `changes = { "SupersededBy": { "before": null, "after": "<newSupersededByGuid>" }, "SupersededAtUtc": { "before": null, "after": "<clock.UtcNow>" } }`
- AND the row's `tenant_id` + `user_id` MUST be enriched from `ITenantContext`

#### Scenario: RiskProfile cross-tenant MarkSuperseded is denied

- GIVEN a risk profile RP1 belongs to user U1 in tenant T1, but `ITenantContext.Current = T2` and `CurrentUserId = T2User`
- WHEN the handler in T2 calls `IRiskProfileRepository.MarkSupersededAsync(someGuid, clock, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "RiskProfile"`, `action = "Denied"`, `changes = { "reason": { "before": null, "after": "cross-tenant supersede attempt" } }`
- AND the decorator MUST throw `UnauthorizedAccessException` (NO main mutation commit)

#### Scenario: RiskProfile GetActiveAsync is not audited

- GIVEN a handler calls `IRiskProfileRepository.GetActiveAsync(userId, ct)` (read-only lookup)
- WHEN the call completes
- THEN NO `audit.events` row MUST be inserted (read operations are not audited)
- AND the active profile MUST be returned (or `null` if none)

#### Scenario: RiskProfile delete attempt writes Failed audit event

- GIVEN a handler calls `IRiskProfileRepository.DeleteAsync(profile, ct)` (the overload added in Wave 7)
- WHEN the call enters the decorator
- THEN a `audit.events` row MUST be inserted with `entity_type = "RiskProfile"`, `action = "Failed"`, `changes = { "reason": { "before": null, "after": "RiskProfile supersede is canonical — use MarkSupersededAsync" } }`
- AND the decorator MUST re-throw `NotSupportedException` (the real `DeleteAsync` impl that delegates to `MarkSupersededAsync` is NOT the public path; the decorator surfaces a clean error)

### Requirement: Audit decorator for Strategy aggregate

The system MUST provide `StrategyAuditDecorator` at `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/StrategyAuditDecorator.cs` that implements `IStrategyRepository`. The decorator MUST be wired via `services.Decorate<IStrategyRepository, StrategyAuditDecorator>()` in `TradingModuleRegistration`. The decorator MUST emit `AuditAction.Created` on `AddAsync`, `AuditAction.Updated` on `UpdateAsync` (with a JSONB diff — including the `isActive: true → false` flip from `Deactivate()`), and `AuditAction.Failed` + `NotSupportedException` on `DeleteAsync`. `IStrategyRepository` MUST extend `IRepository<Strategy>`; the `DeleteAsync(Strategy, ct)` STUB MUST throw `NotSupportedException` with the message `"Strategy deletion happens via Deactivation, not direct delete"`. Cross-tenant access MUST emit `AuditAction.Denied` and throw `UnauthorizedAccessException`. The existing `DecoratedRepository<T>.IsTerminated` reflection check MUST NOT upgrade `UpdateAsync` to `Deleted` for `IsActive: true → false` (the check only fires on `IsDeleted == true` or `Status ∈ {Cancelled, Terminated, Expired}`).

#### Scenario: Strategy creation writes Created audit event

- GIVEN a handler calls `IStrategyRepository.AddAsync(newStrategy, ct)` with `newStrategy.UserId` set
- WHEN the call completes
- THEN a `audit.events` row MUST be inserted with `entity_type = "Strategy"`, `entity_id = newStrategy.Id`, `action = "Created"`, `tenant_id = ITenantContext.Current`, `user_id = ITenantContext.CurrentUserId`

#### Scenario: Strategy update writes Updated audit event with diff

- GIVEN a strategy S1 with `name = "Old Name"`, `description = "Old desc"`
- WHEN the handler updates both `name = "New Name"` and `description = "New desc"` and calls `IStrategyRepository.UpdateAsync(s1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "Strategy"`, `entity_id = S1.Id`, `action = "Updated"`, `changes = { "Name": { "before": "Old Name", "after": "New Name" }, "Description": { "before": "Old desc", "after": "New desc" } }`
- AND the diff MUST order fields alphabetically

#### Scenario: Strategy deactivation writes Updated audit event with isActive diff

- GIVEN a strategy S1 with `IsActive = true`
- WHEN the handler calls `s1.Deactivate(clock)` followed by `IStrategyRepository.UpdateAsync(s1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "Strategy"`, `entity_id = S1.Id`, `action = "Updated"` (NOT `Deleted`), `changes = { "IsActive": { "before": true, "after": false } }`
- AND the existing `IsTerminated` reflection check MUST NOT upgrade to `Deleted` (only `IsDeleted == true` or `Status ∈ {Cancelled, Terminated, Expired}` triggers the upgrade)

#### Scenario: Strategy delete attempt writes Failed audit event

- GIVEN a handler calls `IStrategyRepository.DeleteAsync(strategy, ct)` despite the interface XML doc warning
- WHEN the call enters the decorator
- THEN a `audit.events` row MUST be inserted with `entity_type = "Strategy"`, `action = "Failed"`, `changes = { "reason": { "before": null, "after": "Strategy deletion is not supported — use Deactivation" } }`
- AND the decorator MUST re-throw `NotSupportedException` with the same message

#### Scenario: Strategy cross-tenant update is denied

- GIVEN a strategy S1 belongs to user U1 in tenant T1, but `ITenantContext.Current = T2`
- WHEN the handler in T2 calls `IStrategyRepository.UpdateAsync(s1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "Strategy"`, `action = "Denied"`, `changes = { "reason": { "before": null, "after": "cross-tenant update attempt" } }`
- AND the decorator MUST throw `UnauthorizedAccessException`

### Requirement: Audit decorator for Trade aggregate

The system MUST provide `TradeAuditDecorator` at `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/TradeAuditDecorator.cs` that implements `ITradeRepository`. The decorator MUST be wired via `services.Decorate<ITradeRepository, TradeAuditDecorator>()` in `TradingModuleRegistration`. The decorator MUST emit `AuditAction.Created` on `AddAsync`, `AuditAction.Updated` on `UpdateAsync` (with a JSONB diff covering `Status` transitions like `Open → Closed`, `Open → Cancelled`, and other field changes), and `AuditAction.Deleted` on `DeleteAsync(Trade, ct)` (renamed from `RemoveAsync(Trade, ct)` — breaking change, no deprecation period). All 5 known handler call sites of `ITradeRepository.RemoveAsync` in `src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Trades/{Create,Update,Close,Cancel,Remove}*Handler.cs` MUST be updated to `DeleteAsync` in the same slice. Cross-tenant access MUST emit `AuditAction.Denied` and throw `UnauthorizedAccessException`. `ITradeRepository` MUST extend `IRepository<Trade>` (the rename is the breaking change — `RemoveAsync` MUST NOT remain in the public API; no overload, no `[Obsolete]`).

#### Scenario: Trade creation writes Created audit event

- GIVEN a handler calls `ITradeRepository.AddAsync(newTrade, ct)` with `newTrade.UserId` set
- WHEN the call completes
- THEN a `audit.events` row MUST be inserted with `entity_type = "Trade"`, `entity_id = newTrade.Id`, `action = "Created"`, `tenant_id` + `user_id` from `ITenantContext`

#### Scenario: Trade close writes Updated audit event with status diff

- GIVEN a trade T1 with `Status = Open`
- WHEN the handler updates `Status = Closed` and calls `ITradeRepository.UpdateAsync(t1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "Trade"`, `entity_id = T1.Id`, `action = "Updated"`, `changes = { "Status": { "before": "Open", "after": "Closed" } }`

#### Scenario: Trade cancel writes Updated audit event

- GIVEN a trade T1 with `Status = Open`
- WHEN the handler updates `Status = Cancelled` and calls `ITradeRepository.UpdateAsync(t1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "Trade"`, `entity_id = T1.Id`, `action = "Updated"`, `changes = { "Status": { "before": "Open", "after": "Cancelled" } }`
- AND the `IsTerminated` reflection check MUST upgrade `Updated → Deleted` (because `Status = Cancelled` matches the `Status ∈ {Cancelled, Terminated, Expired}` upgrade rule)

#### Scenario: Trade DeleteAsync writes Deleted audit event with before/after diff

- GIVEN a trade T1 with `Status = Open` (valid for hard-delete per the `ITradeRepository` contract)
- WHEN the handler calls `ITradeRepository.DeleteAsync(t1, ct)` (renamed from `RemoveAsync`)
- THEN a `audit.events` row MUST be inserted with `entity_type = "Trade"`, `entity_id = T1.Id`, `action = "Deleted"`, `changes = { "Status": { "before": "Open", "after": null } }` (or whichever fields the diff captures for hard-delete)

#### Scenario: Trade cross-tenant update is denied

- GIVEN a trade T1 belongs to user U1 in tenant T1, but `ITenantContext.Current = T2`
- WHEN the handler in T2 calls `ITradeRepository.UpdateAsync(t1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "Trade"`, `action = "Denied"`, `changes = { "reason": { "before": null, "after": "cross-tenant update attempt" } }`
- AND the decorator MUST throw `UnauthorizedAccessException`

#### Scenario: ITradeRepository.RemoveAsync no longer exists

- GIVEN Wave 7 has renamed `ITradeRepository.RemoveAsync(Trade)` to `DeleteAsync(Trade, ct)`
- WHEN a developer compiles the project after Wave 7
- THEN any source file referencing `ITradeRepository.RemoveAsync` MUST produce a compile error
- AND the only public delete method on `ITradeRepository` MUST be `DeleteAsync(Trade, ct)`

### Requirement: Audit decorator for JournalEntry aggregate

The system MUST provide `JournalEntryAuditDecorator` at `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/JournalEntryAuditDecorator.cs` that implements `IJournalEntryRepository`. The decorator MUST be wired via `services.Decorate<IJournalEntryRepository, JournalEntryAuditDecorator>()` in `TradingModuleRegistration`. The decorator MUST emit `AuditAction.Created` on `AddAsync`, `AuditAction.Updated` on `UpdateAsync` (with a JSONB diff — including `premarket_plan` and other field changes), and `AuditAction.Deleted` on `DeleteAsync(JournalEntry, ct)`. The decorator MUST have a bespoke shape because `IJournalEntryRepository.DeleteAsync(Guid, ct)` is the original signature — Wave 7 adds a `DeleteAsync(JournalEntry, ct)` overload (decorator-friendly) that internally calls `DeleteAsync(Guid, ct)` (production path). The decorator wraps the entity overload via `_decorated.DeleteAsync(entry, ct)`. Cross-tenant access MUST emit `AuditAction.Denied` and throw `UnauthorizedAccessException`. Read-only methods (`FindByIdAsync`, `ListByUserAsync`, `ListByDateRangeAsync`) MUST forward to `_inner` without auditing.

#### Scenario: JournalEntry creation writes Created audit event

- GIVEN a handler calls `IJournalEntryRepository.AddAsync(newEntry, ct)` with `newEntry.UserId` set
- WHEN the call completes
- THEN a `audit.events` row MUST be inserted with `entity_type = "JournalEntry"`, `entity_id = newEntry.Id`, `action = "Created"`, `tenant_id` + `user_id` from `ITenantContext`

#### Scenario: JournalEntry update writes Updated audit event with premarket_plan diff

- GIVEN a journal entry JE1 with `premarketPlan = "Old plan"`
- WHEN the handler updates `premarketPlan = "New plan"` and calls `IJournalEntryRepository.UpdateAsync(je1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "JournalEntry"`, `entity_id = JE1.Id`, `action = "Updated"`, `changes = { "PremarketPlan": { "before": "Old plan", "after": "New plan" } }`

#### Scenario: JournalEntry deletion by entity writes Deleted audit event

- GIVEN a journal entry JE1 with `Status = Open`
- WHEN the handler calls `IJournalEntryRepository.DeleteAsync(je1, ct)` (the new decorator-friendly overload)
- THEN a `audit.events` row MUST be inserted with `entity_type = "JournalEntry"`, `entity_id = JE1.Id`, `action = "Deleted"`
- AND the overload internally calls `DeleteAsync(je1.Id, ct)` to perform the actual delete (production path is unchanged)

#### Scenario: JournalEntry cross-tenant delete is denied

- GIVEN a journal entry JE1 belongs to user U1 in tenant T1, but `ITenantContext.Current = T2`
- WHEN the handler in T2 calls `IJournalEntryRepository.DeleteAsync(je1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "JournalEntry"`, `action = "Denied"`, `changes = { "reason": { "before": null, "after": "cross-tenant delete attempt" } }`
- AND the decorator MUST throw `UnauthorizedAccessException` (NO main mutation commit)

#### Scenario: JournalEntry FindByIdAsync is not audited

- GIVEN a handler calls `IJournalEntryRepository.FindByIdAsync(entryId, ct)` for an existing entry
- WHEN the call completes
- THEN NO `audit.events` row MUST be inserted (read operations are not audited)
- AND the entry MUST be returned with the persisted state

### Requirement: DecoratedRepository lives in Shared.Infrastructure

The generic `DecoratedRepository<T>` class MUST be located at `src/3.Shared/JadeCapital.Shared.Infrastructure/Persistence/DecoratedRepository.cs` with namespace `JadeCapital.Shared.Infrastructure.Persistence`. `Identity.Infrastructure.csproj` MUST NOT declare `DecoratedRepository<T>` as a public type (the file MUST be removed from `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/`). `Trading.Infrastructure.csproj` and `Billing.Infrastructure.csproj` MUST each add an explicit `<PackageReference Include="Scrutor" Version="4.2.2" />` (no longer transitive via the `Identity.Infrastructure` reference). `Trading.Infrastructure.csproj` and `Billing.Infrastructure.csproj` MUST drop the redundant `<ProjectReference Include="..\..\Identity\JadeCapital.Identity.Infrastructure\JadeCapital.Identity.Infrastructure.csproj" />` (the only reason for the ref was to import `JadeCapital.Identity.Infrastructure.Persistence` for the generic helper). The 3 existing typed decorators (`TenantAuditDecorator`, `ImportJobAuditDecorator`, `SubscriptionAuditDecorator`) MUST update their `using` import to `JadeCapital.Shared.Infrastructure.Persistence` instead of `JadeCapital.Identity.Infrastructure.Persistence`. The `DecoratedRepositoryTests.cs` test file MUST also update its `using` import. The 2 unused `using JadeCapital.Identity.{Application,Domain}…` imports in `DecoratedRepository.cs` MUST be removed (the helper operates on `T` via reflection; it does not depend on Identity types).

#### Scenario: DecoratedRepository moved to Shared.Infrastructure — Identity.Infrastructure no longer declares it

- GIVEN the file move in slice 7a.0 has landed
- WHEN a developer searches the codebase for `class DecoratedRepository`
- THEN the only public declaration MUST be at `src/3.Shared/JadeCapital.Shared.Infrastructure/Persistence/DecoratedRepository.cs`
- AND the file at `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/DecoratedRepository.cs` MUST NOT exist

#### Scenario: Trading.Infrastructure no longer references Identity.Infrastructure for the helper

- GIVEN slice 7a.0 has dropped the Identity ref and added Scrutor
- WHEN a developer opens `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/JadeCapital.Trading.Infrastructure.csproj`
- THEN the file MUST NOT contain a `<ProjectReference>` to `JadeCapital.Identity.Infrastructure.csproj`
- AND the file MUST contain `<PackageReference Include="Scrutor" Version="4.2.2" />`

#### Scenario: Billing.Infrastructure no longer references Identity.Infrastructure for the helper

- GIVEN slice 7a.0 has dropped the Identity ref and added Scrutor
- WHEN a developer opens `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/JadeCapital.Billing.Infrastructure.csproj`
- THEN the file MUST NOT contain a `<ProjectReference>` to `JadeCapital.Identity.Infrastructure.csproj`
- AND the file MUST contain `<PackageReference Include="Scrutor" Version="4.2.2" />`

#### Scenario: Typed decorators use the new namespace

- GIVEN the file move in slice 7a.0 has landed
- WHEN the build runs
- THEN `TenantAuditDecorator.cs` (Identity) MUST `using JadeCapital.Shared.Infrastructure.Persistence;`
- AND `ImportJobAuditDecorator.cs` (Trading) MUST `using JadeCapital.Shared.Infrastructure.Persistence;`
- AND `SubscriptionAuditDecorator.cs` (Billing) MUST `using JadeCapital.Shared.Infrastructure.Persistence;`
- AND the legacy `using JadeCapital.Identity.Infrastructure.Persistence;` MUST NOT appear in those files

#### Scenario: DecoratedRepositoryTests use the new namespace

- GIVEN the file move in slice 7a.0 has landed
- WHEN `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/DecoratedRepositoryTests.cs` is built
- THEN the file MUST `using JadeCapital.Shared.Infrastructure.Persistence;`
- AND the legacy `using JadeCapital.Identity.Infrastructure.Persistence;` MUST NOT appear

#### Scenario: 30 existing audit tests pass zero modification

- GIVEN slice 7a.0 has moved the helper and updated all `using` imports
- WHEN `dotnet test --filter "FullyQualifiedName~DecoratedRepository|TenantRepositoryIntegration|ImportJobRepositoryIntegration|SubscriptionRepositoryIntegration"` runs
- THEN all 30 existing tests MUST pass (12 `DecoratedRepositoryTests` + 5 `TenantRepositoryIntegrationTests` + 3 `ImportJobRepositoryIntegrationTests` + 3 `SubscriptionRepositoryIntegrationTests` + 7 other Audit-related tests)
- AND the test logic MUST NOT have been modified (only `using` imports changed)

### Requirement: ITradeRepository.RemoveAsync renamed to DeleteAsync

`ITradeRepository.RemoveAsync(Trade, ct)` MUST be renamed to `DeleteAsync(Trade, ct)` (matching the `IRepository<T>.DeleteAsync(T, ct)` signature). The rename is a BREAKING change — no `[Obsolete]` attribute, no overload, no deprecation period. All 5 known handler call sites in `src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Trades/{Create,Update,Close,Cancel,Remove}*Handler.cs` MUST be updated to `DeleteAsync` in the SAME slice (atomic rename on the branch — `git grep RemoveAsync` BEFORE the rename enumerates call sites; all 5 known handlers are the only call sites; no test directly calls `RemoveAsync`). After Wave 7 lands, the only public delete method on `ITradeRepository` MUST be `DeleteAsync(Trade, ct)`. The `TradeAuditDecorator` MUST forward `DeleteAsync(Trade, ct)` to `_decorated.DeleteAsync(trade, ct)` and emit `AuditAction.Deleted`.

#### Scenario: RemoveAsync signature no longer exists in the public API

- GIVEN Wave 7 has landed
- WHEN a developer runs `git grep -n "RemoveAsync" src/2.Modules/Trading/`
- THEN the only references MUST be in the slice's apply-progress document (if any) or in the git history
- AND NO `.cs` file in `src/2.Modules/Trading/` MUST reference `RemoveAsync` in active code

#### Scenario: All 5 known handler call sites updated atomically

- GIVEN slice 7b.1 has landed
- WHEN a developer runs `git grep -n "DeleteAsync" src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Trades/`
- THEN the 5 known handlers (`CreateTradeHandler`, `UpdateTradeHandler`, `CloseTradeHandler`, `CancelTradeHandler`, `RemoveTradeHandler` — or their Wave-7-renamed equivalents) MUST all call `DeleteAsync` (for the RemoveTradeHandler equivalent) or `UpdateAsync` / `AddAsync` as appropriate
- AND the compile MUST succeed with no unresolved method references

#### Scenario: Trade delete emits Deleted audit event via the renamed method

- GIVEN the rename has landed
- WHEN the handler calls `ITradeRepository.DeleteAsync(trade, ct)` (not `RemoveAsync` — the method no longer exists)
- THEN a `audit.events` row MUST be inserted with `entity_type = "Trade"`, `action = "Deleted"`, `entity_id = trade.Id`

### Requirement: DeleteAsync stubs on non-deletable repos

`IUserRepository.DeleteAsync(User, ct)`, `IStrategyRepository.DeleteAsync(Strategy, ct)`, and `IRiskProfileRepository.DeleteAsync(RiskProfile, ct)` MUST throw `NotSupportedException` with a clear message pointing to the correct path (Tenant reassignment / Deactivation / MarkSupersededAsync). The interface XML doc MUST warn handlers not to call these stubs. The `UserAuditDecorator`, `StrategyAuditDecorator`, and `RiskProfileAuditDecorator` MUST implement the full interface (including `DeleteAsync`) so the decorator pattern is consistent across all 5 typed decorators. The decorator MUST emit `AuditAction.Failed` BEFORE re-throwing `NotSupportedException` so the attempt is recorded in `audit.events` with `changes = { "reason": { "before": null, "after": "<message>" } }`. The `IRiskProfileRepository.DeleteAsync` overload is a real implementation in the underlying repo (delegates to `MarkSupersededAsync`); the decorator surfaces a `Failed` audit row when the handler calls the entity overload instead of `MarkSupersededAsync` directly.

#### Scenario: User DeleteAsync stub throws NotSupportedException with clear message

- GIVEN a handler calls `IUserRepository.DeleteAsync(user, ct)`
- WHEN the call enters the `UserAuditDecorator`
- THEN the decorator MUST emit an `AuditAction.Failed` audit event with `entity_type = "User"`, `changes = { "reason": { "before": null, "after": "User deletion is not supported — use Tenant reassignment or Deactivation" } }`
- AND the decorator MUST re-throw `NotSupportedException` with the same message
- AND the underlying `IRepository<User>.DeleteAsync` MUST NOT be invoked (the decorator short-circuits before reaching `_inner`)

#### Scenario: Strategy DeleteAsync stub throws NotSupportedException with clear message

- GIVEN a handler calls `IStrategyRepository.DeleteAsync(strategy, ct)`
- WHEN the call enters the `StrategyAuditDecorator`
- THEN the decorator MUST emit an `AuditAction.Failed` audit event with `entity_type = "Strategy"`, `changes = { "reason": { "before": null, "after": "Strategy deletion is not supported — use Deactivation" } }`
- AND the decorator MUST re-throw `NotSupportedException` with the same message

#### Scenario: RiskProfile DeleteAsync surfaces Failed audit row

- GIVEN a handler calls `IRiskProfileRepository.DeleteAsync(profile, ct)` instead of `MarkSupersededAsync`
- WHEN the call enters the `RiskProfileAuditDecorator`
- THEN the decorator MUST emit an `AuditAction.Failed` audit event with `entity_type = "RiskProfile"`, `changes = { "reason": { "before": null, "after": "RiskProfile supersede is canonical — use MarkSupersededAsync" } }`
- AND the decorator MUST re-throw `NotSupportedException` with the same message
- AND the underlying `IRepository<RiskProfile>.DeleteAsync` (which delegates to `MarkSupersededAsync`) MUST NOT be invoked (the decorator short-circuits with the clean error)

#### Scenario: Interface XML doc warns handlers

- GIVEN the Wave 7 interface surgery has landed
- WHEN a developer hovers over `IUserRepository.DeleteAsync` (or `IStrategyRepository.DeleteAsync` or `IRiskProfileRepository.DeleteAsync`) in their IDE
- THEN the XML doc MUST contain a `<exception cref="NotSupportedException">` tag AND a `<remarks>` block stating "Handlers should not call this method — use [Tenant reassignment / Deactivation / MarkSupersededAsync] instead"

### Requirement: AuditAction enum supports Denied and Failed

The `AuditAction` enum in `src/3.Shared/JadeCapital.Shared.Kernel/Audit/AuditAction.cs` MUST add two new values: `Denied = 4` (for cross-tenant access attempts) and `Failed = 5` (for `DeleteAsync` `NotSupportedException` paths). The byte values MUST be 4 and 5 respectively (extending the existing `0=Created, 1=Updated, 2=Deleted, 3=Restored` sequence without renumbering). The `audit.events.action SMALLINT` column's CHECK constraint `ck_audit_events_action CHECK (action IN (0, 1, 2, 3))` MUST be widened in a NEW migration `0029_audit_events_action_denied_failed.sql` to `CHECK (action IN (0, 1, 2, 3, 4, 5))`. The migration MUST be idempotent (`DROP CONSTRAINT IF EXISTS ck_audit_events_action; ADD CONSTRAINT ck_audit_events_action CHECK (action IN (0, 1, 2, 3, 4, 5))`). The migration MUST be wired into `infrastructure/postgres/migrations/migrate.Dockerfile` happy + retry path. The new enum values are introduced in the SAME PR that introduces the decorators that use them (slice 7a.1); the migration lands BEFORE any new typed decorator is wired (atomic — decorator without migration = runtime INSERT fails on CHECK constraint).

#### Scenario: AuditAction.Denied exists and serializes to byte 4

- GIVEN the enum extension has landed
- WHEN a developer reads `src/3.Shared/JadeCapital.Shared.Kernel/Audit/AuditAction.cs`
- THEN the enum MUST include `Denied = 4` after `Restored = 3`
- AND `((byte)AuditAction.Denied) == 4` MUST hold

#### Scenario: AuditAction.Failed exists and serializes to byte 5

- GIVEN the enum extension has landed
- WHEN a developer reads `src/3.Shared/JadeCapital.Shared.Kernel/Audit/AuditAction.cs`
- THEN the enum MUST include `Failed = 5` after `Denied = 4`
- AND `((byte)AuditAction.Failed) == 5` MUST hold

#### Scenario: Migration widens CHECK constraint to allow Denied and Failed

- GIVEN migration `0029_audit_events_action_denied_failed.sql` has landed and run on the DB
- WHEN a developer attempts to insert a `audit.events` row with `action = 4` (Denied) or `action = 5` (Failed)
- THEN the INSERT MUST succeed
- AND re-running the migration MUST be a no-op (idempotent)
- AND the previous `Created/Updated/Deleted/Restored` rows MUST remain intact (constraint replacement, not table rebuild)

#### Scenario: Denied audit event is queryable

- GIVEN a row exists in `audit.events` with `action = 4` (Denied)
- WHEN an admin queries `audit.events WHERE entity_type = 'User' AND action = 4`
- THEN the result MUST include the cross-tenant attempt with `tenant_id`, `user_id`, `changes = { "reason": { "before": null, "after": "cross-tenant update attempt" } }`, `occurred_at`

#### Scenario: Failed audit event is queryable

- GIVEN a row exists in `audit.events` with `action = 5` (Failed)
- WHEN an admin queries `audit.events WHERE entity_type = 'User' AND action = 5`
- THEN the result MUST include the `DeleteAsync` `NotSupportedException` attempt with `changes = { "reason": { "before": null, "after": "User deletion is not supported — use Tenant reassignment or Deactivation" } }`

### Requirement: IAccountRepository.RemoveAsync renamed to DeleteAsync

`IAccountRepository.RemoveAsync(Account, ct)` MUST be renamed to `DeleteAsync(Account, ct)` (matching the `IRepository<T>.DeleteAsync(T, ct)` signature in `Shared.Kernel/Repository/IRepository.cs`). The rename is a BREAKING change — no `[Obsolete]` attribute, no overload, no deprecation period. The 1 known handler call site in `src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Accounts/DeleteAccount/DeleteAccountHandler.cs:47` MUST be updated to `DeleteAsync` in the SAME slice (atomic rename on the branch). After Wave 8 lands, the only public delete method on `IAccountRepository` MUST be `DeleteAsync(Account, ct)`. `IAccountRepository` MUST extend `IRepository<Account>` (gaining `AddAsync` + `UpdateAsync` + `GetByIdAsync`); the concrete `AccountRepository.DeleteAsync(Account, ct)` MUST perform the actual DELETE (`_db.Accounts.Remove(account); await _db.SaveChangesAsync(ct);`). The `AccountAuditDecorator` MUST wrap `DeleteAsync(Account, ct)` and emit `AuditAction.Deleted` with `IsOwner` cross-tenant check on `account.UserId`.

#### Scenario: RemoveAsync signature no longer exists on IAccountRepository

- GIVEN Wave 8 has landed
- WHEN a developer runs `git grep -n "RemoveAsync" src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/Repositories/IAccountRepository.cs`
- THEN no `RemoveAsync` declaration MUST exist
- AND the only public delete method on `IAccountRepository` MUST be `DeleteAsync(Account, ct)`

#### Scenario: DeleteAsync exists on IAccountRepository with correct signature

- GIVEN Wave 8 has landed
- WHEN a developer inspects `IAccountRepository`
- THEN the interface MUST declare `Task DeleteAsync(Account account, CancellationToken ct = default)`
- AND `IAccountRepository` MUST extend `IRepository<Account>`

#### Scenario: DeleteAccountHandler updated atomically

- GIVEN slice 8a.1 has landed
- WHEN a developer runs `git grep -n "DeleteAsync" src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Accounts/DeleteAccount/DeleteAccountHandler.cs:47`
- THEN the handler MUST call `_accounts.DeleteAsync(account, ct)` (not `RemoveAsync`)
- AND the compile MUST succeed with no unresolved method references

### Requirement: IInstrumentRepository.RemoveAsync renamed to DeleteAsync

`IInstrumentRepository.RemoveAsync(Instrument, ct)` MUST be renamed to `DeleteAsync(Instrument, ct)` (matching the `IRepository<T>.DeleteAsync(T, ct)` signature). The rename is a BREAKING change — no `[Obsolete]` attribute, no overload, no deprecation period. The 1 known handler call site in `src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Instruments/DeleteInstrument/DeleteInstrumentHandler.cs:45` MUST be updated to `DeleteAsync` in the SAME slice (atomic rename on the branch). After Wave 8 lands, the only public delete method on `IInstrumentRepository` MUST be `DeleteAsync(Instrument, ct)`. `IInstrumentRepository` MUST extend `IRepository<Instrument>` (gaining `AddAsync` + `UpdateAsync` + `GetByIdAsync`); the concrete `InstrumentRepository.DeleteAsync(Instrument, ct)` MUST perform the actual DELETE. **No `IsOwner` cross-tenant check** — Instrument is a catalog entity ("NO es Aggregate Root: es una Entity compartida por todos los usuarios"); mirrors the `TenantAuditDecorator` precedent. The `InstrumentAuditDecorator` MUST emit `AuditAction.Deleted` for the delete path.

#### Scenario: RemoveAsync signature no longer exists on IInstrumentRepository

- GIVEN Wave 8 has landed
- WHEN a developer runs `git grep -n "RemoveAsync" src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/Repositories/IInstrumentRepository.cs`
- THEN no `RemoveAsync` declaration MUST exist
- AND the only public delete method on `IInstrumentRepository` MUST be `DeleteAsync(Instrument, ct)`

#### Scenario: DeleteAsync exists on IInstrumentRepository with correct signature

- GIVEN Wave 8 has landed
- WHEN a developer inspects `IInstrumentRepository`
- THEN the interface MUST declare `Task DeleteAsync(Instrument instrument, CancellationToken ct = default)`
- AND `IInstrumentRepository` MUST extend `IRepository<Instrument>`

#### Scenario: DeleteInstrumentHandler updated atomically

- GIVEN slice 8a.1 has landed
- WHEN a developer runs `git grep -n "DeleteAsync" src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Instruments/DeleteInstrument/DeleteInstrumentHandler.cs:45`
- THEN the handler MUST call `_instruments.DeleteAsync(instrument, ct)` (not `RemoveAsync`)
- AND the compile MUST succeed with no unresolved method references

### Requirement: Audit decorator for Account aggregate

The system MUST provide `AccountAuditDecorator` at `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AccountAuditDecorator.cs` that implements `IAccountRepository`. The decorator MUST be wired via `services.Decorate<IAccountRepository, AccountAuditDecorator>()` in `TradingModuleRegistration`. The decorator MUST emit `AuditAction.Created` on `AddAsync`, `AuditAction.Updated` on `UpdateAsync` (with a JSONB diff of changed fields including `DisplayName`, `AccountType`, `BrokerName`, `IsActive`), and `AuditAction.Deleted` on `DeleteAsync(Account, ct)` (renamed from `RemoveAsync`). Cross-tenant `IsOwner` check MUST compare `account.UserId` to `ITenantContext.CurrentUserId`; on mismatch, emit `AuditAction.Denied` and throw `UnauthorizedAccessException`. Read-only methods (`FindByIdAsync`, `ListByUserIdAsync`) MUST forward to `_inner` without auditing. `IAccountRepository` MUST extend `IRepository<Account>` (the `RemoveAsync → DeleteAsync` rename is the breaking change — `RemoveAsync` MUST NOT remain in the public API; no overload, no `[Obsolete]`).

#### Scenario: Account creation writes Created audit event

- GIVEN a handler calls `IAccountRepository.AddAsync(newAccount, ct)` with `newAccount.UserId` set
- WHEN the call completes
- THEN a `audit.events` row MUST be inserted with `entity_type = "Account"`, `entity_id = newAccount.Id`, `action = "Created"`, `tenant_id` + `user_id` from `ITenantContext`

#### Scenario: Account update writes Updated audit event with diff

- GIVEN an account A1 with `DisplayName = "Old"` persisted in the database
- WHEN the handler updates `DisplayName = "New"` and calls `IAccountRepository.UpdateAsync(a1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "Account"`, `entity_id = A1.Id`, `action = "Updated"`, `changes = { "DisplayName": { "before": "Old", "after": "New" } }`

#### Scenario: Account DeleteAsync writes Deleted audit event

- GIVEN an account A1 with `IsActive = true`
- WHEN the handler calls `IAccountRepository.DeleteAsync(a1, ct)` (the renamed method, NOT `RemoveAsync`)
- THEN a `audit.events` row MUST be inserted with `entity_type = "Account"`, `entity_id = A1.Id`, `action = "Deleted"`

#### Scenario: Account cross-tenant update is denied

- GIVEN an account A1 belongs to user U1 in tenant T1, but `ITenantContext.Current = T2`
- WHEN the handler in T2 calls `IAccountRepository.UpdateAsync(a1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "Account"`, `action = "Denied"`, `changes = { "reason": { "before": null, "after": "cross-tenant update attempt" } }`
- AND the decorator MUST throw `UnauthorizedAccessException`

#### Scenario: Account FindByIdAsync is not audited

- GIVEN a handler calls `IAccountRepository.FindByIdAsync(accountId, ct)` for an existing account
- WHEN the call completes
- THEN NO `audit.events` row MUST be inserted (read operations are not audited)

### Requirement: Audit decorator for Instrument aggregate

The system MUST provide `InstrumentAuditDecorator` at `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/InstrumentAuditDecorator.cs` that implements `IInstrumentRepository`. The decorator MUST be wired via `services.Decorate<IInstrumentRepository, InstrumentAuditDecorator>()` in `TradingModuleRegistration`. The decorator MUST emit `AuditAction.Created` on `AddAsync`, `AuditAction.Updated` on `UpdateAsync` (with a JSONB diff of changed fields including `Symbol`, `Name`, `AssetClass`, `IsActive`), and `AuditAction.Deleted` on `DeleteAsync(Instrument, ct)` (renamed from `RemoveAsync`). **`IsOwner` cross-tenant check MUST NOT be enforced** — Instrument is a catalog entity shared across all users (mirrors the `TenantAuditDecorator` precedent where Tenant IS the tenant boundary, not subject to it; Instrument is catalog data, not user-scoped). Read-only methods (`FindByIdAsync`, `FindBySymbolAsync`, `ListActiveAsync`, `ListAllAsync`) MUST forward to `_inner` without auditing. `IInstrumentRepository` MUST extend `IRepository<Instrument>`.

#### Scenario: Instrument creation writes Created audit event

- GIVEN a handler calls `IInstrumentRepository.AddAsync(newInstrument, ct)` with `newInstrument.Symbol` set
- WHEN the call completes
- THEN a `audit.events` row MUST be inserted with `entity_type = "Instrument"`, `entity_id = newInstrument.Id`, `action = "Created"`, `tenant_id` + `user_id` from `ITenantContext`

#### Scenario: Instrument update writes Updated audit event with diff

- GIVEN an instrument I1 with `Name = "Old Name"`, `IsActive = true`
- WHEN the handler updates both `Name = "New Name"` and `IsActive = false` and calls `IInstrumentRepository.UpdateAsync(i1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "Instrument"`, `entity_id = I1.Id`, `action = "Updated"`, `changes = { "IsActive": { "before": true, "after": false }, "Name": { "before": "Old Name", "after": "New Name" } }`

#### Scenario: Instrument DeleteAsync writes Deleted audit event

- GIVEN an instrument I1 with `IsActive = true`
- WHEN the handler calls `IInstrumentRepository.DeleteAsync(i1, ct)` (the renamed method)
- THEN a `audit.events` row MUST be inserted with `entity_type = "Instrument"`, `entity_id = I1.Id`, `action = "Deleted"`

#### Scenario: Instrument admin mutation is NOT cross-tenant denied

- GIVEN an instrument I1 is a catalog entity shared across all tenants
- WHEN a handler in any tenant calls `IInstrumentRepository.UpdateAsync(i1, ct)` (no `IsOwner` enforcement)
- THEN NO `AuditAction.Denied` row MUST be emitted (instrument catalog mutations are legitimate admin operations, not cross-tenant breaches)

#### Scenario: Instrument ListActiveAsync is not audited

- GIVEN a handler calls `IInstrumentRepository.ListActiveAsync(ct)` for the catalog
- WHEN the call completes
- THEN NO `audit.events` row MUST be inserted (read operations are not audited)

### Requirement: Audit decorator for Alert aggregate

The system MUST provide `AlertAuditDecorator` at `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AlertAuditDecorator.cs` that implements `IAlertRepository`. The decorator MUST be wired via `services.Decorate<IAlertRepository, AlertAuditDecorator>()` in `TradingModuleRegistration`. The decorator MUST emit `AuditAction.Created` on `AddAsync` ONLY when the inner returns `true` (row inserted), and MUST NOT emit an audit row when `AddAsync` returns `false` (row deduped by the `ux_alerts_user_rule_day` UNIQUE INDEX — the existing row's audit history is preserved). The decorator MUST emit `AuditAction.Updated` on `UpdateAsync` (with a JSONB diff covering the `AcknowledgedAt: null → now` transition after `Acknowledge()` + other field changes). Cross-tenant `IsOwner` check MUST compare `alert.UserId` to `ITenantContext.CurrentUserId`; on mismatch, emit `AuditAction.Denied` and throw `UnauthorizedAccessException`. Read-only methods (`ListByUserAsync`, `GetByIdAsync`) MUST forward to `_inner` without auditing. Alert has no `Status` enum — the `IsTerminated` reflection check does NOT apply (every mutation emits `AuditAction.Updated`).

#### Scenario: Alert AddAsync returning true writes Created audit event

- GIVEN a handler calls `IAlertRepository.AddAsync(newAlert, ct)` and the inner inserts the row (returns `true`)
- WHEN the call completes
- THEN a `audit.events` row MUST be inserted with `entity_type = "Alert"`, `entity_id = newAlert.Id`, `action = "Created"`, `tenant_id` + `user_id` from `ITenantContext`

#### Scenario: Alert AddAsync returning false writes NO audit event (dedup)

- GIVEN a handler calls `IAlertRepository.AddAsync(duplicateAlert, ct)` and the inner dedups via `ux_alerts_user_rule_day` (returns `false`)
- WHEN the call completes
- THEN NO `audit.events` row MUST be inserted (no new entity was created; the existing row's audit history is preserved)

#### Scenario: Alert Acknowledge writes Updated audit event with AcknowledgedAt diff

- GIVEN an alert AL1 with `AcknowledgedAt = null` persisted in the database
- WHEN the handler calls `alert.Acknowledge(clock)` followed by `IAlertRepository.UpdateAsync(al1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "Alert"`, `entity_id = AL1.Id`, `action = "Updated"`, `changes = { "AcknowledgedAt": { "before": null, "after": "<clock.UtcNow>" } }`

#### Scenario: Alert cross-tenant update is denied

- GIVEN an alert AL1 belongs to user U1 in tenant T1, but `ITenantContext.Current = T2`
- WHEN the handler in T2 calls `IAlertRepository.UpdateAsync(al1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "Alert"`, `action = "Denied"`, `changes = { "reason": { "before": null, "after": "cross-tenant update attempt" } }`
- AND the decorator MUST throw `UnauthorizedAccessException`

### Requirement: Audit decorator for TradeReview aggregate

The system MUST provide `TradeReviewAuditDecorator` at `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/TradeReviewAuditDecorator.cs` that implements `ITradeReviewRepository`. The decorator MUST be wired via `services.Decorate<ITradeReviewRepository, TradeReviewAuditDecorator>()` in `TradingModuleRegistration`. The decorator MUST emit `AuditAction.Created` on `AddAsync(TradeReview, ct)`, `AuditAction.Updated` on `UpdateAsync(TradeReview, ct)` (with a JSONB diff — including `Title`, `Body`, `Rating`, `Outcome` field changes). Cross-tenant `IsOwner` check MUST compare `review.UserId` to `ITenantContext.CurrentUserId`; on mismatch, emit `AuditAction.Denied` and throw `UnauthorizedAccessException`. Read-only methods (`FindByTradeIdAsync`, `FindByIdAsync`, `ListAttachmentsByReviewIdAsync`, `CountAttachmentsByReviewIdAsync`, `FindAttachmentByIdAsync`, `GetTradeIdByAttachmentIdAsync`) MUST forward to `_inner` without auditing. The decorator MUST forward `AddAttachmentAsync` / `UpdateAttachmentAsync` / `RemoveAttachmentAsync` to `_inner` WITHOUT emitting audit rows — `TradeAttachment` is a child entity of the review, not a separately-audited aggregate; the review's Update events + handler-side MinIO cleanup log already capture attachment lifecycle. No interface surgery required (the interface already exposes `AddAsync` + `UpdateAsync` only — no `DeleteAsync` per the entity docstring "no delete" product decision).

#### Scenario: TradeReview creation writes Created audit event

- GIVEN a handler calls `ITradeReviewRepository.AddAsync(newReview, ct)` with `newReview.UserId` set
- WHEN the call completes
- THEN a `audit.events` row MUST be inserted with `entity_type = "TradeReview"`, `entity_id = newReview.Id`, `action = "Created"`, `tenant_id` + `user_id` from `ITenantContext`

#### Scenario: TradeReview update writes Updated audit event with diff

- GIVEN a trade review TR1 with `Title = "Old Title"`, `Rating = 3`
- WHEN the handler updates `Title = "New Title"` and `Rating = 5` and calls `ITradeReviewRepository.UpdateAsync(tr1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "TradeReview"`, `entity_id = TR1.Id`, `action = "Updated"`, `changes = { "Rating": { "before": 3, "after": 5 }, "Title": { "before": "Old Title", "after": "New Title" } }`

#### Scenario: TradeReview attachment ops are forwarded WITHOUT audit

- GIVEN a trade review TR1 has 0 attachments
- WHEN the handler calls `ITradeReviewRepository.AddAttachmentAsync(attachment, ct)`, `UpdateAttachmentAsync(attachment, ct)`, or `RemoveAttachmentAsync(attachmentId, ct)`
- THEN the inner's mutation MUST execute (attachment persisted / modified / removed)
- AND NO `audit.events` row MUST be inserted with `entity_type = "TradeAttachment"` (child entities are not separately audited; the review's update events + MinIO cleanup log capture attachment lifecycle)

#### Scenario: TradeReview cross-tenant update is denied

- GIVEN a trade review TR1 belongs to user U1 in tenant T1, but `ITenantContext.Current = T2`
- WHEN the handler in T2 calls `ITradeReviewRepository.UpdateAsync(tr1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "TradeReview"`, `action = "Denied"`, `changes = { "reason": { "before": null, "after": "cross-tenant update attempt" } }`
- AND the decorator MUST throw `UnauthorizedAccessException`

#### Scenario: TradeReview FindByTradeIdAsync is not audited

- GIVEN a handler calls `ITradeReviewRepository.FindByTradeIdAsync(tradeId, ct)` for an existing trade
- WHEN the call completes
- THEN NO `audit.events` row MUST be inserted (read operations are not audited)
- AND the review(s) MUST be returned with the persisted state

### Requirement: Audit decorator for PlannerSession aggregate

The system MUST provide `PlannerSessionAuditDecorator` at `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/PlannerSessionAuditDecorator.cs` that implements `IPlannerSessionRepository`. The decorator MUST be wired via `services.Decorate<IPlannerSessionRepository, PlannerSessionAuditDecorator>()` in `TradingModuleRegistration`. The decorator MUST emit `AuditAction.Created` on `AddAsync(PlannerSession, ct)`, `AuditAction.Updated` on `UpdateAsync(PlannerSession, ct)` (with a JSONB diff — including the `PlannerStatus.Planned → Completed` / `Planned → Skipped` transitions). **When the new `Status == PlannerStatus.Cancelled`**, the decorator MUST upgrade the emitted action to `AuditAction.Deleted` per the `IsTerminated` reflection rule (`Status ∈ {Cancelled, Terminated, Expired}` — matches the Wave 6 6d.2 rule). Cross-tenant `IsOwner` check MUST compare `session.UserId` to `ITenantContext.CurrentUserId`; on mismatch, emit `AuditAction.Denied` and throw `UnauthorizedAccessException`. Read-only methods (`GetByIdAsync`, `ListByUserAndWeekAsync`, `ExistsForDateAsync`, `GetWeekComparisonAsync`) MUST forward to `_inner` without auditing. No interface surgery required.

#### Scenario: PlannerSession creation writes Created audit event

- GIVEN a handler calls `IPlannerSessionRepository.AddAsync(newSession, ct)` with `newSession.UserId` set
- WHEN the call completes
- THEN a `audit.events` row MUST be inserted with `entity_type = "PlannerSession"`, `entity_id = newSession.Id`, `action = "Created"`, `tenant_id` + `user_id` from `ITenantContext`

#### Scenario: PlannerSession update writes Updated audit event with diff

- GIVEN a planner session PS1 with `Notes = "Old notes"`, `Status = PlannerStatus.Planned`
- WHEN the handler updates `Notes = "New notes"` and `Status = PlannerStatus.Completed` and calls `IPlannerSessionRepository.UpdateAsync(ps1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "PlannerSession"`, `entity_id = PS1.Id`, `action = "Updated"`, `changes = { "Notes": { "before": "Old notes", "after": "New notes" }, "Status": { "before": "Planned", "after": "Completed" } }`
- AND the action MUST NOT be `Deleted` (only `Cancelled` triggers the upgrade per the `IsTerminated` reflection rule)

#### Scenario: PlannerSession cancellation upgrades Updated to Deleted

- GIVEN a planner session PS1 with `Status = PlannerStatus.Planned`
- WHEN the handler updates `Status = PlannerStatus.Cancelled` and calls `IPlannerSessionRepository.UpdateAsync(ps1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "PlannerSession"`, `entity_id = PS1.Id`, `action = "Deleted"` (NOT `Updated`), `changes = { "Status": { "before": "Planned", "after": "Cancelled" } }`
- AND the upgrade MUST come from the local `IsTerminated` reflection rule (`PlannerStatus.Cancelled` matches `Status ∈ {Cancelled, Terminated, Expired}`)

#### Scenario: PlannerSession cross-tenant update is denied

- GIVEN a planner session PS1 belongs to user U1 in tenant T1, but `ITenantContext.Current = T2`
- WHEN the handler in T2 calls `IPlannerSessionRepository.UpdateAsync(ps1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "PlannerSession"`, `action = "Denied"`, `changes = { "reason": { "before": null, "after": "cross-tenant update attempt" } }`
- AND the decorator MUST throw `UnauthorizedAccessException`

#### Scenario: PlannerSession reads are not audited

- GIVEN a handler calls `IPlannerSessionRepository.GetByIdAsync(sessionId, ct)` (or `ListByUserAndWeekAsync` / `ExistsForDateAsync` / `GetWeekComparisonAsync`)
- WHEN the call completes
- THEN NO `audit.events` row MUST be inserted (read operations are not audited)

### Requirement: Audit decorator for PreTradeChecklist aggregate

The system MUST provide `PreTradeChecklistAuditDecorator` at `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/PreTradeChecklistAuditDecorator.cs` that implements `IPreTradeChecklistRepository`. The decorator MUST be wired via `services.Decorate<IPreTradeChecklistRepository, PreTradeChecklistAuditDecorator>()` in `TradingModuleRegistration`. The decorator MUST emit `AuditAction.Created` on `AddAsync(PreTradeChecklist, ct)`. **No `UpdateAsync` or `DeleteAsync` exists on the interface** — the checklist is write-once per the entity docstring ("UNA fila por trade — enforced por UNIQUE INDEX sobre trade_id en la DB. La API no expone UPDATE del checklist"). Read-only method (`ListByUserIdAsync`) MUST forward to `_inner` without auditing. Cross-tenant `IsOwner` check MUST compare `checklist.UserId` to `ITenantContext.CurrentUserId`; on mismatch, emit `AuditAction.Denied` and throw `UnauthorizedAccessException`.

#### Scenario: PreTradeChecklist creation writes Created audit event

- GIVEN a handler calls `IPreTradeChecklistRepository.AddAsync(newChecklist, ct)` with `newChecklist.UserId` set
- WHEN the call completes
- THEN a `audit.events` row MUST be inserted with `entity_type = "PreTradeChecklist"`, `entity_id = newChecklist.Id`, `action = "Created"`, `tenant_id` + `user_id` from `ITenantContext`

#### Scenario: PreTradeChecklist ListByUserIdAsync is not audited

- GIVEN a handler calls `IPreTradeChecklistRepository.ListByUserIdAsync(userId, ct)` for an existing user
- WHEN the call completes
- THEN NO `audit.events` row MUST be inserted (read operations are not audited)
- AND the checklists MUST be returned with the persisted state

#### Scenario: PreTradeChecklist has no Update or Delete methods on the interface (contract pin)

- GIVEN the checklist is write-once per the entity docstring + `ux_checklists_trade_id` UNIQUE INDEX
- WHEN a developer inspects `IPreTradeChecklistRepository`
- THEN the interface MUST NOT declare `UpdateAsync` or `DeleteAsync`
- AND `AttachAIRiskAdvisory` (in-memory mutation called within the same UoW) MUST NOT flow through a repo-level `UpdateAsync` (it is a domain-op on the aggregate, not a persistence mutation)

### Requirement: Audit decorator for StripeCustomer aggregate

The system MUST provide `StripeCustomerAuditDecorator` at `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Audit/StripeCustomerAuditDecorator.cs` that implements `IStripeCustomerRepository`. The decorator MUST be wired via `services.Decorate<IStripeCustomerRepository, StripeCustomerAuditDecorator>()` in `BillingModuleRegistration`. The decorator MUST emit `AuditAction.Created` on `AddAsync(StripeCustomer, ct)`. **No `UpdateAsync` or `DeleteAsync` exists on the interface** — the `StripeCustomer` aggregate is immutable after `Create` per the entity docstring ("Aggregate is immutable after Create — no mutators"). Read-only methods (`GetByUserIdAsync`, `GetByStripeCustomerIdAsync`) MUST forward to `_inner` without auditing. Cross-tenant `IsOwner` check MUST compare `customer.UserId` to `ITenantContext.CurrentUserId`; on mismatch, emit `AuditAction.Denied` and throw `UnauthorizedAccessException`.

#### Scenario: StripeCustomer creation writes Created audit event

- GIVEN a handler calls `IStripeCustomerRepository.AddAsync(newCustomer, ct)` with `newCustomer.UserId` set
- WHEN the call completes
- THEN a `audit.events` row MUST be inserted with `entity_type = "StripeCustomer"`, `entity_id = newCustomer.Id`, `action = "Created"`, `tenant_id` + `user_id` from `ITenantContext`

#### Scenario: StripeCustomer reads are not audited

- GIVEN a handler calls `IStripeCustomerRepository.GetByUserIdAsync(userId, ct)` or `GetByStripeCustomerIdAsync(stripeCustomerId, ct)`
- WHEN the call completes
- THEN NO `audit.events` row MUST be inserted (read operations are not audited)
- AND the customer MUST be returned with the persisted state

#### Scenario: StripeCustomer has no Update or Delete methods on the interface (contract pin)

- GIVEN `StripeCustomer` is immutable after Create per the entity docstring
- WHEN a developer inspects `IStripeCustomerRepository`
- THEN the interface MUST NOT declare `UpdateAsync` or `DeleteAsync`
- AND `audit.events` MUST only ever receive `Created` rows for `entity_type = "StripeCustomer"` (compliance officers can rely on this guarantee)

### Requirement: Audit decorator for AIRiskAdvice aggregate

The system MUST provide `AIRiskAdviceAuditDecorator` at `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AIRiskAdviceAuditDecorator.cs` that implements `IAIRiskAdviceRepository`. The decorator MUST be wired via `services.Decorate<IAIRiskAdviceRepository, AIRiskAdviceAuditDecorator>()` in `TradingModuleRegistration`. The decorator MUST emit `AuditAction.Created` on `AddAsync(advice, ct)` and forward `FindByUserAndTradeAsync(...)` without audit. Cross-tenant access MUST emit `AuditAction.Denied` and throw `UnauthorizedAccessException`. No `UpdateAsync` or `DeleteAsync` exists on the interface (the aggregate is immutable post-Create per the entity docstring); the decorator MUST NOT introduce them.

#### Scenario: AIRiskAdvice creation writes Created audit event

- GIVEN a handler calls `IAIRiskAdviceRepository.AddAsync(newAdvice, ct)` with `newAdvice.UserId` set
- WHEN the call completes
- THEN a `audit.events` row MUST be inserted with `entity_type = "AIRiskAdvice"`, `entity_id = newAdvice.Id`, `action = "Created"`, `tenant_id` + `user_id` from `ITenantContext`

#### Scenario: AIRiskAdvice cross-tenant AddAsync is denied

- GIVEN an advice belongs to user U1 in tenant T1, but `ITenantContext.Current = T2`
- WHEN the handler in T2 calls `IAIRiskAdviceRepository.AddAsync(advice, ct)`
- THEN a `audit.events` row MUST be written with `entity_type = "AIRiskAdvice"`, `action = "Denied"`, `changes = { "reason": { "before": null, "after": "cross-tenant mutation attempt" } }`
- AND the decorator MUST throw `UnauthorizedAccessException`

#### Scenario: AIRiskAdvice FindByUserAndTradeAsync is not audited

- GIVEN a handler calls `IAIRiskAdviceRepository.FindByUserAndTradeAsync(userId, tradeId, ct)` (read)
- WHEN the call completes
- THEN NO `audit.events` row MUST be inserted (read operations are never audited)
- AND the matching advice MUST be returned (or null)

### Requirement: Audit decorator for CoachingPrompt aggregate

The system MUST provide `CoachingPromptAuditDecorator` at `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/CoachingPromptAuditDecorator.cs` that implements `ICoachingPromptRepository`. The decorator MUST be wired via `services.Decorate<ICoachingPromptRepository, CoachingPromptAuditDecorator>()` in `TradingModuleRegistration`. The decorator MUST emit `AuditAction.Created` on `AddAsync(prompt, ct)` and forward `FindByUserAndDateAsync` + `ListByUserAndWindowAsync` without audit. Cross-tenant access MUST emit `AuditAction.Denied` and throw `UnauthorizedAccessException`. No `UpdateAsync` or `DeleteAsync` exists on the interface (the aggregate is immutable post-Create per the entity docstring); the decorator MUST NOT introduce them.

#### Scenario: CoachingPrompt creation writes Created audit event

- GIVEN a handler calls `ICoachingPromptRepository.AddAsync(newPrompt, ct)` with `newPrompt.UserId` set
- WHEN the call completes
- THEN a `audit.events` row MUST be inserted with `entity_type = "CoachingPrompt"`, `entity_id = newPrompt.Id`, `action = "Created"`, `tenant_id` + `user_id` from `ITenantContext`

#### Scenario: CoachingPrompt cross-tenant AddAsync is denied

- GIVEN a prompt belongs to user U1 in tenant T1, but `ITenantContext.Current = T2`
- WHEN the handler in T2 calls `ICoachingPromptRepository.AddAsync(prompt, ct)`
- THEN a `audit.events` row MUST be written with `entity_type = "CoachingPrompt"`, `action = "Denied"`, `changes = { "reason": { "before": null, "after": "cross-tenant mutation attempt" } }`
- AND the decorator MUST throw `UnauthorizedAccessException`

#### Scenario: CoachingPrompt reads are not audited

- GIVEN a handler calls `ICoachingPromptRepository.FindByUserAndDateAsync(userId, day, ct)` or `ListByUserAndWindowAsync(userId, from, to, ct)` (reads)
- WHEN the call completes
- THEN NO `audit.events` row MUST be inserted (read operations are never audited)

### Requirement: Audit decorator for ScannerFilter aggregate

The system MUST provide `ScannerFilterAuditDecorator` at `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/ScannerFilterAuditDecorator.cs` that implements `IScannerFilterRepository`. The decorator MUST be wired via `services.Decorate<IScannerFilterRepository, ScannerFilterAuditDecorator>()` in `TradingModuleRegistration`. The decorator MUST emit `AuditAction.Created` on `AddAsync(filter, ct)` and `AuditAction.Updated` on `UpdateAsync(filter, ct)` (with a JSONB diff of changed fields including the `IsActive: true → false` transition from `Deactivate(IClock)`). Read methods (`GetByIdAsync`, `GetByUserAndNameAsync`, `ListByUserAsync`) MUST forward to `_inner` without audit. Cross-tenant access MUST emit `AuditAction.Denied` and throw `UnauthorizedAccessException`. The interface MUST extend `IRepository<ScannerFilter>` (Wave 9 surface addition); the `DeleteAsync(ScannerFilter, ct)` STUB MUST throw `NotSupportedException` with the message `"ScannerFilter deletion is not supported — use Deactivate (IsActive = false)"`. The decorator MUST emit `AuditAction.Failed` BEFORE re-throwing. The `Deactivate(IClock)` mutation captured inside `UpdateAsync` MUST be recorded as `AuditAction.Updated` (the `DecoratedRepository<T>.IsTerminated` reflection rule does NOT match `ScannerFilter.IsActive`).

#### Scenario: ScannerFilter creation writes Created audit event

- GIVEN a handler calls `IScannerFilterRepository.AddAsync(newFilter, ct)` with `newFilter.UserId` set
- WHEN the call completes
- THEN a `audit.events` row MUST be inserted with `entity_type = "ScannerFilter"`, `entity_id = newFilter.Id`, `action = "Created"`, `tenant_id` + `user_id` from `ITenantContext`

#### Scenario: ScannerFilter update writes Updated audit event with diff

- GIVEN a filter F1 with `name = "Old"` persisted
- WHEN the handler calls `IScannerFilterRepository.UpdateAsync(f1, ct)` after `f1.Update(name: "New", clock)` mutated the aggregate
- THEN a `audit.events` row MUST be inserted with `entity_type = "ScannerFilter"`, `action = "Updated"`, `changes = { "Name": { "before": "Old", "after": "New" } }`
- AND the diff MUST order fields alphabetically

#### Scenario: ScannerFilter deactivation is audited as Updated (not Deleted)

- GIVEN a filter F1 with `IsActive = true`
- WHEN the handler calls `f1.Deactivate(clock)` followed by `IScannerFilterRepository.UpdateAsync(f1, ct)`
- THEN a `audit.events` row MUST be inserted with `entity_type = "ScannerFilter"`, `action = "Updated"` (NOT `"Deleted"`), `changes = { "IsActive": { "before": true, "after": false } }`
- AND the `IsTerminated` reflection rule MUST NOT upgrade the action

#### Scenario: ScannerFilter DeleteAsync stub writes Failed audit event

- GIVEN a handler calls `IScannerFilterRepository.DeleteAsync(filter, ct)` (the defensive stub added by Wave 9)
- WHEN the call enters the decorator
- THEN a `audit.events` row MUST be written with `entity_type = "ScannerFilter"`, `action = "Failed"`, `changes = { "reason": { "before": null, "after": "ScannerFilter deletion is not supported — use Deactivate (IsActive = false)" } }`
- AND the decorator MUST re-throw `NotSupportedException`

### Requirement: Audit decorator for AttachmentSweep aggregate (batch soft-delete)

The system MUST provide `AttachmentSweepAuditDecorator` at `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AttachmentSweepAuditDecorator.cs` that implements `IAttachmentSweepRepository`. The decorator MUST be wired via `services.Decorate<IAttachmentSweepRepository, AttachmentSweepAuditDecorator>()` in `TradingModuleRegistration`. The decorator MUST emit ONE `AuditAction.Deleted` audit row PER `id` in the batch when `SoftDeleteBatchAsync(IReadOnlyList<Guid> ids, ct)` is called, with `EntityType = "TradeAttachment"`, `EntityId = id`, `ChangesJson = { "IsActive": { "before": true, "after": false } }`. Cross-tenant access MUST be checked PER id (using the loaded `attachment.UserId` from the inner EF tracked instance); on any cross-tenant id the decorator MUST emit `AuditAction.Denied` for that id AND throw `UnauthorizedAccessException` for the whole batch. The 3 read methods (`GetExpiredBatchAsync`, `GetUserAggregateAsync`, `GetActiveUserIdsAsync`) MUST forward to `_inner` without audit. `InsertAuditAsync(...)` MUST forward to `_inner` without emitting an `audit.events` row (the destination `trading.attachments_quota_audit` table IS the audit log for the sweep — emitting on top would be doubly-recorded noise; mirrors Wave 8 8b.2 `IStripeWebhookEventRepository` SKIP rationale).

#### Scenario: SoftDeleteBatchAsync emits N audit rows for N ids

- GIVEN 3 active attachments A1, A2, A3 all owned by user U1 in tenant T1
- WHEN `IAttachmentSweepRepository.SoftDeleteBatchAsync([A1.Id, A2.Id, A3.Id], ct)` is called
- THEN 3 `audit.events` rows MUST be inserted (one per id), each with `entity_type = "TradeAttachment"`, `action = "Deleted"`, `changes = { "IsActive": { "before": true, "after": false } }`
- AND each row MUST carry `tenant_id` + `user_id` from `ITenantContext`

#### Scenario: Cross-tenant id in the batch emits Denied and throws

- GIVEN a batch of 3 ids: A1 (tenant T1), A2 (tenant T2), A3 (tenant T1)
- WHEN `IAttachmentSweepRepository.SoftDeleteBatchAsync([A1.Id, A2.Id, A3.Id], ct)` is called from T1
- THEN a `audit.events` row MUST be inserted for A2 with `entity_type = "TradeAttachment"`, `action = "Denied"`, `changes = { "reason": { "before": null, "after": "cross-tenant mutation attempt" } }`
- AND the decorator MUST throw `UnauthorizedAccessException` for the whole batch

#### Scenario: AttachmentSweep reads are not audited

- GIVEN a handler calls `IAttachmentSweepRepository.GetExpiredBatchAsync(...)` + `GetUserAggregateAsync(...)` + `GetActiveUserIdsAsync(...)` (reads)
- WHEN the calls complete
- THEN NO `audit.events` rows MUST be inserted (read operations are never audited)

#### Scenario: AttachmentSweep InsertAuditAsync is not audited

- GIVEN the sweep calls `IAttachmentSweepRepository.InsertAuditAsync(userId, bytesUsed, ct)` to write to `trading.attachments_quota_audit`
- WHEN the call completes
- THEN NO `audit.events` row MUST be inserted (the destination table IS the sweep's own audit log)

## REMOVED Requirements

### Requirement: Audit decorator for ITradeAttachmentUsageRepository

(Reason: The interface `ITradeAttachmentUsageRepository` exposes ONLY the read-side aggregate query `GetUsageAsync(Guid userId, ct)` returning `(long TotalBytes, int Count)` — NO `AddAsync`, `UpdateAsync`, or `DeleteAsync` exists. Verified via `git grep -E "Task (Add|Update|Delete)Async" src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/ITradeAttachmentUsageRepository.cs` → no matches. The right seam for `TradeAttachment` audit IS `IAttachmentSweepRepository.SoftDeleteBatchAsync` (Wave 9 9a.3) — user-impacting soft-delete happens there, not at the usage projection. Wrapping `ITradeAttachmentUsageRepository` would be a no-op on the audit-write path; only reads are left to forward, and Wave 6/7/8 precedent (mirrored in the existing "GetById is NOT audited" scenario) says reads are never audited.)
(Migration: None — `audit.events` already captures `TradeAttachment` soft-deletes via `AttachmentSweepAuditDecorator` (Wave 9 9a.3). The `<remarks>` XML doc on `ITradeAttachmentUsageRepository.cs` documents the SKIP rationale inline.)

## Data Model

```
audit.events
  id              UUID PK
  entity_type     VARCHAR(80) NOT NULL
  entity_id       UUID NOT NULL
  action          SMALLINT NOT NULL  -- 0=Created, 1=Updated, 2=Deleted, 3=Restored, 4=Denied (Wave 7), 5=Failed (Wave 7)
  tenant_id       UUID
  user_id         UUID
  changes         JSONB  -- { "field": { "before": ..., "after": ... } } or null
  occurred_at     TIMESTAMPTZ NOT NULL DEFAULT now()

Constraints:
  ck_audit_events_action  CHECK (action IN (0, 1, 2, 3, 4, 5))  -- Wave 7 widens from IN (0,1,2,3) via migration 0029

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
