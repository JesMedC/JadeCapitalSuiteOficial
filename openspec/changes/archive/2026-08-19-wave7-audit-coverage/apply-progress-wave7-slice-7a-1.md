# Apply Progress — Wave 7, Slices 7a.0 + 7a.1 (2026-08-18 / 2026-08-19)

> **This file MERGES the Wave 7 slice 7a.0 apply-progress (already shipped
> at `80e6a0f`) with slice 7a.1 (this slice) into a single cumulative
> progress document. Slice 7a.0 is included verbatim above the
> `--- 7a.1 ---` divider for context; slice 7a.1 details follow below.**

---

## Slice 7a.0 — `DecoratedRepository<T>` → `Shared.Infrastructure` (shipped at `80e6a0f`)

| Item | Value |
|---|---|
| Branch | `feature/wave7-shared-decorator` (branched from `feature/0a-identity-model` @ `f312fac`) |
| Final commit SHA | `80e6a0f chore(wave7-shared-decorator): slice 7a.0 - apply-progress` |
| PR | #21 (OPEN against `feature/0a-identity-model`) |
| Test result (focused) | 23/23 pass on `--filter "FullyQualifiedName~DecoratedRepository|TenantRepositoryIntegration|ImportJobRepositoryIntegration|SubscriptionRepositoryIntegration"` |
| Test result (full BE suite) | 1289/1289 cumulative (no regressions, 0 new tests, 0 tests modified) |
| LOC delta | 15 insertions + 22 deletions + 1 rename = 37 authored net |
| Path count | 7 paths (≤ 32 OK) |
| max_changed_lines budget | 800 → 37 used (4.6%) |
| Build | 0 errors, 0 new warnings |
| Delivery strategy | `auto-chain` (chain_strategy: `feature-branch-chain`) |
| Workload decision | `size:exception` accepted (mechanical count 815 vs authored 179 due to rename double-count; per Wave 5/6 precedent; `80e6a0f` evidence revision shipped) |

Slice 7a.0 was a pure refactor: 30 existing Wave 6 audit tests pass zero modification; zero behavior change.

---

# --- 7a.1 ---

# Apply Progress — Wave 7, Slice 7a.1 (2026-08-19)

## Final State

| Item | Value |
|---|---|
| Branch | `feature/wave7-identity-audit` (branched from `feature/wave7-shared-decorator` @ `80e6a0f`) |
| Final commit SHA | `4773b05 feat(wave7-identity-audit): slice 7a.1 phase 6 - migration 0029 audit events CHECK widen` (HEAD before this apply-progress lands; will add a 6th `chore(wave7-identity-audit): slice 7a.1 validate - apply-progress` commit) |
| PR | new #22 (OPEN against `feature/wave7-shared-decorator` per `feature-branch-chain` strategy; child PR after PR #21) |
| Test result (focused) | 14/14 pass on `--filter "FullyQualifiedName~UserAudit\|RiskProfileAudit\|AuditAction\|UserRepositoryIntegration\|RiskProfileRepositoryIntegration\|IUserRepository\|IRiskProfileRepository"` (per-project Identity.UnitTests) |
| Test result (full BE suite) | 1306/1306 cumulative (was 1289 baseline + 17 new = 1306; orchestrator claimed 1298 — actual is 1306 because the 5 test files per phase list yield 17 new tests, not 9) |
| LOC delta | 1401 insertions + 11 deletions = 1412 authored net (excludes this apply-progress file) |
| Path count | 16 paths (≤ 32 OK per tasks.md §7a.1 budget) |
| max_changed_lines budget | 1500 → 1412 used (94.1%) |
| Build | 0 errors, 0 new warnings (3 pre-existing CA2263 unchanged) |
| Delivery strategy | `auto-chain` (chain_strategy: `feature-branch-chain`) |
| Workload decision | `size:exception` likely — 1412/1500 (94.1%) per orchestrator's pre-acquired budget |

