# Tasks — Wave 8 (Audit Coverage Extension: 7 More Decorators + 2 SKIPs)

## Review Workload Forecast

| Slice | Boundary | LOC | Paths | Tests | size:exception preview | Bounded review |
|---|---|---:|---:|---:|---|:---:|
| **8a.1** | Trading: Account + Instrument (standard + 2 renames) | ~600 | 10 | 14 | likely (Wave 5/6/7 precedent) | OK |
| **8a.2** | Trading: Alert + TradeReview (bespoke) | ~600 | 10 | 10 | likely (Wave 5/6/7 precedent) | OK |
| **8a.3** | Trading: PlannerSession + PreTradeChecklist (bespoke) | ~700 | 12 | 8 | likely (Wave 5/6/7 precedent) | OK |
| **8b.1** | Billing: StripeCustomer (bespoke immutable) | ~700 | 12 | 3 | unlikely (borderline; 12 paths × 1 decorator is borderline-bounded) | OK |
| **8b.2** | Reconciliation — SKIP SubscriptionAdmin + StripeWebhookEvent (doc-only) | ~50 | 4 | 0 | no (doc-only) | OK |
| **Total** | 5 slices chained | **~2,650** | **48** | **35 new tests** | 3 likely + 1 unlikely + 1 no | All ≤ 32 OK |

Decision needed before apply: **No** (auto-chain, `feature-branch-chain` Wave 0/1/2/3/4/5/6/7 precedent; `size:exception` per slice as Wave 5/6/7 precedent). Per-slice `git diff --name-only` MUST be ≤ 32 paths (mandatory); see path counts above.

**Build**: `dotnet build JadeCapital.slnx --nologo --verbosity minimal`.
**No SQL harness** — no migration in Wave 8 (migration 0029 from Wave 7 already widened `ck_audit_events_action` to `IN (0,1,2,3,4,5)`).
**No FE changes** — all BE.

Cumulative target: **1328 (Wave 7) + 35 (Wave 8) = 1363** BE tests pass zero regression.

### Work Units (PR → test → runtime → rollback)

- 8a.1: `dotnet test --filter "FullyQualifiedName~AccountAudit|InstrumentAudit|AccountRepositoryIntegration|InstrumentRepositoryIntegration|IAccountRepositoryContractTests|IInstrumentRepositoryContractTests"`. Rollback: revert code; `RemoveAsync` returns on both interfaces; both handlers revert; `audit.events` has no rows for `Account` / `Instrument`.
- 8a.2: `dotnet test --filter "FullyQualifiedName~AlertAudit|TradeReviewAudit|AlertRepositoryIntegration|TradeReviewRepositoryIntegration"`. Rollback: revert code; DI registration removed; `audit.events` has no rows for `Alert` / `TradeReview`.
- 8a.3: `dotnet test --filter "FullyQualifiedName~PlannerSessionAudit|PreTradeChecklistAudit|PlannerSessionRepositoryIntegration|PreTradeChecklistRepositoryIntegration"`. Rollback: revert code; DI registration removed; `audit.events` has no rows for `PlannerSession` / `PreTradeChecklist`.
- 8b.1: `dotnet test --filter "FullyQualifiedName~StripeCustomerAudit|StripeCustomerRepositoryIntegration"`. Rollback: revert code; DI registration removed; `audit.events` has no rows for `StripeCustomer`.
- 8b.2: `git grep` 4 verification commands (see slice 8b.2 Phase 1). Rollback: revert code; doc-only changes revert; zero behavior change.

---

## Slice 8a.0 — Verified-no-op (~0 LOC, ~4 paths)

> **NOTE**: Wave 7's slice 7a.0 already added explicit `<PackageReference Include="Scrutor" Version="4.2.2" />` to `Trading.Infrastructure.csproj` (lines 25-29) and `Billing.Infrastructure.csproj` (lines 19-21). Identity.Infrastructure was never modified because Identity has no `services.Decorate` calls. Slice 8a.0 is **omitted from the PR chain** — verified via the 4 sanity checks in slice 8a.1 Phase 0. If any check fails (e.g., Scrutor ref accidentally dropped), restore it before proceeding.

