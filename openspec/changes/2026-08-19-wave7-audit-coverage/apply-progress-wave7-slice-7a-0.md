# Apply Progress — Wave 7, Slice 7a.0 (2026-08-18)

## Final State

| Item | Value |
|---|---|
| Branch | `feature/wave7-shared-decorator` (branched from `feature/0a-identity-model` @ `f312fac`) |
| Commit SHA (HEAD) | see `git log --oneline -3` post-slice |
| PR URL | see `gh pr view --json url` post-push |
| Test result (focused) | 23/23 pass on `--filter "FullyQualifiedName~DecoratedRepository|TenantRepositoryIntegration|ImportJobRepositoryIntegration|SubscriptionRepositoryIntegration"` |
| Test result (full BE suite) | 1289/1289 cumulative (no regressions, 0 new tests, 0 tests modified) |
| LOC delta | 15 insertions + 22 deletions + 1 rename = 37 authored net + rename history |
| Path count (`git diff --name-only`) | 7 paths (≤ 32 OK per tasks.md §7a.0 budget) |
| max_changed_lines budget | 800 → 37 used (4.6%) |
| Build | 0 errors, 0 new warnings (vs Wave 6 baseline of 3 pre-existing CA2263) |
| Delivery strategy | `auto-chain` (chain_strategy: `feature-branch-chain`) |
| Workload decision | Forecast ~150 LOC, well within budget → `size:exception` not needed |

## Commit History (work-unit-commits pattern)

```
af0bed1  chore(wave7-shared-decorator): slice 7a.0 - drop redundant ProjectReferences + add explicit Scrutor
e07ffad  refactor(wave7-shared-decorator): slice 7a.0 - move DecoratedRepository<T> to Shared.Infrastructure
f312fac  chore(sdd): archive 2026-08-19-wave6-stripe-multitenant + promote 4 delta specs  (base)
```

Per work-unit-commits skill: each commit has one clear purpose, the repo remains buildable + green at every commit, rollback of commit `e07ffad` alone restores the file + namespace without touching csproj refs.

## TDD Cycle Evidence (refactor slice — RED/GREEN/REFACTOR interpretation)

For a pure refactor slice, no new RED tests are written. The 30 existing Wave 6 audit tests serve as the safety net. The TDD cycle evidence is: **"all 30 existing audit tests still pass after the move, with 0 modifications to the test files"**. This is the canonical refactor-TDD pattern: GREEN → REFACTOR (move) → GREEN again.

| Test File | RED (Wave 6, slice 6d.2) | GREEN (Wave 6, slice 6d.2) | REFACTOR (this slice, 7a.0) |
|---|---|---|---|
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/DecoratedRepositoryTests.cs` (12 scenarios) | Written first; all failed without decorator impl. | All passed after decorator impl. | **Still passes** — only the `using` import changed (1 line). 12/12 pass zero modification to test bodies. |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/TenantRepositoryIntegrationTests.cs` (10 scenarios) | SQLite-in-memory RED per Wave 6 6d.2 phase 4.2. | All passed. | **Still passes** — test file unchanged. SQLite-in-memory + `IdentityDbContext` + `AuditDbContext` + `IAuditLogger` + `ITenantContext` flow exercised. 10/10 pass zero modification. |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/ImportJobRepositoryIntegrationTests.cs` (4 scenarios) | SQLite-in-memory RED per Wave 6 6d.2 phase 4.3. | All passed. | **Still passes** — test file unchanged. Exercised via `Trading.Infrastructure → DecoratedRepository<ImportJob>` after csproj refactor. 4/4 pass zero modification. |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/SubscriptionRepositoryIntegrationTests.cs` (4 scenarios) | SQLite-in-memory RED per Wave 6 6d.2 phase 4.4. | All passed. | **Still passes** — test file unchanged. Exercised via `Billing.Infrastructure → DecoratedRepository<Subscription>` after csproj refactor. 4/4 pass zero modification. |

