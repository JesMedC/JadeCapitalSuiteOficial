# Delta for Soft-Delete and Audit — Wave 9

## ADDED Requirements

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

## MODIFIED Requirements

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

## REMOVED Requirements

### Requirement: Audit decorator for ITradeAttachmentUsageRepository

(Reason: The interface `ITradeAttachmentUsageRepository` exposes ONLY the read-side aggregate query `GetUsageAsync(Guid userId, ct)` returning `(long TotalBytes, int Count)` — NO `AddAsync`, `UpdateAsync`, or `DeleteAsync` exists. Verified via `git grep -E "Task (Add|Update|Delete)Async" src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/ITradeAttachmentUsageRepository.cs` → no matches. The right seam for `TradeAttachment` audit IS `IAttachmentSweepRepository.SoftDeleteBatchAsync` (Wave 9 9a.3) — user-impacting soft-delete happens there, not at the usage projection. Wrapping `ITradeAttachmentUsageRepository` would be a no-op on the audit-write path; only reads are left to forward, and Wave 6/7/8 precedent (mirrored in the existing "GetById is NOT audited" scenario) says reads are never audited.)
(Migration: None — `audit.events` already captures `TradeAttachment` soft-deletes via `AttachmentSweepAuditDecorator` (Wave 9 9a.3). The `<remarks>` XML doc on `ITradeAttachmentUsageRepository.cs` documents the SKIP rationale inline.)
