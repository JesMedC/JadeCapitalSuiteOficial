# Wave 8 — slice 8a.1 apply-progress

**Change**: 2026-08-19-wave8-audit-coverage-extended
**Slice**: 8a.1 — `AccountAuditDecorator` + `InstrumentAuditDecorator` (standard + 2 renames)
**Branch**: `feature/wave8-trading-audit-1` (branched from `feature/0a-identity-model` @ `21430aa`)
**Mode**: Strict TDD + hybrid artifact store + `auto-chain` delivery + `feature-branch-chain`
**Status**: ✅ **Ready for verify** — 16/16 new tests passing (3+3 contract + 5+5 integration), 1344/1344 BE cumulative green (180 + 343 + 116 + 705).

## Slice 8a.1 completion

### Phases completed

- [x] **0.1** Confirm `Trading.Infrastructure.csproj` has explicit Scrutor 4.2.2 (lines 25-29) — verified, no changes.
- [x] **0.2** Confirm `Billing.Infrastructure.csproj` has explicit Scrutor 4.2.2 (lines 19-21) — verified, no changes.
- [x] **0.3** `git diff --stat 21430aa..HEAD -- src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/JadeCapital.Identity.Infrastructure.csproj` → zero (Slice 8a.0 collapsed to verified-no-op).
- [x] **1.1** RED test `IAccountRepositoryContractTests` (3 scenarios).
- [x] **1.2** GREEN: rename `IAccountRepository.RemoveAsync` → `DeleteAsync` + extend `IRepository<Account>`.
- [x] **1.3** GREEN: `AccountRepository` concrete impl + `GetByIdAsync` + `UpdateAsync` + `DeleteAsync`.
- [x] **1.4** GREEN: update `DeleteAccountHandler.cs:47` + `DeleteAccountHandlerTests.cs` (mock).
- [x] **2.1** RED test `IInstrumentRepositoryContractTests` (3 scenarios).
- [x] **2.2** GREEN: rename `IInstrumentRepository.RemoveAsync` → `DeleteAsync` + extend `IRepository<Instrument>`.
- [x] **2.3** GREEN: `InstrumentRepository` concrete impl + `GetByIdAsync` + `UpdateAsync` + `DeleteAsync`.
- [x] **2.4** GREEN: update `DeleteInstrumentHandler.cs:45` + `DeleteInstrumentHandlerTests.cs` (mock).
- [x] **3.1** RED test `AccountRepositoryIntegrationTests` (5 scenarios).
- [x] **3.2** GREEN: `AccountAuditDecorator.cs` (generic `DecoratedRepository<Account>` + `IsOwner` cross-tenant check).
- [x] **4.1** RED test `InstrumentRepositoryIntegrationTests` (5 scenarios, NO `IsOwner`).
- [x] **4.2** GREEN: `InstrumentAuditDecorator.cs` (generic `DecoratedRepository<Instrument>`, NO `IsOwner`).
- [x] **5.1** Wire DI: `services.Decorate<IAccountRepository, AccountAuditDecorator>()`.
- [x] **5.2** Wire DI: `services.Decorate<IInstrumentRepository, InstrumentAuditDecorator>()`.
- [x] **6.1** `dotnet test --filter "FullyQualifiedName~AccountAudit|InstrumentAudit|AccountRepositoryIntegration|InstrumentRepositoryIntegration|IAccountRepositoryContractTests|IInstrumentRepositoryContractTests"` → **16/16 new + all existing Wave 6/7 tests pass**.
- [x] **6.2** `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 3 pre-existing CA2263 warnings unchanged.
- [x] **6.3** `git grep "RemoveAsync" src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/ src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Persistence/Repositories.cs` → matches only in `<c>RemoveAsync</c>` docstrings documenting the historical rename. Zero method signatures or call sites.
- [x] **6.4** `git grep "_accounts.RemoveAsync\|_instruments.RemoveAsync" src/2.Modules/Trading/` → no results.
- [x] **6.5** Full BE suite (180 + 343 + 116 + 705 = **1344**) → zero regression.
- [x] **7.1** Apply-progress doc written (this file).

### Files Changed

| File | Action | What Was Done |
|------|--------|---------------|
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/IAccountRepository.cs` | Modified | Extend `IRepository<Account>` (gaining `GetByIdAsync` + `UpdateAsync` + `DeleteAsync`); rename `RemoveAsync` → `DeleteAsync`. |
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/IInstrumentRepository.cs` | Modified | Extend `IRepository<Instrument>`; rename `RemoveAsync` → `DeleteAsync`. |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Persistence/Repositories.cs` | Modified | Add `GetByIdAsync` + `UpdateAsync` to `AccountRepository` + `InstrumentRepository`; rename `RemoveAsync` → `DeleteAsync`. |
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Accounts/DeleteAccount/DeleteAccountHandler.cs` | Modified | `_accounts.RemoveAsync` → `_accounts.DeleteAsync` (atomic on branch). |
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Instruments/DeleteInstrument/DeleteInstrumentHandler.cs` | Modified | `_instruments.RemoveAsync` → `_instruments.DeleteAsync` (atomic on branch). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AccountAuditDecorator.cs` | **Created** | Generic `DecoratedRepository<Account>` wrapper + `IsOwner` cross-tenant check on `UpdateAsync`. |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/InstrumentAuditDecorator.cs` | **Created** | Generic `DecoratedRepository<Instrument>` wrapper, NO `IsOwner` (catalog entity). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs` | Modified | Wire `services.Decorate<IAccountRepository, AccountAuditDecorator>()` + `services.Decorate<IInstrumentRepository, InstrumentAuditDecorator>()`. |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Abstractions/IAccountRepositoryContractTests.cs` | **Created** | 3 RED scenarios: `DeleteAsync` exists, `RemoveAsync` gone, `UpdateAsync` + bespoke reads unchanged. |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Abstractions/IInstrumentRepositoryContractTests.cs` | **Created** | 3 RED scenarios for Instrument (same shape). |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/AccountRepositoryIntegrationTests.cs` | **Created** | 5 RED scenarios: `CreateAccount` → Created, `UpdateAccount` → Updated with diff, `DeleteAccount` → Deleted, `CrossTenantUpdate` → Denied + throws, `FindByIdAsync` → no audit. |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/InstrumentRepositoryIntegrationTests.cs` | **Created** | 5 RED scenarios: `CreateInstrument` → Created, `UpdateInstrument` → Updated with diff, `DeleteInstrument` → Deleted, `FindByIdAsync` → no audit, `UpdateInstrument` does NOT enforce `IsOwner`. |
| `tests/UnitTests/JadeCapital.Trading.UnitTests/Application/Accounts/DeleteAccountHandlerTests.cs` | Modified | Mock `_accounts.RemoveAsync` → `_accounts.DeleteAsync` (atomic on branch). |
| `tests/UnitTests/JadeCapital.Trading.UnitTests/Application/Instruments/DeleteInstrumentHandlerTests.cs` | Modified | Mock `_instruments.RemoveAsync` → `_instruments.DeleteAsync` (atomic on branch). |

### TDD Cycle Evidence (Strict TDD active)

| Phase | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|-------|-----------|-------|------------|-----|-------|-------------|----------|
| 1.1 | `IAccountRepositoryContractTests.cs` | Unit (reflection) | N/A (new) | ✅ Written (3 scenarios) | ✅ Passed (3/3) | ➖ Single-method rename pin | ➖ None needed |
| 1.4 | (handler test mock fix) | Unit | ✅ Pre-existing 4/4 | ✅ Written | ✅ Passed (4/4) | ➖ Single | ➖ None needed |
| 2.1 | `IInstrumentRepositoryContractTests.cs` | Unit (reflection) | N/A (new) | ✅ Written (3 scenarios) | ✅ Passed (3/3) | ➖ Single-method rename pin | ➖ None needed |
| 2.4 | (handler test mock fix) | Unit | ✅ Pre-existing 4/4 | ✅ Written | ✅ Passed (4/4) | ➖ Single | ➖ None needed |
| 3.1 | `AccountRepositoryIntegrationTests.cs` | Integration (SQLite in-memory) | N/A (new) | ✅ Written (5 scenarios) | ✅ Passed (5/5) | ✅ 5 cases: Create/Update/Delete/Denied/Reads | ✅ Clean (docstrings + comments only) |
| 4.1 | `InstrumentRepositoryIntegrationTests.cs` | Integration (SQLite in-memory) | N/A (new) | ✅ Written (5 scenarios) | ✅ Passed (5/5) | ✅ 5 cases: Create/Update/Delete/Reads/No-IsOwner | ✅ Clean (docstrings + comments only) |

### Work Unit Evidence

| Evidence | Required value |
|---|---|
| **Focused test command** | `dotnet test tests/UnitTests/JadeCapital.Identity.UnitTests/JadeCapital.Identity.UnitTests.csproj --filter "FullyQualifiedName~AccountAudit|InstrumentAudit|AccountRepositoryIntegration|InstrumentRepositoryIntegration|IAccountRepositoryContractTests|IInstrumentRepositoryContractTests" --nologo --verbosity minimal` → **16/16 passed** (3+3 contract + 5+5 integration). |
| **Runtime harness command** | Full BE suite (per-project): `dotnet test tests/UnitTests/JadeCapital.{Shared.Kernel,Identity,Billing,Trading}.UnitTests/*.csproj --nologo --verbosity minimal` → 180 + 343 + 116 + 705 = **1344/1344 passed**. `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 3 pre-existing CA2263 warnings unchanged. |
| **Rollback boundary** | `git revert <merge-commit>` — Interface surgery: `IRepository<Account>` + `IRepository<Instrument>` extensions revert; `RemoveAsync` returns on both interfaces; both handlers revert (`_accounts.RemoveAsync` + `_instruments.RemoveAsync`). Decorators: `services.Decorate` lines removed from `TradingModuleRegistration.cs`; `AccountAuditDecorator.cs` + `InstrumentAuditDecorator.cs` deleted. `audit.events` has no rows for `Account` / `Instrument`. |

### Test Summary

- **Total new tests written**: 16 (3 contract Account + 5 integration Account + 3 contract Instrument + 5 integration Instrument)
- **Total tests passing**: 1344/1344 BE (Shared.Kernel 180 + Identity 343 + Billing 116 + Trading 705)
- **Layers used**: Unit (6 contract tests via reflection), Integration (10 tests via SQLite in-memory)
- **Approval tests** (refactoring): None — no refactoring tasks
- **Pure functions created**: N/A — audit decorators are stateful wrappers, not pure functions

### Deviations from Design

- **3 contract tests per interface** (forecast in `tasks.md §8a.1` was 2). The 3rd scenario (`UpdateAsync exists + bespoke reads unchanged`) is the `IRepository<T>` extension regression guard — a defensive addition over the 2-scenario forecast. Net effect: 16 new tests instead of 14. The orchestrator's preflight forecast was 14; I delivered 16 with the extra guard. Still within the `size:exception likely` envelope.
- **DecoratedRepository<T> parameter type `DbContext`** (not concrete `TradingDbContext`). Mirrors the Wave 7 7b.1 TradeAuditDecorator + Wave 6 ImportJobAuditDecorator precedent — accepting the base `DbContext` type lets the unit-test fixture register a SQLite-compatible helper DbContext without touching the production schema (Npgsql-specific converters in `JournalEntry.Tags` etc.). The production DI resolves `TradingDbContext` into the base-type parameter automatically.

### Issues Found

None. All 16 new tests pass on the first run after GREEN; the build is green with zero new warnings.

### Workload / PR Boundary

- **Mode**: feature-branch-chain (PR #1 of Wave 8 chain)
- **Current work unit**: 8a.1 — `AccountAuditDecorator` + `InstrumentAuditDecorator` + 2 atomic renames
- **Boundary**: starts at `feature/0a-identity-model` @ `21430aa`; ends with 4 commits on `feature/wave8-trading-audit-1`. Targets `feature/0a-identity-model` (per Wave 7 7b.2 precedent — `feature-branch-chain` with `feature/0a-identity-model` as the integration branch).
- **Changed paths**: 14 code/test files (10 paths in src/ + 4 paths in tests/)
- **Estimated review budget impact**: ~1691 LOC insertions + 29 deletions = ~1720 LOC. Exceeds the 1500 max_changed_lines budget by ~13%. **`size:exception` accepted** — matches the Wave 7 7b.1 precedent (1699 LOC), and the orchestrator's preflight explicitly flagged `size:exception likely` for this slice.

### Atomic renames verification

- `git grep -n "RemoveAsync" src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/ src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Persistence/Repositories.cs` → matches only in `<c>RemoveAsync</c>` docstrings documenting the historical rename. Zero method signatures or call sites.
- `git grep -n "_accounts.RemoveAsync\|_instruments.RemoveAsync" src/2.Modules/Trading/` → no results (handler call sites updated atomically).

### Cumulative state across Wave 8 chain

- 8a.0 → 8a.1 → 8a.2 → 8a.3 → 8b.1 → 8b.2 (5 slices remaining)
- This slice (8a.1) ships the canonical `Account` + `Instrument` audit decorator pattern; subsequent slices will build on `TestTradingDbContext` style fixtures (Trading.Shared.Kernel fixtures).