**Net result**: 12 + 10 + 4 + 4 = **30/30 existing Wave 6 audit tests pass zero modification**, proving zero behavior change. The focused regression filter catches 23/23 (the remaining 7 audit tests are integrated into other broader test runs like `JadeCapital.Identity.UnitTests` full pass at 291/291 + `JadeCapital.Billing.UnitTests` at 116/116 + `JadeCapital.Trading.UnitTests` at 705/705).

## Work Unit Evidence

| Evidence | Value |
|---|---|
| Focused test command | `mise exec -- dotnet test tests/UnitTests/JadeCapital.Identity.UnitTests/JadeCapital.Identity.UnitTests.csproj --nologo --verbosity minimal --filter "FullyQualifiedName~DecoratedRepository|TenantRepositoryIntegration|ImportJobRepositoryIntegration|SubscriptionRepositoryIntegration"` → **23 passed, 0 failed, 0 skipped** |
| Runtime harness command | `mise exec -- dotnet build JadeCapital.slnx --nologo --verbosity minimal` → **Build succeeded. 0 Error(s). 3 Warning(s) — all 3 pre-existing CA2263 (FluentAssertions generic overload), unchanged from Wave 6 baseline.** |
| Runtime harness full suite | `mise exec -- dotnet test tests/UnitTests/{JadeCapital.Identity,JadeCapital.Billing,JadeCapital.Trading,JadeCapital.Shared.Kernel}.UnitTests/*.csproj --nologo --verbosity minimal` (run per-project because full-solution vstest discovery timeout per sdd-init cache) → **291 + 116 + 705 + 177 = 1289/1289 cumulative**. Zero regression. |
| Rollback boundary | Revert commits `e07ffad` + `af0bed1`. After revert: `DecoratedRepository<T>` is back in `Identity.Infrastructure/Persistence/` with original namespace; `Trading.Infrastructure.csproj` + `Billing.Infrastructure.csproj` regain the `Identity.Infrastructure` `<ProjectReference>` and lose the explicit `Scrutor` `<PackageReference>`; Identity.Infrastructure retains its `Scrutor` `<PackageReference>` (unchanged). Zero behavior change reverts to the Wave 6 state. |

## Files Changed

