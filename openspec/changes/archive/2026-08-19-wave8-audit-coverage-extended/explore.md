# Exploration — Wave 8 Audit Coverage Extension

**Change**: `2026-08-19-wave8-audit-coverage-extended`
**Branch**: `feature/0a-identity-model` @ `21430aa` (Wave 7 just archived)
**Stack**: ASP.NET Core 10 / .NET SDK 10.0.400 via `mise exec -- dotnet …`
**Mode**: explore is **read-only** (Strict TDD does not apply to research)

---

## Executive Scope

Of the **9 candidate repositories** named in Wave 7's verify-report SUGGESTION #1:

| # | Repo | Module | Verdict |
|---|---|---|---|
| 1 | `ITradeReviewRepository` | Trading | **AUDIT — bespoke decorator** |
| 2 | `IPlannerSessionRepository` | Trading | **AUDIT — bespoke decorator** |
| 3 | `IPreTradeChecklistRepository` | Trading | **AUDIT — bespoke write-once decorator** |
| 4 | `IAccountRepository` | Trading | **AUDIT — generic decorator + `RemoveAsync` → `DeleteAsync` rename** |
| 5 | `IInstrumentRepository` | Trading | **AUDIT — generic decorator + `RemoveAsync` → `DeleteAsync` rename** |
| 6 | `IAlertRepository` | Trading | **AUDIT — bespoke decorator** |
| 7 | `ISubscriptionAdminRepository` | Billing | **SKIP — redundant (Subscription aggregate already audited by Wave 6)** |
| 8 | `IStripeCustomerRepository` | Billing | **AUDIT — bespoke decorator (immutable after Create)** |
| 9 | `IStripeWebhookEventRepository` | Billing | **SKIP — append-only per Wave 6 proposal** |

**Effective scope**: **7 audited + 2 skipped** of 9. The 2 SKIPs are documented with rationale (no behavior change either way). The 7 audited targets split as: 4 bespoke + 1 write-once + 2 standard-with-rename.

---

## Current State

### What Wave 7 shipped (the inheritance)

Wave 7 closed the audit decorator rollout to **8 user-owned aggregates** (Tenant, ImportJob, Subscription, User, RiskProfile, Strategy, Trade, JournalEntry) and moved the generic helper to `Shared.Infrastructure`:

- **Generic helper**: `DecoratedRepository<T>` at `src/3.Shared/JadeCapital.Shared.Infrastructure/Persistence/DecoratedRepository.cs` (319 LOC including `IDiff` + `JsonDiff`). Wraps the narrow generic `IRepository<T>` at `src/3.Shared/JadeCapital.Shared.Kernel/Repository/IRepository.cs` (4 methods: `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`). Reflection helper `IsTerminated` upgrades `UpdateAsync` → `AuditAction.Deleted` when `IsDeleted == true` OR `Status ∈ {Cancelled, Terminated, Expired}`.
- **Typed decorators** co-located per module: 3 in Identity (Tenant, User, RiskProfile), 4 in Trading (ImportJob, Strategy, Trade, JournalEntry), 1 in Billing (Subscription). All implement the typed interface directly, instantiating `DecoratedRepository<T>` for the standard shape or reimplementing the bespoke pattern inline.
- **Cross-tenant `IsOwner` check** is the Wave 7 norm: every typed decorator (except Tenant) compares `entity.UserId` to `ITenantContext.CurrentUserId` before allowing mutation. On mismatch: `AuditAction.Denied` audit row + `UnauthorizedAccessException`.
- **`DeleteAsync` defensive stubs** throw `NotSupportedException` after writing `AuditAction.Failed` for non-deletable aggregates (User, Strategy, RiskProfile). The decorator NEVER reaches the inner on the stub path.
- **`ITradeRepository.RemoveAsync` → `DeleteAsync` rename** in Wave 7 was the only breaking change. 1 handler + 1 test updated atomically.
- **`AuditAction.Denied = 4` + `AuditAction.Failed = 5`** added to the enum + migration 0029 widened the `ck_audit_events_action` CHECK constraint to `IN (0,1,2,3,4,5)`.
- **Scrutor** 4.2.2 is now an explicit `<PackageReference>` in Trading + Billing csprojs (was transitive); both modules already wire `services.Decorate<IXxxRepository, XxxAuditDecorator>()` extensively.
- **`ITenantContext`** (Shared.Kernel) exposes `CurrentUserId`, `IsSuperAdmin`. Anonymous / service actors bypass the cross-tenant check (matching Wave 6 precedent).

### Forecast rule (Wave 7 verify-report SUGGESTION #2)

```
forecast = integration_scenarios + 2 * contract_scenarios_per_interface_surgery
```

Interface surgery = `RemoveAsync` → `DeleteAsync` rename, additive overload (like `IJournalEntryRepository.DeleteAsync(JournalEntry, ct)`), or any other interface-shape change that requires handler/test call-site updates.

### Pattern templates Wave 8 will replicate

