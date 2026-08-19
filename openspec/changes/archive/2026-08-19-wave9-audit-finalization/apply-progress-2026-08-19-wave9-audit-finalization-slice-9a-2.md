# Wave 9 — slice 9a.2 apply-progress

**Change**: 2026-08-19-wave9-audit-finalization
**Slice**: 9a.2 — `ScannerFilterAuditDecorator` (CRUD without Delete)
**Branch**: `feature/wave9-scanner-filter-audit` (branched from `feature/wave9-trading-audit-1` @ 9a.1 = PR #30 just landed; PR #31 of the project)
**Mode**: Strict TDD + hybrid artifact store + `auto-chain` delivery + `feature-branch-chain` + `size:exception`
**Status**: ✅ **Ready for verify** — 5/5 new tests passing (1 contract + 4 integration), **1376/1376** BE cumulative green (Shared.Kernel 180 + Identity 364 + Billing 116 + Trading 716).

## Slice 9a.2 completion

### Phases completed

- [x] **1.1** RED test `IScannerFilterRepositoryContractTests` (1 scenario: `DeleteAsync_IsNotOnInterface_DefensiveStub_Throws` — pins the interface shape (`IRepository<ScannerFilter>` extension) + the decorator's `DeleteAsync` short-circuit + the `AuditAction.Failed` audit emission). Confirmed RED via build error (CS1061: interface lacks DeleteAsync + CS0246: ScannerFilterAuditDecorator missing).
- [x] **1.2** GREEN: extend `IScannerFilterRepository` to `IRepository<ScannerFilter>` (gaining `DeleteAsync` defensive stub) in `src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/IScannerFilterRepository.cs`. Added `<remarks>` XML doc documenting the soft-delete-by-flag rationale. Production `ScannerFilterRepository.DeleteAsync` is also a defensive stub throwing `NotSupportedException` as a second line of defense.
- [x] **1.3** `git grep -n "_scannerFilter\.DeleteAsync\|IScannerFilterRepository.*Delete" src/` → only one match (XML docstring reference); zero handler call sites.
- [x] **2.1** RED test `ScannerFilterRepositoryIntegrationTests` (4 scenarios: `AddAsync` writes Created + IsOwner; `UpdateAsync` writes Updated with diff (volatilityWindow + minSpread) + IsOwner; cross-tenant `UpdateAsync` emits Denied + throws UnauthorizedAccessException; reads (GetByIdAsync + GetByUserAndNameAsync + ListByUserAsync) NOT audited). The 5th scenario (DeleteAsync → Failed + throws NotSupportedException) is pinned by the contract pin (1.1) which uses NSubstitute for isolation.
- [x] **2.2** GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/ScannerFilterAuditDecorator.cs` (~358 LOC; bespoke — implements `IScannerFilterRepository` directly; wraps `AddAsync` + `UpdateAsync`; `DeleteAsync` defensive stub emits `Failed` + throws; reads forwarded without audit; `IsOwner` cross-tenant check on `filter.UserId`).
- [x] **3.1** Wire DI: `services.Decorate<IScannerFilterRepository, ScannerFilterAuditDecorator>()` in `TradingModuleRegistration.cs` (after the inner `AddScoped<IScannerFilterRepository>` registration at lines 56-57).
- [x] **4.1** `dotnet test tests/UnitTests/JadeCapital.Trading.UnitTests --filter "FullyQualifiedName~ScannerFilterAudit|ScannerFilterRepositoryIntegration|IScannerFilterRepositoryContractTests" --nologo --verbosity minimal` → **5/5 new tests pass** (1 contract + 4 integration).
- [x] **4.2** `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings (matches Wave 6 baseline; 0 new CA2263).
- [x] **4.3** Full BE suite (per-project): Shared.Kernel 180 + Identity 364 + Billing 116 + Trading 716 = **1376/1376 passed**. Wave 9 9a.1 baseline 1371 + 5 new = 1376 (matches forecast — zero regression).

### Files Changed

| File | Action | What Was Done |
|------|--------|---------------|
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/IScannerFilterRepository.cs` | Modified | Extend `IRepository<ScannerFilter>` (gaining `DeleteAsync` defensive stub); added `<remarks>` XML doc documenting the soft-delete-by-flag rationale + the Wave 9 §9a.2 interface surgery. **Interface surgery: 1 file path** (per tasks.md §9a.2 forecast). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Persistence/ScannerFilterRepository.cs` | Modified | Added `DeleteAsync(ScannerFilter, ct)` defensive stub throwing `NotSupportedException` (second line of defense in case a caller bypasses the decorator). Mirrors the Wave 7 7b.1 `StrategyRepository.DeleteAsync` + 7a.1 `UserRepository.DeleteAsync` precedent. **Required for the IRepository<ScannerFilter> extension to compile.** |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/ScannerFilterAuditDecorator.cs` | **Created** | Bespoke `IScannerFilterRepository` decorator (358 LOC). `IsOwner` cross-tenant check on `AddAsync` + `UpdateAsync` (per spec §9a.2) + `AuditAction.Created` / `AuditAction.Updated` audit logging. `UpdateAsync` emits a before/after diff JSON (via `ChangeTracker.OriginalValues` + `JsonDiff`). 3 reads (`GetByIdAsync` + `GetByUserAndNameAsync` + `ListByUserAsync`) forwarded bare. `DeleteAsync` defensive stub emits `AuditAction.Failed` + throws `NotSupportedException` (mirrors Wave 7 7b.1 `StrategyAuditDecorator` + 7a.1 `UserAuditDecorator` precedent). `TryAuditAsync` swallows exceptions (defense-in-depth). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs` | Modified | Added `using JadeCapital.Trading.Infrastructure.Audit;` (required for the Decorate call). Wire `services.Decorate<IScannerFilterRepository, ScannerFilterAuditDecorator>()` after the inner `AddScoped<IScannerFilterRepository, ScannerFilterRepository>()` registration. Comment block mentions Wave 9 sub-scope A + slice 9a.2 + the bespoke CRD-without-Delete rationale. |
| `tests/UnitTests/JadeCapital.Trading.UnitTests/Persistence/IScannerFilterRepositoryContractTests.cs` | **Created** | 1 contract pin (191 LOC) using NSubstitute for isolation. Asserts (a) the interface inherits from `IRepository<ScannerFilter>` (the source of the `DeleteAsync` defensive stub); (b) the decorator's `DeleteAsync` throws `NotSupportedException` with the canonical "use Deactivate" message; (c) the inner is NEVER reached (NSubstitute's `DidNotReceiveWithAnyArgs`); (d) the audit logger receives an `AuditAction.Failed` entry. |
| `tests/UnitTests/JadeCapital.Trading.UnitTests/Persistence/ScannerFilterRepositoryIntegrationTests.cs` | **Created** | 4 integration scenarios (485 LOC) via SQLite in-memory + `TestScannerFilterDbContext` + `TestScannerFilterRepository`. The 4 scenarios cover Created/Updated+diff/Cross-tenant+Denied/Reads-no-audit per tasks.md §9a.2 Phase 2.1. The 5th scenario (DeleteAsync → Failed + throws NotSupportedException) is covered by the contract pin (1.1) for isolation. |
| `openspec/changes/2026-08-19-wave9-audit-finalization/tasks.md` | Modified | 9a.2 phases marked [x]; cumulative target updated to 1376. |
| `openspec/changes/2026-08-19-wave9-audit-finalization/apply-progress-2026-08-19-wave9-audit-finalization-slice-9a-2.md` | **Created** | This file. |

### TDD Cycle Evidence (Strict TDD active)

| Phase | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|-------|-----------|-------|------------|-----|-------|-------------|----------|
| 1.1 | `IScannerFilterRepositoryContractTests.cs` | Unit (NSubstitute) | N/A (new) | ✅ Confirmed (CS1061 + CS0246) | ✅ Passed (1/1) | ✅ 4 checks: interface shape, throw, no-inner, Failed audit | ✅ Clean (docstrings + comments only) |
| 1.2 | (interface surgery) | Compile-time | 1.1 + 2.1 REDs | ✅ Confirmed (interface lacks DeleteAsync) | ✅ Compiles | ➖ Single-method extension | ➖ None needed |
| 2.1 | `ScannerFilterRepositoryIntegrationTests.cs` | Integration (SQLite in-memory) | N/A (new) | ✅ Confirmed (CS0246: ScannerFilterAuditDecorator not found) | ✅ Passed (4/4) | ✅ 4 cases: Created/Updated+diff/Cross-tenant/Reads | ✅ Clean (docstrings + comments only) |
| 2.2 | (decorator impl) | Compile-time | 1.1 + 2.1 REDs | ✅ Confirmed (decorator missing) | ✅ All 5 REDs pass | ➖ Single-file implementation | ➖ None needed |

### Work Unit Evidence

| Evidence | Required value |
|---|---|
| **Focused test command** | `dotnet test tests/UnitTests/JadeCapital.Trading.UnitTests --filter "FullyQualifiedName~ScannerFilterAudit|ScannerFilterRepositoryIntegration|IScannerFilterRepositoryContractTests" --nologo --verbosity minimal` → **5/5 passed** (1 contract + 4 integration). |
| **Runtime harness command** | Full BE suite (per-project): `dotnet test tests/UnitTests/JadeCapital.{Shared.Kernel,Identity,Billing,Trading}.UnitTests --nologo --verbosity minimal` → Shared.Kernel 180 + Identity 364 + Billing 116 + Trading 716 = **1376/1376 passed**. `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings. |
| **Rollback boundary** | `git revert <merge-commit>` — Interface surgery: `IScannerFilterRepository` reverts to the bespoke shape (no `IRepository<ScannerFilter>` extension); `ScannerFilterRepository.DeleteAsync` stub reverts. Decorator: `services.Decorate` line removed from `TradingModuleRegistration.cs`; `ScannerFilterAuditDecorator.cs` deleted. Tests: `IScannerFilterRepositoryContractTests.cs` + `ScannerFilterRepositoryIntegrationTests.cs` deleted. `audit.events` has no rows for `ScannerFilter`. Zero behavior change to the production `ScannerFilterRepository` (only audit logging is added on the `AddAsync` + `UpdateAsync` paths; the `DeleteAsync` defensive stub is preserved at both the decorator + the production repo levels). |

### Test Summary

- **Total new tests written**: 5 (1 contract + 4 integration).
- **Total tests passing**: 1376/1376 BE (Shared.Kernel 180 + Identity 364 + Billing 116 + Trading 716).
- **Layers used**: Unit (1 contract via NSubstitute), Integration (4 via SQLite in-memory).
- **Approval tests** (refactoring): None — no refactoring tasks.
- **Pure functions created**: N/A — audit decorators are stateful wrappers, not pure functions.

### Deviations from Design

- **Interface surgery count: 2 file paths (forecast was 1)**. tasks.md §9a.2 forecast "1 file path" for the interface modification; the actual slice modified 2 files: (1) `IScannerFilterRepository.cs` (the interface surgery itself) and (2) `ScannerFilterRepository.cs` (the production `DeleteAsync` defensive stub — required for the IRepository<ScannerFilter> extension to compile). Both file paths are part of the SAME logical surgery (the interface can't compile without the production impl satisfying the inherited method). The 1-2 path delta is a sub-budget adjustment, not a scope expansion.
- **Test count: 5 (forecast was 5)**. tasks.md §9a.2 forecast "5/5 new tests pass (4 integration + 1 contract)" — matches actual exactly. The 4 integration scenarios are Created/Updated+diff/Cross-tenant/Reads; the 5th scenario (DeleteAsync → Failed + throws NotSupportedException) is pinned by the contract pin via NSubstitute for isolation. The contract pin tests the same intent (the inner is NEVER reached, the audit row is emitted, the throw happens) without spinning up SQLite — the cleaner separation keeps the test file sizes manageable (~191 LOC contract + ~485 LOC integration vs. ~700 LOC if combined).
- **Decorator signature — 6 dependencies (5 required + 1 optional)**: matches the Wave 7 7b.1 `StrategyAuditDecorator` shape (inner + audit + tenant + clock + IDiff? + DbContext?). The `IDiff?` defaults to the shared `JsonDiff` helper; the `DbContext?` is the production path for `ChangeTracker.OriginalValues` access. The in-memory test path passes a SQLite-compatible `TestScannerFilterDbContext` into the base-type `DbContext` parameter.
- **Decorator stays bespoke (does NOT use the generic `DecoratedRepository<ScannerFilter>` helper)**: the interface exposes user-scoped reads (`GetByUserAndNameAsync` + `ListByUserAsync`) that don't fit the generic `IRepository<T>.GetByIdAsync(Guid, ct)` shape. The bespoke decorator preserves the user-scoped read signatures + the bespoke `UpdateAsync` diff JSON contract. Mirrors the Wave 7 7b.1 `StrategyAuditDecorator` + 8a.2 `AlertAuditDecorator` + 8a.2 `TradeReviewAuditDecorator` + 8a.3 `PlannerSessionAuditDecorator` precedent.
- **Decorator UpdateAsync emits `AuditAction.Updated` (NOT `AuditAction.Deleted` when `IsActive = false`)**: per the design (and the Wave 7 7b.1 user decision #3 contract for soft-delete-by-flag aggregates), deactivation via `IsActive = false` is a state change, NOT a deletion event. The `IsTerminated` reflection check stays unchanged in the generic `DecoratedRepository<T>` — it only fires on `IsDeleted == true` or `Status ∈ {Cancelled, Terminated, Expired}` (neither matches `ScannerFilter.IsActive`). The result is an `Updated` event with an `isActive: true → false` diff. **NOTE**: the Wave 9 9a.2 integration tests do NOT explicitly cover the `Deactivate + UpdateAsync` path — the contract pin covers the `DeleteAsync` defensive stub, and the cross-tenant test covers the `UpdateAsync` ownership check. The `Deactivate` transition's exact diff JSON format (whether the diff includes `isActive: {before: true, after: false}`) is implicit in the implementation but not explicitly asserted. This is consistent with the Wave 7 7b.1 `StrategyAuditDecorator` precedent (the 7b.1 integration tests cover Created/Updated+diff/Cross-tenant/Reads/DeleteFailed; the Deactivate path is exercised but its diff format is asserted via the `Contains("isActive")` semantic check, not an exact-string match).
- **Path count = 6 (forecast was 5)**. tasks.md §9a.2 forecast "5 paths" — actual 6 paths (3 modified + 3 new). The +1 delta is the production `ScannerFilterRepository.cs` modification (see first deviation). All 6 paths are well below the 32-path budget.
- **No code-level issues**. All 5 new tests pass on the first run after GREEN; the build is green with zero new warnings; the cumulative suite is green with zero regression.

### Issues Found

- **No code-level issues**. All 5 new tests pass on the first run after GREEN; the build is green with zero new warnings; the cumulative suite is green with zero regression.

### Workload / PR Boundary

- **Mode**: feature-branch-chain (PR #31 of Wave 9 chain — targets `feature/wave9-trading-audit-1`).
- **Current work unit**: 9a.2 — `ScannerFilterAuditDecorator` (bespoke CRD-without-Delete).
- **Boundary**: starts at `feature/wave9-trading-audit-1` (where 9a.1 = PR #30 just landed); ends with 1 commit on `feature/wave9-scanner-filter-audit`. Targets `feature/wave9-trading-audit-1` (per Wave 9 §9a.2 PR table — PR #31 of the project, the 3rd slice in the Wave 9 chain).
- **Changed paths**: 6 (3 modified + 3 new for the slice code).
- **Estimated review budget impact**: ~1034 LOC insertions + 5 deletions = ~1039 LOC (well under the 1500 max_changed_lines budget). `size:exception` per Wave 5/6/7/8/9a.1 precedent (the spec explicitly anticipates this for 9a.2 — tasks.md §9a.2 "9a.2 size:exception preview").

### Cumulative state across Wave 9 chain

- 9a.1 (PR #30) → **9a.2 (PR #31, THIS)** → 9a.3 → 9b.1 → 9b.2 (3 slices remaining)
- This slice (9a.2) ships 1 bespoke CRD-without-Delete audit decorator for the `ScannerFilter` aggregate (Wave 4 slice 4a). The critical design decisions captured:
  1. **Interface surgery** — `IScannerFilterRepository` extends `IRepository<ScannerFilter>` (gaining the `DeleteAsync` defensive stub). The interface was already CRUD-shaped; the extension is backward-compatible. The production `ScannerFilterRepository.DeleteAsync` is a defensive stub throwing `NotSupportedException` (second line of defense in case a caller bypasses the decorator).
  2. **IsOwner cross-tenant check** on `AddAsync` + `UpdateAsync` (matches Wave 7 7a.1 User + 8a.1 Account + 8a.2 Alert + 8a.2 TradeReview + 8a.3 PlannerSession + 8a.3 PreTradeChecklist + 8b.1 StripeCustomer + 9a.1 AIRiskAdvice + 9a.1 CoachingPrompt precedent). On cross-tenant attempt: `AuditAction.Denied` + `UnauthorizedAccessException` + inner is NEVER reached.
  3. **DeleteAsync defensive stub** emits `AuditAction.Failed` (5) + throws `NotSupportedException` (matches Wave 7 7b.1 `StrategyAuditDecorator` + 7a.1 `UserAuditDecorator` precedent for non-deletable aggregates). The inner is NEVER reached.
  4. **`UpdateAsync` writes `Updated` with diff JSON** (before/after per changed field) via `ChangeTracker.OriginalValues` + the shared `JsonDiff` helper (matches Wave 7 7b.2 `JournalEntryAuditDecorator` + 8a.1 `AccountAuditDecorator` precedent).
  5. **Deactivate is `Updated`, not `Deleted`** — per the Wave 7 7b.1 user decision #3 contract for soft-delete-by-flag aggregates. The decorator's `UpdateAsync` is the canonical mutation path for `IsActive: true → false` transitions; the `DeleteAsync` defensive stub is only for misuse (e.g., a future caller that tries to hard-delete).
  6. **Co-located in Trading** (Trading → Trading) to mirror the Wave 8 8a.1 + 8a.2 + 8a.3 + 9a.1 precedent.
- Subsequent slices:
  - 9a.3 (AttachmentSweepAuditDecorator, ~350 LOC) — NEW batch soft-delete pattern (1-call-many-audit-rows).
  - 9b.1 (AdminAuditEndpoints + IAuditEventQueryStore + AuditRetentionBackgroundService, ~1000 LOC) — fills the empty Admin.Application + Admin.Infrastructure folders + adds the audit retention BackgroundService.
  - 9b.2 (TradeAttachmentUsage documentation, ~50 LOC) — doc-only; no code changes.

### Cross-slice invariants preserved

- **Cross-tenant `IsOwner` check** on `AddAsync` + `UpdateAsync` for `ScannerFilter` (mirrors 7a.1 User + 8a.1 Account + 8a.2 Alert + 8a.2 TradeReview + 8a.3 PlannerSession + 8a.3 PreTradeChecklist + 8b.1 StripeCustomer + 9a.1 AIRiskAdvice + 9a.1 CoachingPrompt). On cross-tenant attempt: `AuditAction.Denied` + `UnauthorizedAccessException` + inner is NEVER reached.
- **DeleteAsync defensive stub** emits `AuditAction.Failed` (5) + throws `NotSupportedException` (mirrors 7a.1 User + 7b.1 Strategy). The inner is NEVER reached.
- **Reads forwarded without audit** (no `GetByIdAsync` / `GetByUserAndNameAsync` / `ListByUserAsync` audit row emission). Matches Wave 6 + 7 + 8a.x + 8b.1 + 9a.1 precedent.
- **EF `ChangeTracker.OriginalValues` diff source** for `UpdateAsync` pre-mutation snapshot (when DbContext is wired). Falls back to post-mutation JSON snapshot when DbContext isn't tracked.
- **No-throw `TryAuditAsync`** wrapper around every `_audit.LogAsync` call. Defense-in-depth: a buggy audit logger never rolls back the main mutation.
- **DI registration via Scrutor's `services.Decorate<IScannerFilterRepository, ScannerFilterAuditDecorator>()`** with the underlying service registered first (the `Persistence.ScannerFilterRepository` registration precedes the decorate call).
- **Interface-level contract pin** enforced via `IScannerFilterRepository_DeleteAsync_DefensiveStub_Throws` reflection + behavior test. Mirrors the 7b.1 `IStrategyRepository` defensive stub + 7a.1 `IUserRepository` defensive stub precedent.
- **Entity-level docstring guarantee**: `ScannerFilter` is a soft-delete-by-flag aggregate per `src/2.Modules/Trading/JadeCapital.Trading.Domain/Scanner/ScannerFilter.cs` — `Activate(IClock)` / `Deactivate(IClock)` methods flip `IsActive`. The decorator + contract pin + interface surgery enforce the guarantee at 4 layers: domain docstring, interface shape (no public Delete method, only the inherited `IRepository<ScannerFilter>.DeleteAsync` defensive stub), runtime contract (decorator's `DeleteAsync` throws + emits `AuditAction.Failed`), and production repo's `DeleteAsync` defensive stub (second line of defense).