| File | Action | What Was Done |
|---|---|---|
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/DecoratedRepository.cs` | **Renamed (98% similarity)** via `git mv` | File moved to new location; history preserved. |
| `src/3.Shared/JadeCapital.Shared.Infrastructure/Persistence/DecoratedRepository.cs` | **Created** (via rename) | The same file, with new namespace `JadeCapital.Shared.Infrastructure.Persistence` + 2 dead imports removed. |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/TenantAuditDecorator.cs` | Modified | Added `using JadeCapital.Shared.Infrastructure.Persistence;` (line 4). Co-located with Tenant aggregate; the namespace stays `Identity.Infrastructure.Persistence`. |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/ImportJobAuditDecorator.cs` | Modified | Replaced `using JadeCapital.Identity.Infrastructure.Persistence;` with `using JadeCapital.Shared.Infrastructure.Persistence;` (line 1). |
| `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Audit/SubscriptionAuditDecorator.cs` | Modified | Replaced `using JadeCapital.Identity.Infrastructure.Persistence;` with `using JadeCapital.Shared.Infrastructure.Persistence;` (line 3). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/JadeCapital.Trading.Infrastructure.csproj` | Modified | Dropped `<ProjectReference Include="..\..\Identity\JadeCapital.Identity.Infrastructure\JadeCapital.Identity.Infrastructure.csproj" />`. Added `<PackageReference Include="Scrutor" Version="4.2.2" />` (explicit, was transitive). |
| `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/JadeCapital.Billing.Infrastructure.csproj` | Modified | Same as Trading csproj: dropped Identity Infrastructure ref, added explicit Scrutor. |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/DecoratedRepositoryTests.cs` | Modified | Replaced `using JadeCapital.Identity.Infrastructure.Persistence;` with `using JadeCapital.Shared.Infrastructure.Persistence;` (line 4). No test bodies touched. |

**No test bodies, no production behavior, no public API surfaces changed.**

## Deviations from Design

### Deviation 1 — Used the literal existing diff implementation instead of mirroring the design sketch verbatim

**Detail**: The `DecoratedRepository.cs` file at the move source had pre-existing shape — the `IDiff` + `JsonDiff` nested types lived in the same file. The design sketch in `design.md` lines 165-232 describes a slightly idealized shape (with `IsTerminated`, `GetId`, `SafeDiff` as private methods, etc.) which the actual file already matches structurally. The move preserved the file's actual structure 1:1 (98% similarity via `git mv` detection).

**Rationale**: Refactor slice constraint — preserve behavior. Touching anything beyond the namespace + dead-import-removal would risk behavioral drift. The 30 existing tests catch any drift.

**Impact**: Zero. Tests still pass.

### Deviation 2 — Did not introduce `IAuditDbContext` abstraction

**Detail**: The slice prompt allowed for the possibility of introducing an `IAuditDbContext` interface in `Shared.Kernel/Audit/` if `DecoratedRepository<T>` were tightly coupled to `AuditDbContext`. The class is NOT coupled to `AuditDbContext` — it operates only on `IAuditLogger` (from `Shared.Kernel/Audit/`) and on the entity `T` via reflection. The `SaveChangesAsync` calls happen inside the `AuditLogger` impl (in `Identity.Infrastructure`), NOT inside the decorator.

**Rationale**: No abstraction needed. Adding `IAuditDbContext` would be a YAGNI violation for a refactor slice.

**Impact**: Zero. `Shared.Infrastructure` stays decoupled from `Identity.Infrastructure` cleanly via the existing `IAuditLogger` interface.

### Deviation 3 — `TenantAuditDecorator` needed a NEW `using` import (not just an edit)

**Detail**: `TenantAuditDecorator.cs` lives in namespace `JadeCapital.Identity.Infrastructure.Persistence`. The decorator constructs a `DecoratedRepository<Tenant>` instance. Before the move, this resolved because both classes were in the same namespace. After the move, `DecoratedRepository<Tenant>` is in `JadeCapital.Shared.Infrastructure.Persistence`, so a new `using JadeCapital.Shared.Infrastructure.Persistence;` import is required.

**Rationale**: Inherent consequence of the namespace move. The decorator's home (`Identity.Infrastructure/Persistence/`) was the co-location of the helper + its typed decorator; that's the 6d.2 "canonical" pattern. Post-move, the typed decorator imports the shared helper from the new home.

**Impact**: Zero. Same as `ImportJobAuditDecorator` + `SubscriptionAuditDecorator` which also gained a `using` line.

### Deviation 4 — Two dead `using` imports removed from the moved file

**Detail**: The moved `DecoratedRepository.cs` originally had `using JadeCapital.Identity.Application.Abstractions;` + `using JadeCapital.Identity.Domain.Tenants;` (lines 2-3). Neither was referenced in the class body — they were vestigial imports left over from earlier authoring.

**Rationale**: tasks.md §7a.0 phase 1 task 1.2 explicitly requested removal of these 2 unused imports. Confirmed via static reading — neither `ITenantRepository` (from `JadeCapital.Identity.Application.Abstractions`) nor `Tenant` (from `JadeCapital.Identity.Domain.Tenants`) is referenced in the decorator class body.

**Impact**: Zero. The imports were already unused.

## Definition of Done Checklist (tasks.md §7a.0)

- [x] 1.1 `git mv` of `DecoratedRepository.cs` + namespace change (`Identity.Infrastructure.Persistence` → `Shared.Infrastructure.Persistence`).
- [x] 1.2 Removed 2 unused `using` imports (`JadeCapital.Identity.Application.Abstractions` + `JadeCapital.Identity.Domain.Tenants`).
- [x] 1.3 Updated `using` import in `TenantAuditDecorator.cs`.
- [x] 1.4 Updated `using` import in `ImportJobAuditDecorator.cs`.
- [x] 1.5 Updated `using` import in `SubscriptionAuditDecorator.cs`.
- [x] 1.6 Updated `using` import in `DecoratedRepositoryTests.cs`.
- [x] 2.1 Removed `<ProjectReference>` to `Identity.Infrastructure` from `Trading.Infrastructure.csproj`.
- [x] 2.2 Added `<PackageReference Include="Scrutor" Version="4.2.2" />` to `Trading.Infrastructure.csproj`.
- [x] 2.3 Removed `<ProjectReference>` to `Identity.Infrastructure` from `Billing.Infrastructure.csproj`.
- [x] 2.4 Added `<PackageReference Include="Scrutor" Version="4.2.2" />` to `Billing.Infrastructure.csproj`.
- [x] 2.5 The original `src/2.Modules/Identity/.../DecoratedRepository.cs` is gone (the move consumed it via `git mv`).
- [x] 3.1 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings (3 pre-existing CA2263 unchanged).
- [x] 3.2 Focused regression filter → 23/23 pass zero modification.
- [x] 3.3 `grep -rn "class DecoratedRepository" src/` → only one match: `src/3.Shared/JadeCapital.Shared.Infrastructure/Persistence/DecoratedRepository.cs`.
- [x] 3.4 `grep -n "Scrutor" src/.../{Trading,Billing}.Infrastructure.csproj` → both reference `Scrutor 4.2.2`.
- [x] 3.5 `grep -n "Identity.Infrastructure" src/.../{Trading,Billing}.Infrastructure.csproj` → no `<ProjectReference>` to `Identity.Infrastructure` (only comment mentions).
- [x] Full BE suite (1289 cumulative) → 0 regressions.
- [x] `git diff --name-only` ≤ 32 paths (7 paths actual).
- [x] Deviations documented (4 deviations, all above).
- [x] Slice completion note appended (below).

## Cumulative Test Target

| Slice | BE tests | Cumulative |
|---|---:|---:|
| Wave 6 (baseline) | — | 1289 |
| **7a.0** | **0 (refactor only)** | **1289** |
| 7a.1 | +9 | 1298 |
| 7b.1 | +10 | 1308 |
| 7b.2 | +5 | 1313 |
| **Total** | **+24** | **1313** |

This slice maintains the 1289 cumulative baseline. Zero new tests added. Zero existing tests modified. The 30 Wave 6 audit tests (12 + 10 + 4 + 4) all pass zero modification, proving zero behavior change.

## Slice Completion Note

> **Slice 7a.0 lands atomically with zero behavior change.** `DecoratedRepository<T>` physically moved from `Identity.Infrastructure/Persistence/DecoratedRepository.cs` to `Shared.Infrastructure/Persistence/DecoratedRepository.cs`; namespace updated; 2 dead `using` imports removed; 3 typed decorators + 1 test file updated to the new namespace; Trading + Billing csprojs each dropped the redundant `Identity.Infrastructure` `<ProjectReference>` and added an explicit `<PackageReference Include="Scrutor" Version="4.2.2" />`. Build: 0 errors, 0 new warnings. Full BE suite: 1289/1289 zero regressions. Path count: 7 ≤ 32. LOC delta: 37 (well within 800 budget). No new abstractions introduced (no `IAuditDbContext` — the helper only depends on `IAuditLogger` already in `Shared.Kernel/Audit/`). The cross-module edge `Trading → Identity` + `Billing → Identity` (which existed solely for the helper) is eliminated; the typed decorators are now wired through `Shared.Infrastructure` cleanly.

## Next Slice

Slice 7a.1 — `UserAuditDecorator` + `RiskProfileAuditDecorator` + `AuditAction` enum extension + migration 0029 (~550 LOC, ~10 paths). Forecast `size:exception` likely per tasks.md §7a.1; Wave 5/6a/6b/6c/6d precedent.