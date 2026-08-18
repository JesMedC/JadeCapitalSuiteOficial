# Delta for Soft-Delete and Audit — Wave 7 Audit Coverage + Shared.Infrastructure Helper Refactor

## MODIFIED Requirements

### Requirement: Apply decorator to existing repositories

The system MUST provide a typed audit decorator per `IXxxRepository` that mutates state on a user-owned aggregate, registered in its module's `*ModuleRegistration` via `services.Decorate<IXxxRepository, XxxAuditDecorator>()` (Scrutor). The decorator MUST forward `AddAsync` / `UpdateAsync` / `DeleteAsync` (or the aggregate-specific mutation method like `MarkSupersededAsync` or `DeleteAsync(Guid)`) to a `DecoratedRepository<T>` instance and emit an `AuditEvent` with `EntityType = typeof(T).Name`, `Action` matching the mutation, `TenantId` + `UserId` from `ITenantContext`, and (for non-Create actions) a JSONB diff. Cross-tenant access MUST emit `AuditAction.Denied` and throw `UnauthorizedAccessException`. Failed `DeleteAsync` calls on repos whose aggregates have no delete path MUST emit `AuditAction.Failed` and re-throw `NotSupportedException`.

Covered aggregates across all waves:
- **Wave 6 (6d.2)**: `Tenant`, `ImportJob`, `Subscription`.
- **Wave 7 (this delta)**: `User`, `RiskProfile`, `Strategy`, `Trade`, `JournalEntry`.
- **Wave 8+**: `TradeReview`, `PlannerSession`, `PreTradeChecklist`, `Account`, `Instrument`, `Alert`, `RiskConfiguration`, `PlanVersion`, `JournalDaily`, `BacktestRun`, `StrategyVersion`, `TradeTag`, `Note`, `Mood`, `BehavioralMetric`, and any other user-owned aggregate.

(Previously: Wave 6 covered `Tenant`, `ImportJob`, `Subscription`. Wave 7 widens to the 5 remaining user-owned aggregates — `User`, `RiskProfile`, `Strategy`, `Trade`, `JournalEntry` — and introduces `Denied` + `Failed` actions for cross-tenant rejection and `NotSupportedException` on delete paths.)

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
- WHEN a handler calls `IUserRepository.DeleteAsync(user, ct)` (or `IStrategyRepository.DeleteAsync(strategy, ct)`)
- THEN a `audit.events` row MUST be written with `entity_type = "<T>"`, `action = "Failed"`, `changes = { "reason": { "before": null, "after": "User/Strategy deletion is not supported — use Tenant reassignment / Deactivation" } }`
- AND the decorator MUST re-throw `NotSupportedException`

#### Scenario: GetById is NOT audited

- GIVEN any of the 5 new typed decorators
- WHEN the handler calls any read-only method (`GetByIdAsync`, `FindByXxxAsync`, `ListByXxxAsync`)
- THEN NO `audit.events` row MUST be inserted (read operations are not audited)

## ADDED Requirements

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
