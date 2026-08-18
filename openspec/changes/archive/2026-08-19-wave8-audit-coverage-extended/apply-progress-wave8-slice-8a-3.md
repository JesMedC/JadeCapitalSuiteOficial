# Wave 8 — slice 8a.3 apply-progress

**Change**: 2026-08-19-wave8-audit-coverage-extended
**Slice**: 8a.3 — `PlannerSessionAuditDecorator` + `PreTradeChecklistAuditDecorator` (bespoke)
**Branch**: `feature/wave8-trading-audit-3` (branched from `feature/wave8-trading-audit-2` @ `5b76803`)
**Mode**: Strict TDD + hybrid artifact store + `auto-chain` delivery + `feature-branch-chain`
**Status**: ✅ **Ready for verify** — 8/8 new tests passing, 1362/1362 BE cumulative green (180 + 361 + 116 + 705).

## Slice 8a.3 completion

### Phases completed

- [x] **1.1** RED test `PlannerSessionRepositoryIntegrationTests` (5 scenarios). `cs0246: PlannerSessionAuditDecorator` not found → RED confirmed via build error.
- [x] **1.2** GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/PlannerSessionAuditDecorator.cs` (~250 LOC; bespoke — mirrors `TradeAuditDecorator` shape; CRITICAL: `IsTerminated` reflection rule re-implemented locally for `PlannerStatus.Cancelled` per orchestrator preflight decision 8; `IsOwner` cross-tenant check on `UpdateAsync`; reads forwarded without audit).
- [x] **2.1** RED test `PreTradeChecklistRepositoryIntegrationTests` (3 scenarios). `cs0246: PreTradeChecklistAuditDecorator` not found → RED confirmed via build error.
- [x] **2.2** GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/PreTradeChecklistAuditDecorator.cs` (~110 LOC; bespoke write-once — only `AddAsync` wraps with `AuditAction.Created`; `ListByUserIdAsync` forwarded without audit; no IsOwner check — interface is write-once, the foreign-key consistency is guaranteed by `OpenTradeHandler`).
- [x] **3.1** Wire DI: `services.Decorate<IPlannerSessionRepository, PlannerSessionAuditDecorator>()` in `TradingModuleRegistration.cs`.
- [x] **3.2** Wire DI: `services.Decorate<IPreTradeChecklistRepository, PreTradeChecklistAuditDecorator>()` in the same file.
- [x] **4.1** `dotnet test tests/UnitTests/JadeCapital.Identity.UnitTests/JadeCapital.Identity.UnitTests.csproj --filter "FullyQualifiedName~PlannerSessionAudit|PreTradeChecklistAudit|PlannerSessionRepositoryIntegration|PreTradeChecklistRepositoryIntegration" --nologo --verbosity minimal` → **8/8 new tests pass** (5 PlannerSession + 3 PreTradeChecklist).
- [x] **4.2** `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 3 pre-existing CA2263 warnings unchanged from Wave 6 baseline.
- [x] **4.3** Full BE suite (per-project) → Shared.Kernel 180 + Identity 361 + Billing 116 + Trading 705 = **1362/1362 passed**. Zero regression (forecast was 1360; actual per-project count yields 1362 = +2 over forecast — see Deviations below).
- [x] **5.1** Apply-progress doc written (this file).

### Files Changed

| File | Action | What Was Done |
|------|--------|---------------|
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/PlannerSessionAuditDecorator.cs` | **Created** | Bespoke `IPlannerSessionRepository` decorator. `IsTerminated` reflection re-implemented locally for `PlannerStatus.Cancelled` (slice 8a.3 bespoke deviation, mirrors Wave 7 7b.1 Trade pattern). `IsOwner` cross-tenant check on `UpdateAsync`; reads forwarded without audit. |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/PreTradeChecklistAuditDecorator.cs` | **Created** | Bespoke write-once `IPreTradeChecklistRepository` decorator. Smallest decorator in the wave: only `AddAsync` wraps with `AuditAction.Created`; `ListByUserIdAsync` forwarded without audit. |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs` | Modified | Wire `services.Decorate<IPlannerSessionRepository, PlannerSessionAuditDecorator>()` + `services.Decorate<IPreTradeChecklistRepository, PreTradeChecklistAuditDecorator>()`. |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/PlannerSessionRepositoryIntegrationTests.cs` | **Created** | 5 RED scenarios: `AddAsync` → Created, `UpdateAsync` with Notes + Status=Completed → Updated with diff, `UpdateAsync` with Status=Cancelled → Deleted via IsTerminated reflection, reads → no audit, cross-tenant → Denied + throws. |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/PreTradeChecklistRepositoryIntegrationTests.cs` | **Created** | 3 RED scenarios: `AddAsync` → Created, `ListByUserIdAsync` → no audit, contract pin (interface has no UpdateAsync or DeleteAsync methods). |
| `openspec/changes/2026-08-19-wave8-audit-coverage-extended/tasks.md` | Modified | 8a.3 phases marked [x]; cumulative target updated to 1362. |
| `openspec/changes/2026-08-19-wave8-audit-coverage-extended/apply-progress-wave8-slice-8a-3.md` | **Created** | This file. |

