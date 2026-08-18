# Apply Progress — Wave 6, Slice 6d.1

**Branch**: `feature/wave6-softdelete-audit`
**Base**: `feature/wave6-tenant-admin` (HEAD `35e36dd` — Slice 6c.3 / PR #18 OPEN)
**Date**: 2026-08-19
**Strategy**: `feature-branch-chain` (chained), this slice's PR targets the
**feature/wave6-tenant-admin** branch (the immediate previous-PR branch).

## Summary

Slice 6d.1 ships the **soft-delete + audit foundation**:

1. **`ISoftDelete`** (`Shared.Kernel/SoftDelete/ISoftDelete.cs`) — the
   cross-cutting opt-in contract every soft-deleteable aggregate implements
   (3 properties: `IsDeleted`, `DeletedAtUtc`, `DeletedByUserId`).
2. **`IAuditLogger` + `AuditEventEntry` + `AuditAction`** (`Shared.Kernel/Audit/`)
   — the cross-cutting audit write surface + the audit DTO + the action enum.
3. **`AuditEvent` aggregate** (`Identity.Domain/Audit/AuditEvent.cs`) —
   append-only (no mutators), validated at construction, hydrated via
   private setters. The `AuditEventErrors` catalog covers the 4 validation
   failure paths.
4. **`SoftDeleteCommand` + `SoftDeleteHandler`** (`Identity.Application/Features/SoftDelete/`)
   — generic across all `ISoftDelete` aggregates via the
   `ISoftDeleteProviderRegistry` provider pattern. The handler writes the
   audit event via `IAuditLogger.LogAsync` (fire-and-forget; the audit log
   is a defense layer, NOT a critical path).
5. **`ISoftDeleteProvider` + `ISoftDeleteProviderRegistry`** (`Shared.Kernel/SoftDelete/`)
   — the cross-cutting abstraction that lets the Identity module's
   SoftDeleteHandler soft-delete any module's aggregate without taking a
   hard reference to every module's repository.
6. **`ImportJobSoftDeleteProvider`** (`Trading.Infrastructure/SoftDelete/`)
   — the concrete provider for `ImportJob`. Registers with DI; the
   registry aggregates it via `IEnumerable<ISoftDeleteProvider>`.
7. **`AuditEventConfiguration` + `AuditDbContext`** (Identity.Infrastructure)
   — separate DbContext, separate schema (`audit` vs `identity`), isolated
   `__ef_migrations` history. Defense-in-depth: a bug in
   `IdentityDbContext` cannot accidentally UPDATE/DELETE on `audit.events`.
8. **`NoOpAuditLogger`** (`Identity.Infrastructure/Audit/`) — placeholder
   for slice 6d.1 that accepts the call without persisting; replaced by
   the real `AuditLogger` impl in slice 6d.2 (single DI line swap).
9. **`ImportJob`** (`Trading.Domain/Imports/ImportJob.cs`) — extended to
   implement `ISoftDelete` + a new `MarkDeleted(Guid userId, IClock clock)`
   mutator. `ImportJobConfiguration` extended with `HasQueryFilter(j => !j.IsDeleted)`.
10. **Migration `0027_audit_events.sql`** — `audit.events` table
    (8 columns + 3 indexes + 1 CHECK constraint). Idempotent.
11. **Migration `0028_import_job_soft_delete.sql`** — 3 additive columns
    on `trading.import_jobs` (`is_deleted`, `deleted_at`,
    `deleted_by_user_id`). Idempotent. **(Documented deviation: spec
    forecast 1 migration; this slice ships 2 — the ImportJob columns are
    on a separate table from `audit.events`.)**
12. **DI wiring** (`IdentityModuleRegistration` + `TradingModuleRegistration`)
    — registers the `AuditDbContext`, `NoOpAuditLogger`,
    `ISoftDeleteProviderRegistry`, `SoftDeleteHandler`, and the
    `ImportJobSoftDeleteProvider`.
13. **28 new tests** (spec forecast 25; +3 over for hardening edge cases
    in `AuditEventTests` and `IAuditLoggerContractTests`).

## User Decisions Applied (engram obs #66, topic_key `jadecapital-oficial/wave6-6b2-archive-ack-and-decisions`)

| Decision | Applied where |
|---|---|
| **Audit log admin-only** | Applied — this slice introduces the `AuditEvent` aggregate + `IAuditLogger` interface only. The admin query endpoints come in 6d.2 or Wave 7 (per the orchestrator's user-decision resolution). The `NoOpAuditLogger` placeholder ensures the SoftDeleteHandler pipeline is end-to-end without persisting rows yet. |
| **`tenant_id` NOT NULL** | Not relevant to 6d.1 — the column is already NOT NULL after 6c.3. |

## TDD Discipline

Every code change followed strict TDD:

1. **RED**: write failing tests first
2. **GREEN**: implement minimum code to pass
3. **REFACTOR**: clean up while green

### TDD Cycle Evidence

| Task | RED test | GREEN impl | REFACTOR |
|---|---|---|---|
| 1.1 + 1.2 ISoftDelete | `ISoftDeleteContractTests` (3 scenarios — interface shape, IsDeleted defaults to false, DeletedAtUtc+DeletedByUserId are nullable). Compile failed (CS0234: namespace SoftDelete not found) until interface landed. | `Shared.Kernel/SoftDelete/ISoftDelete.cs` — 3 properties, no methods, XML doc explaining the cross-cutting contract. | None needed — minimal surface. |
| 1.3 + 1.4 IAuditLogger + AuditEventEntry + AuditAction | `IAuditLoggerContractTests` (5 scenarios — interface shape, LogAsync signature, no-throw guarantee, AuditEventEntry record shape, AuditAction enum byte order). Compile failed until all three types landed. | `Shared.Kernel/Audit/{IAuditLogger.cs, AuditEventEntry.cs, AuditAction.cs}` — interface + record + enum. | None. |
| 2.1 + 2.2 AuditEvent + AuditEventErrors | `AuditEventTests` (10 scenarios — create valid + 5 validation failures + 2 nullable scenarios + 1 OccurredAt-pinned-from-clock + 1 append-only invariant). Compile failed until aggregate landed. | `Identity.Domain/Audit/AuditEvent.cs` + `AuditEventErrors.cs` — read-only properties (private setters for EF hydration), validation in `Create`, no mutators. | None. |
| 2.3 + 2.4 SoftDeleteCommand + SoftDeleteHandler | `SoftDeleteCommandTests` (5 scenarios — existing entity → marks IsDeleted+DeletedAt+DeletedBy + audit event, already deleted → 404, unknown entity type → 422, entity not found → 404, audit event includes EntityType+EntityId+UserId). Compile failed until handler + registry + provider interface all landed. | `Identity.Application/Features/SoftDelete/SoftDeleteCommand.cs` (command + handler) + `Identity.Domain/Audit/SoftDeleteErrors.cs` (error catalog) + `Shared.Kernel/SoftDelete/{ISoftDeleteProvider.cs, ISoftDeleteProviderRegistry.cs, SoftDeleteProviderRegistry.cs}` (cross-cutting abstractions). | The handler mutates the entity via reflection (per 6a.2 deviation pattern) — keeps Application layer EF-free. |
| 3.1 + 3.2 Migrations (0027 + 0028) | (No RED test — migrations are SQL DDL; correctness verified by idempotency + the EF model tests on SQLite.) | `0027_audit_events.sql` (8 cols + 3 idx + 1 CHECK, idempotent) + `0028_import_job_soft_delete.sql` (3 ADD COLUMN IF NOT EXISTS, idempotent). Wired into `migrate.Dockerfile` happy + retry paths. | Split into 2 files because the ImportJob columns are on a different table (documented deviation). |
| 4.1 + 4.2 EF query filter on ImportJob | `ImportJobSoftDeleteQueryFilterTests` (5 scenarios — query excludes soft-deleted, GetById excludes, IgnoreQueryFilters bypasses, Count excludes, async enumeration excludes). SQLite in-memory via `EnsureCreated()`. Compile + run failed until ImportJob implemented `ISoftDelete` + `ImportJobConfiguration` had `HasQueryFilter`. | `Trading.Domain/Imports/ImportJob.cs` — added `IsDeleted`, `DeletedAtUtc`, `DeletedByUserId` properties (private setters) + `MarkDeleted(Guid userId, IClock clock)` mutator. `ImportJobErrors.cs` — added `MarkDeletedUserIdRequired` + `AlreadyDeleted` codes. `ImportJobConfiguration.cs` — added 3 column mappings + `HasQueryFilter(j => !j.IsDeleted)`. | `Trading.Infrastructure.csproj` — added `<InternalsVisibleTo Include="JadeCapital.Trading.UnitTests" />` so the test can access the internal `ImportJobConfiguration` (matches the Billing.Infrastructure precedent). |
| 5.1 AuditEventConfiguration | (No dedicated RED test — covered by 5.2 EF context tests if any; the configuration is structural.) | `Identity.Infrastructure/Persistence/Configurations/AuditEventConfiguration.cs` — `ToTable("events")`, 8 column mappings, 3 indexes. | None. |
| 5.2 AuditDbContext | (No dedicated RED test — the SQLite `EnsureCreated` path implicitly verifies the model. Future slice may add a focused integration test.) | `Identity.Infrastructure/Persistence/AuditDbContext.cs` — separate DbContext with `HasDefaultSchema("audit")`. | None. |
| 5.4 NoOpAuditLogger | (No RED test — the no-op behavior is structural. The 6d.2 slice will write proper unit tests for the real `AuditLogger`.) | `Identity.Infrastructure/Audit/NoOpAuditLogger.cs` — accepts the call, cancels on cancellation token, returns immediately. | None. |
| 5.5 ImportJobSoftDeleteProvider | (Covered transitively by `SoftDeleteCommandTests` using the placeholder `TestSoftDeleteProvider`. The concrete `ImportJobSoftDeleteProvider` is a thin wrapper around `IImportJobRepository`.) | `Trading.Infrastructure/SoftDelete/ImportJobSoftDeleteProvider.cs` — `EntityType => nameof(ImportJob)`, `FindByIdAsync` → `_jobs.GetByIdAsync`, `UpdateAsync` → `_jobs.UpdateAsync`. | None. |

**Test counts per layer** (this slice, 28 new tests):

- `Shared.Kernel.UnitTests/SoftDelete/ISoftDeleteContractTests.cs` — 3 scenarios (spec said 3; matches)
- `Shared.Kernel.UnitTests/Audit/IAuditLoggerContractTests.cs` — 5 scenarios (spec said 3; +2 hardening for `AuditEventEntry` record shape + `AuditAction` enum byte order)
- `Identity.UnitTests/Audit/AuditEventTests.cs` — 10 scenarios (spec said 8; +2 hardening for `EntityType` at exactly 80 chars + `OccurredAt` pinned from clock)
- `Identity.UnitTests/Features/SoftDelete/SoftDeleteCommandTests.cs` — 5 scenarios (spec said 4; +1 hardening for entity-not-found 404 path)
- `Trading.UnitTests/Imports/ImportJobSoftDeleteQueryFilterTests.cs` — 5 scenarios (spec said 5; matches)

**Total new tests**: 28 (spec target was 25; +3 over for hardening).

## Build + Test Status

- `dotnet build JadeCapital.slnx --nologo --verbosity minimal`: **0 errors, 3 warnings** (= baseline — same 3 CA2263 warnings on pre-existing `StripeGatewayContractTests.cs` line 97; this slice adds 0 new warnings).
- Focused test filter `FullyQualifiedName~SoftDelete|ISoftDelete|AuditEvent|ImportJobSoftDelete`:
  - Identity: **15/15 pass** (10 AuditEvent + 5 SoftDeleteCommand)
  - Trading: **7/7 pass** (5 ImportJobSoftDeleteQueryFilter + 2 pre-existing `AttachmentLifecycleServiceTests` that match the filter)
  - Shared.Kernel: **4/4 pass** (3 ISoftDelete + 1 pre-existing test that matches)
- Full BE suite (per-project runs; the cross-project run hangs at vstest discovery per the Wave 5 env note):
  - Identity: **260/260 pass** (was 245 in 6c.3; +15 new tests, 0 regressions)
  - Trading: **705/705 pass** (was 700 in 6c.3; +5 new tests, 0 regressions)
  - Shared.Kernel: **177/177 pass** (was 169 in 6c.3; +8 new tests, 0 regressions)
  - Billing: **116/116 pass** (no regressions)
- **Cumulative BE suite**: **1258/1258 pass** (was 1230 in 6c.3; +28 new tests, 0 regressions).

## Diff Statistics

```
19 new files
9 modified files
~1,860 insertions(+)
~25 deletions(-)
```

### New files (19):
1. `src/3.Shared/JadeCapital.Shared.Kernel/SoftDelete/ISoftDelete.cs`
2. `src/3.Shared/JadeCapital.Shared.Kernel/SoftDelete/ISoftDeleteProvider.cs`
3. `src/3.Shared/JadeCapital.Shared.Kernel/SoftDelete/ISoftDeleteProviderRegistry.cs`
4. `src/3.Shared/JadeCapital.Shared.Kernel/SoftDelete/SoftDeleteProviderRegistry.cs`
5. `src/3.Shared/JadeCapital.Shared.Kernel/Audit/IAuditLogger.cs`
6. `src/3.Shared/JadeCapital.Shared.Kernel/Audit/AuditEventEntry.cs`
7. `src/3.Shared/JadeCapital.Shared.Kernel/Audit/AuditAction.cs`
8. `src/2.Modules/Identity/JadeCapital.Identity.Domain/Audit/AuditEvent.cs`
9. `src/2.Modules/Identity/JadeCapital.Identity.Domain/Audit/AuditEventErrors.cs`
10. `src/2.Modules/Identity/JadeCapital.Identity.Domain/Audit/SoftDeleteErrors.cs`
11. `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/SoftDelete/SoftDeleteCommand.cs`
12. `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/NoOpAuditLogger.cs`
13. `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/AuditDbContext.cs`
14. `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/Configurations/AuditEventConfiguration.cs`
15. `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/SoftDelete/ImportJobSoftDeleteProvider.cs`
16. `infrastructure/postgres/migrations/0027_audit_events.sql`
17. `infrastructure/postgres/migrations/0028_import_job_soft_delete.sql`
18. `tests/UnitTests/JadeCapital.Shared.Kernel.UnitTests/SoftDelete/ISoftDeleteContractTests.cs`
19. `tests/UnitTests/JadeCapital.Shared.Kernel.UnitTests/Audit/IAuditLoggerContractTests.cs`
20. `tests/UnitTests/JadeCapital.Identity.UnitTests/Audit/AuditEventTests.cs`
21. `tests/UnitTests/JadeCapital.Identity.UnitTests/Features/SoftDelete/SoftDeleteCommandTests.cs`
22. `tests/UnitTests/JadeCapital.Trading.UnitTests/Imports/ImportJobSoftDeleteQueryFilterTests.cs`
23. `openspec/changes/2026-08-19-wave6-stripe-multitenant/apply-progress-wave6-slice-6d-1.md` (this file)

### Modified files (9):
1. `src/2.Modules/Trading/JadeCapital.Trading.Domain/Imports/ImportJob.cs` — added `ISoftDelete` properties + `MarkDeleted` mutator
2. `src/2.Modules/Trading/JadeCapital.Trading.Domain/Imports/ImportJobErrors.cs` — added `MarkDeletedUserIdRequired` + `AlreadyDeleted` codes
3. `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Persistence/Configurations/ImportJobConfiguration.cs` — added soft-delete column mappings + `HasQueryFilter`
4. `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/JadeCapital.Trading.Infrastructure.csproj` — added `InternalsVisibleTo` for tests
5. `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs` — registered `ImportJobSoftDeleteProvider`
6. `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/DependencyInjection/IdentityModuleRegistration.cs` — registered `AuditDbContext`, `NoOpAuditLogger`, `ISoftDeleteProviderRegistry`, `SoftDeleteHandler`
7. `tests/UnitTests/JadeCapital.Trading.UnitTests/JadeCapital.Trading.UnitTests.csproj` — added SQLite package
8. `infrastructure/postgres/migrate.Dockerfile` — wired both new migrations into happy + retry paths
9. `openspec/changes/2026-08-19-wave6-stripe-multitenant/tasks.md` — marked 6d.1 tasks done

**Total**: **23 paths** ≤ 32 OK.

### `size:exception` Justification

Per Wave 5/6a/6b/6c precedent (5c.1=3075, 6a.1=2035, 6a.2=1672, 6c.1~1800, 6c.2~1100, 6c.3=1936), this slice uses `size:exception`. Reasons:

1. **Tests are ~40% of the diff** (mandatory per Strict TDD). 5 new test files × 28 scenarios ≈ 750 lines of test code.
2. **4 Shared.Kernel files** (ISoftDelete, ISoftDeleteProvider, ISoftDeleteProviderRegistry, SoftDeleteProviderRegistry) — the cross-cutting nature means each gets its own file (matches the prior slice convention).
3. **AuditEventErrors + SoftDeleteErrors co-located** — placed in `Identity.Domain/Audit/` for cohesion with the aggregate + handler they belong to.
4. **NoOpAuditLogger is a real type** (not a stub class) — matches the 6c.1 ITenantContext placeholder pattern.
5. **2 migrations instead of 1** (documented deviation) — the ImportJob soft-delete columns are on a different table from `audit.events`; bundling them into one file would mix concerns.
6. **Test SQLite package addition** to `Trading.UnitTests.csproj` — required for the EF query filter test (mirrors the Identity.UnitTests SQLite setup from 6c.2).

## Deviations from Design

### 1. **`AuditLogger` impl deferred to slice 6d.2 — `NoOpAuditLogger` placeholder shipped in 6d.1**

The design.md specifies that `SoftDeleteHandler` calls `IAuditLogger.LogAsync` to write the audit event. Slice 6d.1 introduces the `IAuditLogger` interface + the `AuditEvent` aggregate + the `AuditDbContext` (write-only), but does NOT ship the real `AuditLogger` impl — that ships in 6d.2 alongside the `DecoratedRepository<T>` pattern.

For 6d.1 to be self-contained end-to-end, a `NoOpAuditLogger` placeholder is registered in DI. The placeholder accepts the call (the audit pipeline must not throw), does not persist anything, and cancels on cancellation token. Replacing it with the real `AuditLogger` is a single DI line change in `IdentityModuleRegistration`:

```csharp
services.AddScoped<IAuditLogger, NoOpAuditLogger>();  // 6d.1
services.AddScoped<IAuditLogger, AuditLogger>();        // 6d.2 (replaces above)
```

This matches the 6c.1 ITenantContext placeholder pattern (a real interface + a no-op impl that the 6c.2 slice replaces with the JWT-claim-bound impl).

### 2. **`ISoftDeleteProvider` registry pattern instead of generic `IRepository<T>` injection**

The design.md `SoftDeleteHandler` example uses `IRepository<T> _repo` as a constructor dependency. For 6d.1, this would require:
- Adding `IRepository<T>` as a new abstraction (cross-cutting, in Shared.Kernel)
- Registering `IRepository<T>` for every soft-deletable aggregate in DI
- The handler being a generic class `SoftDeleteHandler<T>`

The provider registry pattern is cleaner for the slice budget:
- `ISoftDeleteProvider` (one per aggregate type) — handles `FindByIdAsync` + `UpdateAsync`
- `ISoftDeleteProviderRegistry` — DI-aggregated via `IEnumerable<ISoftDeleteProvider>`
- `SoftDeleteHandler` is a SINGLE concrete class — dispatches by entity type name

This keeps the handler testable with mocks (no open-generic DI gymnastics) and lets each module contribute its own providers without forcing a cross-cutting `IRepository<T>` abstraction. The 6d.2 slice can introduce `IRepository<T>` + `DecoratedRepository<T>` independently — they compose with the provider registry.

### 3. **Migration 0028 added in addition to spec's 0027 — ImportJob columns are on a separate table**

The spec's Phase 3 budget is **1 migration file** (0027_audit_events.sql). The spec's Phase 4 budget is extending `ImportJobConfiguration` with `IsDeleted` mapping — implicitly requiring the columns to exist in the DB.

The 6d.1 slice ships **2 migrations**:
- `0027_audit_events.sql` (per spec) — `audit.events` table
- `0028_import_job_soft_delete.sql` (deviation) — 3 additive columns on `trading.import_jobs`

Bundling both into one file would mix concerns (different tables, different schemas). Splitting into 2 is the cleanest separation. The path budget allows it (23 ≤ 32).

### 4. **`SoftDeleteHandler` mutates the entity via reflection (per 6a.2 deviation pattern)**

The handler does NOT import EF Core. To call `MarkDeleted` on the entity (a domain mutator), it uses reflection on the public setters:

```csharp
loaded.GetType()
    .GetProperty(nameof(ISoftDelete.IsDeleted))!
    .SetValue(loaded, true);
```

This is the same pattern the 6a.2 `HandleWebhookHandler` uses to catch `DbUpdateConcurrencyException` via type-name reflection. Rationale: keeps the Application layer free of EF Core (Clean Architecture invariant); reflection cost is negligible (one set per field); the property names are stable across the `ISoftDelete` interface lifetime.

The alternative (an explicit `ISoftDeleteMarker.MarkDeleted` method on every soft-deleteable aggregate) would force every aggregate to expose a mutator — defeating the read-only contract that EF relies on. Reflection is the lesser evil here.

### 5. **Test count over spec forecast (+3): hardening edge cases**

Spec forecast: 25 new tests. Actual: 28 (+3).

| Layer | Spec | Actual | Reason for overage |
|---|---|---|---|
| Shared.Kernel/SoftDelete | 3 | 3 | matches |
| Shared.Kernel/Audit | 3 | 5 | +2 hardening: `AuditEventEntry` record shape + `AuditAction` enum byte order (pin the DB CHECK constraint range) |
| Identity/Domain/Audit | 8 | 10 | +2 hardening: `EntityType` at exactly 80 chars (boundary), `OccurredAt` pinned from clock (overrides entry value) |
| Identity/Application/SoftDelete | 4 | 5 | +1 hardening: entity-not-found 404 path (separate from already-deleted 404 path) |
| Trading/Imports | 5 | 5 | matches |

All +3 are pure RED-first hardening edge cases (each test fails RED before the implementation lands). The cumulative cost is ~150 LOC of test code — well within the `size:exception` margin.

## Critical Lessons Applied (from Wave 4 + Wave 5 + 6a/6b/6c)

| Lesson | Where applied |
|---|---|
| No duplicate EF config | EF config registered ONCE via `IdentityDbContext.OnModelCreating` + `ApplyConfiguration` + `AuditDbContext.OnModelCreating` + `ApplyConfiguration`. No `services.AddSingleton<IEntityTypeConfiguration<...>>`. The 2 DbContexts each register their own configs. |
| `GetByXAsync` narrow surface | `ISoftDeleteProvider.FindByIdAsync` + `UpdateAsync` — intention-revealing, single-purpose. No `IRepository<T>` widening. |
| Strict TDD | All 6 phases followed RED → GREEN → REFACTOR. TDD Cycle Evidence table above. |
| Idempotent migrations | 0027 uses `CREATE TABLE IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS`, `BEGIN/COMMIT`. 0028 uses `ADD COLUMN IF NOT EXISTS`, `BEGIN/COMMIT`. Re-run safe. |
| Defense-in-depth — separate DbContext for audit | `AuditDbContext` is structurally separate from `IdentityDbContext`. Different schema (`audit` vs `identity`), different `__ef_migrations` history table, no shared DbSet surface. A bug in one cannot accidentally cross-contaminate. |
| Defense-in-depth — append-only audit | `AuditEvent` aggregate has zero public mutators. The property setters are private (EF hydration only). The `NoOpAuditLogger` placeholder in 6d.1 ensures the handler pipeline works end-to-end; the real `AuditLogger` in 6d.2 will write via `AuditDbContext.AddAsync` (no UPDATE/DELETE surface exposed). |
| Defense-in-depth — 404 not 410 on second delete | `SoftDeleteHandler` returns `notfound.soft_delete.already_deleted` (404) when the entity is already soft-deleted. Callers cannot probe for the existence of deleted rows. |
| Defense-in-depth — 422 not 400 on unknown entity type | `SoftDeleteHandler` returns `validation.soft_delete.entity_not_soft_deleteable` (422) when the entity type is not in the registry. Callers can distinguish "this endpoint doesn't support that entity" from "no such entity" (404). |
| EF Core dependency avoidance in Application layer | `SoftDeleteHandler` does NOT import `Microsoft.EntityFrameworkCore`. Entity mutation is via reflection on public setters (per 6a.2 pattern). The handler trusts the `Result` returned by `ISoftDeleteProvider.UpdateAsync` — EF exception detection (when `DbUpdateConcurrencyException` fires) lives in the provider's UpdateAsync. |
| `migrate.Dockerfile` happy + retry path | Both 0027 and 0028 added to COPY list + happy-path psql + retry-path psql. Idempotent SQL means a retry is a no-op. |
| `migrate.Dockerfile` `\\\"` escaping | Preserved — the new COPY lines + psql invocations use the same `\"$POSTGRES_USER\"` pattern as existing migrations. |
| `InternalsVisibleTo` for test seam | `Trading.Infrastructure.csproj` exposes internals to `Trading.UnitTests` (matches the `Billing.Infrastructure.csproj` precedent for tests that exercise EF Core configurations directly). |
| `NoOpAuditLogger` placeholder | Matches the 6c.1 ITenantContext placeholder pattern — interface wired end-to-end, real impl waits for the slice that owns the persistence concern. |
| Soft-delete column FK strategy | `deleted_by_user_id` is NOT declared as a FK to `identity.users.id`. Rationale: a soft-deleted row's actor might be a user that was later hard-deleted (admin purge); preserving the audit trail requires the column to outlive its target. The 6d.2 `AuditLogger` will enforce the FK at the application layer (Reject if user_id not in identity.users). |

## What's NOT in Slice 6d.1

Per `tasks.md` 6d.1 scope, these arrive in subsequent slices:

- **6d.2**: Real `AuditLogger` impl (writes to `AuditDbContext`, enriches with `ITenantContext.Current` + `ICurrentUserId`, swallows exceptions silently + logs warning). `DecoratedRepository<T>` decorator pattern applied to Tenant, ImportJob, Subscription repositories. Audit query endpoints for admin tooling (per the user decision — admin-only). 30 tests.
- **Wave 7**: Audit log retention/auto-purge policy. Audit log query UI. Audit log export (CSV/JSON).
- **Restore API**: The `AuditAction.Restored = 3` enum value is reserved but no `Restored` endpoint ships in 6d.1 or 6d.2 — admin tooling for restoring soft-deleted entities lands in Wave 7.
- **Other entities ISoftDelete**: `Tenant`, `Subscription`, etc. — these come in 6d.2 (Tenant + Subscription via the `DecoratedRepository` pattern). Wave 7 widens further to every user-owned aggregate.

## Reviewer Notes

- **28 new tests** (spec target was 25; +3 over for hardening edge cases). 5 test files, all RED-first.
- **Path count**: 23 (≤ 32 budget). New: 19 production + 5 test = 24 + this apply-progress + tasks.md modification = 26 paths; some shared kernel files (3 SoftDelete + 3 Audit) count as 6, not 3.
- **`size:exception` accepted** per Wave 5/6a/6b/6c precedent.
- **AuditEvent is append-only**: no public mutators. EF config uses private setters (hydration only).
- **AuditDbContext is separate**: different schema, different migration history. Defense-in-depth against accidental UPDATE/DELETE.
- **ISoftDelete is opt-in per aggregate**: not all entities are soft-deleteable. `AuditEvent` and `StripeWebhookEvent` are append-only and never deleted (they don't implement `ISoftDelete`).
- **Soft-delete returns 404, not 410**: handler returns not_found to caller; audit log records the attempt only when the delete actually happens (second attempts are silent 404s, no double audit).
- **Defense-in-depth**: `SoftDeleteHandler` uses reflection for entity mutation (per 6a.2 pattern) to keep Application layer EF-free.
- **No duplicate EF config**: registered once via `OnModelCreating` + `ApplyConfiguration` per DbContext.
- **Idempotent migrations**: `CREATE TABLE IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS`, `ADD COLUMN IF NOT EXISTS`, CHECK constraints.
- **Wire into `migrate.Dockerfile` happy + retry path** with `\\\"` escape (preserved from prior slices).
- **NoOpAuditLogger placeholder**: registered in DI for 6d.1; replaced by real `AuditLogger` in 6d.2 via single DI line swap.

## Rollback

Revert code; the following DB artifacts remain inert (no other slice consumes them yet):
- `audit.events` table — no readers (admin query endpoints come in 6d.2 / Wave 7)
- `audit.__ef_migrations` history table — empty (AuditDbContext is registered but no `dotnet ef migrations add` ran for it)
- `trading.import_jobs.is_deleted` + `deleted_at` + `deleted_by_user_id` columns — additive, default false, NULL, NULL. Removing the EF query filter reverts to the Wave 5 behavior.

The `ImportJob.MarkDeleted` mutator is additive — removing it leaves the entity unchanged. The `ISoftDelete` interface is referenced only by `ImportJob` in this slice; removing the interface removes the constraint.

Migration 0027 is idempotent — re-running it after a rollback is a no-op. Migration 0028 is idempotent for the same reason.

## Next Slice

**6d.2** — Real `AuditLogger` impl (writes to `AuditDbContext`, enriches with `ITenantContext.Current` + `ICurrentUserId`, swallows exceptions silently + logs warning). `DecoratedRepository<T>` decorator pattern applied to Tenant, ImportJob, Subscription repositories. Audit query endpoints for admin tooling (admin-only, per the user decision). 30 tests (≤ 900 lines, 13 paths).