| Template | Path | Shape |
|---|---|---|
| Canonical typed decorator | `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/TenantAuditDecorator.cs` (73 LOC) | Generic `DecoratedRepository<T>` for Add/Update/Delete; reads forwarded. |
| Bespoke typed decorator (cross-tenant) | `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/TradeAuditDecorator.cs` (321 LOC) | Implements `IXxxRepository` directly; `IsOwner` on `UpdateAsync` + `DeleteAsync`; `ResolveBefore` via EF `ChangeTracker.OriginalValues`; `SafeDiff` reimplemented. |
| Bespoke typed decorator (write-once domain op) | `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/RiskProfileAuditDecorator.cs` (219 LOC) | Wraps domain-op `MarkSupersededAsync`; emits `Deleted` on supersession; defensive `DeleteAsync` stub emits `Failed`. |
| Bespoke typed decorator (additive overload) | `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/JournalEntryAuditDecorator.cs` (343 LOC) | `DeleteAsync(JournalEntry, ct)` additive overload wraps the canonical `DeleteAsync(Guid, ct)` for auditing; cross-tenant check on entity path; Guid path forwarded without audit. |

---

## Affected Areas (9 target repos — current surface)

| Interface | Inherits `IRepository<T>`? | Mutation surface | ISoftDelete | Status enum (for `IsTerminated`) | EF query filter? | Concrete impl |
|---|---|---|---|---|---|---|
| `ITradeReviewRepository` | **No** (bespoke) | `AddAsync(TradeReview)`, `UpdateAsync(TradeReview)`, attachment ops (`AddAttachmentAsync`, `UpdateAttachmentAsync`, `RemoveAttachmentAsync`) | No | No | none | `TradeReviewRepository` ✓ |
| `IPlannerSessionRepository` | **No** (bespoke) | `AddAsync`, `UpdateAsync` only — no delete method on the interface | No | **Yes** (`PlannerStatus.Cancelled` matches the rule) | none | `PlannerSessionRepository` ✓ |
| `IPreTradeChecklistRepository` | **No** (bespoke — write-once) | `AddAsync` only — no update, no delete | No | No | none | `ChecklistRepository` ✓ (under `Persistence/ChecklistRepository.cs`) |
| `IAccountRepository` | **No** (no `GetByIdAsync(Guid)`, has `FindByIdAsync(Guid)`) | `AddAsync`, `RemoveAsync(Account)` — rename opportunity | No | No (uses `IsActive bool`) | none | `AccountRepository` ✓ (in `Repositories.cs`) |
| `IInstrumentRepository` | **No** (same as Account) | `AddAsync`, `RemoveAsync(Instrument)` — rename opportunity | No | No (uses `IsActive bool`) | none | `InstrumentRepository` ✓ (in `Repositories.cs`) |
| `IAlertRepository` | **No** (bespoke, dedup-bounded) | `AddAsync(Alert)` returns `bool` (true = inserted, false = deduped), `UpdateAsync(Alert)` | No | No (only `AcknowledgedAt`/`ExpiresAt`) | none | `AlertRepository` ✓ |
| `ISubscriptionAdminRepository` | **No** | **No mutation methods at all** — only `ListPagedAsync`, `LoadForUpdateAsync`, `FindByStripeSubscriptionIdAsync`, `FindByUserIdAsync`. Mutations flow through `ISubscriptionAdminUnitOfWork.AddHistoryEntry` + `SaveChangesAsync` + the `Subscription` aggregate's own `ChangeTier`/`Cancel`/`ExtendTrial`/`SyncFromStripe`. | n/a (Subscription already audited) | n/a | none | `SubscriptionAdminRepository` ✓ |
| `IStripeCustomerRepository` | **No** (bespoke — immutable aggregate) | `AddAsync` only — `StripeCustomer` has no mutators (entity docstring: "Aggregate is immutable after Create — no mutators") | No | No | none | `StripeCustomerRepository` ✓ |
| `IStripeWebhookEventRepository` | **No** (bespoke — append-only log) | `AddAsync`, `UpdateAsync` (only for `ProcessedAt`/`ProcessingError` per `MarkProcessed`/`MarkFailed`). Aggregate docstring: "append-only after Record; EventId/EventType/PayloadJson/ReceivedAt are NOT publicly settable." | No | No (derived status) | none | `StripeWebhookEventRepository` ✓ |

### All 9 entities lack `ISoftDelete`

`grep HasQueryFilter src/2.Modules/.../Persistence/Configurations/` returns only `ImportJobConfiguration.cs:58` (Wave 6). The 9 target entities do NOT implement `ISoftDelete`. The only one with a Status enum that matches the `IsTerminated` reflection rule is **`PlannerSession`** (`PlannerStatus.Cancelled` upgrades `UpdateAsync` to `AuditAction.Deleted`). The others emit straight `AuditAction.Updated` on mutations.

---

## Decisions per target repo

### 1. `ITradeReviewRepository` — AUDIT (bespoke)

**Pattern**: Mirror `JournalEntryAuditDecorator` (Wave 7 slice 7b.2). The interface is bespoke (cross-user scope on every read; no `IRepository<T>` extension possible without forcing a security regression). The decorator implements `ITradeReviewRepository` directly:

- Forward all reads (`FindByTradeIdAsync`, `FindByIdAsync`, `ListAttachmentsByReviewIdAsync`, `CountAttachmentsByReviewIdAsync`, `FindAttachmentByIdAsync`, `GetTradeIdByAttachmentIdAsync`) — no audit (Wave 7 precedent: reads are never audited).
- `AddAsync(TradeReview)` → `Created` audit on the review.
- `UpdateAsync(TradeReview)` → `Updated` audit with before/after diff + `IsOwner` check on `review.UserId`; cross-tenant rejection emits `Denied` + throws `UnauthorizedAccessException`. Resolve before via EF `ChangeTracker.OriginalValues` when DbContext wired; fall back to post-mutation JSON snapshot.
- Forward attachment methods (`AddAttachmentAsync`, `UpdateAttachmentAsync`, `RemoveAttachmentAsync`) without audit. **Justification**: `TradeAttachment` is a child entity of the review, not a separately-audited aggregate. The review's Update events + the handler-side MinIO cleanup log already capture attachment lifecycle. Adding per-attachment audit rows would be noisy without proportional compliance value.

**Interface surgery**: 0. The interface shape already supports the decorator (no rename, no additive overload needed). Review deletion is intentionally NOT a product feature — there's no `DeleteAsync(TradeReview, ct)` on the interface.

**Forecast**: ~5 integration scenarios + 0 contract = **5 scenarios**.

### 2. `IPlannerSessionRepository` — AUDIT (bespoke)

**Pattern**: Bespoke decorator similar to `TradeAuditDecorator` (Wave 7 slice 7b.1), but using `PlannerStatus` for the `IsTerminated` reflection rule.

- Forward all reads (`GetByIdAsync`, `ListByUserAndWeekAsync`, `ExistsForDateAsync`, `GetWeekComparisonAsync`) — no audit.
- `AddAsync(PlannerSession)` → `Created` audit.
- `UpdateAsync(PlannerSession)` → `Updated` audit with diff. **When `Status == PlannerStatus.Cancelled`** (via reflection: `Status.ToString() is "Cancelled"`), upgrade to `AuditAction.Deleted` per the `DecoratedRepository<T>.IsTerminated` rule. `IsOwner` check on `session.UserId`.
- Forward the `Update` for `MarkCompleted/MarkSkipped/MarkCancelled` domain ops (handlers call `UpdateAsync` after mutating status). The decorator captures the status transition via the standard `IsTerminated` reflection.

**Interface surgery**: 0. The interface already exposes `UpdateAsync(PlannerSession, ct)` — no rename needed.

**Forecast**: ~5 integration scenarios + 0 contract = **5 scenarios**.

### 3. `IPreTradeChecklistRepository` — AUDIT (bespoke, write-once)

**Pattern**: Smallest decorator in the wave. The repo has only `AddAsync` + `ListByUserIdAsync`. The checklist is write-once per the entity docstring ("UNA fila por trade — enforced por UNIQUE INDEX sobre trade_id en la DB. La API no expone UPDATE del checklist"). No Update, no Delete.

