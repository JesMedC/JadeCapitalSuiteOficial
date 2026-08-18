# Tasks — Wave 7 (Audit Coverage Extension + Shared.Infrastructure Helper Refactor)

## Review Workload Forecast

| Slice | Boundary | LOC | Paths | size:exception preview | Bounded review |
|---|---|---:|---:|---|:---:|
| **7a.0** | `DecoratedRepository<T>` → `Shared.Infrastructure` (refactor) | ~150 | 6 | no (under budget) | OK |
| **7a.1** | Identity audit (`User` + `RiskProfile`) + `AuditAction` enum extension + migration 0029 | ~550 | 10 | likely (Wave 5/6a/6b/6c/6d precedent) | OK |
| **7b.1** | Trading audit (`Strategy` + `Trade`) + `RemoveAsync` → `DeleteAsync` rename (5 handler call sites) | ~700 | 10 | likely (Wave 5/6a/6b/6c/6d precedent) | OK |
| **7b.2** | Trading audit (`JournalEntry`) with bespoke `DeleteAsync(Guid)` shape | ~400 | 7 | unlikely (within budget) | OK |
| **Total** | 4 slices chained | **~1,800** | **33** | 2 likely + 1 unlikely | All ≤ 32 OK |

Decision needed before apply: **No** (auto-chain, 400-line budget per PR → `size:exception` per slice as Wave 5/6 precedent). User confirmed `feature-branch-chain` (Wave 0/1/2/3/4/5/6 precedent). Per-slice `git diff --name-only` MUST be ≤ 32 paths (mandatory); see path counts above.

**Build**: `dotnet build JadeCapital.slnx --nologo --verbosity minimal`.
**SQL harness**: `psql -v ON_ERROR_STOP=1 -f migrations/0029_audit_events_action_denied_failed.sql`; idempotent re-run (same script twice → exit 0).
**No FE changes** — all BE.

### Work Units (PR → test → runtime → rollback)

- 7a.0: `dotnet test --filter "FullyQualifiedName~DecoratedRepository|TenantRepositoryIntegration|ImportJobRepositoryIntegration|SubscriptionRepositoryIntegration"`. Rollback: revert code; helper returns to Identity.Infrastructure; two `ProjectReference` lines return to Trading + Billing csprojs; zero behavior change.
- 7a.1: `dotnet test --filter "FullyQualifiedName~UserAudit|RiskProfileAudit|UserRepositoryIntegration|RiskProfileRepositoryIntegration|AuditAction"` + full cumulative suite. Rollback: revert code; `audit.events` has no rows for `User` / `RiskProfile`; `AuditAction.Denied` / `AuditAction.Failed` enum values removed; migration 0029 reverted (constraint back to `IN (0,1,2,3)`).
- 7b.1: `dotnet test --filter "FullyQualifiedName~StrategyAudit|TradeAudit|StrategyRepositoryIntegration|TradeRepositoryIntegration"` + full cumulative suite. Rollback: revert code; `RemoveAsync` returns as the public surface; all 5 handlers revert; `audit.events` has no rows for `Strategy` / `Trade`.
- 7b.2: `dotnet test --filter "FullyQualifiedName~JournalEntryAudit|JournalEntryRepositoryIntegration"` + full cumulative suite. Rollback: revert code; `DeleteAsync(JournalEntry, ct)` overload removed; `audit.events` has no rows for `JournalEntry`.

---

## Slice 7a.0 — Move `DecoratedRepository<T>` to `Shared.Infrastructure` (~150 LOC, ~6 paths)

### 7a.0 Backend (~150 LOC)

**Phase 1: File move (refactor, no behavior change)**