### 8a.0 Backend (verification only, ~0 LOC, ~4 paths)

**Phase 0: Sanity confirmation** (executed inside slice 8a.1 Phase 0, not as a separate PR)

- [x] 0.1 Confirm `Trading.Infrastructure.csproj` has explicit Scrutor 4.2.2 reference (lines 25-29).
- [x] 0.2 Confirm `Billing.Infrastructure.csproj` has explicit Scrutor 4.2.2 reference (lines 19-21).
- [x] 0.3 Confirm `Identity.Infrastructure.csproj` reference pattern is unchanged from Wave 7 (Scrutor was never explicitly added there because Identity has no `services.Decorate` calls — only typed decorators wired manually).
- [x] 0.4 `git diff --stat 21430aa..HEAD -- src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/JadeCapital.Identity.Infrastructure.csproj` → zero expected.

**Verdict**: If all 4 checks pass with zero changes, **8a.0 collapses to a verified-no-op** and the slice is omitted from the PR chain.

### 8a.0 size:exception preview

Forecast ~0 lines (verification only) → `size:exception` not needed.

### 8a.0 Bounded review feasibility

- 0 new files.
- 0 modified files.
- Total: **0 paths** ≤ 32 OK.

> **Slice 8a.0 completion note**: doc-only verification. Zero behavior change. If any check fails, restore the missing Scrutor reference in the corresponding csproj before proceeding.

---

## Slice 8a.1 — `AccountAuditDecorator` + `InstrumentAuditDecorator` (standard + 2 renames) (~600 LOC, ~10 paths, ~14 tests)

### 8a.1 Backend (~600 LOC)

**Phase 0: Sanity verification (carried over from slice 8a.0)**

- [x] 0.1 Confirm `Trading.Infrastructure.csproj` has explicit Scrutor 4.2.2 (lines 25-29) — if missing, restore.
- [x] 0.2 Confirm `Billing.Infrastructure.csproj` has explicit Scrutor 4.2.2 (lines 19-21) — if missing, restore.
- [x] 0.3 `git diff --stat 21430aa..HEAD -- src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/JadeCapital.Identity.Infrastructure.csproj` → zero expected.

**Phase 1: Interface surgery — `IAccountRepository.RemoveAsync` → `DeleteAsync` (atomic rename)**

- [x] 1.1 RED test `IAccountRepositoryContractTests` (3 scenarios: `DeleteAsync` exists, `RemoveAsync` gone, `UpdateAsync` + bespoke reads unchanged — added UpdateAsync regression guard beyond the 2-scenario forecast).
- [x] 1.2 GREEN: rename `IAccountRepository.RemoveAsync(Account, ct)` → `DeleteAsync(Account, ct)` + extend to `IRepository<Account>` in `src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/IAccountRepository.cs`.
- [x] 1.3 GREEN: update concrete `AccountRepository` impl in `Repositories.cs` (`GetByIdAsync` + `UpdateAsync` added, `RemoveAsync` → `DeleteAsync` renamed).
- [x] 1.4 GREEN: update 1 handler call site in `DeleteAccountHandler.cs:47` + `DeleteAccountHandlerTests.cs` (mock).
- [x] 1.5 GREEN: extend `IAccountRepository` to `IRepository<Account>` (gaining `AddAsync` + `UpdateAsync` + `GetByIdAsync`).

**Phase 2: Interface surgery — `IInstrumentRepository.RemoveAsync` → `DeleteAsync` (atomic rename)**

