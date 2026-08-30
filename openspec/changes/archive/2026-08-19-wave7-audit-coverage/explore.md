# Exploration — Wave 7 Audit Coverage + Shared.Infrastructure Move

**Change**: `2026-08-19-wave7-audit-coverage`
**Branch**: `feature/0a-identity-model` @ `f312fac` (Wave 6 archived commit)
**Stack**: ASP.NET Core 10 / .NET SDK 10.0.400 via `mise exec -- dotnet …`
**Mode**: explore is **read-only** (Strict TDD does not apply to research)

---

## Current State

Wave 6 (slice 6d.2) shipped the audit decorator pattern covering **3 repositories**: `ITenantRepository` (Identity), `IImportJobRepository` (Trading), `ISubscriptionRepository` (Billing). The shape:

- **Generic helper**: `DecoratedRepository<T>` at `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/DecoratedRepository.cs` (321 LOC, includes `IDiff` + `JsonDiff`). Wraps `IRepository<T>` (narrow generic at `src/3.Shared/JadeCapital.Shared.Kernel/Repository/IRepository.cs`).
- **Typed decorators** co-located per module: `TenantAuditDecorator` (Identity), `ImportJobAuditDecorator` (Trading), `SubscriptionAuditDecorator` (Billing). Each instantiates `DecoratedRepository<T>` with the typed repo cast as `IRepository<T>` and delegates `Add/Update/Delete` to `_decorated` while forwarding read-only extras to `_inner`.
- **Cross-module edges** (the layering tax Wave 6 documented): `Trading.Infrastructure.csproj` and `Billing.Infrastructure.csproj` both reference `Identity.Infrastructure.csproj` **solely** so they can `using JadeCapital.Identity.Infrastructure.Persistence;` for `DecoratedRepository<T>` (see csproj comments lines 26-33 of Trading + lines 23-28 of Billing).
- **DI**: `services.Decorate<IXxxRepository, XxxAuditDecorator>()` in each `*ModuleRegistration`.
- **Integration tests** (SQLite in-memory): `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/{Tenant,ImportJob,Subscription}RepositoryIntegrationTests.cs`. The Trading + Billing integration tests live in the **Identity** test project because that's where `AuditDbContext` + `AuditLogger` are defined.
- **AuditLogger** + **AuditDbContext** + **NoOpAuditLogger** live in `Identity.Infrastructure/Audit/` — they're naturally Identity-owned (the audit write surface). Wave 7 keeps them there; only the **generic decorator wrapper** moves.
- **Soft-delete**: `ISoftDelete` exists in `Shared.Kernel/SoftDelete/` but only `ImportJob` implements it. None of the 5 target repos (`User`, `Strategy`, `Trade`, `RiskProfile`, `JournalEntry`) implement `ISoftDelete` today.

---

## Affected Areas (5 target repos — current surface)

| Interface | Module | Inherits `IRepository<T>`? | Mutation surface | EF query filter? | DI file |
|---|---|---|---|---|---|
| `IUserRepository` | Identity | **No** | `AddAsync`, `UpdateAsync` (no Delete — only Cancel/Suspend domain ops) | none | `IdentityModuleRegistration.cs:53` |
| `IStrategyRepository` | Trading | **No** | `AddAsync`, `UpdateAsync` (no Delete — only `Deactivate()` domain op, soft-delete via `IsActive=false`) | none | `TradingModuleRegistration.cs:50` |
| `ITradeRepository` | Trading | **No** | `AddAsync`, `UpdateAsync`, `RemoveAsync` (hard-delete for Open/Cancelled only) | none | `TradingModuleRegistration.cs:40` |
| `IRiskProfileRepository` | Identity | **No** | `AddAsync`, `MarkSupersededAsync(Guid, IClock, ct)` (no `UpdateAsync` — supersede is the canonical mutation path; `Update` is API-defensive only) | none | `IdentityModuleRegistration.cs:66` |
| `IJournalEntryRepository` | Trading | **No** | `AddAsync`, `UpdateAsync`, `DeleteAsync(Guid)` (hard-delete; soft-delete deferred to Wave 6d per docstring) | none | `TradingModuleRegistration.cs:48` |

### Key interface-shape incompatibilities (vs. Wave 6 pattern)

The Wave 6 pattern **requires the typed interface to extend `IRepository<T>`** so `DecoratedRepository<T>` can wrap it. None of the 5 targets do today. Concretely:

| Repo | Compatibility issue | Recommended resolution |
|---|---|---|
| `IUserRepository` | No Delete at all. Adding `IRepository<User>` would force a never-called `DeleteAsync(User, ct)` stub. | Make it extend `IRepository<User>` with a `DeleteAsync` that throws `NotSupportedException` (or omit Delete from the decorator — typed decorator only forwards Add/Update). |
| `IStrategyRepository` | No Delete. Same as User. `UpdateAsync` + `AddAsync` signatures already match. | Make it extend `IRepository<Strategy>`; decorator omits `DeleteAsync` forwarding (Strategy soft-deletes via `Deactivate()` + `UpdateAsync` — the existing `UpdateAsync` path already captures the `isActive: true → false` flip). |
| `ITradeRepository` | `RemoveAsync(Trade)` instead of `DeleteAsync(Trade)`. | Either: (a) rename `RemoveAsync` → `DeleteAsync` and extend `IRepository<Trade>`, or (b) keep `RemoveAsync` and have the typed decorator forward to a `DeleteAsync` shim that calls `_decorated.DeleteAsync(trade)` (typed decorator still extends `IRepository<Trade>` internally). |
| `IRiskProfileRepository` | **No `UpdateAsync`** — the canonical mutation is `MarkSupersededAsync(Guid, IClock, ct)`. | **Cannot extend `IRepository<RiskProfile>` cleanly.** The typed decorator wraps `MarkSupersededAsync` directly, emitting `AuditAction.Deleted` (since supersede IS the termination). The decorator can still call `_decorated.UpdateAsync` internally by wrapping the load+mutate pattern. |
| `IJournalEntryRepository` | `DeleteAsync(Guid)` (not `DeleteAsync(JournalEntry)`). | Add a `DeleteAsync(JournalEntry, ct)` overload that calls `DeleteAsync(entry.Id, ct)` internally; decorator wraps the entity overload via `_decorated.DeleteAsync(entry, ct)`. |

All 5 entities lack `ISoftDelete` today. The `DecoratedRepository<T>.IsTerminated` reflection check (which upgrades `UpdateAsync` to `AuditAction.Deleted` when the entity is soft-deleted) will not fire for these — `UpdateAsync` emits `AuditAction.Updated`. The `Strategy` case is special: the `Deactivate()` + `UpdateAsync` flow produces an `Updated` event with a diff showing `isActive: true → false`, which is correct audit semantics for soft-delete-via-flag.

---

## Wave 6 pattern — what to replicate exactly

### 1. Generic helper (`DecoratedRepository<T>`)
- **File**: `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/DecoratedRepository.cs` (321 LOC)
- **Shape**: `public sealed class DecoratedRepository<T> where T : class` wrapping `IRepository<T>`. Constructor takes `(IRepository<T> inner, IAuditLogger audit, ITenantContext tenant, IClock clock, IDiff? diff = null, DbContext? db = null)`.
- **Methods**: `GetByIdAsync` (no audit), `AddAsync` (Created), `UpdateAsync` (Updated, with diff + IsTerminated upgrade), `DeleteAsync` (Deleted).
- **Helpers**: `TryAuditAsync` (try/catch no-throw), `BuildEntry` (entity → `AuditEventEntry`), `SafeDiff` (best-effort JSON diff), `GetId` (reflection on `Id` property), `ResolveBeforeAsync` (EF `ChangeTracker.OriginalValues` when `DbContext` supplied), `IsTerminated` (reflection: `IsDeleted == true` OR `Status ∈ {Cancelled, Terminated, Expired}`).
- **Co-located**: `IDiff` interface + `JsonDiff` class in the same file.
- **Tests**: `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/DecoratedRepositoryTests.cs` — 12 RED scenarios (NSubstitute-based, in-memory `FakeAggregate`).