## Commit History (work-unit-commits pattern)

```
4773b05  feat(wave7-identity-audit): slice 7a.1 phase 6 - migration 0029 audit events CHECK widen
1d5d6f9  feat(wave7-identity-audit): slice 7a.1 phase 4 - UserAuditDecorator
cb89d9e  feat(wave7-identity-audit): slice 7a.1 phase 2-3 - IUserRepository + IRiskProfileRepository surgery
d85459b  feat(wave7-identity-audit): slice 7a.1 phase 1 - AuditAction.Denied+Failed enum extension
80e6a0f  chore(wave7-shared-decorator): slice 7a.0 - apply-progress  (base)
```

The orchestrator's suggested 6-commit plan was condensed to 4 because phases 2+3 were one logical surgery unit (both interfaces + concrete impls in one PR is atomic — splitting would leave the interface mismatched the impl), and phases 4+5 (both decorators + DI) were also condensed to share the `IdentityModuleRegistration` DI edit (the RiskProfile DI line landed with phase 5, not phase 4). The 6th commit is this apply-progress document.

Per work-unit-commits skill: each commit has one clear purpose, the repo remains buildable + green at every commit, rollback of any single commit cleanly reverses its scope.

## TDD Cycle Evidence

| Phase | Task | Test File | Layer | Safety Net | RED | GREEN | REFACTOR | Notes |
|---|---|---|---|---|---|---|---|---|
| 1 | 1.1 | `tests/UnitTests/JadeCapital.Shared.Kernel.UnitTests/Audit/AuditActionTests.cs` | Unit | ✅ 177/177 Shared.Kernel baseline | ✅ CS0117 `AuditAction` no `Denied`/`Failed` | ✅ 3/3 pass after enum extension | ✅ Single (literal byte value assertions) | — |
| 1 | 1.2 | `tests/UnitTests/JadeCapital.Shared.Kernel.UnitTests/Audit/IAuditLoggerContractTests.cs` | Unit | N/A (this is the fixup) | ❌ was `HasExactlyFourValues` — broke with enum extension | ✅ now `HasExactlySixValues` 180/180 pass | ✅ Single (renamed test + updated expected list) | Required because the contract test pinned the Wave 6 enum shape |
| 2 | 2.1+2.2 | `tests/UnitTests/JadeCapital.Identity.UnitTests/Abstractions/IUserRepositoryContractTests.cs` | Unit | ✅ 291/291 Identity baseline | ✅ CS1061 `'UserRepository' does not contain DeleteAsync` | ✅ 2/2 pass after interface + impl | ➖ None needed | Reflection test walks `IRepository<User>` base (Type.GetMethod doesn't flatten inherited interface members) |
| 3 | 3.1+3.2 | `tests/UnitTests/JadeCapital.Identity.UnitTests/Abstractions/IRiskProfileRepositoryContractTests.cs` | Unit | ✅ 291/291 Identity baseline | ✅ CS1061 `'RiskProfileRepository' does not contain DeleteAsync` | ✅ 3/3 pass after interface + impl | ➖ None needed | Scenario 1 is a baseline pin of the existing `MarkSupersededAsync` signature from slice 1a.1b |
| 4 | 4.1+4.2 | `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/UserRepositoryIntegrationTests.cs` | Integration (SQLite-in-memory) | ✅ 5/5 focused Identity slice pre-test | ✅ CS0246 `'UserAuditDecorator' not found` | ✅ 5/5 pass after decorator + DI | ➖ None needed | Mirrors `TenantRepositoryIntegrationTests` fixture pattern; force-creates `audit.events` table via raw SQL workaround |
| 4 | fix | `IUserRepositoryContractTests.cs` reflection fixup | Unit | N/A | ❌ regression after IRepository<User> extension — `GetMethod` doesn't walk base | ✅ reflection walks `IUserRepository` + `IRepository<User>` | ➖ None needed | Required because IUserRepository now extends IRepository<User>; reflection test must walk the chain |
| 5 | 5.1+5.2 | `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/RiskProfileRepositoryIntegrationTests.cs` | Integration (SQLite-in-memory) | ✅ 305/305 Identity baseline | ✅ CS0246 `'RiskProfileAuditDecorator' not found` | ✅ 4/4 pass after decorator + DI | ➖ None needed | Bespoke shape; supersession diff uses `isActive` + `supersededAt` (NOT design.md's `SupersededBy` — entity has no such field; documented deviation) |

### Test Summary

- **Total tests written**: 17 (3 AuditActionTests + 2 IUserRepositoryContractTests + 3 IRiskProfileRepositoryContractTests + 5 UserRepositoryIntegrationTests + 4 RiskProfileRepositoryIntegrationTests)
- **Total tests passing**: 17 new + 1289 cumulative = 1306
- **Layers used**: Unit (8 — AuditAction + contract tests), Integration (9 — User + RiskProfile integration tests with SQLite-in-memory)
- **Approval tests** (refactoring): 0 (no refactoring tasks; the IAuditLoggerContractTests 4→6 fixup is REFACTOR not approval)
- **Pure functions created**: 0 (audit decorators are inherently stateful)

## Work Unit Evidence

| Evidence | Required value |
|---|---|
| Focused test command | `mise exec -- dotnet test tests/UnitTests/JadeCapital.Identity.UnitTests/JadeCapital.Identity.UnitTests.csproj --nologo --verbosity minimal --filter "FullyQualifiedName~UserAudit\|RiskProfileAudit\|AuditAction\|UserRepositoryIntegration\|RiskProfileRepositoryIntegration\|IUserRepository\|IRiskProfileRepository"` → **14 passed, 0 failed, 0 skipped** (Identity.UnitTests; Shared.Kernel.UnitTests adds 3 more for total 17) |
| Runtime harness command | `mise exec -- dotnet build JadeCapital.slnx --no-incremental --nologo --verbosity minimal` → **Build succeeded. 0 Error(s). 3 Warning(s) — all 3 pre-existing CA2263, unchanged from Wave 6 + 7a.0 baseline.** |
| Runtime harness full suite | `mise exec -- dotnet test tests/UnitTests/{JadeCapital.Identity,JadeCapital.Billing,JadeCapital.Trading,JadeCapital.Shared.Kernel}.UnitTests/*.csproj --nologo --verbosity minimal` (run per-project because full-solution vstest discovery times out per sdd-init cache) → **305 + 116 + 705 + 180 = 1306/1306 cumulative**. Zero regression. |
| Runtime harness migration | `psql -v ON_ERROR_STOP=1 -f infrastructure/postgres/migrations/0029_audit_events_action_check_widen.sql` → **N/A (sandbox constraint)** — SQL syntax reviewed statically against the 0027 / 0028 templates; idempotency confirmed via the `DROP CONSTRAINT IF EXISTS` guard. Runtime psql verification lands in `sdd-verify` (the sandbox does not have psql). |
| Rollback boundary | Revert commits `d85459b` + `cb89d9e` + `1d5d6f9` + `4773b05`. After revert: `AuditAction.Denied` + `Failed` enum values are gone (audit.events CHECK constraint would reject them); `IUserRepository.DeleteAsync` stub is gone; `IRiskProfileRepository.DeleteAsync` stub is gone; `UserAuditDecorator` + `RiskProfileAuditDecorator` are gone; DI `services.Decorate` calls are gone; migration 0029 is gone (the migration runner skips it). RiskProfile supersession diff reverts to `isActive` + `supersededAt` (the original Wave 6 shape, not the design.md aspirational `SupersededBy`). Zero behavior change for the existing Tenant/ImportJob/Subscription decorators. |

## Files Changed

| File | Action | What Was Done |
|---|---|---|
| `src/3.Shared/JadeCapital.Shared.Kernel/Audit/AuditAction.cs` | Modified | Added `Denied = 4` + `Failed = 5` after `Restored = 3`. No renumbering — historical audit.events rows remain readable. |
| `tests/UnitTests/JadeCapital.Shared.Kernel.UnitTests/Audit/AuditActionTests.cs` | **Created** | 3 RED scenarios: Denied/Failed exist at bytes 4/5; existing values unchanged. |
| `tests/UnitTests/JadeCapital.Shared.Kernel.UnitTests/Audit/IAuditLoggerContractTests.cs` | Modified | Renamed `HasExactlyFourValues_WithExpectedByteOrder` → `HasExactlySixValues_WithExpectedByteOrder`. Updated expected list to include Denied + Failed. The Wave 6 contract pin now reflects the new shape. |
| `src/2.Modules/Identity/JadeCapital.Identity.Application/Abstractions/IUserRepository.cs` | Modified | Extends `IRepository<User>` (was a standalone interface before). Removed the explicit `AddAsync`/`UpdateAsync` declarations (inherited from base). Kept User-specific reads (FindByEmailAsync, FindByIdAsync, ListByTenantIdAsync, CountByTenantIdAsync). |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/Repositories.cs` | Modified | `UserRepository.GetByIdAsync(Guid, ct)` added to satisfy `IRepository<T>` base. `UserRepository.DeleteAsync(User, ct)` is the defensive STUB that throws `NotSupportedException("User deletion happens via Tenant reassignment, not direct delete")`. |
| `src/2.Modules/Identity/JadeCapital.Identity.Application/Abstractions/IRiskProfileRepository.cs` | Modified | Added `DeleteAsync(RiskProfile, ct)` overload. Keeps bespoke interface shape (does NOT extend `IRepository<RiskProfile>` per orchestrator's "bespoke shape" requirement). |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/RiskProfileRepository.cs` | Modified | `RiskProfileRepository.DeleteAsync(RiskProfile, ct)` is the defensive STUB that throws `NotSupportedException("RiskProfile deletion happens via MarkSupersededAsync, not direct delete")`. |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Abstractions/IUserRepositoryContractTests.cs` | **Created** | 2 scenarios: interface method `DeleteAsync(User, ct)` exists (walks `IRepository<User>` base); concrete stub throws with canonical message containing "Tenant reassignment". |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Abstractions/IRiskProfileRepositoryContractTests.cs` | **Created** | 3 scenarios: `MarkSupersededAsync(Guid, IClock, ct)` baseline pin; `DeleteAsync(RiskProfile, ct)` exists; concrete stub throws with "MarkSupersededAsync". |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/UserAuditDecorator.cs` | **Created** | Mirrors `TenantAuditDecorator` shape with 3 slice-specific deviations: (1) cross-tenant `IsOwner` check `user.Id == _tenant.CurrentUserId.Value OR IsSuperAdmin` (per design.md); (2) `DeleteAsync` is a defensive STUB that emits `AuditAction.Failed` BEFORE re-throwing `NotSupportedException` (the inner is NEVER reached); (3) `UpdateAsync` cross-tenant rejection emits `AuditAction.Denied` + throws `UnauthorizedAccessException`. |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/RiskProfileAuditDecorator.cs` | **Created** | BESPOKE — does NOT use `DecoratedRepository<T>`. Wraps `MarkSupersededAsync` directly + emits `AuditAction.Deleted` with supersession diff `{isActive: true→false, supersededAt: null→now}`. `DeleteAsync` emits `AuditAction.Failed` + re-throws. Cross-tenant `IsOwner` check on `profile.UserId`. |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/DependencyInjection/IdentityModuleRegistration.cs` | Modified | `services.Decorate<IUserRepository, UserAuditDecorator>()` + `services.Decorate<IRiskProfileRepository, RiskProfileAuditDecorator>()`. |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/UserRepositoryIntegrationTests.cs` | **Created** | 5 scenarios: Create → Created; Update (displayName) → Updated with diff; Delete → Failed + re-throws; TenantId from ITenantContext.Current; fully attributable (entity_type + user_id + tenant_id). SQLite-in-memory fixture mirrors `TenantRepositoryIntegrationTests` pattern. |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/RiskProfileRepositoryIntegrationTests.cs` | **Created** | 4 scenarios: Create → Created; MarkSuperseded → Deleted with supersession diff (isActive + supersededAt); Delete → Failed + re-throws; TenantId from ITenantContext.Current. Same SQLite-in-memory fixture. |
| `infrastructure/postgres/migrations/0029_audit_events_action_check_widen.sql` | **Created** | Idempotent CHECK widening. `BEGIN; ALTER TABLE audit.events DROP CONSTRAINT IF EXISTS ck_audit_events_action; ALTER TABLE audit.events ADD CONSTRAINT ck_audit_events_action CHECK (action IN (0, 1, 2, 3, 4, 5)); COMMENT ON CONSTRAINT ck_audit_events_action ON audit.events IS '...'; COMMIT;` |
| `infrastructure/postgres/migrate.Dockerfile` | Modified | COPY line + 2 psql invocations (happy path + retry path) for migration 0029. |

**No production behavior for existing decorators (Tenant/ImportJob/Subscription) changed. No public API surface beyond the 2 new decorators + 2 interface methods broke.**

## Deviations from Design

### Deviation 1 — `IUserRepository` extended `IRepository<User>` (deviation from design.md's "shape" but matches Wave 6 canonical pattern)

**Detail**: design.md §"New Abstractions" line 49 shows `IUserRepository : IRepository<User>` as the canonical shape. The actual pre-7a.1 interface was standalone (did NOT extend `IRepository<User>`). The 7a.1 surgery extended the interface to match both design.md AND the Wave 6 `ITenantRepository` precedent.

**Rationale**: `DecoratedRepository<T>` requires `IRepository<T>` as the inner type. Without the extension, the UserAuditDecorator cannot wrap the inner. The extension is the cleanest fix and matches the Wave 6 pattern.

**Impact**: 
- Added 1 method to UserRepository (`GetByIdAsync`) — delegates to the same EF query as `FindByIdAsync`.
- The orchestrator's prompt phase 2 didn't list this surgery explicitly, but the decorator (phase 4) requires it.
- Reflected in apply-progress with the `IUserRepositoryContractTests` reflection fixup (Type.GetMethod doesn't walk inherited interface members).

### Deviation 2 — `IRiskProfileRepository` does NOT extend `IRepository<RiskProfile>` (kept bespoke)

**Detail**: tasks.md Phase 5.1 says "interface now extends `IRepository<RiskProfile>`". The orchestrator's runtime instruction is explicit: "RiskProfile decorator is bespoke — wraps `MarkSupersededAsync` directly. Do NOT try to fit it into the generic `DecoratedRepository<T>` shape." The interface stays bespoke; the decorator does NOT use `DecoratedRepository<T>`.

**Rationale**: RiskProfile has no `UpdateAsync` in its canonical mutation surface (the supersede IS the termination). The bespoke shape is the correct design. tasks.md's "extends `IRepository<RiskProfile>`" is aspirational and conflicts with the orchestrator's explicit instruction.

**Impact**: Zero. The bespoke decorator handles AddAsync, MarkSupersededAsync, GetActiveAsync, GetByIdAsync, DeleteAsync, ListByUserAsync directly without the generic helper. The DI works via Scrutor's `services.Decorate<IRiskProfileRepository, RiskProfileAuditDecorator>()`.

### Deviation 3 — `RiskProfile` supersession diff uses `isActive` + `supersededAt` (NOT design.md's `SupersededBy`)

**Detail**: design.md §"The 5 Typed Decorator Shapes" → "RiskProfileAuditDecorator" line 332 suggests the diff payload uses `{ "SupersededBy": { "before": null, "after": "<guid>" }, "SupersededAtUtc": { "before": null, "after": "<utcNow>" } }`. The actual `RiskProfile` aggregate (`src/2.Modules/Identity/JadeCapital.Identity.Domain/RiskProfile/RiskProfile.cs` lines 47-50) has NO `SupersededBy` field — only `IsActive` (bool) + `SupersededAt` (DateTimeOffset?).

**Rationale**: The aggregate's real shape models the supersession via IsActive + SupersededAt. The diff payload must reflect the actual entity, not an aspirational field. The decorator writes `{isActive: true→false, supersededAt: null→now}` to match.

**Impact**: The audit row's `changes` JSON uses `isActive` + `supersededAt` instead of `SupersededBy` + `SupersededAtUtc`. The test (`RiskProfileRepositoryIntegrationTests.SupersedeProfile_WritesAuditEvent_WithActionDeleted_AndSupersessionDiff`) asserts both keys are present, pinning the new shape.

### Deviation 4 — Phase ordering: migration 0029 lands AFTER decorators in commit history

**Detail**: tasks.md phases Migration 0029 as Phase 2 (immediately after AuditAction enum). The orchestrator's runtime instruction orders migration as Phase 6 (after both decorators). The commit history follows the orchestrator's Phase 6 ordering.

**Rationale**: The orchestrator's ordering groups migration with Dockerfile wiring as a unit; tasks.md's ordering interleaves interface surgery between the migration and the migration's intended consumers. Both orderings are valid; the orchestrator's is the runtime authority.

**Impact**: All decorators and migration land in the SAME PR (atomicity per the orchestrator's "Important behavioral constraints" contract), regardless of commit order. The branch state at the final commit has both.

### Deviation 5 — Test count: 17 new tests vs. orchestrator's claimed 9

**Detail**: The orchestrator's "Definition of Done" says "9 new tests pass + all existing Wave 6 + 7a.0 tests pass (zero regression)" and "1298/1298 cumulative". The actual is 17 new tests + 1289 baseline = 1306 cumulative.

**Rationale**: The orchestrator's phase list enumerates 5 test files: AuditActionTests (3 scenarios), IUserRepositoryContractTests (2 scenarios), IRiskProfileRepositoryContractTests (3 scenarios), UserRepositoryIntegrationTests (5 scenarios), RiskProfileRepositoryIntegrationTests (4 scenarios). Total: 17 tests, not 9. The orchestrator's "9" appears to be a typo or an undercount.

**Impact**: Cumulative suite is 1306/1306, not 1298/1298. Zero regression either way (1289 baseline preserved). The 17 extra tests are all on the same focused filter, all passing.

### Deviation 6 — `IRiskProfileRepository.DeleteAsync(RiskProfile, ct)` is a STUB, not a delegation to `MarkSupersededAsync`

**Detail**: tasks.md Phase 5.2 says the concrete `DeleteAsync(RiskProfile, ct)` is a "real implementation that calls `MarkSupersededAsync(profile.SupersededBy, clock, ct)`". The orchestrator's runtime instruction is explicit: "The `RiskProfileAuditDecorator.DeleteAsync(RiskProfile, ct)` short-circuits to `Failed` audit + re-throw (the underlying impl is NOT the public path)".

**Rationale**: The orchestrator's design is correct — making DeleteAsync a real delegation would mean handlers could bypass `MarkSupersededAsync` by calling `DeleteAsync`. The decorator short-circuit + the inner stub throw together enforce the canonical surface.

**Impact**: `RiskProfileRepository.DeleteAsync(RiskProfile, ct)` throws `NotSupportedException("RiskProfile deletion happens via MarkSupersededAsync, not direct delete")`. The decorator never reaches it; the inner is defense-in-depth.

### Deviation 7 — `IUserRepositoryContractTests` reflection test walks `IRepository<User>` base

**Detail**: After making `IUserRepository` extend `IRepository<User>`, `Type.GetMethod("DeleteAsync", new[] { typeof(User), typeof(CancellationToken) })` returns null because `GetMethod` doesn't flatten inherited interface members. The test must walk the chain manually.

**Rationale**: The contract test verifies the interface surface — including inherited members. Walking the chain is the correct reflection pattern.

**Impact**: The reflection test now includes a helper `FindMethodOnInterfaceOrBases` that searches the interface and its `IRepository<T>` base. The test passes; the inheritance is correctly verified.

## Definition of Done Checklist (tasks.md §7a.1)

- [x] 1.1 `AuditActionEnumTests` RED → 3/3 pass.
- [x] 1.2 `AuditAction.cs` extended with `Denied = 4` + `Failed = 5`.
- [x] 2.1 `0029_audit_events_action_check_widen.sql` created (filename per orchestrator's runtime instruction). Idempotent. Wired into `migrate.Dockerfile` happy + retry path.
- [x] 2.2 Migration SQL reviewed statically; idempotency confirmed via `DROP CONSTRAINT IF EXISTS` guard. Runtime psql verification lands in `sdd-verify` (sandbox has no psql).
- [x] 3.1 `IRepository<T>` already had the 4 methods from Wave 6 6d.2. No surgery needed.
- [x] 4.1 `IUserRepositoryContractTests` RED → 2/2 pass.
- [x] 4.2 `IUserRepository.cs` extended to `IRepository<User>`. `UserRepository.DeleteAsync(User, ct)` STUB throws.
- [x] 4.3 `UserRepositoryDeleteAsyncStubTests` — folded into `IUserRepositoryContractTests` scenario #2 (no separate file; the test contract test verifies both interface shape and concrete throw semantics).
- [x] 5.1 `IRiskProfileRepositoryContractTests` RED → 3/3 pass.
- [x] 5.2 `IRiskProfileRepository.cs` keeps bespoke shape. `DeleteAsync(RiskProfile, ct)` overload added; concrete impl throws.
- [x] 6.1 `UserAuditDecoratorTests` (5 scenarios) — folded into `UserRepositoryIntegrationTests` (the orchestrator's prompt consolidates unit + integration into the SQLite-in-memory integration test, matching Wave 6 precedent).
- [x] 6.2 `UserAuditDecorator.cs` created.
- [x] 7.1 `UserRepositoryIntegrationTests` RED → 5/5 pass.
- [x] 7.2 `UserRepositoryIntegrationTests.cs` (SQLite-in-memory) created.
- [x] 8.1 `RiskProfileAuditDecoratorTests` (4 scenarios) — folded into `RiskProfileRepositoryIntegrationTests` (same consolidation rationale).
- [x] 8.2 `RiskProfileAuditDecorator.cs` created (bespoke).
- [x] 9.1 `RiskProfileRepositoryIntegrationTests` RED → 4/4 pass.
- [x] 9.2 `RiskProfileRepositoryIntegrationTests.cs` (SQLite-in-memory) created.
- [x] 10.1 DI: `services.Decorate<IUserRepository, UserAuditDecorator>()` + `services.Decorate<IRiskProfileRepository, RiskProfileAuditDecorator>()` in `IdentityModuleRegistration.cs`.
- [x] 11.1 Focused test filter → 14/14 Identity + 3/3 Shared.Kernel = 17 new tests pass.
- [x] 11.2 Build clean: 0 errors, 0 new warnings (3 pre-existing CA2263 unchanged).
- [x] 11.3 Full BE suite (1289 + 17 = 1306) → zero regression.
- [x] 11.4 Migration SQL syntax verified statically; idempotent re-run safe (DROP IF EXISTS + ADD CONSTRAINT).
- [x] `git diff --name-only feature/wave7-shared-decorator..feature/wave7-identity-audit` ≤ 32 paths (16 paths actual).
- [x] All `[ ]` tasks for the slice marked `[x]`.
- [x] Slice completion note appended (below).
- [x] Deviations documented (7 deviations, all above).
- [x] Cumulative suite remains green (no regressions).

## Cumulative Test Target

| Slice | BE tests | Cumulative |
|---|---:|---:|
| Wave 6 (baseline) | — | 1289 |
| **7a.0** | **0 (refactor only)** | **1289** |
| **7a.1** | **+17** | **1306** |
| 7b.1 | +10 (forecast) | 1316 |
| 7b.2 | +5 (forecast) | 1321 |
| **Cumulative Wave 7 (forecast)** | **+32** | **1321** |

This slice adds 17 BE tests (orchestrator's "9" claim was an undercount). The cumulative suite remains 100% green. The decorators extend the audit-event coverage from 3 of 8 user-owned aggregates (Tenant + ImportJob + Subscription from Wave 6) to 5 of 8 (User + RiskProfile added here); Strategy + Trade + JournalEntry land in slices 7b.1 + 7b.2.

## Slice Completion Note

> **Slice 7a.1 lands atomically: `AuditAction` enum extension + migration 0029 widening + 2 interface surgeries + 2 typed audit decorators + 2 integration test files + 17 new BE tests.** The `UserAuditDecorator` mirrors the Wave 6 `TenantAuditDecorator` shape with 3 slice-specific deviations (cross-tenant IsOwner check; DeleteAsync defensive stub emitting `AuditAction.Failed`; cross-tenant `UpdateAsync` emitting `AuditAction.Denied` + `UnauthorizedAccessException`). The `RiskProfileAuditDecorator` is bespoke (does NOT use `DecoratedRepository<T>` because RiskProfile has no `UpdateAsync`; the supersede IS the termination). The supersession diff payload uses `isActive` + `supersededAt` (the actual aggregate fields), NOT the design.md sketch's `SupersededBy` (the entity has no such field). Migration 0029 widens the `audit.events` CHECK constraint atomically with the enum extension — without the migration, the new decorators cannot insert `Denied`/`Failed` rows. DI: `services.Decorate<IUserRepository, UserAuditDecorator>()` + `services.Decorate<IRiskProfileRepository, RiskProfileAuditDecorator>()` in `IdentityModuleRegistration`. Build: 0 errors, 0 new warnings. Full BE suite: 1306/1306 zero regressions. Path count: 16 ≤ 32. LOC delta: 1412/1500 (94.1%) — within the orchestrator's pre-acquired `size:exception` budget. PR #22 opens against `feature/wave7-shared-decorator` (per `feature-branch-chain` strategy; PR #21 is the parent already OPEN against `feature/0a-identity-model`).

## Cumulative PR Chain

| PR | Branch | Status | Base | Title |
|---|---|---|---|---|
| #21 | `feature/wave7-shared-decorator` | OPEN (from 7a.0) | `feature/0a-identity-model` | slice 7a.0 — DecoratedRepository<T> → Shared.Infrastructure |
| **#22** | **`feature/wave7-identity-audit`** | **OPEN (this slice)** | **`feature/wave7-shared-decorator`** | **slice 7a.1 — UserAuditDecorator + RiskProfileAuditDecorator + AuditAction.Denied/Failed + migration 0029** |

The chain follows the `feature-branch-chain` strategy: PR #1 (slice 7a.0) targets `feature/0a-identity-model`; PR #2 (slice 7a.1) targets `feature/wave7-shared-decorator` (the previous slice's branch). The tracker PR aggregates the feature branch to `main` later (deferred to sdd-archive).

## Next Slice

Slice 7b.1 — `StrategyAuditDecorator` + `TradeAuditDecorator` + `RemoveAsync` → `DeleteAsync` rename (5 handler call sites) + migration 0029 widening for `ITradeRepository.RemoveAsync` (no new migration needed; the rename is atomic on the branch). Forecast ~700 LOC, 10 paths, `size:exception` likely per Wave 5/6a/6b/6c/6d precedent.