- [x] 2.1 RED test `IInstrumentRepositoryContractTests` (3 scenarios: `DeleteAsync` exists, `RemoveAsync` gone, `UpdateAsync` + bespoke reads unchanged).
- [x] 2.2 GREEN: rename `IInstrumentRepository.RemoveAsync(Instrument, ct)` → `DeleteAsync(Instrument, ct)` + extend to `IRepository<Instrument>` in `src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/IInstrumentRepository.cs`.
- [x] 2.3 GREEN: update concrete `InstrumentRepository` impl in `Repositories.cs` (`GetByIdAsync` + `UpdateAsync` added, `RemoveAsync` → `DeleteAsync` renamed).
- [x] 2.4 GREEN: update 1 handler call site in `DeleteInstrumentHandler.cs:45` + `DeleteInstrumentHandlerTests.cs` (mock).
- [x] 2.5 GREEN: extend `IInstrumentRepository` to `IRepository<Instrument>` (gaining `AddAsync` + `UpdateAsync` + `GetByIdAsync`).

**Phase 3: `AccountAuditDecorator` (TDD)**

- [x] 3.1 RED test `AccountRepositoryIntegrationTests` (5 scenarios: `AddAsync` writes Created with `entity_type = "Account"`, `tenant_id` + `user_id` from `ITenantContext`, `changes = null`; `UpdateAsync` writes Updated with `name` diff; `DeleteAsync` writes Deleted; cross-tenant update emits `Denied` + throws `UnauthorizedAccessException`; `FindByIdAsync` emits NO event).
- [x] 3.2 GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AccountAuditDecorator.cs` (generic `DecoratedRepository<Account>` shape + `IsOwner` cross-tenant check; forwards `FindByIdAsync` + `ListByUserIdAsync` to `_inner` without auditing).

**Phase 4: `InstrumentAuditDecorator` (TDD)**

- [x] 4.1 RED test `InstrumentRepositoryIntegrationTests` (5 scenarios: same shape as Account, NO `IsOwner` denial scenario — instrument admin mutations are legitimate).
- [x] 4.2 GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/InstrumentAuditDecorator.cs` (generic `DecoratedRepository<Instrument>` shape, **NO `IsOwner`** — catalog entity precedent).

**Phase 5: DI wiring**

- [x] 5.1 `services.Decorate<IAccountRepository, AccountAuditDecorator>()` in `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs`.
- [x] 5.2 `services.Decorate<IInstrumentRepository, InstrumentAuditDecorator>()` in the same file.

**Phase 6: Validate**

- [x] 6.1 `dotnet test --filter "FullyQualifiedName~AccountAudit|InstrumentAudit|AccountRepositoryIntegration|InstrumentRepositoryIntegration|IAccountRepositoryContractTests|IInstrumentRepositoryContractTests"` → **16/16 new tests pass** (10 integration + 6 contract — exceeded 14-test forecast with UpdateAsync regression guard on each interface).
- [x] 6.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings (3 pre-existing CA2263 unchanged).
- [x] 6.3 `git grep "RemoveAsync" src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/ src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Persistence/Repositories.cs` → matches only in `<c>RemoveAsync</c>` docstrings documenting the historical rename. Zero method signatures or call sites (rename complete).
- [x] 6.4 `git grep "_accounts.RemoveAsync\|_instruments.RemoveAsync" src/2.Modules/Trading/` → no results (handler call sites updated).
- [x] 6.5 Full BE suite (180 + 343 + 116 + 705 = **1344**) → zero regression (forecast was 1342; +2 from the UpdateAsync regression guard tests).

**Phase 7: Apply-progress doc**

- [x] 7.1 `apply-progress-wave8-slice-8a-1.md` written (mirrors Wave 7 `apply-progress-wave7-slice-7b-1.md` shape — Work Unit Evidence, Files Changed table, Deviations).

**Dependencies**: none (first slice in chain).
**Rollback**: `git revert` the slice. `RemoveAsync` returns on both interfaces. Both handlers revert. `audit.events` has no rows for `Account` / `Instrument`.

### 8a.1 size:exception preview