### TDD Cycle Evidence (Strict TDD active)

| Phase | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|-------|-----------|-------|------------|-----|-------|-------------|----------|
| 1.1 | `PlannerSessionRepositoryIntegrationTests.cs` | Integration (SQLite in-memory) | N/A (new) | ✅ Confirmed (`PlannerSessionAuditDecorator` not found, cs0246 error) | ✅ Passed (5/5) | ✅ 5 cases: Created/Updated/Cancelled→Deleted/Reads/Denied | ✅ Clean (docstrings + comments only) |
| 2.1 | `PreTradeChecklistRepositoryIntegrationTests.cs` | Integration (SQLite in-memory) | N/A (new) | ✅ Confirmed (`PreTradeChecklistAuditDecorator` not found, cs0246 error) | ✅ Passed (3/3) | ✅ 3 cases: Created/Reads/ContractPin | ✅ Clean (docstrings + comments only) |

### Work Unit Evidence

| Evidence | Required value |
|---|---|
| **Focused test command** | `dotnet test tests/UnitTests/JadeCapital.Identity.UnitTests/JadeCapital.Identity.UnitTests.csproj --filter "FullyQualifiedName~PlannerSessionAudit|PreTradeChecklistAudit|PlannerSessionRepositoryIntegration|PreTradeChecklistRepositoryIntegration" --nologo --verbosity minimal` → **8/8 passed** (5 PlannerSession + 3 PreTradeChecklist). |
| **Runtime harness command** | Full BE suite (per-project): Shared.Kernel 180 + Identity 361 + Billing 116 + Trading 705 = **1362/1362 passed**. `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 3 pre-existing CA2263 warnings unchanged. |
| **Rollback boundary** | `git revert <merge-commit>` — Decorators: `services.Decorate` lines removed from `TradingModuleRegistration.cs`; `PlannerSessionAuditDecorator.cs` + `PreTradeChecklistAuditDecorator.cs` deleted. `audit.events` has no rows for `PlannerSession` / `PreTradeChecklist`. |

### Test Summary

- **Total new tests written**: 8 (5 PlannerSession integration + 3 PreTradeChecklist integration)
- **Total tests passing**: 1362/1362 BE (Shared.Kernel 180 + Identity 361 + Billing 116 + Trading 705)
- **Layers used**: Integration (8 tests via SQLite in-memory). No contract tests on IPlannerSessionRepository (no interface surgery in 8a.3 — bespoke shape preserved).
- **Approval tests** (refactoring): None — no refactoring tasks
- **Pure functions created**: N/A — audit decorators are stateful wrappers, not pure functions

### Deviations from Design

- **Cumulative forecast vs actual**: tasks.md §8a.3 forecast 1360 (1352 + 8); actual per-project 1362 (+2). The forecast in tasks.md has been slightly off across all 8a slices — minor accounting artifact (the Identity project grew by +2 over the forecast baseline). The 8 new tests in this slice are correct (5 PlannerSession + 3 PreTradeChecklist integration).
- **`DbContext` parameter type** (not concrete `TradingDbContext`): mirrors Wave 7 7b.1 + 8a.1 + 8a.2 precedent — accepting the base `DbContext` type lets the unit-test fixture register a SQLite-compatible helper DbContext without touching the production schema (Npgsql-specific converters in `JournalEntry.Tags` etc.). The production DI resolves `TradingDbContext` into the base-type parameter automatically.
- **PlannerSession cross-tenant test bypasses the decorator for setup**. Same pattern as 8a.1 + 8a.2 — we stage the session directly via `plannerDb.PlannerSessions.Add(session)` (the underlying DbContext, not the decorated repo) so the `IsOwner` check doesn't fire on the setup path. Then the cross-tenant user attempts `repo.UpdateAsync(session)` which fires the `IsOwner` denial + throws.
- **PlannerSession `IsTerminated` reflection re-implemented locally** (not delegated to the generic `DecoratedRepository<T>.IsTerminated`). Per design.md §5 + tasks.md §8a.3 Phase 1.2: the bespoke decorator keeps the rule local + explicit. `PlannerStatus` has only one lifecycle-terminated value (`Cancelled = 4`); the local check is `session.Status == PlannerStatus.Cancelled`. The generic helper covers the same case via reflection (Status enum name == "Cancelled"), but the bespoke pattern keeps the rule visible in the decorator for reviewer clarity (matches Wave 7 7b.1 TradeAuditDecorator precedent).
- **PreTradeChecklist: no `IsOwner` cross-tenant check on `AddAsync`**. The interface is write-once (only `AddAsync` + `ListByUserIdAsync`); the foreign-key consistency (`TradeId` + `UserId` consistency with the trade's userId) is guaranteed by the production `OpenTradeHandler` which passes both from the authenticated context + the same UoW. A cross-tenant `AddAsync` is impossible at the application layer because the trade itself wouldn't have been created cross-tenant (the OpenTrade path enforces `trade.UserId == currentUserId`). Decorator emits Created unconditionally for `AddAsync`.
- **Test file lives in `JadeCapital.Identity.UnitTests/Persistence/`** (not `JadeCapital.Trading.UnitTests/Persistence/`). Mirrors the 8a.1 + 8a.2 precedent — the Identity UnitTests project hosts the integration tests for cross-module decorators (Trading decorators tested via Identity project for DI/Scrutor integration). The project choice matches the Wave 7 7b.1 + 7b.2 + 8a.1 + 8a.2 established pattern.
- **Path count = 5** (matches `tasks.md §8a.3` forecast). 4 new files + 1 modified DI file = 5 paths. Well under the 32-path budget.

### Issues Found

None. All 8 new tests pass on the first run after GREEN; the build is green with zero new warnings; the cumulative suite is green with zero regression.

### Workload / PR Boundary

- **Mode**: feature-branch-chain (PR #3 of Wave 8 chain — targets `feature/wave8-trading-audit-2`)
- **Current work unit**: 8a.3 — `PlannerSessionAuditDecorator` + `PreTradeChecklistAuditDecorator` (bespoke)
- **Boundary**: starts at `feature/wave8-trading-audit-2` @ `5b76803`; ends with 3 commits on `feature/wave8-trading-audit-3`. Targets `feature/wave8-trading-audit-2` (per Wave 7 7b.2 + 8a.2 precedent — `feature-branch-chain` with the previous PR's branch as the integration base).
- **Changed paths**: 5 (4 new + 1 modified DI)
- **Estimated review budget impact**: ~1100 LOC insertions + ~0 deletions = ~1100 LOC (under the 1500 max_changed_lines budget). **`size:exception` likely accepted** per Wave 7/8a.1/8a.2 precedent; the path count (5) is well within the 32-path budget.

### Cumulative state across Wave 8 chain

- 8a.0 → 8a.1 → 8a.2 → 8a.3 (THIS) → 8b.1 → 8b.2 (2 slices remaining)
- This slice (8a.3) ships the bespoke `PlannerSession` + `PreTradeChecklist` audit decorator pattern. The critical deviation captured:
  1. `PlannerSession.IsTerminated` reflection re-implemented locally for `PlannerStatus.Cancelled` (single terminated value, vs the generic helper's three-value set).
  2. `PreTradeChecklist` is write-once (only `AddAsync` wraps) — the "write-once" design decision applies to the checklist aggregate itself, not its children (the checklist items don't even exist as children in this aggregate — it's a single-row header).
- Subsequent slices (8b.1 = StripeCustomer, 8b.2 = SKIP documentation for ISubscriptionAdminRepository + IStripeWebhookEventRepository) will build on the bespoke decorator pattern established here.

### Cross-slice invariants preserved

- **Cross-tenant `IsOwner` check** on `UpdateAsync` for `PlannerSession` (mirrors 7b.1 Trade + 7b.2 JournalEntry + 8a.1 Account + 8a.2 Alert + 8a.2 TradeReview). On cross-tenant attempt: `AuditAction.Denied` + `UnauthorizedAccessException`.
- **Reads forwarded without audit** (no `GetByIdAsync` / `ListByUserAndWeekAsync` / `ExistsForDateAsync` / `GetWeekComparisonAsync` / `ListByUserIdAsync` audit row emission). Matches Wave 6 + 7 + 8a.1 + 8a.2 precedent.
- **EF `ChangeTracker.OriginalValues` diff source** for `PlannerSession.UpdateAsync` pre-mutation snapshot (when DbContext is wired). Falls back to post-mutation JSON snapshot when DbContext isn't tracked.
- **No-throw `TryAuditAsync`** wrapper around every `_audit.LogAsync` call. Defense-in-depth: a buggy audit logger never rolls back the main mutation.
- **DI registration via Scrutor's `services.Decorate<IXxxRepository, XxxAuditDecorator>()`** with the underlying service registered first.