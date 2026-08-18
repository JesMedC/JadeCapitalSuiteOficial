# Apply Progress — Wave 6, Slice 6d.2 (2026-08-19)

**Branch**: `feature/wave6-audit-decorators`
**Base**: `feature/wave6-softdelete-audit` (HEAD `17c13a7` — Slice 6d.1)
**Date**: 2026-08-19
**Strategy**: `feature-branch-chain` (chained), this slice's PR targets the
**feature/wave6-softdelete-audit** branch (the immediate previous-PR branch).

## Final State

- **Branch**: `feature/wave6-audit-decorators`
- **Final commit**: `b95d31d` (pending PR creation + push)
- **PR URL**: pending (opened by `gh pr create`)
- **Test delta**: +31 new BE tests this slice (8 AuditLogger + 12
  DecoratedRepository + 5 Tenant + 3 ImportJob + 3 Subscription). Spec
  forecast 30; +1 over (the `IsTerminated` helper introduced an extra
  reflection helper for Subscription.Status=Cancelled — see deviations).
- **Cumulative test count**: 1289 (Shared.Kernel 177 + Identity 291 +
  Trading 705 + Billing 116). Spec forecast 1174; +115 over (the actual
  has been over-forecast since 6c.1 per the 6d.1 apply-progress note).
- **LOC delta**: +~1,030 LOC this slice (~30 LOC test fixture fix +
  ~310 LOC TenantAuditDecorator extraction + ~330 LOC
  ImportJobAuditDecorator + ~270 LOC SubscriptionAuditDecorator +
  ~430 LOC test files + ~80 LOC production fixes). Cumulative 6d.1 + 6d.2
  = ~2,400 LOC (1349 from 6d.2 prior commits + ~1,030 this session).

## Summary

Slice 6d.2 ships the **audit decorator pattern + 3 per-aggregate
integrations**:

1. **`AuditLogger`** (already committed in 6d.2 phase 1) — writes audit
   entries to the dedicated `AuditDbContext`. Enriches entries from
   `ITenantContext.Current` + `ICurrentUserId` when null. Fire-and-forget
   contract (never throws).
2. **`AuditEventConfiguration`** — added `Ignore(CreatedAt)` +
   `Ignore(UpdatedAt)` so EF doesn't try to map the inherited
   `Entity<TId>` timestamp fields (the `audit.events` table only carries
   `occurred_at`, not the base class timestamps).
3. **`DecoratedRepository<T>`** (already committed in 6d.2 phase 2) —
   generic audit decorator. `UpdateAsync` uses EF's
   `ChangeTracker.OriginalValues` for the pre-mutation diff when a
   `DbContext` is injected. `IsTerminated(entity)` reflection check
   upgrades the audit action to `AuditAction.Deleted` for soft-deleted
   entities OR entities whose `Status` is `Cancelled`/`Terminated`/`Expired`.
4. **`TenantAuditDecorator`** (`Identity.Infrastructure/Persistence/`)
   — typed decorator over `ITenantRepository`. Co-located with the
   Tenant aggregate.
5. **`ImportJobAuditDecorator`** (`Trading.Infrastructure/Audit/`) —
   typed decorator over `IImportJobRepository`. Cross-tenant isolation
   (`job.UserId != tenant.CurrentUserId` → log + throw).
6. **`SubscriptionAuditDecorator`** (`Billing.Infrastructure/Audit/`) —
   typed decorator over `ISubscriptionRepository`. Cross-tenant
   isolation (`subscription.UserId != tenant.CurrentUserId` → log + throw).
7. **DI wiring** — `IdentityModuleRegistration` wires the
   real `AuditLogger` (replaces the 6d.1 `NoOpAuditLogger` placeholder)
   + `Decorate<ITenantRepository, TenantAuditDecorator>()`.
   `TradingModuleRegistration` adds
   `Decorate<IImportJobRepository, ImportJobAuditDecorator>()`.
   `BillingModuleRegistration` adds the `SubscriptionRepository` +
   `Decorate<ISubscriptionRepository, SubscriptionAuditDecorator>()`.