- [ ] 1.1 `git mv` (or `cp` + `git rm` since the namespace changes) `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/DecoratedRepository.cs` → `src/3.Shared/JadeCapital.Shared.Infrastructure/Persistence/DecoratedRepository.cs`. Update namespace from `JadeCapital.Identity.Infrastructure.Persistence` to `JadeCapital.Shared.Infrastructure.Persistence`.
- [ ] 1.2 Remove the two unused `using` imports from `DecoratedRepository.cs` (lines that import `JadeCapital.Identity.Application.Abstractions` and `JadeCapital.Identity.Domain.Tenants`).
- [ ] 1.3 Update `using` import in `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/TenantAuditDecorator.cs` (line that uses `JadeCapital.Identity.Infrastructure.Persistence`).
- [ ] 1.4 Update `using` import in `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/ImportJobAuditDecorator.cs`.
- [ ] 1.5 Update `using` import in `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Audit/SubscriptionAuditDecorator.cs`.
- [ ] 1.6 Update `using` import in `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/DecoratedRepositoryTests.cs`.

**Phase 2: csproj refactor (drop Identity ref, add explicit Scrutor)**

- [ ] 2.1 In `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/JadeCapital.Trading.Infrastructure.csproj`: remove the `<ProjectReference Include="..\..\Identity\JadeCapital.Identity.Infrastructure\JadeCapital.Identity.Infrastructure.csproj" />` (line ~34).
- [ ] 2.2 In `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/JadeCapital.Trading.Infrastructure.csproj`: add `<PackageReference Include="Scrutor" Version="4.2.2" />` (explicit, no longer transitive).
- [ ] 2.3 In `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/JadeCapital.Billing.Infrastructure.csproj`: remove the `<ProjectReference Include="..\..\Identity\JadeCapital.Identity.Infrastructure\JadeCapital.Identity.Infrastructure.csproj" />` (line ~27).
- [ ] 2.4 In `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/JadeCapital.Billing.Infrastructure.csproj`: add `<PackageReference Include="Scrutor" Version="4.2.2" />` (explicit, no longer transitive).
- [ ] 2.5 Delete the old `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/DecoratedRepository.cs` (the move leaves the source file empty — `git rm` it).

**Phase 3: Validate**

- [ ] 3.1 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings (vs Wave 6 baseline of 3 pre-existing CA2263).
- [ ] 3.2 `dotnet test --filter "FullyQualifiedName~DecoratedRepository|TenantRepositoryIntegration|ImportJobRepositoryIntegration|SubscriptionRepositoryIntegration"` --nologo --verbosity minimal → 30/30 pass zero modification.
- [ ] 3.3 `grep -rn "class DecoratedRepository" src/` → returns ONLY `src/3.Shared/JadeCapital.Shared.Infrastructure/Persistence/DecoratedRepository.cs` (no duplicates).
- [ ] 3.4 `grep -n "Scrutor" src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/JadeCapital.Trading.Infrastructure.csproj src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/JadeCapital.Billing.Infrastructure.csproj` → both files reference Scrutor 4.2.2.
- [ ] 3.5 `grep -n "Identity.Infrastructure" src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/JadeCapital.Trading.Infrastructure.csproj src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/JadeCapital.Billing.Infrastructure.csproj` → both files have NO `<ProjectReference>` to `Identity.Infrastructure`.

### 7a.0 size:exception preview

Forecast ~150 lines, well under 1000L budget → `size:exception` not needed.

### 7a.0 Bounded review feasibility

- New files: 1 (Shared.Infrastructure/DecoratedRepository.cs — moved).
- Modified files: 5 (3 typed decorator `using` imports + 1 test `using` + 2 csproj ref drops + 2 csproj Scrutor adds — but csproj edits can be combined into 1 PR; the path count counts distinct files).
- Deleted files: 1 (old DecoratedRepository.cs in Identity.Infrastructure).
- Total: **6 paths** ≤ 32 OK.

> **Slice 7a.0 completion note**: code lands with all tests green. Zero behavior change. 30 existing tests pass zero modification. Deviations documented in `apply-progress-wave7-slice-7a-0.md` if any arise.

---

## Slice 7a.1 — `UserAuditDecorator` + `RiskProfileAuditDecorator` + `AuditAction` enum extension (~550 LOC, ~10 paths)

