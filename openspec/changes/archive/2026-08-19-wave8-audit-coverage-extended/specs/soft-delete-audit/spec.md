# Delta for Soft-Delete and Audit — Wave 8 Audit Coverage Extension

## MODIFIED Requirements

### Requirement: Apply decorator to existing repositories

The system MUST provide a typed audit decorator per `IXxxRepository` that mutates state on a user-owned aggregate, registered in its module's `*ModuleRegistration` via `services.Decorate<IXxxRepository, XxxAuditDecorator>()` (Scrutor). The decorator MUST forward `AddAsync` / `UpdateAsync` / `DeleteAsync` (or the aggregate-specific mutation method like `MarkSupersededAsync` or `DeleteAsync(Guid)`) to a `DecoratedRepository<T>` instance and emit an `AuditEvent` with `EntityType = typeof(T).Name`, `Action` matching the mutation, `TenantId` + `UserId` from `ITenantContext`, and (for non-Create actions) a JSONB diff. Cross-tenant access MUST emit `AuditAction.Denied` and throw `UnauthorizedAccessException`. Failed `DeleteAsync` calls on repos whose aggregates have no delete path MUST emit `AuditAction.Failed` and re-throw `NotSupportedException`.

Covered aggregates across all waves:
- **Wave 6 (6d.2)**: `Tenant`, `ImportJob`, `Subscription`.
- **Wave 7 (7a.1 / 7b.1 / 7b.2)**: `User`, `RiskProfile`, `Strategy`, `Trade`, `JournalEntry`.
- **Wave 8 (this delta — 8a.1 / 8a.2 / 8a.3 / 8b.1)**: `Account`, `Instrument`, `Alert`, `TradeReview`, `PlannerSession`, `PreTradeChecklist`, `StripeCustomer`.
- **Wave 9+**: `RiskConfiguration`, `PlanVersion`, `JournalDaily`, `BacktestRun`, `StrategyVersion`, `TradeTag`, `Note`, `Mood`, `BehavioralMetric`, and any other user-owned aggregate.

(Previously: Wave 6 covered `Tenant`, `ImportJob`, `Subscription`. Wave 7 widened to `User`, `RiskProfile`, `Strategy`, `Trade`, `JournalEntry`. Wave 8 extends to 7 more user-owned aggregates and documents 2 explicit SKIPs: `ISubscriptionAdminRepository` (no mutation methods) and `IStripeWebhookEventRepository` (append-only). The `ISoftDelete` query filter does NOT apply to any of the 7 new aggregates; `PlannerSession` uses `PlannerStatus.Cancelled` via the `IsTerminated` reflection rule for soft-delete-via-status. `IInstrumentRepository` is a catalog entity — no `IsOwner` cross-tenant check (mirrors `TenantAuditDecorator` precedent).)

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
- AND the row MUST NOT be `action = "Deleted"`

#### Scenario: Trade deletion is audited

- GIVEN `ITradeRepository` is decorated with `TradeAuditDecorator`
- WHEN the handler calls `DeleteAsync(trade, ct)`
- THEN a `audit.events` row MUST be written with `entity_type = "Trade"`, `action = "Deleted"`

#### Scenario: JournalEntry deletion by Guid is audited

- GIVEN `IJournalEntryRepository` is decorated with `JournalEntryAuditDecorator`
- WHEN the handler calls `DeleteAsync(JournalEntry, ct)`
- THEN a `audit.events` row MUST be written with `entity_type = "JournalEntry"`, `action = "Deleted"`

#### Scenario: Cross-tenant mutation is audited as Denied

- GIVEN the typed decorator's `IsOwner` check compares `entity.UserId` (or `entity.Id` for User) to `ITenantContext.CurrentUserId`
- WHEN a handler in tenant T2 attempts to mutate an entity owned by tenant T1
- THEN a `audit.events` row MUST be written with `entity_type = "<T>"`, `action = "Denied"`, `changes = { "reason": { "before": null, "after": "cross-tenant mutation attempt" } }`
- AND the decorator MUST throw `UnauthorizedAccessException`

#### Scenario: Delete on a non-deletable aggregate is audited as Failed

- GIVEN the typed decorator's `DeleteAsync(T, ct)` method implements the full interface
- WHEN a handler calls `IUserRepository.DeleteAsync(user, ct)` (or `IStrategyRepository.DeleteAsync(strategy, ct)`)
- THEN a `audit.events` row MUST be written with `entity_type = "<T>"`, `action = "Failed"`, `changes = { "reason": { "before": null, "after": "User/Strategy deletion is not supported — use Tenant reassignment / Deactivation" } }`
- AND the decorator MUST re-throw `NotSupportedException`

#### Scenario: GetById is NOT audited

- GIVEN any of the typed decorators (Wave 6 / Wave 7 / Wave 8)
- WHEN the handler calls any read-only method (`GetByIdAsync`, `FindByXxxAsync`, `ListByXxxAsync`)
- THEN NO `audit.events` row MUST be inserted (read operations are not audited)

## ADDED Requirements

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

## REMOVED Requirements

### Requirement: Audit decorator for ISubscriptionAdminRepository (REMOVED)

(Reason: no mutation methods on this interface; the Subscription aggregate is already audited by Wave 6's `SubscriptionAuditDecorator`.)
(Migration: None.)

### Requirement: Audit decorator for IStripeWebhookEventRepository (REMOVED)

(Reason: append-only per Wave 6 design.)
(Migration: None.)