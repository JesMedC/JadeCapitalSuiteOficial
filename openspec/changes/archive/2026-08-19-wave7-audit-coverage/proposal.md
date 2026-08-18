# Proposal: Wave 7 — Audit Coverage Extension + Shared.Infrastructure Helper Refactor

**Change**: `2026-08-19-wave7-audit-coverage`
**Branch**: `feature/0a-identity-model` @ `f312fac` (Wave 6 archived commit)
**Stack**: ASP.NET Core 10 / .NET SDK 10.0.400 via `mise exec -- dotnet …`
**Strategy**: `feature-branch-chain` (carries the Wave 6 chain)

## Intent and Problem

Wave 6 (slice 6d.2) shipped the audit decorator pattern (`DecoratedRepository<T>` + `IAuditLogger` + typed `TenantAuditDecorator` / `ImportJobAuditDecorator` / `SubscriptionAuditDecorator`) covering **3 of 8** user-owned aggregates. The remaining 5 — `User`, `Strategy`, `Trade`, `RiskProfile`, `JournalEntry` — have no audit trail. The compliance gap is real: every mutation on those aggregates is invisible to `audit.events`. The verify report flagged this explicitly as a Wave 7 scope item (suggestion #1 in `verify-report-wave6-final.md`).

Two problems in one wave:

1. **Compliance gap (3/8 coverage).** `Identity.Infrastructure/Persistence/DecoratedRepository.cs` is generic, ready to wire to any `IRepository<T>` consumer. Wave 7 widens coverage to the 5 remaining user-owned aggregates. Without this, mutations on `User` / `Strategy` / `Trade` / `RiskProfile` / `JournalEntry` are NOT audited — a real liability for any multi-tenant SaaS that needs to answer "who changed what when".
2. **Maintenance friction (cross-module edges).** `Trading.Infrastructure.csproj` and `Billing.Infrastructure.csproj` both reference `Identity.Infrastructure.csproj` **solely** to import `JadeCapital.Identity.Infrastructure.Persistence` for the generic `DecoratedRepository<T>` helper (see Trading csproj lines 26-33, Billing csproj lines 23-28). Identity is the natural owner of `AuditLogger` + `AuditDbContext` (the audit write surface), but the generic decorator wrapper is a cross-module concern. Move it to `Shared.Infrastructure` to eliminate the edge and drop the two redundant `ProjectReference`s.

## Goals

- **Coverage**: 5 new typed audit decorators (`UserAuditDecorator`, `StrategyAuditDecorator`, `TradeAuditDecorator`, `RiskProfileAuditDecorator`, `JournalEntryAuditDecorator`). Every user-owned aggregate emits `AuditEvent` writes through the generic `DecoratedRepository<T>`.
- **Layering**: `DecoratedRepository<T>` lives in `Shared.Infrastructure/Persistence/`. `Trading.Infrastructure` and `Billing.Infrastructure` no longer reference `Identity.Infrastructure` for the generic helper. Scrutor becomes an explicit (not transitive) dependency in Trading + Billing csprojs.
- **Surface surgery**: `ITradeRepository.RemoveAsync(Trade)` → `DeleteAsync(Trade)` (breaking rename). `IUserRepository` + `IStrategyRepository` + `IRiskProfileRepository` + `IJournalEntryRepository` each gain a `DeleteAsync` overload (the first two throw `NotSupportedException`; the last two are real implementations).
- **Zero regression**: all 1289 existing Wave 6 tests still pass after the move.

## Scope Boundaries

### In Scope

| Sub-scope | Boundary | Deliverable |
|---|---|---|
| **A. Coverage** | 5 typed audit decorators | `UserAuditDecorator` (Identity), `RiskProfileAuditDecorator` (Identity), `StrategyAuditDecorator` (Trading), `TradeAuditDecorator` (Trading), `JournalEntryAuditDecorator` (Trading) |
| **B. Refactor** | Move generic helper | `DecoratedRepository<T>` lives at `src/3.Shared/JadeCapital.Shared.Infrastructure/Persistence/DecoratedRepository.cs`; drop 2 redundant `<ProjectReference>`s in Trading + Billing csprojs; add explicit `<PackageReference Include="Scrutor" Version="4.2.2" />` to Trading + Billing csprojs |
| **C. Surface surgery** | Interface updates | `ITradeRepository.RemoveAsync` → `DeleteAsync` rename; `IUserRepository.DeleteAsync(User)` + `IStrategyRepository.DeleteAsync(Strategy)` + `IRiskProfileRepository.DeleteAsync(RiskProfile)` + `IJournalEntryRepository.DeleteAsync(JournalEntry)` stubs/overloads |
| **Tests** | SQLite-in-memory | 3 new per-aggregate integration test files in `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/` (User, RiskProfile, Strategy/Trade, JournalEntry) — ~24 new tests total |

### Out of Scope (deferred to Wave 8+)

- **Audit for TradeReview, PlannerSession, PreTradeChecklist, Account, Instrument, Alert, RiskConfiguration, etc.** — Wave 8 widens coverage further.
- **Reporting / Query API for `audit.events`** — admin-only `GET /api/audit/events` (with filters by entity_type, action, user_id, date range) is Wave 8.
- **Audit UI / Export** — read-side surface for compliance officers is Wave 8+ (CSV/JSON export deferred to Wave 9).
- **Audit log retention / auto-purge** — 90-day retention job is Wave 8 (per Wave 6's explicit deferral).
- **Soft-delete propagation cascade** — beyond `ImportJob` (the only Wave 6 soft-delete surface), the cascade behavior for `RiskProfile` / `Strategy` / `Trade` is Wave 8.
- **Migration to a different audit sink** — Kafka / S3 / external SIEM is post-1.0.
- **Bulk audit events for `AddRangeAsync`** — out of scope (same as Wave 6).
- **`AuditAction.Restored` end-to-end support** — the enum value is reserved; the soft-delete restore command + admin tooling is Wave 8.

## Capabilities (modified)

- **`soft-delete-audit`** (Wave 6, `openspec/specs/soft-delete-audit/spec.md`) — MODIFIED. The Wave 6 spec covers `ISoftDelete` + `IAuditLogger` + `DecoratedRepository<T>` + the 3 typed decorators (Tenant, ImportJob, Subscription). Wave 7 adds a new requirement:

  > **Requirement: Decorator coverage for user-owned aggregates**
  > Every `IXxxRepository` that mutates state on a user-owned aggregate MUST have a typed audit decorator registered in its module's `*ModuleRegistration`. The decorator MUST forward `AddAsync` / `UpdateAsync` / `DeleteAsync` (or the aggregate-specific mutation method like `MarkSupersededAsync`) to a `DecoratedRepository<T>` instance and emit an `AuditEvent` with `EntityType = typeof(T).Name`. Covered aggregates: `Tenant`, `ImportJob`, `Subscription`, `User`, `Strategy`, `Trade`, `RiskProfile`, `JournalEntry`. New aggregates added in Wave 8+ extend this list.

  This is a **capability expansion**, NOT a new capability. The sdd-spec phase writes a delta spec for `soft-delete-audit` (5 new sub-scenarios per decorated aggregate per the existing Wave 6 shape).

## Capabilities (new)

- None. The `DecoratedRepository<T>` move is internal. The interface surgeries are versioned widens of existing contracts (sub-scope C above).

## Architectural Decisions

- **Bespoke per-aggregate decorator vs single generic `IRepository<T>` decorator**: per the Explore analysis (Approach A2), the 5 new typed decorators instantiate `DecoratedRepository<T>` with the typed repo cast as `IRepository<T>` for the Add/Update/Delete paths that exist, and forward aggregate-specific methods (e.g., `IRiskProfileRepository.MarkSupersededAsync`, `IJournalEntryRepository.DeleteAsync(Guid)`) directly with the right `AuditAction`. Reason: `IRiskProfileRepository` has no `UpdateAsync` (only `MarkSupersededAsync`); `IJournalEntryRepository` has `DeleteAsync(Guid)` not `DeleteAsync(JournalEntry)`. A single generic decorator would force a never-called UpdateAsync stub on RiskProfile.
- **`MarkSupersededAsync` emits `AuditAction.Deleted`**: per user decision. The supersede IS the termination (no separate restore path). The diff recorded is `SupersededBy: null → Guid` and `SupersededAtUtc: null → now`. The decorator calls `_decorated.MarkSupersededAsync(...)` first, then emits the audit event with the diff.
- **`ITradeRepository.RemoveAsync` → `DeleteAsync` rename (breaking)**: per user decision. All call sites must be updated in the same slice (no overload, no deprecation period). The rename happens in slice 7b.1 atomically — `git grep RemoveAsync` enumerates call sites BEFORE the rename; all 5 known handlers + 0 tests use it. The `DeleteAsync(Trade, ct)` signature matches `IRepository<T>.DeleteAsync(T, ct)`.
- **`IUserRepository.DeleteAsync` + `IStrategyRepository.DeleteAsync` throw `NotSupportedException`**: per user decision. User / Strategy have no hard-delete path — User cancellation/suspension is a domain op, Strategy deactivation is a domain op + `UpdateAsync`. The decorator implements the full interface; the `NotSupportedException` is emitted (via `audit.events` with a `Failed` action) and then re-thrown so the handler sees the error.
- **`Strategy.Deactivate()` + `UpdateAsync` emits `AuditAction.Updated` (NOT `Deleted`)**: per user decision. `Strategy` is not `ISoftDelete`; the existing `DecoratedRepository<T>.IsTerminated` reflection check only upgrades `Updated → Deleted` when `IsDeleted == true` OR `Status ∈ {Cancelled, Terminated, Expired}`. `IsActive: true → false` does NOT trigger the upgrade. The audit row carries the diff `isActive: true → false` — semantically correct audit trail for "soft-delete-by-flag".
- **`Shared.Infrastructure` is the natural home for the generic helper**: it already references `Shared.Kernel`, EF Core, Npgsql, MediatR, FluentValidation, Serilog. The two `using JadeCapital.Identity.{Application,Domain}…` imports in `DecoratedRepository.cs` are unused (the helper operates on `T` via reflection). Drop them on the move.
- **Scrutor becomes explicit (not transitive)**: `Identity.Infrastructure.csproj` references `Scrutor 4.2.2`. After dropping the Identity ref from Trading + Billing csprojs, both modules must add `<PackageReference Include="Scrutor" Version="4.2.2" />` themselves. The `services.Decorate<IXxxRepository, XxxAuditDecorator>()` calls depend on it.
- **Cross-tenant isolation in the decorator stays in the user-owned decorator** (same as `ImportJobAuditDecorator` / `SubscriptionAuditDecorator` precedent). For User / Strategy / Trade / JournalEntry (all have `UserId` or `TenantId`), the decorator checks `entity.UserId != tenant.CurrentUserId` BEFORE delegating to the inner. On mismatch: log a `Denied` audit row + throw `UnauthorizedAccessException`. `RiskProfile` is per-user so the same check applies.
- **`AuditDbContext` + `AuditLogger` + `NoOpAuditLogger` stay in `Identity.Infrastructure`**: they are the audit write surface and Identity is the natural owner. The decorator's `IAuditLogger` dependency resolves to the real `AuditLogger` registered in `IdentityModuleRegistration`.
- **Test fixture per aggregate**: `JournalEntryRepositoryIntegrationTests` follows the `ImportJobRepositoryIntegrationTests` template — focused helper `DbContext` (the production `TradingDbContext` has Npgsql-specific array converters that fail on SQLite). `UserRepositoryIntegrationTests` + `RiskProfileRepositoryIntegrationTests` use the existing `IdentityDbContext` directly. `StrategyRepositoryIntegrationTests` + `TradeRepositoryIntegrationTests` use a new `TestTradingDbContext` (subset of `TradingDbContext` with only the entities under test).
- **`size:exception` precedent**: Wave 5/6a/6b/6c/6d all accepted `size:exception` per slice. Wave 7 follows the model — expect `size:exception` for 7a.1 + 7b.1 (the two coverage slices; 7a.0 is under 200 LOC; 7b.2 is borderline).

## Chained Delivery, Validation, and Rollback

| Slice | Boundary | LOC | Paths | Tests | size:exception | Validate | Rollback |
|---|---|---:|---:|---:|---|---|---|
| **7a.0** | `DecoratedRepository<T>` → `Shared.Infrastructure` (refactor) | ~150 | 6 | 0 (regression-only) | no | `dotnet test --filter "FullyQualifiedName~DecoratedRepository\|TenantRepositoryIntegration\|ImportJobRepositoryIntegration\|SubscriptionRepositoryIntegration"`. All 30 existing tests pass zero change. | `git revert` the move commit. Three decorator using imports revert. Two `ProjectReference` lines return. Zero behavior change. |
| **7a.1** | Identity audit (`User` + `RiskProfile`) | ~550 | 10 | 9 | likely | `dotnet test --filter "FullyQualifiedName~UserAudit\|RiskProfileAudit\|UserRepositoryIntegration\|RiskProfileRepositoryIntegration"` + full cumulative suite | `git revert` the slice. `IUserRepository` / `IRiskProfileRepository` revert to no-decorator state. `audit.events` has no rows for User / RiskProfile (acceptable — same as Wave 6 before 6d.2). |
| **7b.1** | Trading audit (`Strategy` + `Trade`) | ~700 | 10 | 10 | likely | `dotnet test --filter "FullyQualifiedName~StrategyAudit\|TradeAudit\|StrategyRepositoryIntegration\|TradeRepositoryIntegration"` + full cumulative suite | `git revert` the slice. `RemoveAsync` returns as the public surface (`DeleteAsync` is gone). All 5 call sites reverted. `audit.events` has no rows for Strategy / Trade. |
| **7b.2** | Trading audit (`JournalEntry`) | ~400 | 7 | 5 | unlikely | `dotnet test --filter "FullyQualifiedName~JournalEntryAudit\|JournalEntryRepositoryIntegration"` + full cumulative suite | `git revert` the slice. `DeleteAsync(JournalEntry, ct)` overload removed. Existing `DeleteAsync(Guid, ct)` is the only public surface. `audit.events` has no rows for JournalEntry. |
| **Total** | 4 slices chained | **~1,800** | **33** | **+24** | 2 likely + 1 unlikely | — | — |

**Chain strategy**: `feature-branch-chain`. PR base = previous PR branch. Each PR merges into the previous PR's branch (or `feature/0a-identity-model` for the first slice). PR #1 targets `feature/0a-identity-model` (the Wave 6 archive commit `f312fac`).

| # | Branch | Base | Title |
|---|---|---|---|
| **#21** | `feature/wave7-decorator-move` (7a.0) | `feature/0a-identity-model` | Slice 7a.0 — Move `DecoratedRepository<T>` to `Shared.Infrastructure` |
| **#22** | `feature/wave7-identity-audit` (7a.1) | `feature/wave7-decorator-move` | Slice 7a.1 — `UserAuditDecorator` + `RiskProfileAuditDecorator` |
| **#23** | `feature/wave7-trading-audit` (7b.1) | `feature/wave7-identity-audit` | Slice 7b.1 — `StrategyAuditDecorator` + `TradeAuditDecorator` (incl. `RemoveAsync` → `DeleteAsync` rename) |
| **#24** | `feature/wave7-journal-audit` (7b.2) | `feature/wave7-trading-audit` | Slice 7b.2 — `JournalEntryAuditDecorator` (with `DeleteAsync(Guid)` → `DeleteAsync(JournalEntry)` overload) |

Chain integrity: 4 PRs total. Each PR targets the previous PR's branch. Order matches slice order. No PR targets `main` directly.

### Per-slice detail

#### Slice 7a.0 — Move `DecoratedRepository<T>` to `Shared.Infrastructure` (~150 LOC, ~6 paths)

- **Phase 1** (no new tests, refactor only): copy `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/DecoratedRepository.cs` → `src/3.Shared/JadeCapital.Shared.Infrastructure/Persistence/DecoratedRepository.cs`. Update namespace `JadeCapital.Identity.Infrastructure.Persistence` → `JadeCapital.Shared.Infrastructure.Persistence`. Remove the two `using JadeCapital.Identity.{Application,Domain}…` imports. Update `TenantAuditDecorator.cs` (still Identity-owned), `ImportJobAuditDecorator.cs`, `SubscriptionAuditDecorator.cs` using imports. Update `DecoratedRepositoryTests.cs` using import. Delete old `DecoratedRepository.cs` from Identity.
- **Phase 2**: drop redundant `<ProjectReference Include="..\..\Identity\JadeCapital.Identity.Infrastructure\JadeCapital.Identity.Infrastructure.csproj" />` from `Trading.Infrastructure.csproj` (line 34) and `Billing.Infrastructure.csproj` (line 27). Add explicit `<PackageReference Include="Scrutor" Version="4.2.2" />` to both csprojs (Scrutor was transitive via the Identity ref).
- **Phase 3** Validate: `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors. `dotnet test --filter "FullyQualifiedName~DecoratedRepository\|TenantRepositoryIntegration\|ImportJobRepositoryIntegration\|SubscriptionRepositoryIntegration"` → 30/30 pass zero modification.
- **Dependencies**: none beyond Wave 6 archive.
- **Rollback**: `git revert` the slice. Two csproj lines return. Three decorator using imports revert. Zero behavior change.

#### Slice 7a.1 — `UserAuditDecorator` + `RiskProfileAuditDecorator` (Identity, ~550 LOC, ~10 paths)

- **Phase 1** (RED): 5 RED scenarios for `UserAuditDecorator` — Create user → `Created` event; Update user (`ChangeDisplayName`) → `Updated` event with diff; Cancel domain op → `Updated` event (no `IsSoftDelete`); cross-tenant update rejected → `Denied` event + 403; `GetByIdAsync` → no audit. 4 RED scenarios for `RiskProfileAuditDecorator` — Create profile → `Created`; `MarkSupersededAsync` → `Deleted` event with diff (`SupersededBy: null → Guid`, `SupersededAtUtc: null → now`); `GetActiveAsync` → no audit; cross-tenant MarkSuperseded rejected → `Denied` event + 403.
- **Phase 2** (Surface surgery): `IUserRepository` extends `IRepository<User>` with `DeleteAsync(User, ct)` throwing `NotSupportedException("User deletion happens via Tenant reassignment or Deactivation, not direct delete")`. `IRiskProfileRepository` extends `IRepository<RiskProfile>` with `DeleteAsync(RiskProfile, ct)` (real impl — calls `MarkSupersededAsync` internally).
- **Phase 3** (GREEN): `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/UserAuditDecorator.cs` (mirrors `TenantAuditDecorator`). `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/RiskProfileAuditDecorator.cs` (bespoke — wraps `MarkSupersededAsync` directly). Wire `services.Decorate<IUserRepository, UserAuditDecorator>()` + `services.Decorate<IRiskProfileRepository, RiskProfileAuditDecorator>()` in `IdentityModuleRegistration`.
- **Phase 4** Validate: 9 new tests pass. `dotnet build` green. Full cumulative suite (1289 + 9 = 1298) zero regression.
- **Dependencies**: 7a.0 must be merged.
- **Rollback**: `git revert` the slice. `IUserRepository.DeleteAsync` STUB removed; `IRiskProfileRepository.DeleteAsync` overload removed. `audit.events` has no rows for User / RiskProfile.

#### Slice 7b.1 — `StrategyAuditDecorator` + `TradeAuditDecorator` (Trading, ~700 LOC, ~10 paths)

- **Phase 1** (`git grep RemoveAsync`): enumerate all call sites of `ITradeRepository.RemoveAsync` BEFORE the rename. Known: 5 handlers in `Trading.Application/Features/Trades/`. No tests use it directly. Atomic rename in this slice.
- **Phase 2** (Surface surgery): `ITradeRepository.RemoveAsync(Trade)` → `DeleteAsync(Trade, ct)` (breaking). Update all 5 handlers. `IStrategyRepository` extends `IRepository<Strategy>` with `DeleteAsync(Strategy, ct)` throwing `NotSupportedException("Strategy deletion happens via Deactivation, not direct delete")`.
- **Phase 3** (RED): 5 RED scenarios for `StrategyAuditDecorator` — Create → `Created`; Update (rename, description) → `Updated` with diff; Deactivate via `UpdateAsync` → `Updated` with `isActive: true → false` diff; `GetByIdAsync` → no audit; cross-tenant Update rejected → `Denied`. 5 RED scenarios for `TradeAuditDecorator` — Create → `Created`; Close (status Open → Closed) → `Updated` with status diff; Cancel → `Updated`; `RemoveAsync` (now `DeleteAsync`) → `Deleted`; cross-tenant Update rejected.
- **Phase 4** (GREEN): `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/StrategyAuditDecorator.cs` + `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/TradeAuditDecorator.cs`. Wire DI in `TradingModuleRegistration`.
- **Phase 5** Validate: 10 new tests pass. Full cumulative suite (1298 + 10 = 1308) zero regression.
- **Dependencies**: 7a.1 must be merged.
- **Rollback**: `git revert` the slice. `RemoveAsync` returns as the public surface. All 5 handlers revert. `audit.events` has no rows for Strategy / Trade.

#### Slice 7b.2 — `JournalEntryAuditDecorator` (Trading, ~400 LOC, ~7 paths)

- **Phase 1** (Surface surgery): `IJournalEntryRepository.DeleteAsync(JournalEntry, ct)` overload (calls existing `DeleteAsync(Guid, ct)` internally). Document as "decorator-friendly overload — production handlers can use either signature".
- **Phase 2** (RED): 5 RED scenarios for `JournalEntryAuditDecorator` — Create → `Created`; Update (premarket_plan change) → `Updated` with diff; `DeleteAsync(Guid)` → `Deleted` (decorator loads entity first then calls `_decorated.DeleteAsync(entity, ct)`); `FindByIdAsync` → no audit; cross-tenant Delete rejected → `Denied`.
- **Phase 3** (GREEN): `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/JournalEntryAuditDecorator.cs`. Wire DI.
- **Phase 4** Validate: 5 new tests pass. Full cumulative suite (1308 + 5 = 1313) zero regression.
- **Dependencies**: 7b.1 must be merged (shares the `TestTradingDbContext` fixture).
- **Rollback**: `git revert` the slice. `DeleteAsync(JournalEntry, ct)` overload removed. `audit.events` has no rows for JournalEntry.

## Critical Questions for User — RESOLVED (per orchestrator preflight)

The 4 questions flagged in `explore.md` § Ambiguity were resolved before the proposal was written. Recording the resolutions here so future readers / Wave 8 sessions know the decisions were deliberate.

| # | Question | Resolution | Source |
|---|---|---|---|
| 1 | `IRiskProfileRepository.MarkSupersededAsync` emits `AuditAction.Deleted` or `Updated`? | **`Deleted`**. The supersede IS the termination. Diff: `SupersededBy: null → Guid`, `SupersededAtUtc: null → now`. | Orchestrator preflight decision 1 |
| 2 | `ITradeRepository.RemoveAsync` rename vs keep + overload? | **Rename** to `DeleteAsync(Trade, ct)`. Breaking. Atomic in slice 7b.1. No overload, no deprecation period. | Orchestrator preflight decision 2 |
| 3 | `Strategy.Deactivate()` + `UpdateAsync` emits `Updated` or `Deleted`? | **`Updated`** with `isActive: true → false` diff. Semantic: soft-delete-by-flag. The existing `IsTerminated` reflection check (which upgrades `Updated → Deleted`) stays as-is — only fires on `IsDeleted == true` OR `Status ∈ {Cancelled, Terminated, Expired}`. | Orchestrator preflight decision 3 |
| 4 | `IUserRepository` / `IStrategyRepository` `DeleteAsync` — STUB exception or omit from decorator entirely? | **Add `DeleteAsync(T)` STUB throwing `NotSupportedException`** with message "User/Strategy deletion happens via Tenant reassignment or Deactivation, not direct delete". The decorator implements the full interface and emits a `Failed` audit event before re-throwing. | Orchestrator preflight decision 4 |

## Migration Path

**No schema migration required.** The decorator pattern is additive — `audit.events` table already exists (migration 0027 from Wave 6). The only signature change is `ITradeRepository.RemoveAsync` → `DeleteAsync`, which is handled atomically in slice 7b.1 (no consumers outside the 5 trading handlers). The 5 new typed decorators register via `services.Decorate<IXxxRepository, XxxAuditDecorator>()` — no DB schema changes.

**No data migration required.** The `audit.events` table starts receiving User / Strategy / Trade / RiskProfile / JournalEntry rows from the moment the deployment completes. Historical mutations (pre-Wave 7) are not backfilled — the audit log is forward-only.

## Dependencies and Risks

### Dependencies (non-negotiable, from Wave 6 precedent)

- **Strict TDD** per `openspec/config.yaml` `apply.tdd: true`. RED tests FIRST for every new decorator. The 5 integration test files follow the existing `TenantRepositoryIntegrationTests` pattern (SQLite-in-memory + raw SQL `CREATE TABLE IF NOT EXISTS events ...` workaround for EF Core 9 SQLite `EnsureCreated` all-or-nothing gotcha).
- **`size:exception`** likely needed for 7a.1 + 7b.1 (Wave 5/6a/6b/6c/6d.1/6d.2 precedent — all accepted).
- **400-line PR budget**: each slice ≤ 32 paths (7a.0=6, 7a.1=10, 7b.1=10, 7b.2=7 — all under).
- **Scrutor 4.2.2** must be added explicitly to `Trading.Infrastructure.csproj` + `Billing.Infrastructure.csproj` after 7a.0 drops the Identity ref (currently transitive via the Identity reference).

### Risks

| # | Risk | Likelihood | Mitigation |
|---|---|---|---|
| 1 | **`RemoveAsync` → `DeleteAsync` rename breaks all 5 handlers + 0 tests** | Med | `git grep -n "RemoveAsync"` BEFORE the rename. The known 5 handlers are in `src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Trades/{Create,Update,Close,Cancel,Remove}*Handler.cs`. All updated in the same slice. The rename is atomic on the branch. |
| 2 | **`IUserRepository` / `IStrategyRepository` `DeleteAsync` STUB contract addition** | Low | The decorator implements the full interface. The STUB throws `NotSupportedException` with a clear message. Interface XML doc warns handlers not to call it. |
| 3 | **`IRiskProfileRepository.MarkSupersededAsync` decorator shape deviates from `UpdateAsync` wrap pattern** | Low | The decorator wraps `MarkSupersededAsync` directly (the canonical mutation), emits `AuditAction.Deleted` with the supersession diff; this is documented in the slice's deviations + the spec's "Decorator coverage for user-owned aggregates" requirement. |
| 4 | **`IJournalEntryRepository.DeleteAsync(Guid)` → `DeleteAsync(JournalEntry)` overload adds 1 extra SELECT per delete** | Low | Per-aggregate performance cost: 1 SELECT before the DELETE. Negligible for the trade-volume scale (JournalEntry deletion is rare — only on user-initiated journal entry removal). The overload is documented as "decorator-friendly"; production handlers can use `DeleteAsync(Guid)` directly if performance is critical. |
| 5 | **Scrutor transitive ref drop after 7a.0** — Trading + Billing must add explicit Scrutor ref | Med | The csproj edit is in 7a.0 itself. Smoke-test: `dotnet build JadeCapital.slnx` after the edit must succeed. If `services.Decorate<...>()` becomes unresolved, the Scrutor `using` is missing. Easy to catch. |
| 6 | **EF Core 9 SQLite `EnsureCreated` all-or-nothing gotcha** | Med | Every new integration test must include the `CREATE TABLE IF NOT EXISTS events ...` raw SQL workaround (documented in 6d.2 fixture fix lines 102-128 of `TenantRepositoryIntegrationTests.cs`). The 3 new test files copy the pattern verbatim. |
| 7 | **`AuditDbContext` schema collision risk** | Low | All 5 new typed decorators share `audit.events` table. `EntityType` is the discriminator (string `nameof(User)`, `nameof(Strategy)`, `nameof(Trade)`, `nameof(RiskProfile)`, `nameof(JournalEntry)`). No collision as long as each `EntityType` is unique per aggregate (verified — all 5 are distinct). |
| 8 | **`TradingDbContext` `JournalEntry.Tags` array mapping fails on SQLite** | Med | `JournalEntryRepositoryIntegrationTests` uses a focused helper `DbContext` (mirrors `ImportJobRepositoryIntegrationTests.TestTradingDbContext`). The `JournalEntry.Tags` array column is omitted from the test mapping. |
| 9 | **Testcontainers NOT available in sandbox** | Carry-forward | All new integration tests use SQLite-in-memory per Wave 6 precedent. Zero Docker dependency. |
| 10 | **`AuditAction.Failed` enum value may not exist yet** | Low | The User/Strategy `DeleteAsync` STUB emits a `Failed` audit event. If `AuditAction.Failed` is not in the enum, add it to `Shared.Kernel/Audit/AuditAction.cs` (Created=0, Updated=1, Deleted=2, Restored=3, Denied=4, Failed=5). The `Denied` and `Failed` values are reserved for cross-tenant isolation + contract violations. |
| 11 | **`size:exception` precedent** | Likely | Wave 5/6a/6b/6c/6d.1/6d.2 all accepted. Wave 7 follows — expect `size:exception` for 7a.1 + 7b.1. 7a.0 + 7b.2 are under budget. |

## Affected Areas

| Area | Impact | Description |
|---|---|---|
| `src/3.Shared/JadeCapital.Shared.Infrastructure/Persistence/DecoratedRepository.cs` | New | Move destination (from Identity.Infrastructure). New namespace `JadeCapital.Shared.Infrastructure.Persistence`. |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/DecoratedRepository.cs` | Removed | Source file moved in 7a.0. |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/TenantAuditDecorator.cs` | Modified | `using` import update (7a.0). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/ImportJobAuditDecorator.cs` | Modified | `using` import update (7a.0). |
| `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Audit/SubscriptionAuditDecorator.cs` | Modified | `using` import update (7a.0). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/JadeCapital.Trading.Infrastructure.csproj` | Modified | Drop Identity ref (7a.0); add explicit `Scrutor 4.2.2` (7a.0). |
| `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/JadeCapital.Billing.Infrastructure.csproj` | Modified | Drop Identity ref (7a.0); add explicit `Scrutor 4.2.2` (7a.0). |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Abstractions/Repositories/IUserRepository.cs` | Modified | Extend `IRepository<User>`; add `DeleteAsync(User, ct)` STUB throwing `NotSupportedException` (7a.1). |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Abstractions/Repositories/IRiskProfileRepository.cs` | Modified | Extend `IRepository<RiskProfile>`; add `DeleteAsync(RiskProfile, ct)` real impl (7a.1). |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/UserAuditDecorator.cs` | New | Typed decorator (7a.1). |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/RiskProfileAuditDecorator.cs` | New | Typed decorator — bespoke `MarkSupersededAsync` wrap (7a.1). |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/DependencyInjection/IdentityModuleRegistration.cs` | Modified | `Decorate<IUserRepository, UserAuditDecorator>()` + `Decorate<IRiskProfileRepository, RiskProfileAuditDecorator>()` (7a.1). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Abstractions/Repositories/IStrategyRepository.cs` | Modified | Extend `IRepository<Strategy>`; add `DeleteAsync(Strategy, ct)` STUB throwing `NotSupportedException` (7b.1). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Abstractions/Repositories/ITradeRepository.cs` | Modified | `RemoveAsync(Trade)` → `DeleteAsync(Trade, ct)` rename (7b.1). |
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Trades/{Create,Update,Close,Cancel,Remove}*Handler.cs` | Modified | Update all 5 handlers to call `DeleteAsync` instead of `RemoveAsync` (7b.1). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/StrategyAuditDecorator.cs` | New | Typed decorator (7b.1). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/TradeAuditDecorator.cs` | New | Typed decorator — cross-tenant isolation via `Trade.UserId` (7b.1). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs` | Modified | `Decorate<IStrategyRepository, StrategyAuditDecorator>()` + `Decorate<ITradeRepository, TradeAuditDecorator>()` (7b.1). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Abstractions/Repositories/IJournalEntryRepository.cs` | Modified | Add `DeleteAsync(JournalEntry, ct)` overload (7b.2). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/JournalEntryAuditDecorator.cs` | New | Typed decorator — wraps `DeleteAsync(JournalEntry, ct)` (7b.2). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs` | Modified | `Decorate<IJournalEntryRepository, JournalEntryAuditDecorator>()` (7b.2). |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/DecoratedRepositoryTests.cs` | Modified | `using` import update (7a.0). |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/UserRepositoryIntegrationTests.cs` | New | 5 scenarios (7a.1). |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/RiskProfileRepositoryIntegrationTests.cs` | New | 4 scenarios (7a.1). |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/StrategyRepositoryIntegrationTests.cs` | New | 5 scenarios (7b.1). |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/TradeRepositoryIntegrationTests.cs` | New | 5 scenarios (7b.1). |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/JournalEntryRepositoryIntegrationTests.cs` | New | 5 scenarios (7b.2). |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/JadeCapital.Identity.UnitTests.csproj` | Modified | If User/Strategy/Trade/RiskProfile/JournalEntry helper `DbContext` requires cross-module `InternalsVisibleTo` (per 6d.2 precedent). Verify after 7a.1. |
| `src/3.Shared/JadeCapital.Shared.Kernel/Audit/AuditAction.cs` | May extend | Add `Denied=4` + `Failed=5` if not already present (Risk #10). |
| `openspec/specs/soft-delete-audit/spec.md` | Modified | Add delta spec: "Decorator coverage for user-owned aggregates" requirement + 5 sub-scenarios per aggregate (sdd-spec phase). |

## Success Criteria

1. **Decorator move (7a.0)**: `DecoratedRepository<T>` lives at `src/3.Shared/JadeCapital.Shared.Infrastructure/Persistence/DecoratedRepository.cs` with namespace `JadeCapital.Shared.Infrastructure.Persistence`. The 30 existing audit tests (12 DecoratedRepository + 5 Tenant + 3 ImportJob + 3 Subscription + 7 other Audit-related) all pass zero modification. `Trading.Infrastructure.csproj` + `Billing.Infrastructure.csproj` no longer reference `Identity.Infrastructure.csproj`. Both csprojs reference `Scrutor 4.2.2` explicitly.
2. **User audit (7a.1)**: `CreateUserHandler` → `UserAuditDecorator.AddAsync` → `audit.events` row with `Action = Created`, `EntityType = "User"`, `TenantId = …`, `UserId = …`. `UpdateUserHandler` → `Updated` with diff. `CancelUserHandler` → `Updated` (no `IsSoftDelete`). Cross-tenant `Update` → `Denied` event + `UnauthorizedAccessException`. `UserRepositoryIntegrationTests` (5 scenarios) all pass.
3. **RiskProfile audit (7a.1)**: `CreateRiskProfileHandler` → `Created`. `MarkSupersededAsync` → `Deleted` with diff `{SupersededBy: null → Guid, SupersededAtUtc: null → now}`. Cross-tenant `MarkSuperseded` → `Denied`. `RiskProfileRepositoryIntegrationTests` (4 scenarios) all pass.
4. **Strategy audit (7b.1)**: `CreateStrategyHandler` → `Created`. `UpdateStrategyHandler` (rename) → `Updated` with diff. `DeactivateStrategyHandler` → `Updated` with `isActive: true → false` diff (NOT `Deleted`). Cross-tenant update → `Denied`. `StrategyRepositoryIntegrationTests` (5 scenarios) all pass.
5. **Trade audit (7b.1)**: `CreateTradeHandler` → `Created`. `CloseTradeHandler` (status Open → Closed) → `Updated` with status diff. `CancelTradeHandler` → `Updated`. `RemoveTradeHandler` (now `DeleteAsync(Trade, ct)`) → `Deleted`. Cross-tenant update → `Denied`. `TradeRepositoryIntegrationTests` (5 scenarios) all pass. The `RemoveAsync` signature no longer exists in the public API.
6. **JournalEntry audit (7b.2)**: `CreateJournalEntryHandler` → `Created`. `UpdateJournalEntryHandler` (premarket_plan change) → `Updated` with diff. `DeleteJournalEntryHandler` → `Deleted` (via new `DeleteAsync(JournalEntry, ct)` overload). Cross-tenant delete → `Denied`. `JournalEntryRepositoryIntegrationTests` (5 scenarios) all pass.
7. **Build green**: `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings (vs Wave 6 baseline of 3 pre-existing CA2263).
8. **Test cumulative**: 1289 (Wave 6) + 24 (Wave 7) = 1313 tests pass. Zero regressions across the entire BE suite.
9. **Audit log queryable**: `SELECT * FROM audit.events WHERE entity_type = 'User' AND tenant_id = ...` returns the full history of User mutations, newest first. Same for `Strategy`, `Trade`, `RiskProfile`, `JournalEntry`. The queryability is inherited from Wave 6 (migration 0027 + indexes).
10. **All 4 slices under 32 paths** (mandatory). 7a.0=6, 7a.1=10, 7b.1=10, 7b.2=7. `size:exception` expected for 7a.1 + 7b.1 (Wave 5/6 precedent).
11. **PR chain intact**: 4 PRs (#21-#24), each targeting the previous PR's branch. Order matches slice order. No PR targets `main` directly.

## Non-Goals and Later Waves

- **Wave 8**: Audit decorator coverage for `TradeReview`, `PlannerSession`, `PreTradeChecklist`, `Account`, `Instrument`, `Alert`, `RiskConfiguration`, `PlanVersion`, `JournalDaily`, `BacktestRun`, `StrategyVersion`, `TradeTag`, `Note`, `Mood`, `BehavioralMetric`, etc. — every remaining user-owned aggregate.
- **Wave 8**: Admin-only `GET /api/audit/events` query API (filter by `entity_type`, `action`, `user_id`, `tenant_id`, date range, free-text on `changes`). User-facing read API (`GET /api/audit/me`) for "my mutation history" tab.
- **Wave 8**: Audit log retention policy (90-day default) + auto-purge hosted service. Configurable per tenant.
- **Wave 8**: Audit log export (CSV / JSON) for compliance officers.
- **Wave 8**: Soft-delete cascade propagation for `RiskProfile` / `Strategy` / `Trade` / `JournalEntry` (similar to `ImportJob` Wave 6 precedent).
- **Wave 8**: `AuditAction.Restored` end-to-end support (soft-delete restore command + admin tooling).
- **Wave 8+**: Migration to a different audit sink (Kafka, S3, external SIEM). Post-1.0.
- **Wave 8+**: Bulk audit events for `AddRangeAsync` (currently 1 row per entity; bulk path emits 1 batch row with `EntityCount = N`).
- **Wave 9+**: Cross-tenant audit log access (today: tenant-isolated; future: admin tooling for cross-tenant forensics).

## Out of Scope (explicit scope boundary)

- **NOT** adding `ISoftDelete` to `User` / `Strategy` / `Trade` / `RiskProfile` / `JournalEntry`. The decorator upgrades `Updated → Deleted` via `IsTerminated` reflection when `Status ∈ {Cancelled, Terminated, Expired}` (covers `Trade.Status` and `JournalEntry.Status`). Strategy uses `IsActive` (audit semantics: `Updated` with `isActive: true → false` diff). User has no termination flag (cancellation is a domain op). RiskProfile uses `MarkSupersededAsync` (decorator emits `Deleted` directly).
- **NOT** changing the `audit.events` schema. Migration 0027 from Wave 6 already covers `EntityType`, `EntityId`, `Action`, `TenantId`, `UserId`, `Changes JSONB`, `OccurredAt`. No new columns.
- **NOT** changing the `AuditAction` enum (except adding `Denied=4` + `Failed=5` if not already present — Risk #10).
- **NOT** changing the `DecoratedRepository<T>` core implementation. The move is mechanical; the implementation is unchanged.
- **NOT** adding `Restored` action path (deletion is one-way today; restore is admin tooling in Wave 8).
- **NOT** unifying the per-aggregate decorator pattern. Each typed decorator is bespoke to its aggregate's mutation surface (RiskProfile's `MarkSupersededAsync`, JournalEntry's `DeleteAsync(Guid)`). Unification is post-Wave 8 if patterns stabilize.
- **NOT** changing the `Identity.Infrastructure` ownership of `AuditDbContext` / `AuditLogger` / `NoOpAuditLogger`. These are the audit write surface and stay in Identity.
- **NOT** addressing the `JournalEntry.Tags` array mapping in production code (stays as Npgsql-specific). The Wave 7 integration tests use a focused helper `DbContext` that omits the array column.
- **NOT** addressing the `Money` complex type in production code. The Wave 7 integration tests use focused helper `DbContext`s.