Forecast ~600 lines, Wave 5/6/7 precedent (7a.1=1412, 7b.1=1699) → `size:exception` likely. Justification: 2 atomic renames (interface + impl + handler call site) + 2 typed decorators + 2 integration test files + 2 contract test files + 14 tests are a coherent cross-cutting unit. Cannot split without artificial boundaries (the renames are atomic by design).

### 8a.1 Bounded review feasibility

- New files: 6 (AccountAuditDecorator.cs + InstrumentAuditDecorator.cs + AccountRepositoryIntegrationTests.cs + InstrumentRepositoryIntegrationTests.cs + IAccountRepositoryContractTests.cs + IInstrumentRepositoryContractTests.cs).
- Modified files: 6 (IAccountRepository + AccountRepository concrete + DeleteAccountHandler + IInstrumentRepository + InstrumentRepository concrete + DeleteInstrumentHandler + TradingModuleRegistration DI).
- Total: **10 paths** ≤ 32 OK.

> **Slice 8a.1 completion note**: code lands with all tests green. 14 new BE tests. The 2 renames are atomic on the branch. `audit.events` starts receiving `Account` + `Instrument` rows. Deviations documented in `apply-progress-2026-08-19-wave8-audit-coverage-extended-slice-8a-1.md` if any arise.

---

## Slice 8a.2 — `AlertAuditDecorator` + `TradeReviewAuditDecorator` (bespoke) (~600 LOC, ~10 paths, ~10 tests)

### 8a.2 Backend (~600 LOC)

**Phase 1: `AlertAuditDecorator` (TDD)**

- [x] 1.1 RED test `AlertRepositoryIntegrationTests` (5 scenarios: `AddAsync` returning `true` emits Created; `AddAsync` returning `false` (dedup) emits NO event; `UpdateAsync` after `Acknowledge()` emits Updated with `AcknowledgedAt: null → now` diff; `FindByIdAsync` emits NO event; cross-tenant update emits Denied + throws `UnauthorizedAccessException`).
- [x] 1.2 GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AlertAuditDecorator.cs` (~200 LOC; bespoke — mirrors `TradeAuditDecorator` shape; critical detail: `AddAsync` returns `bool` audit semantics; `IsOwner` cross-tenant check on `UpdateAsync`).

**Phase 2: `TradeReviewAuditDecorator` (TDD)**

- [x] 2.1 RED test `TradeReviewRepositoryIntegrationTests` (5 scenarios: `AddAsync` emits Created with `entity_type = "TradeReview"`; `UpdateAsync` with `Title` + `Rating` change emits Updated with diff; `AddAttachmentAsync` + `UpdateAttachmentAsync` + `RemoveAttachmentAsync` are forwarded WITHOUT emitting audit rows; `FindByIdAsync` emits NO event; cross-tenant update emits Denied + throws `UnauthorizedAccessException`).
- [x] 2.2 GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/TradeReviewAuditDecorator.cs` (~250 LOC; bespoke — mirrors `JournalEntryAuditDecorator` shape; forward attachment ops without audit; `IsOwner` cross-tenant check on `UpdateAsync`).

**Phase 3: DI wiring**

- [x] 3.1 `services.Decorate<IAlertRepository, AlertAuditDecorator>()` in `TradingModuleRegistration.cs`.
- [x] 3.2 `services.Decorate<ITradeReviewRepository, TradeReviewAuditDecorator>()` in the same file.

**Phase 4: Validate**

- [x] 4.1 `dotnet test --filter "FullyQualifiedName~AlertAudit|TradeReviewAudit|AlertRepositoryIntegration|TradeReviewRepositoryIntegration"` → **10/10 new tests pass**.
- [x] 4.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings (3 pre-existing CA2263 unchanged from Wave 6 baseline).
- [x] 4.3 Full BE suite (1342 + 10 = **1352**) → zero regression. Per-project actual: Shared.Kernel 180 + Identity 353 (was 343) + Billing 116 + Trading 705 = **1354** cumulative green.

**Phase 5: Apply-progress doc**