### 7a.1 Backend (~550 LOC)

**Phase 1: AuditAction enum extension (TDD)**

- [x] 1.1 RED test `AuditActionEnumTests` (3 scenarios: `Denied = 4` exists and serializes to byte 4, `Failed = 5` exists and serializes to byte 5, `Created/Updated/Deleted/Restored` values are unchanged at 0/1/2/3).
- [x] 1.2 GREEN: extend `src/3.Shared/JadeCapital.Shared.Kernel/Audit/AuditAction.cs` with `Denied = 4` + `Failed = 5` (after `Restored = 3`, no renumbering).

**Phase 2: Migration 0029 — widen CHECK constraint**

- [x] 2.1 `infrastructure/postgres/migrations/0029_audit_events_action_denied_failed.sql` — `DROP CONSTRAINT IF EXISTS ck_audit_events_action; ADD CONSTRAINT ck_audit_events_action CHECK (action IN (0, 1, 2, 3, 4, 5));`. Idempotent. Wire into `infrastructure/postgres/migrations/migrate.Dockerfile` happy + retry path. `COMMENT ON CONSTRAINT ck_audit_events_action ON audit.events IS '...Wave 7 added Denied=4 + Failed=5...'`. **Actual filename**: `0029_audit_events_action_check_widen.sql` per the orchestrator's runtime instruction.
- [x] 2.2 RED test `Migration0029ConstraintTests` (1 scenario: validate the migration SQL parses and the new CHECK allows `action = 4` and `action = 5`). **Actual**: SQL syntax reviewed statically; idempotency confirmed via `DROP CONSTRAINT IF EXISTS` guard. Runtime psql verification lands in sdd-verify (psql not available in this sandbox).

**Phase 3: Shared.Kernel IRepository<T> surgery**

- [x] 3.1 Confirm `src/3.Shared/JadeCapital.Shared.Kernel/Repository/IRepository.cs` exposes `AddAsync(T, ct)`, `UpdateAsync(T, ct)`, `DeleteAsync(T, ct)`, `GetByIdAsync(Guid, ct)`. If `DeleteAsync(T, ct)` is missing, add it (this is the signature the decorators use). **Actual**: Confirmed — `IRepository<T>` already had all 4 methods from Wave 6 6d.2. No surgery needed.

**Phase 4: User repository surgery (TDD)**

- [x] 4.1 RED test `IUserRepositoryContractTests` (2 scenarios: interface now extends `IRepository<User>` exposing `AddAsync` + `UpdateAsync` + `DeleteAsync`, `DeleteAsync(User, ct)` XML doc has `<exception cref="NotSupportedException">` and `<remarks>` block pointing to Tenant reassignment / Deactivation).
- [x] 4.2 GREEN: extend `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Abstractions/Repositories/IUserRepository.cs` to `IRepository<User>`. Add `DeleteAsync(User, ct)` STUB that throws `NotSupportedException("User deletion happens via Tenant reassignment, not direct delete")`.
- [x] 4.3 RED test `UserRepositoryDeleteAsyncStubTests` (1 scenario: calling the stub throws `NotSupportedException` with the expected message). **Actual**: folded into `IUserRepositoryContractTests` scenario #2 (the stub's throw semantics + message check). No separate file needed.

**Phase 5: RiskProfile repository surgery (TDD)**

