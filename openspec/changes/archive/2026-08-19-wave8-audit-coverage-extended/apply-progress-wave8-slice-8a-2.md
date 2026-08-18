# Wave 8 — slice 8a.2 apply-progress

**Change**: 2026-08-19-wave8-audit-coverage-extended
**Slice**: 8a.2 — `AlertAuditDecorator` + `TradeReviewAuditDecorator` (bespoke)
**Branch**: `feature/wave8-trading-audit-2` (branched from `feature/wave8-trading-audit-1` @ `81c1b2a`)
**Mode**: Strict TDD + hybrid artifact store + `auto-chain` delivery + `feature-branch-chain`
**Status**: ✅ **Ready for verify** — 10/10 new tests passing, 1354/1354 BE cumulative green (180 + 353 + 116 + 705).

## Slice 8a.2 completion

### Phases completed

- [x] **1.1** RED test `AlertRepositoryIntegrationTests` (5 scenarios). `cs0246: AlertAuditDecorator` not found → RED confirmed via build error.
- [x] **1.2** GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AlertAuditDecorator.cs` (~270 LOC; bespoke — mirrors `TradeAuditDecorator` shape; CRITICAL: `AddAsync` returns `bool` audit semantics — only emit Created when `true`; silently skip when `false` (dedup hit); `IsOwner` cross-tenant check on `UpdateAsync`).
- [x] **2.1** RED test `TradeReviewRepositoryIntegrationTests` (5 scenarios). `cs0246: TradeReviewAuditDecorator` not found → RED confirmed via build error.
- [x] **2.2** GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/TradeReviewAuditDecorator.cs` (~290 LOC; bespoke — mirrors `JournalEntryAuditDecorator` shape; CRITICAL: attachment ops (`AddAttachmentAsync` / `UpdateAttachmentAsync` / `RemoveAttachmentAsync`) forwarded WITHOUT emitting audit rows per orchestrator preflight decision 7; `IsOwner` cross-tenant check on `UpdateAsync`).
- [x] **3.1** Wire DI: `services.Decorate<IAlertRepository, AlertAuditDecorator>()` in `TradingModuleRegistration.cs`.
- [x] **3.2** Wire DI: `services.Decorate<ITradeReviewRepository, TradeReviewAuditDecorator>()` in the same file.
- [x] **4.1** `dotnet test tests/UnitTests/JadeCapital.Identity.UnitTests/JadeCapital.Identity.UnitTests.csproj --filter "FullyQualifiedName~AlertAudit|TradeReviewAudit|AlertRepositoryIntegration|TradeReviewRepositoryIntegration" --nologo --verbosity minimal` → **10/10 new tests pass** (5 Alert + 5 TradeReview).
- [x] **4.2** `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 3 pre-existing CA2263 warnings unchanged from Wave 6 baseline.
- [x] **4.3** Full BE suite (per-project) → Shared.Kernel 180 + Identity 353 + Billing 116 + Trading 705 = **1354/1354 passed**. Zero regression (forecast was 1352; actual per-project count yields 1354 = +2 over forecast — see Deviations below).
- [x] **5.1** Apply-progress doc written (this file).

### Files Changed

| File | Action | What Was Done |
|------|--------|---------------|
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AlertAuditDecorator.cs` | **Created** | Bespoke `IAlertRepository` decorator. Conditional `AddAsync` audit (only Created on `true`; silent skip on `false` dedup); `IsOwner` cross-tenant check on `UpdateAsync`; reads forwarded without audit. |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/TradeReviewAuditDecorator.cs` | **Created** | Bespoke `ITradeReviewRepository` decorator. `AddAsync` (Created) + `UpdateAsync` (Updated with diff + `IsOwner`) wrapped; attachment ops (`Add`/`Update`/`Remove`) forwarded WITHOUT audit (child-entity decision); reads forwarded without audit. |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs` | Modified | Wire `services.Decorate<IAlertRepository, AlertAuditDecorator>()` + `services.Decorate<ITradeReviewRepository, TradeReviewAuditDecorator>()`. |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/AlertRepositoryIntegrationTests.cs` | **Created** | 5 RED scenarios: `AddAsync` (true) → Created, `AddAsync` (false, dedup) → NO audit, `Acknowledge` → Updated with `acknowledgedAt` diff, cross-tenant → Denied + throws, `GetByIdAsync` → no audit. |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/TradeReviewRepositoryIntegrationTests.cs` | **Created** | 5 RED scenarios: `AddAsync` → Created, `UpdateAsync` with `setupUsed` + `rating` change → Updated with diff, attachment ops → NO audit, cross-tenant → Denied + throws, `FindByIdAsync` → no audit. |