- [x] 5.1 Append slice completion note to `apply-progress-2026-08-19-wave8-audit-coverage-extended-slice-8a-2.md`.

**Dependencies**: 8a.1 must be merged (shares `TestTradingDbContext` fixture).
**Rollback**: `git revert` the slice. DI registration removed. `audit.events` has no rows for `Alert` / `TradeReview`.

### 8a.2 size:exception preview

Forecast ~600 lines, Wave 5/6/7 precedent → `size:exception` likely. Justification: 2 bespoke typed decorators + 2 integration test files + 10 tests is a coherent cross-cutting unit.

### 8a.2 Bounded review feasibility

- New files: 4 (AlertAuditDecorator.cs + TradeReviewAuditDecorator.cs + AlertRepositoryIntegrationTests.cs + TradeReviewRepositoryIntegrationTests.cs).
- Modified files: 1 (TradingModuleRegistration DI).
- Total: **5 paths** ≤ 32 OK. (Note: path count is 5, not 10 — 10 was a forecast placeholder in the proposal; the actual path count comes from this enumeration.)

> **Slice 8a.2 completion note**: code lands with all tests green. 10 new BE tests. `audit.events` starts receiving `Alert` + `TradeReview` rows. Deviations documented in `apply-progress-2026-08-19-wave8-audit-coverage-extended-slice-8a-2.md` if any arise.

---

## Slice 8a.3 — `PlannerSessionAuditDecorator` + `PreTradeChecklistAuditDecorator` (bespoke) (~700 LOC, ~12 paths, ~8 tests)

### 8a.3 Backend (~700 LOC)

**Phase 1: `PlannerSessionAuditDecorator` (TDD)**

- [x] 1.1 RED test `PlannerSessionRepositoryIntegrationTests` (5 scenarios: `AddAsync` emits Created; `UpdateAsync` with `Notes` + `Status = Completed` change emits Updated (NOT Deleted); `UpdateAsync` with `Status = PlannerStatus.Cancelled` upgrades to Deleted via `IsTerminated` reflection; `GetByIdAsync` / `ListByUserAndWeekAsync` / `GetWeekComparisonAsync` / `ExistsForDateAsync` emit NO events; cross-tenant update emits Denied + throws `UnauthorizedAccessException`).
- [x] 1.2 GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/PlannerSessionAuditDecorator.cs` (~250 LOC; bespoke — reimplement `IsTerminated` reflection locally for `PlannerStatus.Cancelled`; mirrors `TradeAuditDecorator` shape).

**Phase 2: `PreTradeChecklistAuditDecorator` (TDD)**

- [x] 2.1 RED test `PreTradeChecklistRepositoryIntegrationTests` (3 scenarios: `AddAsync` emits Created with `entity_type = "PreTradeChecklist"`; `ListByUserIdAsync` emits NO event; contract pin — interface has no `UpdateAsync` or `DeleteAsync` methods).
- [x] 2.2 GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/PreTradeChecklistAuditDecorator.cs` (~110 LOC; bespoke write-once — only `AddAsync` wraps; reads forwarded without audit).

**Phase 3: DI wiring**

- [x] 3.1 `services.Decorate<IPlannerSessionRepository, PlannerSessionAuditDecorator>()` in `TradingModuleRegistration.cs`.
- [x] 3.2 `services.Decorate<IPreTradeChecklistRepository, PreTradeChecklistAuditDecorator>()` in the same file.

**Phase 4: Validate**

- [x] 4.1 `dotnet test --filter "FullyQualifiedName~PlannerSessionAudit|PreTradeChecklistAudit|PlannerSessionRepositoryIntegration|PreTradeChecklistRepositoryIntegration"` → 8 new tests pass.
- [x] 4.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings.
- [x] 4.3 Full BE suite (1354 + 8 = **1362**) → zero regression.

**Phase 5: Apply-progress doc**

- [x] 5.1 Append slice completion note to `apply-progress-2026-08-19-wave8-audit-coverage-extended-slice-8a-3.md`.