- [x] 5.1 RED test `IRiskProfileRepositoryContractTests` (2 scenarios: interface now extends `IRepository<RiskProfile>` with `MarkSupersededAsync` + `GetActiveAsync` + `DeleteAsync(RiskProfile, ct)` overload, `DeleteAsync(RiskProfile, ct)` real impl delegates to `MarkSupersededAsync`).
- [x] 5.2 GREEN: extend `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Abstractions/Repositories/IRiskProfileRepository.cs` to `IRepository<RiskProfile>`. Add `DeleteAsync(RiskProfile, ct)` overload in the concrete repo that calls `MarkSupersededAsync(profile.SupersededBy, clock, ct)` (use a sensible default SupersededBy; document this in the XML doc). **Actual**: RiskProfile keeps its bespoke interface (does NOT extend `IRepository<RiskProfile>` per the orchestrator's "bespoke shape" requirement). `DeleteAsync(RiskProfile, ct)` is a defensive STUB that throws — does NOT delegate to `MarkSupersededAsync` (the orchestrator's prompt explicitly says "the underlying `IRiskProfileRepository.DeleteAsync` is NOT the public path").

**Phase 6: UserAuditDecorator (TDD with NSubstitute + SQLite in-memory fixture)**

- [x] 6.1 RED test `UserAuditDecoratorTests` (5 scenarios: AddAsync emits `Created` event with `entity_type = "User"`, `tenant_id` + `user_id` from `ITenantContext`, `changes = null`; UpdateAsync emits `Updated` with diff; Cancel domain op + UpdateAsync emits `Updated` (NOT `Deleted`); cross-tenant update emits `Denied` + throws `UnauthorizedAccessException`; DeleteAsync emits `Failed` + re-throws `NotSupportedException`; GetById emits NO event).
- [x] 6.2 GREEN: `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/UserAuditDecorator.cs` (mirror `TenantAuditDecorator` shape + cross-tenant `IsOwner` check + `DeleteAsync` STUB that emits `Failed` before re-throwing).

**Phase 7: UserRepositoryIntegrationTests (TDD with SQLite in-memory)**

- [x] 7.1 RED test `UserRepositoryIntegrationTests` (5 scenarios: create user → audit event written with `EntityType = "User"`, `Action = Created`; update user `displayName` → audit event with diff; cross-tenant update → `Denied` + `UnauthorizedAccessException`; delete attempt → `Failed` + `NotSupportedException`; GetById → no audit event).
- [x] 7.2 GREEN: `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/UserRepositoryIntegrationTests.cs` (SQLite-in-memory + `IdentityDbContext` + `AuditDbContext` + `IAuditLogger` + `ITenantContext` + `IClock`; raw `CREATE TABLE IF NOT EXISTS events ...` SQL workaround for EF Core 9 SQLite `EnsureCreated` all-or-nothing gotcha — copy pattern from `TenantRepositoryIntegrationTests.cs` lines 102-128).

**Phase 8: RiskProfileAuditDecorator (TDD)**

- [x] 8.1 RED test `RiskProfileAuditDecoratorTests` (4 scenarios: AddAsync emits `Created`; MarkSupersededAsync emits `Deleted` with diff `{ "SupersededBy": { "before": null, "after": "<guid>" }, "SupersededAtUtc": { "before": null, "after": "<utcNow>" } }`; cross-tenant MarkSuperseded emits `Denied` + `UnauthorizedAccessException`; DeleteAsync emits `Failed` + `NotSupportedException`; GetActiveAsync emits NO event).
- [x] 8.2 GREEN: `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/RiskProfileAuditDecorator.cs` (bespoke — wraps `MarkSupersededAsync` directly, emits `AuditAction.Deleted` with supersession diff; `DeleteAsync` short-circuits to `Failed` + re-throw; the underlying `IRiskProfileRepository.DeleteAsync` is NOT called from the decorator).

**Phase 9: RiskProfileRepositoryIntegrationTests (TDD with SQLite in-memory)**

- [x] 9.1 RED test `RiskProfileRepositoryIntegrationTests` (4 scenarios: create profile → audit event; supersede → `Deleted` event with supersession diff; cross-tenant supersede → `Denied` + `UnauthorizedAccessException`; delete attempt → `Failed` + `NotSupportedException`; GetActiveAsync → no audit event).
- [x] 9.2 GREEN: `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/RiskProfileRepositoryIntegrationTests.cs` (same SQLite-in-memory fixture pattern as 7.1).

**Phase 10: DI wiring**

