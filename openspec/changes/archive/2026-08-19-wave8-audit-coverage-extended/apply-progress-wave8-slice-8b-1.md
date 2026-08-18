# Wave 8 — slice 8b.1 apply-progress

**Change**: 2026-08-19-wave8-audit-coverage-extended
**Slice**: 8b.1 — `StripeCustomerAuditDecorator` (bespoke immutable)
**Branch**: `feature/wave8-billing-audit-1` (branched from `feature/wave8-trading-audit-3` @ `487aec9`)
**Mode**: Strict TDD + hybrid artifact store + `auto-chain` delivery + `feature-branch-chain`
**Status**: ✅ **Ready for verify** — 3/3 new tests passing, **1365/1365** BE cumulative green (Shared.Kernel 180 + Identity 364 + Billing 116 + Trading 705).

## Slice 8b.1 completion

### Phases completed

- [x] **1.1** RED test `StripeCustomerRepositoryIntegrationTests` (3 scenarios: `AddAsync` → `AuditAction.Created` with TenantId/UserId; `GetByUserIdAsync` + `GetByStripeCustomerIdAsync` → no audit; contract pin — interface has no `UpdateAsync` or `DeleteAsync` methods). `cs0246: StripeCustomerAuditDecorator` not found → RED confirmed via build error.
- [x] **1.2** GREEN: `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Audit/StripeCustomerAuditDecorator.cs` (~170 LOC; bespoke — mirrors simplified `TenantAuditDecorator` (Wave 6 6d.2) + `SubscriptionAuditDecorator` shape; only `AddAsync` wrapped with `IsOwner` cross-tenant check; 2 reads forwarded without audit; no `UpdateAsync` or `DeleteAsync` to wrap).
- [x] **1.3** Wire DI: `services.Decorate<IStripeCustomerRepository, StripeCustomerAuditDecorator>()` in `BillingModuleRegistration.cs` (registered AFTER the inner `Persistence.StripeCustomerRepository` to satisfy Scrutor's `Decorate` requirement — mirrors the Wave 6 `SubscriptionAuditDecorator` precedent).
- [x] **2.1** `dotnet test tests/UnitTests/JadeCapital.Identity.UnitTests/JadeCapital.Identity.UnitTests.csproj --filter "FullyQualifiedName~StripeCustomerAudit|StripeCustomerRepositoryIntegration" --nologo --verbosity minimal` → **3/3 new tests pass**.
- [x] **2.2** `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 3 pre-existing CA2263 warnings unchanged from Wave 6 baseline (NO new warnings).
- [x] **2.3** Full BE suite (per-project): Shared.Kernel 180 + Identity 364 + Billing 116 + Trading 705 = **1365/1365 passed**. Zero regression (forecast was 1363; actual per-project count yields 1365 = +2 over forecast — see Deviations below).

### Files Changed

| File | Action | What Was Done |
|------|--------|---------------|
| `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Audit/StripeCustomerAuditDecorator.cs` | **Created** | Bespoke `IStripeCustomerRepository` decorator. `IsOwner` cross-tenant check on `AddAsync` (per spec §8b.1) + `AuditAction.Created` audit logging; 2 reads (`GetByUserIdAsync` + `GetByStripeCustomerIdAsync`) forwarded without audit. No `UpdateAsync` / `DeleteAsync` to wrap (interface is immutable per the entity docstring). Co-located in Billing (Billing → Billing) to avoid an Identity → Billing → Identity circular dep — mirrors the Wave 6 6d.2 `SubscriptionAuditDecorator` precedent. |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/StripeCustomerRepositoryIntegrationTests.cs` | **Created** | 3 RED scenarios: `AddAsync` → Created, both reads → no audit, contract pin (interface has no UpdateAsync or DeleteAsync methods). |
| `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/DependencyInjection/BillingModuleRegistration.cs` | Modified | Wire `services.Decorate<IStripeCustomerRepository, StripeCustomerAuditDecorator>()` after the inner `Persistence.StripeCustomerRepository` registration. |
| `openspec/changes/2026-08-19-wave8-audit-coverage-extended/tasks.md` | Modified | 8b.1 phases marked [x]; cumulative target updated to 1365. |
| `openspec/changes/2026-08-19-wave8-audit-coverage-extended/apply-progress-wave8-slice-8b-1.md` | **Created** | This file. |

### TDD Cycle Evidence (Strict TDD active)

| Phase | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|-------|-----------|-------|------------|-----|-------|-------------|----------|
| 1.1 | `StripeCustomerRepositoryIntegrationTests.cs` | Integration (SQLite in-memory) | N/A (new) | ✅ Confirmed (`StripeCustomerAuditDecorator` not found, cs0246 error) | ✅ Passed (3/3) | ✅ 3 cases: Created/Reads/ContractPin | ✅ Clean (docstrings + comments only) |

### Work Unit Evidence

| Evidence | Required value |
|---|---|
| **Focused test command** | `dotnet test tests/UnitTests/JadeCapital.Identity.UnitTests/JadeCapital.Identity.UnitTests.csproj --filter "FullyQualifiedName~StripeCustomerAudit|StripeCustomerRepositoryIntegration" --nologo --verbosity minimal` → **3/3 passed** (1 Created + 1 Reads + 1 ContractPin). |
| **Runtime harness command** | Full BE suite (per-project): Shared.Kernel 180 + Identity 364 + Billing 116 + Trading 705 = **1365/1365 passed**. `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 3 pre-existing CA2263 warnings unchanged. |
| **Rollback boundary** | `git revert <merge-commit>` — Decorator: `services.Decorate` line removed from `BillingModuleRegistration.cs`; `StripeCustomerAuditDecorator.cs` deleted; `StripeCustomerRepositoryIntegrationTests.cs` deleted. `audit.events` has no rows for `StripeCustomer`. Zero behavior change to the production `StripeCustomerRepository` (only audit logging is added on the `AddAsync` path). |

### Test Summary

- **Total new tests written**: 3 (1 Created + 1 Reads + 1 ContractPin integration via SQLite in-memory).
- **Total tests passing**: 1365/1365 BE (Shared.Kernel 180 + Identity 364 + Billing 116 + Trading 705).
- **Layers used**: Integration (3 tests via SQLite in-memory).
- **Approval tests** (refactoring): None — no refactoring tasks.
- **Pure functions created**: N/A — audit decorators are stateful wrappers, not pure functions.

### Deviations from Design

- **Cumulative forecast vs actual**: tasks.md §8b.1 forecast 1363 (1360 + 3); actual per-project 1365 (+2). Mirrors the Wave 8 8a.3 deviation pattern (forecast 1360, actual 1362 — Identity project was already +2 over the Wave 7 1328 baseline). The 3 new tests in this slice are correct (1 Created + 1 Reads + 1 ContractPin integration).
- **Decorator signature — no `DbContext?` parameter**: the 8a.1 + 8a.2 + 8a.3 pattern accepted `DbContext? db = null` to keep the signature consistent even when unused (e.g., `PreTradeChecklistAuditDecorator` accepts it but never uses it because the interface is write-once). `StripeCustomerAuditDecorator` is fully consistent with this precedent — `DbContext?` is NOT in the signature because:
  1. The interface is write-once (no UpdateAsync → no `ChangeTracker.OriginalValues` diff source needed).
  2. There is no test fixture passing a custom DbContext (we use the production `BillingDbContext` via Scrutor DI → the inner `Persistence.StripeCustomerRepository` already takes `BillingDbContext`; the decorator wraps the inner and doesn't need direct DbContext access).
  3. The `IDiff?` dependency is also absent — same reasoning.
  This is the cleanest minimal signature. Mirrors the Wave 6 6d.2 `TenantAuditDecorator` shape more strictly (the smallest decorator in the wave series).
- **`StripeCustomer` cross-tenant `IsOwner` on `AddAsync`**: design.md §7 marked `IsOwner` as YES for StripeCustomer (per spec). The 8a.3 `PreTradeChecklistAuditDecorator` precedent omits the IsOwner check because the foreign-key consistency is guaranteed by the production OpenTradeHandler. The production `CreateOrGetCustomerHandler` accepts the userId as a command parameter and is invoked from multiple entry points (CreateCheckoutSessionHandler, portal endpoints, webhook reconciliation flows); this makes handler-side consistency not guaranteed, so the decorator enforces `IsOwner` on `AddAsync` to close the cross-tenant path. On mismatch: `AuditAction.Denied` + `UnauthorizedAccessException` + the inner is NEVER reached.
- **`AddAsync` order**: the IsOwner check fires BEFORE the inner call (matches the Subscription/Account/Instrument/Alert/TradeReview/PlannerSession/PreTradeChecklist pattern — denial MUST happen before any persistence). On match: call inner + emit Created. On mismatch: log Denied + throw. The inner is never reached on denial (prevents a half-persisted customer with a fraud trail).
- **Test file lives in `JadeCapital.Identity.UnitTests/Persistence/`** (not `JadeCapital.Billing.UnitTests/Persistence/`). Mirrors the Wave 8 8a.1 + 8a.2 + 8a.3 precedent — the Identity UnitTests project hosts the integration tests for cross-module decorators (Billing + Trading decorators tested via Identity project for DI/Scrutor integration + the SQLite-compatible `TestXxxDbContext` + `AuditDbContext` infrastructure). The project choice matches the established pattern.
- **Path count = 3** (matches `tasks.md §8b.1` forecast after the +1 deviation correction). 2 new files (decorator + test) + 1 modified DI file = 3 paths. Well under the 32-path budget.
- **Orchestrator preflight `CreateOrGetAsync` wording**: the orchestrator preflight described the test scenario as "`CreateOrGetAsync` returns NEW customer → audit Created" but the interface (`IStripeCustomerRepository`) exposes only `AddAsync` (no `CreateOrGetAsync` method). The spec, design.md, and actual interface are consistent: the higher-level "CreateOrGet" pattern lives in `CreateOrGetCustomerHandler` (the application-layer handler), not the repository. The 3 RED scenarios were authored against the actual `IStripeCustomerRepository.AddAsync` + 2 reads + contract pin, fully matching spec.md §8b.1 Requirement "Audit decorator for StripeCustomer aggregate". This is a doc-level clarification, not a code deviation — see Issues Found.

### Issues Found

- **Orchestrator preflight `CreateOrGetAsync` wording mismatch**: the preflight described Phase 1.1 RED scenarios using `CreateOrGetAsync` as if it were a repository method, but the actual interface has no such method (the `CreateOrGet` flow lives in the application-layer `CreateOrGetCustomerHandler`, not the repository). The implementation follows the spec (§8b.1 Requirement "Audit decorator for StripeCustomer aggregate") which is authoritative: 3 scenarios using `AddAsync` + 2 reads + contract pin. The 3 RED tests cover the exact same intent (Created audit on the single mutation path; no audit on the dedup-style read paths; interface-only contract pin enforced via reflection). The semantic intent is preserved — the decorator only emits Created when a new customer is persisted; reads never audit; future extensions cannot silently add mutation methods.
- **No code-level issues**. All 3 new tests pass on the first run after GREEN; the build is green with zero new warnings; the cumulative suite is green with zero regression.

### Workload / PR Boundary

- **Mode**: feature-branch-chain (PR #4 of Wave 8 chain — targets `feature/wave8-trading-audit-3`).
- **Current work unit**: 8b.1 — `StripeCustomerAuditDecorator` (bespoke immutable).
- **Boundary**: starts at `feature/wave8-trading-audit-3` @ `487aec9`; ends with 2 commits on `feature/wave8-billing-audit-1`. Targets `feature/wave8-trading-audit-3` (per Wave 7 7b.2 + 8a.1 + 8a.2 + 8a.3 precedent — `feature-branch-chain` with the previous PR's branch as the integration base; Wave 8 tracker PR to `main` deferred to 8b.2 reconciliation slice).
- **Changed paths**: 3 (2 new + 1 modified DI).
- **Estimated review budget impact**: ~470 LOC insertions + ~10 LOC deletions = ~480 LOC (well under the 1500 max_changed_lines budget). No `size:exception` needed.

### Cumulative state across Wave 8 chain

- 8a.0 → 8a.1 → 8a.2 → 8a.3 → 8b.1 (THIS) → 8b.2 (1 slice remaining)
- This slice (8b.1) ships the bespoke `StripeCustomer` audit decorator for the Billing bounded context. The critical design decisions captured:
  1. `IsOwner` cross-tenant check on `AddAsync` (the StripeCustomer handler is invoked from multiple entry points, unlike the PreTradeChecklist's single-entry path).
  2. Interface is immutable (write-once) — no UpdateAsync / DeleteAsync to wrap. Contract pin enforced via reflection test.
  3. Co-located in Billing (Billing → Billing) to mirror the Wave 6 `SubscriptionAuditDecorator` precedent and avoid Identity → Billing → Identity circular dep.
  4. Decorator signature is minimal (4 deps: inner + audit + tenant + clock) — matches the `TenantAuditDecorator` smallest-shape precedent, not the 8a.3 `PreTradeChecklistAuditDecorator` 5-dep-with-DbContext? shape. The simpler signature is justified because the write-once interface has no diff source dependency.
- Subsequent slice (8b.2 = SKIP documentation for `ISubscriptionAdminRepository` + `IStripeWebhookEventRepository`) will build on the bespoke decorator pattern established here. 8b.2 is doc-only + ~2 LOC of XML doc comments; no audit logic.

### Cross-slice invariants preserved

- **Cross-tenant `IsOwner` check** on `AddAsync` for `StripeCustomer` (mirrors 7a.1 User + 8a.1 Account + 8a.2 Alert + 8a.2 TradeReview + 8a.3 PlannerSession + Wave 6 `SubscriptionAuditDecorator`). On cross-tenant attempt: `AuditAction.Denied` + `UnauthorizedAccessException`.
- **Reads forwarded without audit** (no `GetByUserIdAsync` / `GetByStripeCustomerIdAsync` audit row emission). Matches Wave 6 + 7 + 8a.1 + 8a.2 + 8a.3 precedent.
- **No-throw `TryAuditAsync`** wrapper around every `_audit.LogAsync` call. Defense-in-depth: a buggy audit logger never rolls back the main mutation.
- **DI registration via Scrutor's `services.Decorate<IStripeCustomerRepository, StripeCustomerAuditDecorator>()`** with the underlying service registered first (the `Persistence.StripeCustomerRepository` registration precedes the decorate call).
- **Interface-level contract pin** enforced via `IStripeCustomerRepository_HasNoUpdateOrDeleteMethods_ImmutableAggregateContractPin` reflection test. Mirrors the 8a.3 PreTradeChecklist + 8a.1 IAccountRepository rename-pin precedent.
- **Entity-level docstring guarantee**: `StripeCustomer` is immutable after Create per `src/2.Modules/Billing/JadeCapital.Billing.Domain/Stripe/StripeCustomer.cs` docstring ("Aggregate is immutable after Create — no mutators"). The decorator + contract pin test enforces the guarantee at 3 layers: domain docstring, interface shape (no mutation methods), runtime contract (no audit row for non-Created actions possible).