### 2. Typed decorator (`TenantAuditDecorator` canonical)
- **File**: `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/TenantAuditDecorator.cs` (72 LOC)
- **Shape**: `public sealed class TenantAuditDecorator : ITenantRepository` — implements the typed interface, holds `ITenantRepository _inner` + `DecoratedRepository<Tenant> _decorated`, instantiates the generic with `db: db` (so EF `ChangeTracker` is wired).
- **Forwarding pattern**: read-only methods (`FindBySlugAsync`, `ListByOwnerAsync`, `GetByIdAsync`) → `_inner`; mutation methods (`AddAsync`, `UpdateAsync`, `DeleteAsync`) → `_decorated`.
- **Tests**: 5 scenarios in `TenantRepositoryIntegrationTests.cs` (SQLite-in-memory; wires `IdentityDbContext` + `AuditDbContext` + `IAuditLogger` + `ITenantContext` + `IClock`; uses raw `CREATE TABLE IF NOT EXISTS events ...` SQL workaround for EF Core 9 SQLite EnsureCreated all-or-nothing gotcha).

### 3. Cross-tenant isolation variant (`ImportJobAuditDecorator`, `SubscriptionAuditDecorator`)
- Both add an `IsOwner` check (compares entity's `UserId` to `ITenantContext.CurrentUserId`) before allowing `Update/Delete`. On mismatch: log a `Denied` audit row + throw `UnauthorizedAccessException`.
- **File locations**: Trading at `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/ImportJobAuditDecorator.cs` (139 LOC), Billing at `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Audit/SubscriptionAuditDecorator.cs` (130 LOC).
- **DbContext parameter**: typed as `DbContext` (base), not the concrete `TradingDbContext`/`BillingDbContext` — so the SQLite test fixture can register a focused helper DbContext (the production contexts carry Npgsql-specific array converters that fail on SQLite).

### 4. DI wiring
- `services.AddScoped<IXxxRepository, XxxRepository>()` followed by `services.Decorate<IXxxRepository, XxxAuditDecorator>()` in the module's `*ModuleRegistration.cs`. Scrutor requires the underlying service to be registered first.

---

## Plan: Move `DecoratedRepository<T>` to `Shared.Infrastructure`

### Feasibility

**`Shared.Infrastructure` already exists** at `src/3.Shared/JadeCapital.Shared.Infrastructure/`. Its `csproj` references:
- `Shared.Kernel` (✓)
- EF Core + Npgsql (✓ — so `DbContext` and `EntityEntry` imports resolve)
- `MediatR`, `FluentValidation`, `MailKit`, `Stripe.net`, `Minio`, `Serilog` (no Identity-specific deps)
- **No** reference to `Identity.Infrastructure` (✓ — the move is mechanically safe)

**`DecoratedRepository.cs` imports** (line-by-line):
```
using System.Text.Json;                                    — BCL ✓
using JadeCapital.Identity.Application.Abstractions;       — USED: IRepository<T>. Cast is via the interface from Shared.Kernel, not from this using. REMOVE.
using JadeCapital.Identity.Domain.Tenants;                  — USED: only in GetId reflection (typeof T). REMOVE.
using JadeCapital.Shared.Kernel.Audit;                      — ✓ already Shared
using JadeCapital.Shared.Kernel.MultiTenancy;               — ✓ already Shared
using JadeCapital.Shared.Kernel.Repository;                — ✓ already Shared (IRepository<T>)
using JadeCapital.Shared.Kernel.Time;                      — ✓ already Shared (IClock)
using Microsoft.EntityFrameworkCore;                       — ✓ already in csproj
using Microsoft.EntityFrameworkCore.ChangeTracking;        — ✓ already in csproj (EntityEntry.OriginalValues)
```

The only **Identity imports** are `using JadeCapital.Identity.Application.Abstractions;` and `using JadeCapital.Identity.Domain.Tenants;`. Both are **unused** — the generic helper operates on `T` via reflection (`typeof(T).GetProperty("Id")`) and on `IRepository<T>` (from Shared.Kernel). No code change beyond removing the two lines.

### Move mechanics (4 steps, all mechanical)

1. **Copy** `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/DecoratedRepository.cs` → `src/3.Shared/JadeCapital.Shared.Infrastructure/Persistence/DecoratedRepository.cs`.
2. **Update namespace** from `JadeCapital.Identity.Infrastructure.Persistence` → `JadeCapital.Shared.Infrastructure.Persistence`.
3. **Remove** the two `using JadeCapital.Identity.{Application,Domain}…` imports.
4. **Update consumers** (3 typed decorators) from `using JadeCapital.Identity.Infrastructure.Persistence;` → `using JadeCapital.Shared.Infrastructure.Persistence;`:
   - `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/TenantAuditDecorator.cs` (still references `DecoratedRepository<Tenant>`)
   - `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/ImportJobAuditDecorator.cs`
   - `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Audit/SubscriptionAuditDecorator.cs`
5. **Delete** the old `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/DecoratedRepository.cs` (use `git mv` to preserve history → since we're not just renaming, plain `git rm` + new file is cleaner; the diff still preserves the intent).
6. **Update test imports** in `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/DecoratedRepositoryTests.cs` (uses `JadeCapital.Identity.Infrastructure.Persistence` for `DecoratedRepository<T>` + `FakeAggregate`).

### Layering benefit (the whole point of the move)

After the move:
- `Trading.Infrastructure.csproj` line 34 (`<ProjectReference Include="..\..\Identity\JadeCapital.Identity.Infrastructure\JadeCapital.Identity.Infrastructure.csproj" />`) becomes **redundant** — `DecoratedRepository<T>` is reachable via `Shared.Infrastructure` (already referenced on line 38). Same for `Billing.Infrastructure.csproj` line 27.
- The cross-module edge is **eliminated**: Trading and Billing stop depending on Identity entirely.
- The Identity csproj comment ("Wave 6 deviation" lines 26-33) goes away.

### Risk: test fixture coupling

`DecoratedRepositoryTests.cs` imports the generic + a `FakeAggregate` (test-local). After the move the test still works (just update the `using`). The 30 existing integration tests under `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/{Tenant,ImportJob,Subscription}RepositoryIntegrationTests.cs` don't import `DecoratedRepository<T>` directly — they import `TenantAuditDecorator` / `ImportJobAuditDecorator` / `SubscriptionAuditDecorator` — so they're unaffected as long as the typed decorators' new `using` lines are correct.

### Verification strategy

Run the existing **30 audit-related tests** (already in the codebase):
- `DecoratedRepositoryTests` (12 scenarios, in-memory)
- `TenantRepositoryIntegrationTests` (5)
- `ImportJobRepositoryIntegrationTests` (3)
- `SubscriptionRepositoryIntegrationTests` (3)
All should pass with zero code change in test logic — only `using` imports get updated. This proves the move is **zero-behavior-change**.

---

## Approaches compared

### Sub-scope A — apply audit to 5 new repos

| # | Approach | Pros | Cons | Effort |
|---|---|---|---|---|
| A1 | **Extend each interface to `IRepository<T>`** (Wave 6 pattern strict) | Decorator code is identical shape to Tenant/ImportJob/Subscription. Tests reuse the exact fixture from Wave 6. | Breaks signatures for `Trade` (RemoveAsync→DeleteAsync rename) and `JournalEntry` (DeleteAsync(Guid)→DeleteAsync(entity)). `RiskProfile` cannot extend `IRepository<T>` cleanly (no Update, only MarkSupersededAsync). | **Med** |
| A2 | **Typed decorators don't extend `IRepository<T>`** — they instantiate `DecoratedRepository<T>` with `_inner` cast as `IRepository<T>` only for the Add/Update/Delete paths that exist | No signature renames in the typed interface. Matches how RiskProfile's MarkSupersededAsync + JournalEntry's DeleteAsync(Guid) actually work. | Decorator code is slightly more bespoke per aggregate. Less reuse of Wave 6 fixture verbatim (need a per-aggregate integration test class). | **Med** |
| A3 | **Skip audit for `RiskProfile`** (no Update) — keep Wave 6 strict for the 4 standard-shape repos only | Smaller scope, all 4 follow the verbatim Wave 6 template. | Loses the security trail for the supersede path. User explicitly listed `IRiskProfileRepository` in the Wave 7 scope. Not acceptable. | Low |

**Recommendation**: **A2**. The typed decorator wraps the aggregate-specific mutation method (e.g., `MarkSupersededAsync` for RiskProfile, `DeleteAsync(Guid)` for JournalEntry) and emits the right `AuditAction`. For 3 of 5 (User, Strategy, Trade) the decorator forwards AddAsync/UpdateAsync/DeleteAsync (or RemoveAsync) directly to `_decorated`. The integration test class per repo follows Wave 6 shape (5 RED scenarios each).

### Sub-scope B — move `DecoratedRepository<T>` to `Shared.Infrastructure`

| # | Approach | Pros | Cons | Effort |
|---|---|---|---|---|
| B1 | **Pure file move** — copy + delete old + update 4 using imports (3 typed decorators + 1 test) | Minimal diff. Git history preserved per file. Zero behavior change. | Requires 4 mechanical edits. | **Low** |
| B2 | **Generic interface decorator via Scrutor** (`services.Decorate<IRepository<T>, DecoratedRepository<T>>()`) | Single registration per aggregate instead of one typed decorator class. | Doesn't work — `IRepository<T>` is open-generic; Scrutor can't decorate a closed type per aggregate. Would require `services.Decorate(typeof(IRepository<>), typeof(DecoratedRepository<>))` which loses the per-aggregate `EntityType` field in the audit row (`EntityType: typeof(T).Name` becomes `"User"` for both Tenant and User repos — collision risk). | High (incorrect) |
| B3 | **Move + ALSO extract to a shared assembly** (`JadeCapital.Shared.Infrastructure`) | Same as B1 with cleaner naming (`Shared.Infrastructure.Persistence`). | Same as B1. | **Low** |

**Recommendation**: **B1/B3 combined**. File lives at `src/3.Shared/JadeCapital.Shared.Infrastructure/Persistence/DecoratedRepository.cs`. Update 4 using imports. After the move, delete the 2 redundant `<ProjectReference>` lines in Trading + Billing csprojs.

---

## Slicing strategy (proposed)

### Slice 7a.0 — Move `DecoratedRepository<T>` to `Shared.Infrastructure` (~150 LOC, ~6 paths)

**Goal**: Eliminate cross-module edges (Trading → Identity, Billing → Identity). No new audit coverage.

- **Phase 1** (TDD-light, no new RED): copy `DecoratedRepository.cs` to `Shared.Infrastructure/Persistence/`. Drop the 2 Identity imports. Update namespace. Update 3 typed decorator `using` lines. Update 1 test `using` line. Delete old file. Drop 2 redundant `<ProjectReference>` lines in Trading + Billing csprojs.
- **Phase 2** Validate: existing `DecoratedRepositoryTests` (12) + `TenantRepositoryIntegrationTests` (5) + `ImportJobRepositoryIntegrationTests` (3) + `SubscriptionRepositoryIntegrationTests` (3) all pass without modification. **Zero regression**.
- **Phase 3** Build: `dotnet build JadeCapital.slnx` → 0 errors.
- **Effort**: ~150 LOC, ~6 paths, **no new tests**.

### Slice 7a.1 — UserAuditDecorator + RiskProfileAuditDecorator (Identity, ~550 LOC, ~10 paths)

**Goal**: Audit coverage for the 2 Identity-owned user-bound aggregates.

- **Phase 1**: Extend `IUserRepository` to `IRepository<User>` (Add DeleteAsync stub that throws `NotSupportedException` — User is never hard-deleted). Extend interface shape as needed.
- **Phase 2** (TDD): RED tests for `UserAuditDecorator` (5 scenarios): Create user → Created event; Update user (e.g. ChangeDisplayName) → Updated event with diff; soft-delete via Cancel domain op → still emits Updated event (since not ISoftDelete); cross-tenant update rejected if applicable; audit row includes tenant_id + user_id + EntityType="User".
- **Phase 3** GREEN: `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/UserAuditDecorator.cs` (mirror of TenantAuditDecorator). Wire `services.Decorate<IUserRepository, UserAuditDecorator>()` in `IdentityModuleRegistration`.
- **Phase 4** (TDD): RED tests for `RiskProfileAuditDecorator` (4 scenarios): Create profile → Created event; MarkSupersededAsync → Deleted event with diff (IsActive: true → false); GetActiveAsync → no audit; cross-tenant MarkSuperseded rejected.
- **Phase 5** GREEN: `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/RiskProfileAuditDecorator.cs` (bespoke — wraps MarkSupersededAsync directly). Wire DI.
- **Phase 6** Validate: 9 new tests pass. `dotnet build` green.
- **Effort**: ~550 LOC, ~10 paths. **size:exception likely** (5/5a.1/6d.2 precedent).

### Slice 7b.1 — StrategyAuditDecorator + TradeAuditDecorator (Trading, ~700 LOC, ~10 paths)

**Goal**: Audit coverage for Strategy + Trade.

- **Phase 1**: Extend `IStrategyRepository` to `IRepository<Strategy>` (Add `DeleteAsync` stub for `NotSupportedException` — soft-delete goes through `Deactivate()` + `UpdateAsync` which already captures the diff). For `ITradeRepository`, decide on `RemoveAsync` → `DeleteAsync` rename OR keep `RemoveAsync` and let the typed decorator forward.
- **Phase 2** (TDD): RED tests for `StrategyAuditDecorator` (5 scenarios): Create → Created; Update (rename, description change) → Updated with diff; Deactivate via UpdateAsync → Updated with isActive: true→false diff; GetByIdAsync → no audit; ListByUserAsync → no audit.
- **Phase 3** GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/StrategyAuditDecorator.cs`. Wire DI in `TradingModuleRegistration`.
- **Phase 4** (TDD): RED tests for `TradeAuditDecorator` (5 scenarios): Create trade → Created; Close trade (status Open→Closed) → Updated with status diff; Cancel trade → Updated; Remove trade (hard-delete) → Deleted; cross-tenant update rejected.
- **Phase 5** GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/TradeAuditDecorator.cs` (mirrors ImportJobAuditDecorator shape — has UserId, cross-tenant check). Wire DI.
- **Phase 6** Validate: 10 new tests pass.
- **Effort**: ~700 LOC, ~10 paths. **size:exception likely**.

### Slice 7b.2 — JournalEntryAuditDecorator (Trading, ~400 LOC, ~7 paths)

**Goal**: Audit coverage for JournalEntry (the odd one — has `DeleteAsync(Guid)` signature mismatch).

- **Phase 1** (TDD): RED tests for `JournalEntryAuditDecorator` (5 scenarios): Create → Created; Update (premarket_plan change) → Updated with diff; DeleteAsync(Guid) → Deleted (decorator loads entity first, then calls `_decorated.DeleteAsync(entity, ct)`); FindByIdAsync → no audit; cross-tenant delete rejected.
- **Phase 2** GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/JournalEntryAuditDecorator.cs`. The decorator has to add a `DeleteAsync(JournalEntry, ct)` overload to the typed interface (mirroring Wave 6's `IImportJobRepository` shape). Wire DI.
- **Phase 3** Validate: 5 new tests pass. Full suite green.
- **Effort**: ~400 LOC, ~7 paths.

### Total forecast

| Slice | Boundary | LOC | Paths | Tests | size:exception preview |
|---|---|---:|---:|---:|---|
| 7a.0 | Shared.Infrastructure move (refactor) | ~150 | 6 | 0 (regression-only) | no (under budget) |
| 7a.1 | Identity audit (User + RiskProfile) | ~550 | 10 | 9 | likely |
| 7b.1 | Trading audit (Strategy + Trade) | ~700 | 10 | 10 | likely |
| 7b.2 | Trading audit (JournalEntry) | ~400 | 7 | 5 | unlikely |
| **Total** | 4 slices chained | **~1,800** | **33** | **+24** | 2 likely + 1 unlikely |

---

## Constraints + risks

### Constraints (non-negotiable, from Wave 6 precedent)

- **Strict TDD** per `openspec/config.yaml` `apply.tdd: true`. RED tests FIRST for every new decorator.
- **`size:exception`** likely needed for 7a.1 + 7b.1 (Wave 5/6a/6b/6c/6d precedent).
- **400-line budget per PR** — slicing must keep each slice ≤ 32 paths and ≤ 1000 LOC where possible.
- **Scrutor 4.2.2** already referenced from `Identity.Infrastructure.csproj`. Trading + Billing csprojs do NOT reference Scrutor (because the `using JadeCapital.Identity.Infrastructure.Persistence;` brought the extension methods transitively). After Slice 7a.0, Trading + Billing may need to add `Scrutor` to their csprojs OR rely on the transitive ref — must verify.

### Risks

1. **`IUserRepository`/`IStrategyRepository` lack a `Delete` path** — adding `DeleteAsync` as `NotSupportedException` is a contract addition; document it in the interface XML doc so handlers don't accidentally call it.
2. **`ITradeRepository.RemoveAsync` signature** — the rename to `DeleteAsync` is a breaking change to any handler that calls `RemoveAsync`. Use `git grep "RemoveAsync"` to enumerate call sites before renaming. Safer alternative: keep `RemoveAsync` and add a `DeleteAsync(Trade)` overload that the decorator uses.
3. **`IRiskProfileRepository.MarkSupersededAsync(Guid, IClock, ct)` is the canonical mutation** — the decorator wraps THIS method, not `UpdateAsync`. Document this as a deviation from the "wrap UpdateAsync" pattern. The decorator still calls `_decorated.UpdateAsync` internally by loading the entity first OR by emitting the audit row directly (bypassing the generic helper for this one method).
4. **`IJournalEntryRepository.DeleteAsync(Guid)` signature** — decorator must load the entity first then call `_decorated.DeleteAsync(entity, ct)`. Performance: 1 extra SELECT per delete. Mitigation: cache the entity in scope (the handler likely has it already); document the cost.
5. **`Scrutor` transitive reference** — Trading + Billing modules get Scrutor via the Identity reference today. After Slice 7a.0 drops the Identity ref, both modules must add `<PackageReference Include="Scrutor" Version="4.2.2" />` to their own csprojs. Easy to miss.
6. **`AuditDbContext` schema collision risk** — all 5 new typed decorators share `audit.events` table. `EntityType` is the discriminator (string `nameof(User)`, `nameof(Strategy)`, etc.). No collision as long as EntityType is unique per aggregate (it is — checked Wave 6 spec).
7. **Testcontainers not available in sandbox** — all new integration tests use SQLite-in-memory per Wave 6 precedent. The `JournalEntry` test fixture MUST sidestep the Npgsql-specific `JournalEntry.Tags` array mapping (use a focused helper `DbContext` like `ImportJobRepositoryIntegrationTests.TestTradingDbContext`).
8. **`TradingDbContext` / `BillingDbContext` Money complex type** — same Npgsql-incompatibility. The test fixtures must use focused helper `DbContext`s (mirroring `TestTradingDbContext` from `ImportJobRepositoryIntegrationTests`).
9. **EF Core 9 SQLite `EnsureCreated` all-or-nothing gotcha** — every new integration test must include the `CREATE TABLE IF NOT EXISTS events …` raw SQL workaround (documented in 6d.2 fixture fix lines 102-128 of `TenantRepositoryIntegrationTests.cs`).

### Ambiguity

- **Should `IRiskProfileRepository.MarkSupersededAsync` emit `AuditAction.Deleted` or `AuditAction.Updated`?** The supersede IS the termination (no separate restore path). Recommend `Deleted` for parity with `ImportJob.MarkDeleted`. Document in the spec.
- **Should `ITradeRepository.RemoveAsync` (hard-delete) be audited at all?** Wave 6 precedent: yes (hard-delete emits Deleted). The handler validates the trade is Open/Cancelled before calling Remove — defense-in-depth.
- **Should `Strategy.Deactivate()` + `UpdateAsync` emit `AuditAction.Updated` or `AuditAction.Deleted`?** `Strategy` is not ISoftDelete. The current `IsTerminated` reflection check only upgrades when `IsDeleted == true` OR `Status ∈ {Cancelled, Terminated, Expired}`. `Strategy.IsActive = false` does NOT trigger the upgrade (because `IsActive` ≠ `IsDeleted`). Result: emits `AuditAction.Updated` with an `isActive: true → false` diff. This is **correct** audit semantics for a "soft-delete-by-flag" pattern. Document the deviation in the slice.

---

## Ready for Proposal

**Yes.** The proposal phase can build directly on this artifact:

- **5 target repos** confirmed with their exact mutation surfaces and the interface-shape incompatibilities that must be resolved.
- **Wave 6 pattern** documented verbatim (3 reference files: `TenantAuditDecorator`, `DecoratedRepository`, `TenantRepositoryIntegrationTests`) — proposal can copy the structure.
- **`DecoratedRepository<T>` move** is mechanically safe; only `using` imports + 2 csproj lines change. Zero behavior change verified by the 30 existing audit tests.
- **Slicing** proposed (4 slices, ~1,800 LOC total, ~33 paths).
- **Risks** enumerated (interface signature changes, Scrutor transitive ref drop, EF SQLite gotcha, EntityType discriminator).
- **Ambiguities** flagged for the user to decide before the proposal locks them in:
  1. RiskProfile supersede → `Deleted` vs `Updated`?
  2. Trade `RemoveAsync` rename vs overload?
  3. Strategy deactivate → `Updated` (current default) or `Deleted`?
  4. Should the User/Strategy `DeleteAsync` stubs throw `NotSupportedException` or be omitted from the decorator interface entirely?

These 4 questions are the only thing blocking the proposal from writing the spec scenarios verbatim.