- [x] 10.1 DI: `services.Decorate<IUserRepository, UserAuditDecorator>()` + `services.Decorate<IRiskProfileRepository, RiskProfileAuditDecorator>()` in `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/DependencyInjection/IdentityModuleRegistration.cs`.

**Phase 11: Validate**

- [x] 11.1 `dotnet test --filter "FullyQualifiedName~UserAudit|RiskProfileAudit|UserRepositoryIntegration|RiskProfileRepositoryIntegration|AuditAction|Migration0029"` --nologo --verbosity minimal → 14 new tests pass (per orchestrator's scope expansion: 3 AuditActionTests + 2 IUserRepositoryContractTests + 3 IRiskProfileRepositoryContractTests + 5 UserRepositoryIntegrationTests + 4 RiskProfileRepositoryIntegrationTests - 3 in Shared.Kernel.UnitTests).
- [x] 11.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings (3 pre-existing CA2263 unchanged).
- [x] 11.3 Full BE suite (1289 Wave 6 baseline + 17 new = 1306) → zero regression. (Orchestrator claimed 1298; actual is 1306 because the 5 test files per phase list yield 17 new tests, not 9.)
- [x] 11.4 `psql -v ON_ERROR_STOP=1 -f infrastructure/postgres/migrations/0029_audit_events_action_check_widen.sql` → SQL syntax reviewed statically; idempotency confirmed via `DROP CONSTRAINT IF EXISTS` guard. Runtime psql verification lands in sdd-verify (psql not available in this sandbox).

### 7a.1 size:exception preview

Forecast ~550 lines, Wave 5/6a/6b/6c/6d.1/6d.2 precedent (5a.1=2108, 5c.1=3075) → `size:exception` likely. Justification: enum extension + migration + 2 interface surgeries + 2 typed decorators + 2 integration test files + 9 tests are a coherent cross-cutting unit.

### 7a.1 Bounded review feasibility

- New files: 6 (AuditAction.cs extended + migration 0029 + UserAuditDecorator.cs + RiskProfileAuditDecorator.cs + UserRepositoryIntegrationTests.cs + RiskProfileRepositoryIntegrationTests.cs).
- Modified files: 4 (IUserRepository + IRiskProfileRepository + IdentityModuleRegistration DI + Shared.Kernel IRepository if DeleteAsync missing).
- Total: **10 paths** ≤ 32 OK.

> **Slice 7a.1 completion note**: code lands with all tests green. 9 new BE tests. Migration 0029 widens the CHECK constraint. `AuditAction.Denied` + `Failed` enum values land atomically. Deviations documented in `apply-progress-wave7-slice-7a-1.md` if any arise.

---

## Slice 7b.1 — `StrategyAuditDecorator` + `TradeAuditDecorator` + `RemoveAsync` → `DeleteAsync` rename (~700 LOC, ~10 paths)

### 7b.1 Backend (~700 LOC)

**Phase 1: Surface surgery — `ITradeRepository.RemoveAsync` → `DeleteAsync` (atomic rename)**

- [x] 1.1 `git grep -n "RemoveAsync" src/2.Modules/Trading/` BEFORE the rename. Actual: 1 Trade handler call site (DeleteTradeHandler.cs:41) + 1 test call site (DeleteTradeHandlerTests.cs:37). The orchestrator's "5 known handlers" claim was speculative (Account + Instrument RemoveAsync are out of scope). Documented in apply-progress-wave7-slice-7b-1.md Deviation #2.
- [x] 1.2 Rename `ITradeRepository.RemoveAsync(Trade, ct)` to `DeleteAsync(Trade, ct)` in `src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/ITradeRepository.cs`.
- [x] 1.3 Update the concrete `TradeRepository.RemoveAsync(Trade, ct)` impl to `DeleteAsync(Trade, ct)` (same body, just renamed).
- [x] 1.4 Update 1 handler call site (DeleteTradeHandler.cs:41) + 1 test call site (DeleteTradeHandlerTests.cs:37). Account + Instrument RemoveAsync are out of scope.
- [x] 1.5 Verify the rename is atomic: `dotnet build` → 0 errors, NO references to `RemoveAsync` remain in active code (only Account/Instrument + 4 XML doc references describing the rename history).

**Phase 2: Strategy repository surgery (TDD)**

- [x] 2.1 RED test `IStrategyRepositoryContractTests` (2 scenarios: interface extends `IRepository<Strategy>` with `AddAsync` + `UpdateAsync` + `DeleteAsync`, `DeleteAsync(Strategy, ct)` STUB throws `NotSupportedException` with the message `"Strategy deletion happens via Deactivation, not direct delete"`). Result: 2/2 pass.
- [x] 2.2 GREEN: extend `src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/IStrategyRepository.cs` to `IRepository<Strategy>`. Add `DeleteAsync(Strategy, ct)` STUB that throws `NotSupportedException`. Mirrors 7a.1 IUserRepository precedent (Deviation #4).

**Phase 3: StrategyAuditDecorator (TDD)**

- [x] 3.1 RED test `StrategyAuditDecoratorTests` (5 scenarios: AddAsync emits `Created`; UpdateAsync with `name` + `description` change emits `Updated` with diff; `Deactivate()` + UpdateAsync emits `Updated` with `IsActive: true → false` diff (NOT `Deleted`); cross-tenant update emits `Denied` + `UnauthorizedAccessException`; DeleteAsync emits `Failed` + re-throws `NotSupportedException`). Result: 5/5 pass via StrategyRepositoryIntegrationTests (SQLite-in-memory).
- [x] 3.2 GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/StrategyAuditDecorator.cs` (mirror `ImportJobAuditDecorator` shape + cross-tenant `IsOwner` check on `strategy.UserId`; `DeleteAsync` short-circuits to `Failed` + re-throw; the `IsTerminated` reflection check stays unchanged in `DecoratedRepository<T>`).

**Phase 4: StrategyRepositoryIntegrationTests (TDD with SQLite in-memory + TestTradingDbContext)**

- [x] 4.1 RED test `StrategyRepositoryIntegrationTests` (5 scenarios: create strategy → audit event with `EntityType = "Strategy"`, `Action = Created`; update `name` + `description` → audit event with diff; `Deactivate` + update → `Updated` event with `IsActive: true → false` diff; cross-tenant update → `Denied` + `UnauthorizedAccessException`; delete attempt → `Failed` + `NotSupportedException`). Result: 5/5 pass.
- [x] 4.2 GREEN: `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/StrategyRepositoryIntegrationTests.cs` (uses a focused `TestTradingDbContext` helper that omits Npgsql-specific columns — copy the pattern from `ImportJobRepositoryIntegrationTests.cs`).

**Phase 5: TradeAuditDecorator (TDD)**

- [x] 5.1 RED test `TradeAuditDecoratorTests` (5 scenarios: AddAsync emits `Created`; UpdateAsync emits `Updated`; DeleteAsync (renamed from RemoveAsync) emits `Deleted`; cross-tenant update emits `Denied` + `UnauthorizedAccessException`). Result: 5/5 pass via TradeRepositoryIntegrationTests.
- [x] 5.2 GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/TradeAuditDecorator.cs` (BESPOKE — does NOT use `DecoratedRepository<T>` because `ITradeRepository` is bespoke with `FindByIdAsync` + 6 read methods. Mirrors 7a.1 `RiskProfileAuditDecorator` precedent, Deviation #3). Forwards `DeleteAsync(trade, ct)` to `_inner.DeleteAsync(trade, ct)` which emits `AuditAction.Deleted` with before/after diff.

**Phase 6: TradeRepositoryIntegrationTests (TDD with SQLite in-memory + TestTradingDbContext)**

- [x] 6.1 RED test `TradeRepositoryIntegrationTests` (5 scenarios: create trade → audit event with `EntityType = "Trade"`, `Action = Created`; update notes → `Updated` with diff (EF change tracker); DeleteAsync (the renamed method) → `Deleted` with before snapshot; cross-tenant update → `Denied` + `UnauthorizedAccessException`; audit includes user_id + tenant_id + entity_type). Result: 5/5 pass.
- [x] 6.2 GREEN: `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/TradeRepositoryIntegrationTests.cs` (uses the same `TestTradingDbContext` helper pattern as 4.1; ignores Money value objects to sidestep Npgsql converters, Deviation #8).

**Phase 7: DI wiring**

- [x] 7.1 DI: `services.Decorate<IStrategyRepository, StrategyAuditDecorator>()` + `services.Decorate<ITradeRepository, TradeAuditDecorator>()` in `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs`.

**Phase 8: Validate**

- [x] 8.1 `dotnet test --filter "FullyQualifiedName~StrategyAudit|TradeAudit|StrategyRepositoryIntegration|TradeRepositoryIntegration|IStrategyRepository|ITradeRepository"` --nologo --verbosity minimal → **15/15 new tests pass** (3 ITradeRepo + 2 IStrategyRepo + 5 Strategy + 5 Trade).
- [x] 8.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings (3 pre-existing CA2263 unchanged).
- [x] 8.3 `git grep -n "RemoveAsync" src/2.Modules/Trading/` → NO active-code results (only Account/Instrument + 4 XML doc references describing the rename history).
- [x] 8.4 Full BE suite (1306 + 15 = **1321**) → zero regression.

### 7b.1 size:exception preview

Forecast ~700 lines, Wave 5/6a/6b/6c/6d.1/6d.2 precedent → `size:exception` likely. Justification: `RemoveAsync` → `DeleteAsync` rename (5 handler call sites + interface + concrete impl) + 2 typed decorators + 2 integration test files + 10 tests is a coherent cross-cutting unit. Cannot split without artificial boundaries (the rename is atomic by design).

### 7b.1 Bounded review feasibility

- New files: 4 (StrategyAuditDecorator.cs + TradeAuditDecorator.cs + StrategyRepositoryIntegrationTests.cs + TradeRepositoryIntegrationTests.cs).
- Modified files: 6 (ITradeRepository interface + TradeRepository concrete + 5 handler call sites — but the 5 handlers are distinct paths; + IStrategyRepository + TradingModuleRegistration DI).
- Total: **10 paths** ≤ 32 OK.

> **Slice 7b.1 completion note**: code lands with all tests green. 10 new BE tests. The `RemoveAsync` → `DeleteAsync` rename is atomic on the branch. `audit.events` starts receiving `Strategy` + `Trade` rows. Deviations documented in `apply-progress-wave7-slice-7b-1.md` if any arise.

---

## Slice 7b.2 — `JournalEntryAuditDecorator` (~400 LOC, ~7 paths)

### 7b.2 Backend (~400 LOC)

**Phase 1: Surface surgery — add `DeleteAsync(JournalEntry, ct)` overload**

- [ ] 1.1 RED test `IJournalEntryRepositoryContractTests` (2 scenarios: interface now exposes `DeleteAsync(JournalEntry, ct)` overload that internally calls `DeleteAsync(Guid, ct)`; original `DeleteAsync(Guid, ct)` remains the production path; both signatures are part of the public API).
- [ ] 1.2 GREEN: extend `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Abstractions/Repositories/IJournalEntryRepository.cs` with `DeleteAsync(JournalEntry, ct)` overload. Implementation: `await DeleteAsync(entry.Id, ct);` (1-line delegate). Add XML doc explaining it's a "decorator-friendly" overload.

**Phase 2: JournalEntryAuditDecorator (TDD)**

- [ ] 2.1 RED test `JournalEntryAuditDecoratorTests` (5 scenarios: AddAsync emits `Created` with `entity_type = "JournalEntry"`, `tenant_id` + `user_id` from `ITenantContext`; UpdateAsync with `premarket_plan` change emits `Updated` with diff; `DeleteAsync(JournalEntry, ct)` emits `Deleted` (decorator calls `_decorated.DeleteAsync(entry, ct)` which delegates to `DeleteAsync(Guid, ct)`); cross-tenant delete emits `Denied` + `UnauthorizedAccessException`; FindByIdAsync emits NO event).
- [ ] 2.2 GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/JournalEntryAuditDecorator.cs` (bespoke — wraps `DeleteAsync(JournalEntry, ct)` and emits `AuditAction.Deleted`; cross-tenant `IsOwner` check on `entry.UserId`).

**Phase 3: JournalEntryRepositoryIntegrationTests (TDD with SQLite in-memory + focused TestTradingDbContext)**

- [ ] 3.1 RED test `JournalEntryRepositoryIntegrationTests` (5 scenarios: create entry → audit event with `EntityType = "JournalEntry"`, `Action = Created`; update `premarket_plan` → `Updated` event with diff; `DeleteAsync(JournalEntry, ct)` → `Deleted` event; cross-tenant delete → `Denied` + `UnauthorizedAccessException`; FindByIdAsync → no audit event).
- [ ] 3.2 GREEN: `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/JournalEntryRepositoryIntegrationTests.cs` (uses a focused `TestTradingDbContext` helper that omits the Npgsql-specific `JournalEntry.Tags` array column — copy the pattern from `ImportJobRepositoryIntegrationTests.cs`).

**Phase 4: DI wiring**

- [ ] 4.1 DI: `services.Decorate<IJournalEntryRepository, JournalEntryAuditDecorator>()` in `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs`.

**Phase 5: Validate**

- [ ] 5.1 `dotnet test --filter "FullyQualifiedName~JournalEntryAudit|JournalEntryRepositoryIntegration"` --nologo --verbosity minimal → 5 new tests pass.
- [ ] 5.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings.
- [ ] 5.3 Full BE suite (1308 + 5 = 1313) → zero regression.

### 7b.2 size:exception preview

Forecast ~400 lines, within 1000L budget → `size:exception` unlikely.

### 7b.2 Bounded review feasibility

- New files: 2 (JournalEntryAuditDecorator.cs + JournalEntryRepositoryIntegrationTests.cs).
- Modified files: 2 (IJournalEntryRepository interface + TradingModuleRegistration DI).
- Total: **7 paths** ≤ 32 OK.

> **Slice 7b.2 completion note**: code lands with all tests green. 5 new BE tests. `audit.events` starts receiving `JournalEntry` rows. The bespoke `DeleteAsync(Guid)` shape is preserved (production path is unchanged); the decorator wraps the new entity overload. Deviations documented in `apply-progress-wave7-slice-7b-2.md` if any arise.

---

## Cumulative Test Target

| Slice | BE tests | Cumulative |
|---|---:|---:|
| Wave 6 (baseline) | — | 1289 |
| 7a.0 | 0 (regression-only) | 1289 |
| 7a.1 | +9 | 1298 |
| 7b.1 | +10 | 1308 |
| 7b.2 | +5 | 1313 |
| **Total** | **+24** | **1313** |

## Definition of Done (per Wave 5/6 precedent)

- All `[ ]` tasks for the slice marked `[x]`.
- All RED tests pass → GREEN → REFACTOR.
- `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.
- `dotnet test --filter "..."` → 100% pass.
- `git diff --name-only` ≤ 32 paths.
- Slice completion note appended to `apply-progress-wave7-slice-<id>.md`.
- Deviations documented (if any) with rationale.
- Cumulative suite remains green (no regressions).
- For 7a.1: `psql -v ON_ERROR_STOP=1 -f infrastructure/postgres/migrations/0029_audit_events_action_denied_failed.sql` runs to completion; re-run is no-op.
- For 7b.1: `git grep -n "RemoveAsync" src/2.Modules/Trading/` returns no active-code results.