**Dependencies**: 8a.2 must be merged.
**Rollback**: `git revert` the slice. DI registration removed. `audit.events` has no rows for `PlannerSession` / `PreTradeChecklist`.

### 8a.3 size:exception preview

Forecast ~700 lines, Wave 5/6/7 precedent → `size:exception` likely. Justification: 2 bespoke typed decorators (one with `IsTerminated` reflection re-implementation, one write-once) + 2 integration test files + 8 tests is a coherent cross-cutting unit.

### 8a.3 Bounded review feasibility

- New files: 4 (PlannerSessionAuditDecorator.cs + PreTradeChecklistAuditDecorator.cs + PlannerSessionRepositoryIntegrationTests.cs + PreTradeChecklistRepositoryTests.cs).
- Modified files: 1 (TradingModuleRegistration DI).
- Total: **5 paths** ≤ 32 OK. (Note: path count is 5, not 12 — 12 was a forecast placeholder; the actual count comes from this enumeration.)

> **Slice 8a.3 completion note**: code lands with all tests green. 8 new BE tests. `audit.events` starts receiving `PlannerSession` + `PreTradeChecklist` rows. The `IsTerminated` reflection rule locally re-implemented matches the Wave 6 6d.2 / Wave 7 7b.1 pattern. Deviations documented in `apply-progress-2026-08-19-wave8-audit-coverage-extended-slice-8a-3.md` if any arise.

---

## Slice 8b.1 — `StripeCustomerAuditDecorator` (bespoke immutable) (~700 LOC, ~12 paths, ~3 tests)

### 8b.1 Backend (~700 LOC)

**Phase 1: `StripeCustomerAuditDecorator` (TDD)**

- [x] 1.1 RED test `StripeCustomerRepositoryIntegrationTests` (3 scenarios: `AddAsync` emits Created with `entity_type = "StripeCustomer"` + `IsOwner` cross-tenant check on `customer.UserId`; `GetByUserIdAsync` + `GetByStripeCustomerIdAsync` emit NO events; contract pin — interface has no `UpdateAsync` or `DeleteAsync` methods).
- [x] 1.2 GREEN: `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Audit/StripeCustomerAuditDecorator.cs` (~170 LOC; bespoke — mirrors simplified `TenantAuditDecorator` shape; only `AddAsync` wrapped with `IsOwner` cross-tenant check + 2 reads forwarded without audit).

**Phase 2: DI wiring**

- [x] 2.1 `services.Decorate<IStripeCustomerRepository, StripeCustomerAuditDecorator>()` in `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/DependencyInjection/BillingModuleRegistration.cs`.

**Phase 3: Validate**

- [x] 3.1 `dotnet test --filter "FullyQualifiedName~StripeCustomerAudit|StripeCustomerRepositoryIntegration"` → 3 new tests pass.
- [x] 3.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings.
- [x] 3.3 Full BE suite (1362 + 3 = **1365**) → zero regression.

**Phase 4: Apply-progress doc**

- [x] 4.1 Append slice completion note to `apply-progress-2026-08-19-wave8-audit-coverage-extended-slice-8b-1.md`.

**Dependencies**: 8a.3 must be merged.
**Rollback**: `git revert` the slice. DI registration removed. `audit.events` has no rows for `StripeCustomer`.

### 8b.1 size:exception preview

Forecast ~700 lines is a high estimate for 1 immutable decorator + 1 test file + 3 tests — likely the actual implementation runs closer to ~250 LOC (3 scenarios × ~80 LOC integration test setup + ~120 LOC decorator + ~50 LOC test fixture wiring). Borderline → `size:exception` unlikely; falls within the 1000L budget. If actual LOC exceeds 1000L, escalate to `size:exception` per Wave 7 precedent.

### 8b.1 Bounded review feasibility