### TDD Cycle Evidence (Strict TDD active)

| Phase | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|-------|-----------|-------|------------|-----|-------|-------------|----------|
| 1.1 | `AlertRepositoryIntegrationTests.cs` | Integration (SQLite in-memory) | N/A (new) | ✅ Confirmed (`AlertAuditDecorator` not found, 2 cs0246 errors) | ✅ Passed (5/5) | ✅ 5 cases: Created/Dedup/Updated/Denied/Reads | ✅ Clean (docstrings + comments only) |
| 2.1 | `TradeReviewRepositoryIntegrationTests.cs` | Integration (SQLite in-memory) | N/A (new) | ✅ Confirmed (`TradeReviewAuditDecorator` not found, 1 cs0246 error) | ✅ Passed (5/5) | ✅ 5 cases: Created/Updated/AttachmentOps/Denied/Reads | ✅ Clean (docstrings + comments only) |

### Work Unit Evidence

| Evidence | Required value |
|---|---|
| **Focused test command** | `dotnet test tests/UnitTests/JadeCapital.Identity.UnitTests/JadeCapital.Identity.UnitTests.csproj --filter "FullyQualifiedName~AlertAudit|TradeReviewAudit|AlertRepositoryIntegration|TradeReviewRepositoryIntegration" --nologo --verbosity minimal` → **10/10 passed** (5 Alert + 5 TradeReview). |
| **Runtime harness command** | Full BE suite (per-project): Shared.Kernel 180 + Identity 353 + Billing 116 + Trading 705 = **1354/1354 passed**. `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 3 pre-existing CA2263 warnings unchanged. |
| **Rollback boundary** | `git revert <merge-commit>` — Decorators: `services.Decorate` lines removed from `TradingModuleRegistration.cs`; `AlertAuditDecorator.cs` + `TradeReviewAuditDecorator.cs` deleted. `audit.events` has no rows for `Alert` / `TradeReview`. |

### Test Summary

- **Total new tests written**: 10 (5 Alert integration + 5 TradeReview integration)
- **Total tests passing**: 1354/1354 BE (Shared.Kernel 180 + Identity 353 + Billing 116 + Trading 705)
- **Layers used**: Integration (10 tests via SQLite in-memory). No contract tests (no interface surgery in 8a.2 — bespoke shapes preserved).
- **Approval tests** (refactoring): None — no refactoring tasks
- **Pure functions created**: N/A — audit decorators are stateful wrappers, not pure functions

### Deviations from Design

- **Forecast was 1352 BE tests; actual is 1354 (+2)**. The forecast in `tasks.md §8a.2` was "1342 + 10 = 1352". Actual per-project count yields 180 + 353 + 116 + 705 = **1354**. The discrepancy is +2 in the Identity project (now 353, was 343 baseline). Looking at the 8a.1 apply-progress forecast of 1342, the actual baseline was also slightly off. This is an accounting artifact in the forecast table; the 10 new tests in this slice are correct (5 Alert + 5 TradeReview integration). The "cumulative target" rows in tasks.md are forecast placeholders; the authoritative count is the per-project actual.
- **Alert dedup test fixture uses `BuildServicesWithDedup` helper**. The TestAlertRepository has a `DedupNext` boolean flag; when `true`, `AddAsync` returns `false` without staging an insert. The default `BuildServices` uses the standard registration (DedupNext=false → AddAsync always returns true → Created audit fires). This pattern is necessary because Scrutor wraps the decorator — we can't reach into the inner via cast. The `BuildServicesWithDedup` factory configures the inner in its `AddScoped<IAlertRepository>` lambda with `DedupNext = true` so the next (and only) `AddAsync` call simulates the production dedup UNIQUE INDEX violation.
- **Alert cross-tenant test bypasses the decorator for setup**. Same pattern as 8a.1 AccountRepositoryIntegrationTests — we stage the alert directly via `alertDb.Alerts.Add(alert)` (the underlying DbContext, not the decorated repo) so the `IsOwner` check doesn't fire on the setup path. Then the cross-tenant user attempts `repo.UpdateAsync(alert)` which fires the `IsOwner` denial + throws. This is the 8a.1/8a.2 precedent.
- **TradeReview Update diff test uses `setupUsed` + `rating`** (not `Title` as the forecast mentioned). The forecast in `tasks.md §8a.2 Phase 2.1` referenced `Title + Rating`, but `TradeReview` does not have a `Title` field — it has `SetupUsed` + `Lessons` + `Rating`. The actual mutable text field is `SetupUsed`. The diff test exercises the `setupUsed` + `rating` field transitions, matching the production `TradeReview.Update` semantics.
- **`DbContext` parameter type (not concrete `TradingDbContext`)**. Mirrors the Wave 7 7b.1 TradeAuditDecorator + 8a.1 AccountAuditDecorator precedent — accepting the base `DbContext` type lets the unit-test fixture register a SQLite-compatible helper DbContext without touching the production schema (Npgsql-specific converters in `JournalEntry.Tags` etc.). The production DI resolves `TradingDbContext` into the base-type parameter automatically.
- **Path count = 5** (matches `tasks.md §8a.2` forecast). 4 new files + 1 modified DI file = 5 paths. Well under the 32-path budget.

### Issues Found

None. All 10 new tests pass on the first run after GREEN; the build is green with zero new warnings; the cumulative suite is green with zero regression.

### Workload / PR Boundary

- **Mode**: feature-branch-chain (PR #2 of Wave 8 chain — targets `feature/wave8-trading-audit-1`)
- **Current work unit**: 8a.2 — `AlertAuditDecorator` + `TradeReviewAuditDecorator` (bespoke)
- **Boundary**: starts at `feature/wave8-trading-audit-1` @ `81c1b2a`; ends with 2 commits on `feature/wave8-trading-audit-2`. Targets `feature/wave8-trading-audit-1` (per Wave 7 7b.2 precedent — `feature-branch-chain` with the previous PR's branch as the integration base).
- **Changed paths**: 5 (4 new + 1 modified DI)
- **Estimated review budget impact**: ~1100 LOC insertions + ~0 deletions = ~1100 LOC (under the 1500 max_changed_lines budget). **`size:exception` likely accepted** per Wave 7/8a.1 precedent; the path count (5) is well within the 32-path budget.

### Cumulative state across Wave 8 chain

- 8a.0 → 8a.1 → 8a.2 (THIS) → 8a.3 → 8b.1 → 8b.2 (3 slices remaining)
- This slice (8a.2) ships the bespoke `Alert` + `TradeReview` audit decorator pattern. The critical deviations captured:
  1. `Alert.AddAsync` returns `bool` — decorator emits Created only on `true`, silently skips on `false` (dedup).
  2. `TradeReview` attachment ops forwarded WITHOUT audit (child-entity decision).
- Subsequent slices (8a.3 = PlannerSession + PreTradeChecklist, 8b.1 = StripeCustomer) will build on the bespoke decorator pattern established here.

### Cross-slice invariants preserved

- **Cross-tenant `IsOwner` check** on `UpdateAsync` for both decorators (mirrors 7b.1 Trade + 7b.2 JournalEntry + 8a.1 Account). On cross-tenant attempt: `AuditAction.Denied` + `UnauthorizedAccessException`.
- **Reads forwarded without audit** (no `FindByIdAsync` / `GetByIdAsync` / `ListByUserAsync` / `ListByUserAndWeekAsync` / `FindByTradeIdAsync` audit row emission). Matches Wave 6 + 7 + 8a.1 precedent.
- **EF `ChangeTracker.OriginalValues` diff source** for `UpdateAsync` pre-mutation snapshot (when DbContext is wired). Falls back to post-mutation JSON snapshot when DbContext isn't tracked.
- **No-throw `TryAuditAsync`** wrapper around every `_audit.LogAsync` call. Defense-in-depth: a buggy audit logger never rolls back the main mutation.
- **DI registration via Scrutor's `services.Decorate<IXxxRepository, XxxAuditDecorator>()`** with the underlying service registered first.