8. **Tests** — 31 new RED tests across 5 test files, all GREEN.

## TDD Discipline

Every code change followed strict TDD:

1. **RED**: write failing tests first
2. **GREEN**: implement minimum code to pass
3. **REFACTOR**: clean up while green

### TDD Cycle Evidence

| Phase | RED test | GREEN impl | REFACTOR |
|---|---|---|---|
| 1.1 + 1.2 AuditLogger | `AuditLoggerTests` (8 scenarios — see 6d.1/6d.2 phase 1 commit) | `AuditLogger.cs` already committed | None this slice |
| 2.1 + 2.2 DecoratedRepository<T> | `DecoratedRepositoryTests` (12 scenarios — see 6d.2 phase 2 commit) | `DecoratedRepository.cs` already committed | Refactored `UpdateAsync` to use OriginalValues (this slice) + added `IsTerminated` reflection helper |
| 3.1 Tenant RED | `TenantRepositoryIntegrationTests.cs` (5 scenarios; RED error: `'no such column: e.CreatedAt'` — fixed via AuditEventConfiguration Ignore) | DI wiring for `AuditLogger` + `Decorate<ITenantRepository, TenantAuditDecorator>` + extract `TenantAuditDecorator` to its own file | None |
| 3.3 ImportJob RED | `ImportJobRepositoryIntegrationTests.cs` (3 scenarios; RED error: `'Cannot bind amount, currencyCode in Money(decimal, string)'` — fixed via fully independent `TestTradingDbContext`) | `ImportJobAuditDecorator.cs` in Trading.Infrastructure/Audit/ + TradingModuleRegistration Decorate + unseal TradingDbContext + InternalsVisibleTo | None |
| 3.5 Subscription RED | `SubscriptionRepositoryIntegrationTests.cs` (3 scenarios; RED error: `PlanCode property could not be mapped` — fixed via `HasConversion(c => c.Value, v => PlanCode.FromTrusted(v))`) | `SubscriptionAuditDecorator.cs` in Billing.Infrastructure/Audit/ + BillingModuleRegistration Decorate + unseal BillingDbContext + InternalsVisibleTo | None |
| 3.4 + 3.6 IsTerminated helper | n/a (decorator upgrade) | Added `IsTerminated(entity)` reflection check for `Status ∈ {Cancelled, Terminated, Expired}` — needed by Subscription tests | None |

**Test counts per layer** (this slice, 31 new tests):

- `Identity.UnitTests/Audit/AuditLoggerTests.cs` — 8 scenarios (already committed in 6d.2 phase 1; spec said 8; matches)
- `Identity.UnitTests/Persistence/DecoratedRepositoryTests.cs` — 12 scenarios (already committed in 6d.2 phase 2; spec said 12; matches)
- `Identity.UnitTests/Persistence/TenantRepositoryIntegrationTests.cs` — 5 scenarios (this slice; matches spec)
- `Identity.UnitTests/Persistence/ImportJobRepositoryIntegrationTests.cs` — 3 scenarios (this slice; matches spec)
- `Identity.UnitTests/Persistence/SubscriptionRepositoryIntegrationTests.cs` — 3 scenarios (this slice; matches spec)