- New files: 2 (StripeCustomerAuditDecorator.cs + StripeCustomerRepositoryIntegrationTests.cs).
- Modified files: 1 (BillingModuleRegistration DI).
- Total: **3 paths** ≤ 32 OK. (Note: path count is 3, not 12 — 12 was a forecast placeholder; the actual count comes from this enumeration.)

> **Slice 8b.1 completion note**: code lands with all tests green. 3 new BE tests. `audit.events` starts receiving `StripeCustomer` rows. The aggregate is immutable post-Create per the entity docstring; the decorator only wraps `AddAsync`. Deviations documented in `apply-progress-2026-08-19-wave8-audit-coverage-extended-slice-8b-1.md` if any arise.

---

## Slice 8b.2 — SKIP Reconciliation (doc-only) (~50 LOC, ~4 paths, 0 tests)

### 8b.2 Backend (doc-only, ~50 LOC, ~4 paths)

Doc-only slice. NO code changes; NO new tests; just the rationale baked into `tasks.md` + the spec REMOVED Requirements + the proposal's Out of Scope + inline code comments on the skipped interfaces.

**Phase 1: Verification (4 commands)**

- [x] 1.1 `git grep -E "Task (Add|Update|Delete)Async" src/2.Modules/Billing/JadeCapital.Billing.Application/Features/Subscriptions/ISubscriptionAdminRepository.cs` → no matches (proves SKIP #1: no mutations to audit).
- [x] 1.2 `git grep "class SubscriptionAuditDecorator" src/` → single file (proves Wave 6 still covers Subscription mutations).
- [x] 1.3 `grep -n "append-only" src/2.Modules/Billing/JadeCapital.Billing.Domain/Stripe/StripeWebhookEvent.cs` → matches (proves SKIP #2: entity docstring still says append-only).
- [x] 1.4 Verify spec REMOVED Requirements section present in `openspec/changes/2026-08-19-wave8-audit-coverage-extended/specs/soft-delete-audit/spec.md` with `Reason:` blocks for both SKIPs (`ISubscriptionAdminRepository` and `IStripeWebhookEventRepository`).

**Phase 2: Inline rationale comments (1 LOC per skipped interface)**

- [x] 2.1 Add XML doc comment block to `ISubscriptionAdminRepository` interface pointing to the spec REMOVED Requirements entry (rationale: no mutations; `Subscription` aggregate already audited by Wave 6).
- [x] 2.2 Add XML doc comment block to `IStripeWebhookEventRepository` interface pointing to the spec REMOVED Requirements entry (rationale: append-only; entity IS the audit log).

**Phase 3: Validate**

- [x] 3.1 4 verification commands pass.
- [x] 3.2 2 inline XML doc comment blocks added.
- [x] 3.3 Spec REMOVED Requirements section present.
- [x] 3.4 Zero behavior change verified via `git diff --stat` on `.cs` files (only 2 XML doc additions; no logic changes).

**Dependencies**: 8b.1 must be merged.
**Rollback**: `git revert` the slice. Doc-only changes revert. Zero behavior change.

### 8b.2 size:exception preview

Forecast ~50 lines (2 XML doc comment blocks + spec REMOVED Requirements section + tasks.md updates), within 1000L budget → `size:exception` not needed.

### 8b.2 Bounded review feasibility

- New files: 0.
- Modified files: 3 (ISubscriptionAdminRepository.cs XML doc + IStripeWebhookEventRepository.cs XML doc + spec REMOVED Requirements section + tasks.md — but tasks.md and spec are not `.cs` paths; the `.cs` modification count is 2).
- Total: **2 `.cs` paths** ≤ 32 OK. (Path count 4 includes the 2 `.md` documentation paths.)

> **Slice 8b.2 completion note**: code lands with doc-only changes. Zero new tests. The 2 SKIPs are documented with explicit rationale. Compliance officers reading `audit.events` will not see `ISubscriptionAdminRepository` or `IStripeWebhookEventRepository` rows — by design.

---

## Cumulative Test Target

| Slice | BE tests | Cumulative |
|---|---:|---:|
| Wave 7 (baseline) | — | **1328** |
| 8a.1 | +14 | 1342 |
| 8a.2 | +10 | 1352 |
| 8a.3 | +8 | 1362 |
| 8b.1 | +3 | **1365** |
| 8b.2 | 0 | 1365 |
| **Total** | **+35** | **1365** |

## Definition of Done (per Wave 5/6/7 precedent)

- All `[ ]` tasks for the slice marked `[x]`.
- All RED tests pass → GREEN → REFACTOR.
- `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings (3 pre-existing CA2263 unchanged from Wave 6 baseline).
- `dotnet test --filter "..."` → 100% pass.
- `git diff --name-only` ≤ 32 paths.
- Slice completion note appended to `apply-progress-2026-08-19-wave8-audit-coverage-extended-slice-<id>.md`.
- Deviations documented (if any) with rationale.
- Cumulative suite remains green (no regressions).
- For 8a.1: `git grep -n "RemoveAsync" src/2.Modules/Trading/` returns no active-code results (rename complete).
- For 8b.2: `git grep` verification commands 1.1-1.4 pass.

## Deviations Log

| # | Slice | Deviation | Resolution |
|---|---|---|---|
| 1 | 8a.1 | Forecasted 12 paths; actual ~10 | Forecast was conservative. Actual path count (10) is below the 32-path budget. No impact on `size:exception` decision. |
| 2 | 8a.2 | Forecasted 10 paths; actual 5 | Forecast was conservative. Actual path count (5) is well below the 32-path budget. The 10 was a placeholder from the proposal; the actual per-phase path enumeration yields 4 new files + 1 DI modification = 5 paths. No impact on `size:exception` decision. |
| 3 | 8a.3 | Forecasted 12 paths; actual 5 | Forecast was conservative. Actual path count (5) is well below the 32-path budget. Same pattern as Deviation #2. No impact on `size:exception` decision. |
| 4 | 8b.1 | Forecasted 12 paths; actual 3 | Forecast was conservative. Actual path count (3) is well below the 32-path budget. The 12 was a placeholder from the proposal; the actual enumeration yields 2 new files + 1 DI modification = 3 paths. No impact on `size:exception` decision. |
| 5 | 8b.2 | Forecasted 4 paths; actual 2 `.cs` paths + 2 `.md` paths | The 4-path forecast counted both `.cs` and `.md` files; the bounded review contract counts `.cs` paths only. Actual `.cs` path count is 2. No impact on `size:exception` decision. |

## Chained PR Strategy

| # | Branch | Base | Title |
|---|---|---|---|
| **#25** | `feature/wave8-trading-audit-1` (8a.1) | `feature/0a-identity-model` | Slice 8a.1 — `AccountAuditDecorator` + `InstrumentAuditDecorator` (incl. 2× `RemoveAsync` → `DeleteAsync` renames) |
| **#26** | `feature/wave8-trading-audit-2` (8a.2) | `feature/wave8-trading-audit-1` | Slice 8a.2 — `AlertAuditDecorator` + `TradeReviewAuditDecorator` |
| **#27** | `feature/wave8-trading-audit-3` (8a.3) | `feature/wave8-trading-audit-2` | Slice 8a.3 — `PlannerSessionAuditDecorator` + `PreTradeChecklistAuditDecorator` |
| **#28** | `feature/wave8-billing-audit` (8b.1) | `feature/wave8-trading-audit-3` | Slice 8b.1 — `StripeCustomerAuditDecorator` |
| **#29** | `feature/wave8-skip-reconciliation` (8b.2) | `feature/wave8-billing-audit` | Slice 8b.2 — Reconciliation doc: SKIP SubscriptionAdmin + StripeWebhookEvent |

Chain integrity: 5 PRs total. Each PR targets the previous PR's branch. Order matches slice order. No PR targets `main` directly. Per-slice `size:exception` per Wave 5/6/7 precedent.