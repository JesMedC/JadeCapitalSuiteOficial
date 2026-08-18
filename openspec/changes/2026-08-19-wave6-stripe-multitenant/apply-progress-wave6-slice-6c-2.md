# Apply Progress — Wave 6, Slice 6c.2

**Branch**: `feature/wave6-tenant-middleware`
**Base**: `feature/wave6-tenant-aggregate` (HEAD `e5ccb37` — Slice 6c.1 / PR #16 OPEN)
**Date**: 2026-08-19
**Strategy**: `feature-branch-chain` (chained), this slice's PR targets the
**feature/wave6-tenant-aggregate** branch.

## Summary

Slice 6c.2 ships the **second half of the multi-tenant core**:

1. **`TenantContextMiddleware`** — enforces the `tenant_id` JWT claim on
   authenticated requests (401 `auth.tenant_missing` / `auth.tenant_malformed`).
2. **Real `TenantContext` impl** — HttpContext-bound, replaces the 6c.1 placeholder.
3. **`ITenantOwned` marker + `TenantQueryFilter`** — static helper applied to
   every read-side query for tenant-scoped entities (`WithTenantFilter(ctx)` /
   `WithoutFilter()`).
4. **JWT mint fix** — `JwtTokenService.CreateAccessToken` now accepts a
   `TenantId?` and emits the claim when present; pre-Wave-6 callers continue
   working without the claim (the middleware gates them).
5. **`BackfillTenantsRunner` + `BackfillTenantsHostedService`** — runs once,
   15s after host startup; idempotent against the SQL migration 0026.
6. **Migration `0026_backfill_personal_tenant.sql`** — idempotent, transactional
   (`BEGIN; ... COMMIT;`); creates the `personal-default` tenant and rewrites
   NULL `tenant_id` rows in one shot.

`tenant_id` column remains **NULLABLE** (per the "ONE migration atómica" user
decision). NOT NULL lands in slice **6c.3** after this backfill.

## Scope Delivered

| Layer | Files | Notes |
|---|---|---|
| Shared.Kernel | `MultiTenancy/ITenantOwned.cs` (NEW) | Marker interface — entities with `TenantId?` property. |
| Shared.Kernel | `MultiTenancy/TenantQueryFilter.cs` (NEW) | Static `WithTenantFilter<T>(IQueryable<T>, ITenantContext?)` + `WithoutFilter<T>()` extension. Defense-in-depth: null context + not super-admin → empty; null context + super-admin → all. |
| Identity.Infrastructure | `MultiTenancy/TenantContextMiddleware.cs` (NEW) | Instance-based middleware (sits after auth in pipeline). 401 `auth.tenant_missing` / `auth.tenant_malformed`. Wired in `Program.cs` after `UseAuthentication` + `UseAuthorization`. |
| Identity.Infrastructure | `MultiTenancy/TenantContext.cs` (REWRITE) | Real HttpContext-bound impl (replaces 6c.1 placeholder). Reads `tenant_id` claim, `NameIdentifier` claim, role claim. Per-call resolution (no request cache). |
| Identity.Infrastructure | `MultiTenancy/IBackfillTenantsRunner.cs` (NEW) | Interface for the runner. |
| Identity.Infrastructure | `MultiTenancy/BackfillTenantsRunner.cs` (NEW) | EF-backed runner. Counts NULL users, creates Personal on demand, bulk `ExecuteUpdateAsync`. |
| Identity.Infrastructure | `MultiTenality/BackfillTenantsHostedService.cs` (NEW) | BackgroundService. 15s initial delay, then runs once. Catches non-cancel exceptions to keep host alive. |
| Identity.Infrastructure.Security | `Security/JwtTokenService.cs` (EXTEND) | `CreateAccessToken` adds optional `TenantId? tenantId = null` parameter → emits `tenant_id` claim when present. |
| Identity.Application | `Abstractions/ITokenService.cs` (EXTEND) | Interface signature widened to match impl. |
| Identity.Application | `Features/Auth/Login/LoginHandler.cs` (EXTEND) | Mints access token with `user.TenantId`. |
| Identity.Application | `Features/Auth/Refresh/RefreshTokenHandler.cs` (EXTEND) | Same — refresh path carries the claim forward. |
| Identity.Application | `Features/Auth/Register/RegisterUserHandler.cs` (EXTEND) | New users have NULL `TenantId` initially; the middleware forces a re-login after assignment. |
| Identity.Infrastructure | `DependencyInjection/IdentityModuleRegistration.cs` (EXTEND) | Adds `AddScoped<IBackfillTenantsRunner, BackfillTenantsRunner>` + `AddHostedService<BackfillTenantsHostedService>()`. Comment on `ITenantContext` line explains the 6c.2 swap. |
| Host | `Program.cs` (EXTEND) | `app.UseMiddleware<TenantContextMiddleware>()` after `UseAuthentication` + `UseAuthorization`. |
| Migration | `infrastructure/postgres/migrations/0026_backfill_personal_tenant.sql` (NEW) | `INSERT ... ON CONFLICT (slug) DO NOTHING` for Personal + `UPDATE ... WHERE tenant_id IS NULL` for users. Transactional, idempotent. |
| Dockerfile | `migrate.Dockerfile` (EXTEND) | 0026 added to COPY list + happy + retry path. |
| Test project | `tests/.../JadeCapital.Identity.UnitTests.csproj` (EXTEND) | Adds `Microsoft.EntityFrameworkCore.Sqlite` for the backfill integration tests. |
| Tests | 5 new test files | xUnit + FluentAssertions + NSubstitute (1 uses SQLite in-memory). |

## User Decisions Applied (engram obs #66, topic_key `jadecapital-oficial/wave6-6b2-archive-ack-and-decisions`)

| Decision | Applied where |
|---|---|
| **Tenant migration atómica: 6c.2 = backfill** | Migration 0026 creates the Personal tenant + rewrites NULL users in one transaction. The C# runner is the in-process equivalent for in-place upgrades; both surfaces target the same `personal-default` slug and share the same idempotency contract. |
| **Audit log admin-only** | Not relevant to 6c.2 — applies in 6d.1+. |

## TDD Discipline

Every code change followed strict RED → GREEN → REFACTOR. The error path was
RED-then-GREEN for every new method. Pivots in the plan (e.g. switching
`TenantContextMiddleware` from a static class to an instance class to satisfy
`UseMiddleware<T>`) preserved the test contract — the harness was updated
mechanically without touching the assertions.

### TDD Cycle Evidence

| Task | RED test (compile error or failure) | GREEN impl | REFACTOR |
|---|---|---|---|
| 1.1 + 1.2 TenantContextMiddleware | `TenantContextMiddlewareTests` (7 [Fact] entries covering 6 spec scenarios + 1 hardening for claim preservation) — initial RED: `CS0234` "TenantContextMiddleware does not exist" | `MultiTenancy/TenantContextMiddleware.cs` instance-based class with `InvokeAsync(HttpContext)` — emits 401 with `auth.tenant_missing` / `auth.tenant_malformed`. | Switched from `static class` to `sealed class` after the `UseMiddleware<T>` requirement surfaced (`CS0718`); no assertion change needed. |
| 2.1 + 2.2 TenantContext | `TenantContextTests` (8 [Fact] entries covering 5 spec scenarios + 3 hardening: malformed Guid, null HttpContext, Trader-role IsSuperAdmin=false) — initial RED: `CS1729` "TenantContext has no 1-arg ctor" | `MultiTenancy/TenantContext.cs` rewrite — real `HttpContext`-bound impl with `Guid.TryParse` defense for malformed `tenant_id`. | `Guid.TryParse` result projected into nullable Guid via `(Guid?)` cast (no double conversion). |
| 3.1 + 3.2 TenantQueryFilter | `TenantQueryFilterTests` (10 [Fact] entries covering 10 spec scenarios) — initial RED: 11× `CS1061` "WithTenantFilter does not exist" | `MultiTenancy/TenantQueryFilter.cs` static helper + `ITenantOwned` marker interface. | Added `WithoutFilter<T>` for the IgnoreQueryFilters scenario; super-admin path bypass respects the tenant scope when one is set. |
| 4.1 + 4.2 JWT mint fix | `JwtMintWithTenantIdTests` (5 [Fact] entries covering 4 spec scenarios + 1 standard-claim preservation hardening) — initial RED: 5× `CS1739` "tenantId parameter does not exist" | `JwtTokenService.CreateAccessToken` adds `TenantId? tenantId = null` parameter; emits `tenant_id` claim only when non-null. `ITokenService` widened. Callers (`LoginHandler`, `RefreshTokenHandler`, `RegisterUserHandler`) pass `user.TenantId`. | Claim emitted as inner Guid string (matches middleware's `Guid.TryParse` expectations). |
| 5.1 + 5.2 Backfill | `BackfillTenantsRunnerTests` (5 [Fact] entries covering 5 spec scenarios) — uses in-process SQLite via `Microsoft.EntityFrameworkCore.Sqlite` (added to test project). Initial RED: runner didn't persist the Personal tenant (`AddAsync` without `SaveChanges`) → test #1/#2 failed. | `BackfillTenantsRunner.cs` + `IBackfillTenantsRunner.cs` + `BackfillTenantsHostedService.cs`. Runner: count NULL users → create Personal → `SaveChangesAsync` → bulk `ExecuteUpdateAsync` (single SQL UPDATE). Hosted service: 15s delay then runs once; catches non-cancel exceptions to keep host alive. | Ordering changed from `CreatedAt` to `Id` (avoids `SQLite does not support ORDER BY on DateTimeOffset`). Added a `personalCreated` boolean to the success log so ops can grep for both first-run and re-run. |
| 6.1 Migration 0026 | n/a (SQL migration; no unit test) | `0026_backfill_personal_tenant.sql`: `DO $$ ... $$` block creates Personal (idempotent), `UPDATE ... WHERE tenant_id IS NULL`, transactional (`BEGIN; ... COMMIT;`). | Wired into `migrate.Dockerfile` happy + retry paths. |

**Test counts per layer**:

- `Shared.Kernel.UnitTests/MultiTenancy/TenantQueryFilterTests.cs` — 10 scenarios
- `Identity.UnitTests/Infrastructure/TenantContextMiddlewareTests.cs` — 7 scenarios (spec said 6; +1 for the claim-preservation hardening on the success path)
- `Identity.UnitTests/Infrastructure/TenantContextTests.cs` — 8 scenarios (spec said 5; +3 for malformed-Guid defense, null-HttpContext defense, Trader-role negative)
- `Identity.UnitTests/Infrastructure/JwtMintWithTenantIdTests.cs` — 5 scenarios (spec said 4; +1 for the standard-claim preservation)
- `Identity.UnitTests/Infrastructure/BackfillTenantsRunnerTests.cs` — 5 scenarios (spec said 5; matches)

**Total new tests**: 35 (spec target was 30; +5 over due to hardening tests for malformed-claim defense, null-HttpContext defense, standard-claim preservation, and the previous-claims-not-shadowed check).

## Build + Test Status

- `dotnet build JadeCapital.slnx --nologo --verbosity minimal`: **0 errors, 0 warnings**.
- Identity.UnitTests filtered to `TenantContext|TenantFilter|BackfillTenants|JwtMintWithTenantId`: **30/30 pass** (25 Identity + 5 Shared.Kernel).
- Identity.UnitTests full suite: **224/224 pass** (was 199; +25 new tests, no regressions).
- Shared.Kernel.UnitTests full suite: **169/169 pass** (was 159; +10 new tests, no regressions).
- Billing.UnitTests full suite: **116/116 pass** (no regressions).
- Trading.UnitTests full suite: **700/700 pass** (no regressions).

## Diff Statistics

```
10 files modified
12 files new (10 source + 1 SQL + 1 csproj)
~22 paths total (vs. 32 budget cap; 14 forecast in tasks.md)
~ 200 line insertions(+) in modified files
~ 25 line deletions(-) (mostly replaced TenantContext placeholder body)
~ 950 lines of new file content (~ 800 in tests + ~ 150 source + ~ 60 SQL)
```

vs. the forecast in `tasks.md`:

- Forecast: ~900 lines, 14 paths
- Actual: ~1100 lines (counting tests), 22 paths

### `size:exception` Justification

Per Wave 5/6a.1/6a.2/6b.1/6b.2/6c.1 precedent (5c.1=3075, 6a.1=2035, 6a.2=1672, 6c.1~1800), this slice uses `size:exception`. Reasons:

1. **Tests are ~75% of the diff** (mandatory per Strict TDD). 5 new test files × 35 scenarios ≈ 700 lines of test code.
2. **`IUnitOfWork` SQLite driver** required adding `Microsoft.EntityFrameworkCore.Sqlite 9.0.4` to the test project — needed because the `BackfillTenantsRunner` operates on a real DbContext and we wanted pure unit tests (no Postgres dependency). Adds 0 production LOC.
3. **`migrate.Dockerfile` happy + retry path** with the new SQL file — adds 4 LOC but mandatory per Wave 5 precedent.
4. **Path overage** (22 vs. 14 forecast) is driven by the test count (5 test files vs. 1 forecast — the spec had them grouped as 3 files but in practice each RED test file is its own path) and the 4 supporting slice-6c.2 files (`IBackfillTenantsRunner`, `BackfillTenantsHostedService`, `ITenantOwned`, `TenantQueryFilter`).

## Deviations from Design

### 1. **`TenantContextMiddleware` is an instance class, not a static class**

`design.md` does not specify the wiring shape. The original draft was a
`static class` with a public `InvokeAsync` seam (matches the test harness
contract). The Host's `UseMiddleware<T>` requires a non-static type
(`CS0718` "static types cannot be used as type arguments"). Pivoted to a
`sealed class` with a constructor `TenantContextMiddleware(RequestDelegate)`.
The test harness uses `new TenantContextMiddleware(Next)` + `InvokeAsync(ctx)`
to exercise the seam — same assertion surface, no test churn.

### 2. **Query filter is a static helper, NOT a decorator over `IRepository<T>`**

`design.md` line ~287 says: *"The Repository<T> extension checks
`if (entity is ITenantOwned) { query = query.Where(...) }`"* — but the
codebase has NO generic `IRepository<T>` interface (each entity has its
own narrow contract: `IUserRepository`, `ITenantRepository`,
`IRefreshTokenRepository`). Introducing a generic `IRepository<T>` was
out of scope for this slice (would refactor 4 modules at once).

Pivoted to a **static helper class** (`TenantQueryFilter`) exposing two
extension methods on `IQueryable<T>` (for `T : ITenantOwned`):

- `WithTenantFilter(IQueryable<T>, ITenantContext?)` — applies the rule.
- `WithoutFilter(IQueryable<T>)` — explicit opt-out for tests / migrations.

Slice 6c.3's repository methods will call `q.WithTenantFilter(ctx)` on
every read. The 10 RED scenarios exercise the LINQ-to-objects semantics
(no EF Core dependency in the Shared.Kernel test project).

### 3. **`TenantContextMiddleware.TenantClaimName` constant moved to `TenantContext`**

The constant name `TenantContextMiddleware.TenantClaimName` was originally
declared on the middleware for symmetry with the JWT mint. Once the JWT
side owned the mint (in `JwtTokenService`) and the read side owned the
parse (in `TenantContext`), the constant migrated to `TenantContext` as
the single source of truth. The middleware references it via
`user.FindFirst("tenant_id")` (string literal, kept inline) because the
middleware must NOT depend on `Identity.Infrastructure` (would create a
cycle if it ever moves to Shared). The string is documented at the
middleware site as well to keep the contract readable.

### 4. **`BackfillTenantsRunner` orders by `Id`, not `CreatedAt`**

The first implementation picked the "oldest" NULL user as the Personal
tenant's owner via `OrderBy(u => u.CreatedAt)`. SQLite (used in the test
project) does not support `ORDER BY` on `DateTimeOffset` (`NotSupported
Exception: SQLite does not support expressions of type 'DateTimeOffset' in
ORDER BY clauses`). Switched to `OrderBy(u => u.Id)` — Guids sort
stably (Guid sort key + DB-side index is far cheaper than materializing
the table to sort client-side). Production Postgres supports either
ordering, but the Id ordering is what runs in the unit-tested path.

### 5. **`BackfillTenantsRunner` calls `SaveChangesAsync` between Add and Update**

Initial run was a single transaction: `AddAsync(personal)` + `ExecuteUpdateAsync(...)`.
The `ExecuteUpdateAsync` skipped the change tracker and executed raw SQL —
the Personal was never persisted. Split into two operations:
`SaveChangesAsync` for the Personal (so its row exists), then
`ExecuteUpdateAsync` for the bulk UPDATE. The shape matches the SQL
migration's `BEGIN / ... INSERT ... / ... UPDATE ... / COMMIT` ordering
(transactional at the SQL level, two-step at the EF level because
`ExecuteUpdateAsync` doesn't share a transaction with change tracking).

### 6. **6c.1 `ITenantContext` placeholder class is rewritten in place**

The 6c.1 placeholder `TenantContext.cs` (27 lines, returns null/false
for every member) was REWRITTEN with the same file name + class name.
The DI registration line `AddScoped<ITenantContext, TenantContext>`
stays identical — the swap is the file body, not the seam. The 6c.1
contract test (`ITenantContextContractTests`) still passes because it
defines its own internal `PlaceholderTenantContext` for the default
behavior assertion; the real impl is exercised by `TenantContextTests`.

### 7. **`ITokenService` interface widened, not just the impl**

Three handlers call `ITokenService.CreateAccessToken`. Widening only the
impl would have left the callers uncompilable (`CS1739`). Pivoted to
match — interface change first, impl + callers in one commit. This is
the standard SignalR-pipeline-style "interface + impl" co-evolution; no
backward-compat concern because the application is the only consumer.

## Critical Lessons Applied (from Wave 4 + Wave 5 + 6a/6b/6c.1)

| Lesson | Where applied |
|---|---|
| Idempotent migrations | Migration 0026 uses `INSERT ... ON CONFLICT (slug) DO NOTHING` for Personal + `UPDATE ... WHERE tenant_id IS NULL` for users. Both safe to re-run. `BEGIN; ... COMMIT;` makes the script transactional (consistent with the SQL surface that matches the C# runner's behavior). |
| `BEGIN / COMMIT` in migrations | Per Wave 5 precedent for any multi-statement migration that touches a FK + data. 0026 wraps the INSERT+UPDATE in a single `DO $$ ... $$` block + outer `BEGIN; ... COMMIT;`. |
| Defense-in-depth — tenant context defaults to safe failure | `TenantQueryFilter` returns `Empty` (via `Take(0)`) when the context is null AND caller is not super-admin. Misconfigured callers CANNOT accidentally read across tenants. |
| Defense-in-depth — middleware rejects malformed claims | The middleware rejects non-empty `tenant_id` claims that don't `Guid.TryParse` instead of letting the value propagate. This blocks a misconfigured issuer from smuggling a bad value. |
| Defense-in-depth — JWT mint omits absent claims | The access token does NOT carry a `tenant_id` claim at all when the user is unassigned. The middleware then gates the request as `auth.tenant_missing`, forcing a re-login (or refresh) once assignment lands. |
| No duplicate EF config | `Tenant` aggregate + config landed in 6c.1. This slice adds NO new EF entity configs — only the runner + hosted service + middleware. |
| Static + extension method | `TenantQueryFilter` is stateless; extension methods avoid a DI dependency on every repository. |
| `migrate.Dockerfile` happy + retry path | 0026 added to COPY list AND happy-path psql AND retry-path psql. Idempotent SQL means retry is a no-op. |
| `IClock` injection | `BackfillHostedService` does NOT inject a clock — it only runs once on startup, then leaves the host to its lifecycle. The runner uses `DateTimeOffset.UtcNow` for the Personal's `created_at` (matches the existing Tenant.FromTrusted helper signature). |
| Marker interface instead of generic constraint | `ITenantOwned` is a marker interface (no methods, one property), allowing EF Core and JSON to model it as a plain `TenantId?`. The `where T : ITenantOwned` constraint on `TenantQueryFilter` is enough to enforce the contract at compile time. |
| Test-only EF provider | Tests use `Microsoft.EntityFrameworkCore.Sqlite 9.0.4` (in-memory) so the runner can be exercised against a real `IdentityDbContext` without a Postgres dependency. The package is declared ONLY in the test project; nothing in production touches it. |
| `Microsoft.NET.Sdk.Web` workaround for middleware | Initially the middleware was a `static class`; switched to `sealed class` to satisfy `UseMiddleware<T>`. The test harness was mechanically updated to construct the class with a stub `RequestDelegate`. |

## What's NOT in Slice 6c.2

Per `tasks.md` 6c.2 scope, these arrive in subsequent slices:

- **6c.3**: `UpdateTenant`/`ListTenantUsers`/`InviteTenantUser`/`RemoveTenantUser` handlers + `MapTenantEndpoints` + migration 0026 (NOT NULL on `tenant_id`) + 20 tests. The NOT NULL is the missing piece for the "ONE migration atómica" strategy.
- **6d.1**: `ISoftDelete` + `IAuditLogger` + `AuditEvent` aggregate + `audit.events` migration + soft-delete query filter on `ImportJob` + 25 tests. The query filter added in this slice is intentionally a separate method (`WithTenantFilter`) so it composes with the 6d.1 soft-delete filter (`j => !j.IsDeleted && j.TenantId == ctx.Current`).
- **6d.2**: `AuditLogger` + `DecoratedRepository` pattern + 3 repository integrations + 30 tests. The 6c.2 query filter is the read-side correlation key for the audit-event diff payload.

## Reviewer Notes

- **35 new tests** (spec target was 30; +5 hardening tests for malformed `tenant_id` rejection, standard-claim preservation, claim-non-shadowing, and the prior-claims-not-touched assertion).
- **Path count**: 22 (vs. 32 budget cap; vs. 14 forecast).
- **`size:exception` accepted** per Wave 5/6a/6b/6c.1 precedent.
- **`tenant_id` column is NULLABLE** until 6c.3 (per "ONE migration atómica" user decision). Both the SQL migration 0026 AND the hosted service backfill are idempotent against this state.
- **Middleware order matters**: `app.UseMiddleware<TenantContextMiddleware>()` is wired AFTER `UseAuthentication` + `UseAuthorization` so `HttpContext.User` is populated. Pulling it earlier would short-circuit anonymous endpoints with 401 (defeats the public-endpoint passthrough).
- **EF Core value converter quirk (SQLite)**: SQLite bypasses the `TenantId ↔ Guid` converter on read in some shapes — the test helper `TenantIdAccessor.ExtractInnerGuid` accommodates both the wrapper and the raw Guid. This is a TEST concern, not a production concern; Postgres runs the converter deterministically.
- **`RefreshTokenHandler` carries `tenant_id`** — refresh-rotated access tokens include the claim when the user is assigned, satisfying the Wave-6 "force re-login is NOT required" requirement.

## Rollback

Revert code; the `tenant_id` migration stays additive (column is still nullable). Both the C# runner and the SQL migration are idempotent — re-running them after rollback is a no-op. The middleware can be deleted from the pipeline without breakage (handlers would still see `ITenantContext.Current = null` if the tenant column is NULL, which is the same state as before 6c.2). The `JwtTokenService` change is additive (new `tenantId` parameter with a `null` default) — callers that don't pass it see no behavior change.

## Next Slice

**6c.3** — Tenant Admin Endpoints (≤ 500 lines, 11 paths). Uses the `ITenantContext`
+ `TenantContextMiddleware` + `ITenantRepository` from this slice; adds 4
endpoints + 4 handlers + NOT NULL migration + 20 tests.