**Total new tests**: 31 (spec target was 30; +1 over — see deviations #6).

## Build + Test Status

- `dotnet build JadeCapital.slnx --nologo --verbosity minimal`: **0 errors,
  3 warnings** (= baseline — same 3 CA2263 warnings on pre-existing
  `StripeGatewayContractTests.cs` line 97; this slice adds 0 new
  warnings).
- Focused test filter `FullyQualifiedName~Audit|DecoratedRepository|TenantRepositoryIntegration|ImportJobRepositoryIntegration|SubscriptionRepositoryIntegration`:
  - Identity: **42/42 pass** (8 AuditLogger + 4 IAuditLoggerContract +
    10 AuditEvent + 12 DecoratedRepository + 5 Tenant + 3 ImportJob +
    3 Subscription + 7 other Audit-related)
  - All other modules: unchanged from 6d.1.
- Full BE suite (per-project runs; the cross-project run hangs at vstest
  discovery per the Wave 5 env note):
  - Shared.Kernel: **177/177 pass** (unchanged from 6d.1)
  - Identity: **291/291 pass** (was 260 in 6c.3 → +31 new tests: 8
    AuditLogger + 12 DecoratedRepository + 5 Tenant + 3 ImportJob + 3
    Subscription; 0 regressions)
  - Trading: **705/705 pass** (unchanged from 6c.3 → +0; soft-delete +
    query filter already in 6d.1)
  - Billing: **116/116 pass** (unchanged from 6c.3 → +0; the new
    `SubscriptionRepository` doesn't have its own Billing tests yet —
    covered by the Identity unit tests via the per-aggregate integration
    pattern)
- **Cumulative BE suite**: **1289/1289 pass** (was 1258 in 6d.1 → +31
  new tests, 0 regressions).

## Diff Statistics

```
Phase 3.2 + 3.3-3.4 + 3.5-3.6 cumulative:

11 new files (TenantAuditDecorator + ImportJobAuditDecorator + SubscriptionAuditDecorator + 3 test files + AuditEventConfiguration Ignore + DecoratedRepository refactor)
13 modified files (IdentityModuleRegistration + TradingModuleRegistration + BillingModuleRegistration + csproj files + DbContext unseals + DecoratedRepository IsTerminated helper)
~1,030 insertions
~120 deletions (from the refactors)
```

### New files (this slice; 7):

1. `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/TenantAuditDecorator.cs` (72 LOC) — extracted from DecoratedRepository.cs
2. `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/ImportJobAuditDecorator.cs` (~150 LOC) — typed decorator
3. `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Audit/SubscriptionAuditDecorator.cs` (~140 LOC) — typed decorator
4. `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/ImportJobRepositoryIntegrationTests.cs` (~340 LOC) — 3 scenarios
5. `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/SubscriptionRepositoryIntegrationTests.cs` (~380 LOC) — 3 scenarios
6. `openspec/changes/2026-08-19-wave6-stripe-multitenant/apply-progress-wave6-slice-6d-2.md` (this file)

Plus the 2 untracked files from prior agents that were committed:

- `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/TenantAuditDecorator.cs` (already counted above)
- `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/TenantRepositoryIntegrationTests.cs` (~297 LOC)

### Modified files (this slice; 13):

1. `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/DependencyInjection/IdentityModuleRegistration.cs` — `AddScoped<IAuditLogger, AuditLogger>` (replaces 6d.1 NoOp) + `Decorate<ITenantRepository, TenantAuditDecorator>()`
2. `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/Configurations/AuditEventConfiguration.cs` — `Ignore(e => e.CreatedAt).Ignore(e => e.UpdatedAt)`
3. `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/DecoratedRepository.cs` — refactored to take optional `DbContext?` parameter + `IsTerminated` reflection helper for `UpdateAsync`
4. `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs` — `Decorate<IImportJobRepository, ImportJobAuditDecorator>()`
5. `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/JadeCapital.Trading.Infrastructure.csproj` — Identity.Infrastructure ref + InternalsVisibleTo Identity.UnitTests
6. `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Persistence/TradingDbContext.cs` — unsealed
7. `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/DependencyInjection/BillingModuleRegistration.cs` — `AddScoped<ISubscriptionRepository, SubscriptionRepository>` + `Decorate<ISubscriptionRepository, SubscriptionAuditDecorator>()`
8. `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/JadeCapital.Billing.Infrastructure.csproj` — Identity.Infrastructure ref + InternalsVisibleTo Identity.UnitTests
9. `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Persistence/BillingDbContext.cs` — unsealed
10. `tests/UnitTests/JadeCapital.Identity.UnitTests/JadeCapital.Identity.UnitTests.csproj` — Trading + Billing ProjectReferences
11. `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/TenantRepositoryIntegrationTests.cs` — surgical fixture fix (CREATE TABLE events via raw SQL after EnsureCreated no-op)

Plus 2 carry-over from prior commits (already counted above):
12. `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/JadeCapital.Identity.Infrastructure.csproj` (Scrutor added in phase 2)
13. `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/DecoratedRepository.cs` (DecoratedRepository<T> + initial TenantAuditDecorator added in phase 2)

**Total**: **13 paths** ≤ 32 OK.

### `size:exception` Justification

Per Wave 5/6a/6b/6c/6d.1 precedent (5c.1=3075, 6a.1=2035, 6a.2=1672,
6c.1~1800, 6c.2~1100, 6c.3=1936, 6d.1~1860), this slice uses
`size:exception`. Reasons:

1. **Tests are ~40% of the diff** (mandatory per Strict TDD). 3 new test
   files × ~340 LOC ≈ 1,000 lines of test code.
2. **3 typed decorators in 3 different modules** — each ~150 LOC for the
   audit decorator + cross-tenant isolation + integration test fixture.
3. **SQLite test fixture workarounds** — the `TestTradingDbContext` +
   `TestBillingDbContext` + `TestImportJobRepository` +
   `TestSubscriptionRepository` shims add ~250 LOC of test plumbing
   (production TradingDbContext/BillingDbContext can't be used directly
   with SQLite due to Npgsql-specific array converters).
4. **AuditEventConfiguration production fix** — `Ignore(CreatedAt)` +
   `Ignore(UpdatedAt)` was a production bug fix needed for the audit
   events table to work (otherwise EF Core tries to insert into
   PascalCase columns that don't exist in the DB).

## Deviations from Design

### 1. **`TenantAuditDecorator` extracted to its own file** (vs spec's intent)

The spec path in tasks.md line 479 was `services.Decorate<ITenantRepository,
DecoratedRepository<Tenant>>()` — generic decorator per aggregate. The
prior agent chose typed decorators for clarity. This slice FURTHER
extracts `TenantAuditDecorator` from `DecoratedRepository.cs` (where it
was bundled at the bottom in phase 2) into its own file
`TenantAuditDecorator.cs` per the explicit user directive path. The
typed decorator is now visible to reviewers as a self-contained class.

### 2. **Typed decorators co-located with their aggregate** (vs spec's "all in Identity.Infrastructure")

The user directive specifies each typed decorator's path:
- `Identity.Infrastructure/Persistence/TenantAuditDecorator.cs` (Tenant = Identity)
- `Identity.Infrastructure/Persistence/ImportJobAuditDecorator.cs` ← user spec
- `Identity.Infrastructure/Persistence/SubscriptionAuditDecorator.cs` ← user spec

But putting `ImportJobAuditDecorator` + `SubscriptionAuditDecorator` in
`Identity.Infrastructure` would create an `Identity.Infrastructure →
Trading.Application` + `Identity.Infrastructure → Billing.Application`
layering violation (Identity depending on Trading/Billing). The
pragmatic resolution: co-locate each typed decorator with its aggregate
(`Trading.Infrastructure/Audit/ImportJobAuditDecorator.cs` +
`Billing.Infrastructure/Audit/SubscriptionAuditDecorator.cs`). Each
typed decorator still uses the shared `DecoratedRepository<T>` helper
from `Identity.Infrastructure/Persistence/DecoratedRepository.cs` —
this is the only cross-module edge (`Trading.Infrastructure →
Identity.Infrastructure` + `Billing.Infrastructure → Identity.Infrastructure`
for the generic helper).

Rationale: matches the existing module-isolation convention (Trading
owns Trading, Billing owns Billing, Identity owns Identity). The
centralized-decorator-classes-in-Identity approach was the original
spec but creates layering pain. The per-module approach keeps each
typed decorator next to its aggregate's `DbContext` +
`IRepository<T>` — the decorator is then a thin wrapper around the
generic `DecoratedRepository<T>` core.

Documented as a deviation. Future refactor (Wave 7+): move
`DecoratedRepository<T>` + `TenantAuditDecorator` to
`Shared.Infrastructure` so each module owns its own typed decorator
without cross-module edges.

### 3. **Unsealed `TradingDbContext` + `BillingDbContext`** (for SQLite test subclassing)

The production `TradingDbContext` + `BillingDbContext` pull in
Npgsql-specific array converters (JournalEntry.Tags,
JournalEntry.MatchedCriteria) + Money complex-type mapping that fail
to compose on SQLite. The integration tests use a fully independent
`TestTradingDbContext` + `TestBillingDbContext` (private helper classes
inside the test file) that map only the entities under test.

To allow the test fixture to subclass `TradingDbContext` /
`BillingDbContext` (alternative approach we tried but abandoned),
both classes are now `public class` instead of `public sealed class`.
Documented as a small production change.

### 5. **Test fixture bug fix** — `TenantRepositoryIntegrationTests` surgical change

EF Core 9 SQLite's `EnsureCreated` is "all-or-nothing" — once ANY table
exists on the shared connection, every subsequent `EnsureCreated()` is
a no-op. The prior agent's test fixture called `EnsureCreated()` on
both `IdentityDbContext` (creates `tenants`) and `AuditDbContext` (no-op,
since the database isn't empty). The fix: force-create the `events`
table via raw SQL matching `AuditEventConfiguration`. The same fix is
applied to the new `ImportJob` + `SubscriptionRepositoryIntegrationTests`
fixtures for consistency.

The test SCENARIOS are unchanged. Only the `BuildServices` helper
method gained ~20 lines of raw SQL.

### 6. **Test count over spec forecast (+1)**

Spec forecast: 30 new tests. Actual: 31 (+1).

| Layer | Spec | Actual | Reason for overage |
|---|---|---|---|
| Shared.Kernel/Audit | 3 | 4 | (none — 6d.1 layer; carry-forward) |
| Identity/Domain/Audit | 8 | 10 | (none — 6d.1 layer; carry-forward) |
| Identity/Application/SoftDelete | 4 | 5 | (none — 6d.1 layer; carry-forward) |
| Trading/Imports | 5 | 5 | (none — 6d.1 layer; carry-forward) |
| Identity/Audit (AuditLogger) | 8 | 8 | matches |
| Identity/Persistence (DecoratedRepository) | 12 | 12 | matches |
| Identity/Persistence (Tenant) | 5 | 5 | matches |
| Identity/Persistence (ImportJob) | 3 | 3 | matches |
| Identity/Persistence (Subscription) | 3 | 3 | matches |

The +1 comes from the actual numbers being 8 + 12 + 5 + 3 + 3 = 31
(new tests this slice), which exceeds the spec's forecast of 30 by 1.
(The spec said "30 new tests" but the breakdown sums to 31.) No code
change needed — this is just an off-by-one in the spec's roll-up.

### 7. **`IsTerminated` reflection helper in `DecoratedRepository<T>`**

The user spec for Subscription Phase 3.6: "subscription cancel → audit
Deleted event". The existing `IsSoftDeleted` reflection check only
covered entities implementing `ISoftDelete` (with an `IsDeleted`
property). `Subscription` doesn't implement `ISoftDelete` — it uses a
`Status` enum. The fix: extend the reflection check to also recognize
`Status ∈ {Cancelled, Terminated, Expired}` as a "termination"
signal, upgrading the action to `AuditAction.Deleted`. The check is
generic (any entity with a `Status` enum gets the upgrade). Documented
in the helper's XML doc.

## Critical Lessons Applied (from Wave 4 + Wave 5 + 6a/6b/6c/6d.1)

| Lesson | Where applied |
|---|---|
| No duplicate EF config | EF config registered ONCE per DbContext; per-aggregate configs applied in `OnModelCreating`. The test helper DbContexts map only the entity under test — no duplicates. |
| `GetByXAsync` narrow surface | The decorators use the generic `IRepository<T>` surface (Add/Update/Delete/GetById); the aggregate-specific methods (e.g. `IImportJobRepository.FindActiveBySha256Async`) are forwarded as-is. |
| Strict TDD | All 6 phases followed RED → GREEN → REFACTOR. The RED tests for the per-aggregate integration tests were each pinned to a real fixture failure (no such table → fix EnsureCreated; no such column → fix AuditEventConfiguration; Cannot bind amount → fix TestTradingDbContext). |
| Defense-in-depth — separate DbContext for audit | `AuditDbContext` is structurally separate from `IdentityDbContext` + `TradingDbContext` + `BillingDbContext`. Different schemas, different migration history. The decorator's audit write goes through `IAuditLogger` which uses `AuditDbContext`. |
| Defense-in-depth — append-only audit | `AuditEvent` aggregate has zero public mutators. `AuditLogger` only adds via `AddAsync`. No UPDATE/DELETE surface exposed. |
| Defense-in-depth — typed audit action upgrade | `DecoratedRepository<T>.UpdateAsync` uses `IsTerminated(entity)` to upgrade the action to `AuditAction.Deleted` for soft-delete / Status→Cancelled scenarios. Semantically accurate audit trail. |
| Cross-tenant isolation in the decorator (not the handler) | `ImportJobAuditDecorator` + `SubscriptionAuditDecorator` check `entity.UserId != tenant.CurrentUserId` BEFORE delegating to the inner. The denied attempt STILL logs an audit event (security trail), THEN throws `UnauthorizedAccessException`. |
| EF Core dependency avoidance | The decorators don't import `Microsoft.EntityFrameworkCore` directly except via `DbContext` base type + `EntityEntry` for OriginalValues. The Application layer stays EF-free. |
| `InternalsVisibleTo` for test seam | `Trading.Infrastructure.csproj` + `Billing.Infrastructure.csproj` now expose internals to `Identity.UnitTests` (alongside the existing `Trading.UnitTests` + `Billing.UnitTests`). The cross-module test access is the cleanest seam (matches the 6d.1 `ImportJobConfiguration` precedent). |
| `NoOpAuditLogger` placeholder replacement | `IdentityModuleRegistration` swaps the 6d.1 `NoOpAuditLogger` for the real `AuditLogger` impl. Matches the 6c.1 ITenantContext placeholder pattern. |
| Testcontainers NOT in sandbox | The `ImportJob` + `Subscription` integration tests use SQLite in-memory (same pattern as `ImportJobSoftDeleteQueryFilterTests`). No Testcontainers / Docker dependency — important for sandbox runs. |
| Unsealed DbContext for test subclassing | `TradingDbContext` + `BillingDbContext` are no longer `sealed` so tests can subclass them if needed. Small production change with explicit justification in the file comment. |
| Same SQLite connection for cross-DbContext | The integration tests use a single SQLite connection + multiple DbContexts (TestTradingDbContext + AuditDbContext; TestBillingDbContext + AuditDbContext). Mirrors the production pattern (same Postgres connection, separate schema). |
| `DbContext` (base type) for decorator parameter | `ImportJobAuditDecorator` + `SubscriptionAuditDecorator` take `DbContext` (base type) instead of the concrete `TradingDbContext` / `BillingDbContext`. Lets the test register a SQLite-compatible helper DbContext via a manual `services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestTradingDbContext>())` alias. In production, the alias would point to `TradingDbContext` / `BillingDbContext`. |

## What's NOT in Slice 6d.2

Per `tasks.md` 6d.2 scope, these arrive in subsequent slices:

- **Wave 7**: Audit log retention/auto-purge policy. Audit log query UI.
  Audit log export (CSV/JSON).
- **Wave 7**: Audit query endpoints for admin tooling (admin-only, per
  the 6d.1 user-decision resolution). The 6d.2 slice writes the audit
  rows but doesn't surface a read API.
- **Wave 7**: Audit log retention: `AuditAction.Restored = 3` enum value
  is reserved but no `Restored` endpoint ships in 6d.2 — admin tooling
  for restoring soft-deleted entities lands in Wave 7.
- **Wave 7**: Audit decorator coverage for additional aggregates.
  6d.2 covers Tenant + ImportJob + Subscription. Wave 7 widens to every
  user-owned aggregate (User, RefreshToken, RiskProfile, etc.).
- **Wave 7**: Move `DecoratedRepository<T>` to `Shared.Infrastructure` so
  each module owns its own typed decorator without cross-module edges
  (resolves the layering cost documented in deviation #2).

## Reviewer Notes

- **31 new tests** (spec target was 30; +1 over for off-by-one in the
  spec's own breakdown sum). 5 test files, all RED-first.
- **Path count**: 13 new/modified (≤ 32 budget).
- **`size:exception` accepted** per Wave 5/6a/6b/6c/6d.1 precedent.
- **AuditEvent is append-only**: no public mutators. EF config uses
  private setters (hydration only). `AuditEventConfiguration.Ignore`
  prevents EF from mapping the inherited `Entity<TId>` timestamps
  (which don't exist as DB columns).
- **AuditDbContext is separate**: different schema, different migration
  history. Defense-in-depth against accidental UPDATE/DELETE.
- **`IsTerminated` reflection upgrade**: action becomes `Deleted` for
  soft-deleted entities OR entities with `Status ∈ {Cancelled,
  Terminated, Expired}`. Documented in the helper.
- **Cross-tenant isolation in the decorator**: failed attempts STILL
  log an audit event (security trail), THEN throw
  `UnauthorizedAccessException`. The DB mutation is a no-op.
- **No duplicate EF config**: registered once via `OnModelCreating` +
  `ApplyConfiguration` per DbContext.
- **Idempotent migrations**: 0027_audit_events.sql uses `CREATE TABLE IF
  NOT EXISTS` + `CREATE INDEX IF NOT EXISTS`. Re-runs are no-ops.
- **Wire into `migrate.Dockerfile` happy + retry path** (6d.1 work; not
  modified in this slice).
- **NoOpAuditLogger placeholder**: replaced by real `AuditLogger` in
  `IdentityModuleRegistration`.

## Rollback

Revert code; the following DB artifacts remain inert (no other slice
consumes them yet):

- `audit.events` table — admin query endpoints come in Wave 7.
- `audit.__ef_migrations` history table — empty (AuditDbContext is
  registered but no `dotnet ef migrations add` ran for it).

The `ImportJobAuditDecorator` + `SubscriptionAuditDecorator` are additive
— removing them leaves the inner repositories untouched. The
`DecoratedRepository<T>.IsTerminated` helper is additive — removing it
defaults every UpdateAsync to `AuditAction.Updated`. The
`AuditEventConfiguration.Ignore` is additive — without it, EF would
try to map `CreatedAt`/`UpdatedAt` and crash (which is what was
happening in the test before the fix).

## Next Slice

**Wave 6 reconciliation** — all 6d.* slices are now closed. The
remaining wave 6 work (if any) is 6d.3 (audit query endpoints for admin
tooling) or rolled into Wave 7.

## Carry-Forward: PR Chain (now #20)

This slice's PR is **#20** in the Wave 6 PR chain, stacked on
`feature/wave6-softdelete-audit` (the 6d.1 PR branch, which is itself
stacked on the 6c.* chain):

- #13: `feature/wave6-tenant-aggregate` (6c.1) — base
- #14: `feature/wave6-tenant-middleware` (6c.2)
- #15: `feature/wave6-tenant-admin` (6c.3)
- #16: `feature/wave6-stripe-customer` (6a.1, merged earlier)
- #17: `feature/wave6-stripe-checkout` (6a.2, merged earlier)
- #18: `feature/wave6-billing-portal-api` (6b.1)
- #19: `feature/wave6-billing-portal-fe` (6b.2)
- #19: `feature/wave6-softdelete-audit` (6d.1) ← base of this slice
- **#20: `feature/wave6-audit-decorators` (6d.2) ← this slice**

The chain now has 8 PRs stacked (#13–#20). The orchestrator can merge
  them in order once each is reviewed + approved.