- Forward `ListByUserIdAsync` — no audit.
- `AddAsync(PreTradeChecklist)` → `Created` audit on the checklist.
- No `UpdateAsync` or `DeleteAsync` to wrap (interface doesn't expose them; aggregate is immutable post-Create except for `AttachAIRiskAdvisory` which is called in-memory on the same UoW, not via a repo UpdateAsync).

**Interface surgery**: 0. The interface shape already matches the audit intent.

**Forecast**: ~3 integration scenarios + 0 contract = **3 scenarios** (smaller because only AddAsync is audited).

### 4. `IAccountRepository` — AUDIT (generic + rename)

**Pattern**: Standard generic `DecoratedRepository<Account>` wrapper, mirroring `UserAuditDecorator` (Wave 7 slice 7a.1). The interface has `FindByIdAsync` (not `GetByIdAsync`) + `ListByUserIdAsync` + `AddAsync` + `RemoveAsync`. Two blockers vs. the generic shape:

1. **Rename**: `RemoveAsync(Account, ct)` → `DeleteAsync(Account, ct)` (same Wave 7 7b.1 Trade rename pattern). 1 handler call site: `src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Accounts/DeleteAccount/DeleteAccountHandler.cs:47`. Atomic in-slice rename.
2. **Cross-user check**: The decorator must verify `account.UserId == CurrentUserId` before allowing `UpdateAsync`/`DeleteAsync`. The `Account` aggregate's `UpdateMetadata` / `Deactivate` / `Reactivate` mutators all carry the UserId; the handler is responsible for ownership, but the decorator enforces it as defense-in-depth.

**Interface surgery**: 1 (rename `RemoveAsync` → `DeleteAsync`).
**Contract scenarios**: **+2** (per forecasting rule: `RemoveAsync_IsNotOnInterface_AfterRename` + `DeleteAsync_Exists_OnInterface`).

**Forecast**: ~5 integration scenarios + 2 contract = **7 scenarios**.

### 5. `IInstrumentRepository` — AUDIT (generic + rename)

**Pattern**: Same as Account — standard generic `DecoratedRepository<Instrument>` wrapper. The interface has `FindByIdAsync` + `FindBySymbolAsync` + `ListActiveAsync` + `ListAllAsync` + `AddAsync` + `RemoveAsync`.

**Interface surgery**: 1 (rename `RemoveAsync` → `DeleteAsync`). 1 handler call site: `src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Instruments/DeleteInstrument/DeleteInstrumentHandler.cs:45`.

**Important distinction from Account**: Instrument is **not user-owned** in the strict sense — it's a shared reference data table (entity docstring: "NO es Aggregate Root: es una Entity compartida por todos los usuarios"). All users see the same instruments. The `IsOwner` cross-tenant check does NOT apply — there's no `instrument.UserId` (only `IsActive`). The decorator emits audit rows for `Add`/`Update`/`Delete` but does NOT enforce user-scope (admin actions on the catalog are legitimate and don't represent cross-tenant access). Mirror the `TenantAuditDecorator` precedent: cross-tenant isolation is a non-concern for the instrument catalog (Tenant IS the tenant boundary; Instrument is a catalog entity).

**Forecast**: ~5 integration scenarios + 2 contract = **7 scenarios**.

### 6. `IAlertRepository` — AUDIT (bespoke)

**Pattern**: Bespoke decorator with a critical detail — `AddAsync` returns `bool` (true = inserted, false = deduped by `ux_alerts_user_rule_day` UNIQUE INDEX). The decorator must:

- Forward `ListByUserAsync` + `GetByIdAsync` — no audit.
- `AddAsync(Alert)` → when `true` (inserted), emit `Created` audit. When `false` (deduped), emit **NO** audit row (the row was not created; the existing row's audit history is preserved).
- `UpdateAsync(Alert)` → `Updated` audit with diff. Cross-tenant `IsOwner` check on `alert.UserId`.
- `Update` is called after `Acknowledge()` — the decorator captures the `AcknowledgedAt: null → now` transition. No `IsTerminated` reflection needed (Alert has no Status enum).

**Interface surgery**: 0. The interface shape already supports the decorator (AddAsync returns bool is just a signature detail; no rename needed).

**Forecast**: ~5 integration scenarios + 0 contract = **5 scenarios**.

### 7. `ISubscriptionAdminRepository` — **SKIP**

**Rationale**: The interface has **NO mutation methods at all** — only reads + loads. All subscription mutations flow through `ISubscriptionAdminUnitOfWork.AddHistoryEntry` + `SaveChangesAsync` + the `Subscription` aggregate's own `ChangeTier`/`Cancel`/`ExtendTrial`/`SyncFromStripe` methods.

The `Subscription` aggregate itself is **already audited** by Wave 6's `SubscriptionAuditDecorator` (`src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Audit/SubscriptionAuditDecorator.cs`), which wraps `ISubscriptionRepository` — a different interface that the admin write path doesn't even use (the admin path uses `ISubscriptionAdminRepository` for reads + `LoadForUpdateAsync` to get the entity, then mutates via the aggregate's own methods).

Adding an audit decorator on `ISubscriptionAdminRepository` would be a no-op (no `AddAsync`/`UpdateAsync`/`DeleteAsync` to decorate) — at most it would forward reads without audit. The right answer: **skip with documented rationale**. `ISubscriptionAdminRepository` has nothing to audit; the Subscription aggregate's mutations are already captured.

**Alternative considered + rejected**: Wrap `ISubscriptionAdminUnitOfWork.SaveChangesAsync` instead. Rejected because the SubscriptionAuditDecorator already captures the mutations at the aggregate level. Wrapping the UoW would emit duplicate audit rows for the same transitions.

### 8. `IStripeCustomerRepository` — AUDIT (bespoke, immutable)

**Pattern**: Bespoke decorator mirroring a simplified `TenantAuditDecorator` shape. The repo has only `AddAsync` + 2 reads. The `StripeCustomer` aggregate is immutable post-Create (entity docstring: "Aggregate is immutable after Create — no mutators").

- Forward `GetByUserIdAsync` + `GetByStripeCustomerIdAsync` — no audit.
- `AddAsync(StripeCustomer)` → `Created` audit. Cross-tenant `IsOwner` check on `customer.UserId`.
- No `UpdateAsync` or `DeleteAsync` to wrap (interface doesn't expose them; aggregate has no mutators).

**Interface surgery**: 0. The interface shape already matches the audit intent.

**Forecast**: ~3 integration scenarios + 0 contract = **3 scenarios** (smaller because only AddAsync is audited).

### 9. `IStripeWebhookEventRepository` — **SKIP**

**Rationale**: Append-only per Wave 6's `ISoftDelete` docstring ("ISoftDelete is opt-in per aggregate: not all aggregates are soft-deleteable (e.g., AuditEvent, StripeWebhookEvent are append-only and never deleted)").

The entity itself IS the audit log equivalent: the table `billing.stripe_webhook_events` records every received webhook with `EventId`, `EventType`, `PayloadJson`, `ReceivedAt`, `ProcessedAt`, `ProcessingError`. Adding an `AuditEvent` row for each `StripeWebhookEvent` row would be doubly-recorded noise — compliance officers already query `billing.stripe_webhook_events` directly for webhook audit trails.

The `UpdateAsync` flow (for `MarkProcessed`/`MarkFailed`) is system-internal dispatch bookkeeping, not a user mutation. It does NOT carry user intent and does NOT warrant an audit row.

**Skip with documented rationale.** No code change; no test added.

---

## Pattern decisions summary

### Decorator count + shape

| Slice | Decorator | Pattern | LOC estimate |
|---|---|---|---|
| 8a.1 | `AccountAuditDecorator` | Generic `DecoratedRepository<Account>` (extend interface with `IRepository<Account>` shape) | ~100 LOC |
| 8a.1 | `InstrumentAuditDecorator` | Generic `DecoratedRepository<Instrument>` (similar to Account; no IsOwner — catalog entity) | ~100 LOC |
| 8a.2 | `AlertAuditDecorator` | Bespoke (AddAsync returns bool; IsOwner on UpdateAsync) | ~200 LOC |
| 8a.2 | `TradeReviewAuditDecorator` | Bespoke (cross-user; forward attachment ops without audit) | ~250 LOC |
| 8a.3 | `PlannerSessionAuditDecorator` | Bespoke (IsTerminated reflection on `PlannerStatus.Cancelled`) | ~200 LOC |
| 8a.3 | `PreTradeChecklistAuditDecorator` | Bespoke write-once (only AddAsync audited) | ~100 LOC |
| 8b.1 | `StripeCustomerAuditDecorator` | Bespoke immutable (only AddAsync audited) | ~120 LOC |
| 8b.2 | (none — reconciliation doc) | n/a | ~50 LOC doc-only |

**Total decorator LOC**: ~1,120 LOC. Tests + DI wiring + comments add another ~1,500 LOC. **Total LOC: ~2,700 LOC**, aligning with the orchestrator's ~2,750 LOC forecast.

### Bespoke vs. generic per repo

| Repo | Pattern | Why |
|---|---|---|
| TradeReview | **Bespoke** | Cross-user scope on every read; secondary attachment ops; cannot extend `IRepository<T>` without security regression |
| PlannerSession | **Bespoke** | Cross-user scope; bespoke read methods (`ListByUserAndWeekAsync`, `ExistsForDateAsync`, `GetWeekComparisonAsync`); needs `IsTerminated` reflection on `PlannerStatus.Cancelled` |
| PreTradeChecklist | **Bespoke** (write-once) | Only `AddAsync` + `ListByUserIdAsync`; no Update/Delete to wrap; generic shape adds noise |
| Account | **Generic** (`DecoratedRepository<Account>`) | After `RemoveAsync → DeleteAsync` rename + additive `IRepository<Account>` extension, fits the canonical shape |
| Instrument | **Generic** (`DecoratedRepository<Instrument>`) | Same as Account; no IsOwner (catalog entity) |
| Alert | **Bespoke** | Cross-user scope; dedup-bounded `AddAsync` returns bool; bespoke read methods |
| StripeCustomer | **Bespoke** (immutable) | Only `AddAsync` + 2 reads; aggregate immutable post-Create |

### Wave 6/7 patterns reused (no new patterns introduced)

- Generic `DecoratedRepository<T>` from `Shared.Infrastructure` (Wave 7 7a.0).
- Cross-tenant `IsOwner` check (Wave 6 importJob + subscription; Wave 7 7a.1/7b.1).
- `AuditAction.Denied` + `AuditAction.Failed` enum values (Wave 7 7a.1; migration 0029).
- `IsTerminated` reflection rule for `Status ∈ {Cancelled, Terminated, Expired}` (Wave 6 6d.2).
- `ResolveBefore` via EF `ChangeTracker.OriginalValues` with JSON snapshot fallback (Wave 7 7b.1 + 7b.2).
- Bespoke `DeleteAsync` defensive stub for non-deletable aggregates (Wave 7 7a.1).

**No new infrastructure** — no enum extension, no migration, no Shared.Kernel change.

---

## Slicing strategy

### Forecast summary

Per Wave 7 verify-report SUGGESTION #2 (`forecast = integration_scenarios + 2 * contract_scenarios_per_interface_surgery`):

| Slice | Decorators | Integration scenarios | Contract scenarios | Surgery | Total |
|---|---|---:|---:|---:|---:|
| **8a.0** | (refactor — verify all modules have explicit `Scrutor` PackageReference) | 0 | 0 | 0 | **0** |
| **8a.1** | Account + Instrument (standard; 2 renames) | 10 | 4 | 2 | **14** |
| **8a.2** | Alert + TradeReview (bespoke) | 10 | 0 | 0 | **10** |
| **8a.3** | PlannerSession + PreTradeChecklist (bespoke) | 8 | 0 | 0 | **8** |
| **8b.1** | StripeCustomer (bespoke immutable) | 3 | 0 | 0 | **3** |
| **8b.2** | Reconciliation — SKIP SubscriptionAdmin + StripeWebhookEvent with documented rationale | 0 | 2 (rationale tests / source-inspection) | 0 | **2** |
| **Total** | 7 audited decorators | 31 | 6 | 2 | **37 scenarios / ~2,700 LOC** |

This is ~37 spec scenarios vs. the orchestrator's ~44 estimate. The delta is because (a) PreTradeChecklist + StripeCustomer each audit only one mutation method (AddAsync), reducing their scenarios to ~3 vs. the standard ~5; (b) Wave 8 has no enum extension or migration (no migration scenarios).

### Proposed slice decomposition

#### Slice 8a.0 — Refactor: ensure Scrutor is wired (~100 LOC, ~4 paths)

Pre-check only (Wave 7 7a.0 already added explicit `<PackageReference Include="Scrutor" Version="4.2.2" />` to both csprojs). This slice is a sanity confirmation:

- [ ] 1.1 Confirm `Trading.Infrastructure.csproj` has explicit Scrutor 4.2.2 reference (lines 25-29 of `JadeCapital.Trading.Infrastructure.csproj`).
- [ ] 1.2 Confirm `Billing.Infrastructure.csproj` has explicit Scrutor 4.2.2 reference (lines 19-21 of `JadeCapital.Billing.Infrastructure.csproj`).
- [ ] 1.3 Confirm `Identity.Infrastructure.csproj` reference pattern is unchanged from Wave 7 (Scrutor was never explicitly added there because Identity has no `services.Decorate` calls — only typed decorators that are wired manually).
- [ ] 1.4 Run `git diff --stat 21430aa..HEAD -- src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/JadeCapital.Identity.Infrastructure.csproj` to verify zero changes since Wave 7 archive.

**Verdict**: If all 4 checks pass with zero changes, **8a.0 is a no-op** and the slice collapses to a "verified-no-op" entry in tasks.md. Skip it entirely from the PR chain. If any check fails (e.g., Scrutor ref accidentally dropped), restore it.

#### Slice 8a.1 — Trading: Account + Instrument (standard + rename) (~600 LOC, ~12 paths, ~12 tests)

- Phase 1: Interface surgery (TDD).
  - [ ] 1.1 RED: `IAccountRepositoryContractTests.RemoveAsync_IsNotOnInterface_AfterRename` + `DeleteAsync_Exists_OnInterface` (2 contract tests).
  - [ ] 1.2 RED: `IInstrumentRepositoryContractTests.RemoveAsync_IsNotOnInterface_AfterRename` + `DeleteAsync_Exists_OnInterface` (2 contract tests).
  - [ ] 1.3 GREEN: Rename `IAccountRepository.RemoveAsync(Account, ct)` → `DeleteAsync(Account, ct)`; update `AccountRepository` impl in `Repositories.cs:147`; update `DeleteAccountHandler.cs:47` call site.
  - [ ] 1.4 GREEN: Same rename for `IInstrumentRepository.RemoveAsync` → `DeleteAsync`; update `InstrumentRepository` impl in `Repositories.cs:194`; update `DeleteInstrumentHandler.cs:45` call site.
- Phase 2: Decorators (TDD).
  - [ ] 2.1 RED: `AccountRepositoryIntegrationTests` (5 scenarios — Created, Updated with diff, DeleteAsync writes Deleted, FindByIdAsync is not audited, IsOwner cross-tenant check).
  - [ ] 2.2 GREEN: `AccountAuditDecorator.cs` in `Trading.Infrastructure/Audit/`. Generic `DecoratedRepository<Account>` shape. Extend `IAccountRepository` with `IRepository<Account>` (add `GetByIdAsync(Guid, ct)` if missing — currently has `FindByIdAsync(Guid, ct)`; keep both, decorator forwards `FindByIdAsync` to inner).
  - [ ] 2.3 RED: `InstrumentRepositoryIntegrationTests` (5 scenarios — same shape).
  - [ ] 2.4 GREEN: `InstrumentAuditDecorator.cs` in `Trading.Infrastructure/Audit/`. Generic `DecoratedRepository<Instrument>`. **No `IsOwner`** (catalog entity).
- Phase 3: DI wiring.
  - [ ] 3.1 `services.Decorate<IAccountRepository, AccountAuditDecorator>()` in `TradingModuleRegistration.cs`.
  - [ ] 3.2 `services.Decorate<IInstrumentRepository, InstrumentAuditDecorator>()` in `TradingModuleRegistration.cs`.
- Phase 4: Validate.
  - [ ] 4.1 Build green.
  - [ ] 4.2 All Account + Instrument tests pass + 30 cumulative audit-related tests still pass.
  - [ ] 4.3 `git grep "RemoveAsync" src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/ src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Persistence/Repositories.cs` → no results (rename complete).
  - [ ] 4.4 `git grep "_accounts.RemoveAsync\|_instruments.RemoveAsync" src/2.Modules/Trading/` → no results (handler call sites updated).

#### Slice 8a.2 — Trading: Alert + TradeReview (bespoke) (~600 LOC, ~10 paths, ~10 tests)

- Phase 1: Decorators (TDD).
  - [ ] 1.1 RED: `AlertRepositoryIntegrationTests` (5 scenarios — Created on insert=true, NO audit on dedup=false, Updated with diff on Acknowledge, FindByIdAsync is not audited, IsOwner cross-tenant check).
  - [ ] 1.2 GREEN: `AlertAuditDecorator.cs` in `Trading.Infrastructure/Audit/`. Bespoke (mirrors `TradeAuditDecorator` shape).
  - [ ] 1.3 RED: `TradeReviewRepositoryIntegrationTests` (5 scenarios — Created, Updated with diff + IsOwner, attachment ops forwarded without audit, FindByIdAsync is not audited, FindByTradeIdAsync is not audited).
  - [ ] 1.4 GREEN: `TradeReviewAuditDecorator.cs` in `Trading.Infrastructure/Audit/`. Bespoke. Forward `AddAttachmentAsync`/`UpdateAttachmentAsync`/`RemoveAttachmentAsync` without audit.
- Phase 2: DI wiring.
  - [ ] 2.1 `services.Decorate<IAlertRepository, AlertAuditDecorator>()`.
  - [ ] 2.2 `services.Decorate<ITradeReviewRepository, TradeReviewAuditDecorator>()`.
- Phase 3: Validate.

#### Slice 8a.3 — Trading: PlannerSession + PreTradeChecklist (bespoke) (~700 LOC, ~12 paths, ~12 tests)

- Phase 1: Decorators (TDD).
  - [ ] 1.1 RED: `PlannerSessionRepositoryIntegrationTests` (5 scenarios — Created, Updated with diff, Cancelled transition upgrades to Deleted via `IsTerminated` reflection, IsOwner check, GetByIdAsync/ListByUserAndWeekAsync/GetWeekComparisonAsync/ExistsForDateAsync are not audited).
  - [ ] 1.2 GREEN: `PlannerSessionAuditDecorator.cs` in `Trading.Infrastructure/Audit/`. Bespoke. Reimplement `IsTerminated` reflection for `PlannerStatus.Cancelled` (the enum value matches the Wave 6 rule).
  - [ ] 1.3 RED: `PreTradeChecklistRepositoryIntegrationTests` (3 scenarios — Created, ListByUserIdAsync is not audited, no Update/Delete methods exist — contract pin).
  - [ ] 1.4 GREEN: `PreTradeChecklistAuditDecorator.cs` in `Trading.Infrastructure/Audit/`. Bespoke write-once (only AddAsync wraps; reads forwarded without audit).
- Phase 2: DI wiring.
  - [ ] 2.1 `services.Decorate<IPlannerSessionRepository, PlannerSessionAuditDecorator>()`.
  - [ ] 2.2 `services.Decorate<IPreTradeChecklistRepository, PreTradeChecklistAuditDecorator>()`.
- Phase 3: Validate.

#### Slice 8b.1 — Billing: StripeCustomer (bespoke immutable) (~700 LOC, ~12 paths, ~12 tests)

- Phase 1: Decorator (TDD).
  - [ ] 1.1 RED: `StripeCustomerRepositoryIntegrationTests` (3 scenarios — Created on AddAsync, GetByUserIdAsync/GetByStripeCustomerIdAsync are not audited, no Update/Delete methods exist — contract pin).
  - [ ] 1.2 GREEN: `StripeCustomerAuditDecorator.cs` in `Billing.Infrastructure/Audit/`. Bespoke. Only AddAsync wrapped.
- Phase 2: DI wiring.
  - [ ] 2.1 `services.Decorate<IStripeCustomerRepository, StripeCustomerAuditDecorator>()` in `BillingModuleRegistration.cs`.
- Phase 3: Validate.

#### Slice 8b.2 — Billing: SKIP reconciliation doc (~50 LOC, ~2 paths, 0 tests)

Final reconciliation slice that documents the 2 SKIPs with explicit rationale. NO code changes; NO new tests; just the rationale baked into `tasks.md` + the proposal's "Out of Scope" section + the spec's REMOVED Requirements (with reason).

- [ ] 1.1 Verify `ISubscriptionAdminRepository` has no mutation methods: `git grep -E "Task (Add|Update|Delete)Async" src/2.Modules/Billing/JadeCapital.Billing.Application/Features/Subscriptions/ISubscriptionAdminRepository.cs` → no matches.
- [ ] 1.2 Verify `SubscriptionAuditDecorator` (Wave 6) still covers the Subscription aggregate's mutations: `git grep "class SubscriptionAuditDecorator" src/` → single file (unchanged from Wave 6).
- [ ] 1.3 Verify `StripeWebhookEvent` docstring still says "append-only after Record": `grep -n "append-only" src/2.Modules/Billing/JadeCapital.Billing.Domain/Stripe/StripeWebhookEvent.cs` → matches.
- [ ] 1.4 Add the 2 SKIP rationales to `openspec/changes/2026-08-19-wave8-audit-coverage-extended/proposal.md` §"Out of Scope" + `specs/soft-delete-audit/spec.md` §"## REMOVED Requirements" (with `Reason:` block per OpenSpec convention).

**Forecast guard lines (per `sdd-phase-common.md` §E)**:

- Decision needed before apply: **Yes** (size:exception expected per Wave 5/6/7 precedent; explicit user acceptance per slice recommended).
- Chained PRs recommended: **Yes** (5-6 PRs via `feature-branch-chain`; each targets the immediate previous PR branch).
- 400-line budget risk: **High** — each slice will exceed the 400-line PR review budget per Wave 7 7a.1 (1412 LOC) / 7b.1 (1699 LOC) / 7b.2 (991 LOC) precedent. `size:exception` per slice is the expected resolution (the `feature-branch-chain` strategy keeps each PR's reviewer-load bounded at the PR-level scope of work, not the cumulative chain scope).

### Test strategy

- **In-memory SQLite** for all integration tests (Wave 5/6/7 precedent; Testcontainers Postgres not in sandbox per verify-report carry-forward WARNING #1).
- Tests live in **`tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/`** alongside the Wave 6/7 audit tests — the Identity test project owns the `AuditDbContext` + `AuditLogger` registration per Wave 6's cross-module edge. The Trading + Billing integration tests are in Identity.UnitTests for the same reason.
- **Per-decorator test fixture** mirrors the Wave 7 pattern: wire the typed `DbContext` (TradingDbContext or BillingDbContext for production; SQLite-friendly helper context for tests per Wave 6 `DbContext` parameter typed as base class) + `AuditDbContext` + `IAuditLogger` + `ITenantContext` + `IClock`.

---

## Constraints + Risks

1. **size:exception precedent**: Wave 5/6/7 all needed `size:exception` per slice. Expect Wave 8 to need it for each apply slice — explicit user acceptance per slice recommended. The 400-line PR review budget is exceeded by every Wave 7 slice (7a.0 815 mechanical / 37 authored; 7a.1 1412; 7b.1 1699; 7b.2 991).

2. **2 SKIPs need explicit rationale**:
   - `ISubscriptionAdminRepository` — no mutation methods; Subscription aggregate already audited by Wave 6's `SubscriptionAuditDecorator`. Wrapping would be a no-op.
   - `IStripeWebhookEventRepository` — append-only per Wave 6 proposal (`ISoftDelete` docstring: "StripeWebhookEvent are append-only and never deleted"). Adding audit rows on top of the existing `billing.stripe_webhook_events` table would be doubly-recorded noise.

3. **`RemoveAsync → DeleteAsync` rename (Account + Instrument)**: 1 handler call site each (`DeleteAccountHandler.cs:47`, `DeleteInstrumentHandler.cs:45`). Atomic in-slice rename per Wave 7 7b.1 Trade pattern. No tests expected to break (handler tests don't assert the method name).

4. **Bespoke decorator template count**: Wave 8 introduces **4 bespoke typed decorators** (TradeReview, PlannerSession, PreTradeChecklist, Alert) + 1 write-once (PreTradeChecklist) + 1 immutable (StripeCustomer). Each ~200 LOC. This is the largest bespoke-dec-decorator wave since the pattern was introduced. The `TradeAuditDecorator` + `JournalEntryAuditDecorator` are the templates — Wave 8 reuses their structure verbatim.

5. **Testcontainers Postgres not in sandbox** (carry-forward from Wave 5/6/7): integration tests use SQLite-in-memory with the `DbContext` typed as `base` parameter. Per Wave 7 verify-report WARNING #1, **no blocker**.

6. **`IsTerminated` reflection applies only to PlannerSession**: `PlannerStatus.Cancelled` matches the Wave 6 rule (`Status ∈ {Cancelled, Terminated, Expired}`). The other 6 audited entities have no enum Status field that matches — they emit `AuditAction.Updated` on mutations, not `AuditAction.Deleted`.

7. **TradeAttachment is intentionally NOT audited separately** — it's a child entity of TradeReview. The 9-repo target list does NOT include TradeAttachment. Forwarding attachment ops without audit is the right call (the review's Update events + handler-side MinIO cleanup log already capture attachment lifecycle).

8. **No new Shared.Kernel change**: no enum extension, no migration. Wave 7's `AuditAction.Denied` + `AuditAction.Failed` + migration 0029 already cover all 7 new decorators' audit-action needs.

9. **Cross-module DI edges unchanged**: Trading.Infrastructure + Billing.Infrastructure already reference `Shared.Infrastructure` (via Wave 7 7a.0). The new decorators live in their own module's `Audit/` folder (Trading → Trading, Billing → Billing) — same pattern as Wave 7. No `Identity.Infrastructure` → `Trading.Infrastructure` circular dep risk.

10. **Forecast uncertainty**: ~2,700 LOC + ~37 scenarios is a lower bound. Wave 7's bespoke decorators (Trade 321 LOC, JournalEntry 343 LOC) ran 33-50% over their initial estimates. The 4 bespoke Wave 8 decorators may similarly overrun — explicit per-slice LOC tracking with `size:exception` per Wave 7 precedent.

---

## Ready for Proposal

**Yes.** The scope is bounded (7 decorators + 2 documented SKIPs), the slicing strategy respects module boundaries (3 Trading + 1 Billing + 1 reconciliation), the per-slice forecast respects the 32-path review contract, and the patterns are inherited verbatim from Wave 7's 4 bespoke templates + the canonical `DecoratedRepository<T>`.

The orchestrator should tell the user:
- Effective scope is **7 decorators + 2 SKIPs** (not 9 decorators as the SUGGESTION phrased it).
- Each slice will need `size:exception` per Wave 5/6/7 precedent.
- The 5-PR chain (8a.1 → 8a.2 → 8a.3 → 8b.1 → 8b.2) replaces the original 6-PR sketch (the proposed 8a.0 collapses to a verified-no-op since Wave 7 7a.0 already added explicit Scrutor references).
- Test strategy: in-memory SQLite per Wave 5/6/7 precedent (Testcontainers Postgres not in sandbox).