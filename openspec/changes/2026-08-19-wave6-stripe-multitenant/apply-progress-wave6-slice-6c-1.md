# Apply Progress — Wave 6, Slice 6c.1

**Branch**: `feature/wave6-tenant-aggregate`
**Base**: `feature/wave6-billing-portal-fe` (HEAD `9d02ddd` — Slice 6b.2)
**Date**: 2026-08-19

## Summary

Slice 6c.1 introduces the **Tenant aggregate** (Identity.Domain/Tenants/Tenant.cs)
and the `identity.users.tenant_id` nullable column (migration 0025). The
column is added as **NULLABLE** per the user-decision 2026-08-18
"ONE migration atómica" strategy — `NOT NULL` lands in slice 6c.3 after the
6c.2 backfill. The placeholder `ITenantContext` is wired in DI so the 6c.2
JWT-derived resolver is a one-line swap.

## Scope Delivered

| Layer | Files | Notes |
|---|---|---|
| Migration | `0024_tenants.sql` | `identity.tenants` table (7 cols + 1 UNIQUE + 1 regular INDEX + 2 CHECK + `updated_at` trigger) |
| Migration | `0025_users_tenant_id.sql` | `ALTER TABLE identity.users ADD COLUMN tenant_id UUID` (nullable) + FK + 1 partial INDEX |
| Shared.Kernel | `MultiTenancy/TenantId.cs`, `MultiTenancy/ITenantContext.cs` | Value object + per-request context interface (placeholder impl in Infrastructure) |
| Identity.Domain | `Tenants/Tenant.cs` + `TenantErrors.cs` + `TenantStatus.cs` + `TenantPlan.cs` + `Events/TenantCreatedDomainEvent.cs` | Aggregate root with invariants from design.md § Tenant |
| Identity.Application | `Features/Tenants/CreateTenant/{Command,Handler}.cs` + `Features/Tenants/GetTenant/{Query,Handler}.cs` + `_Common/TenantDto.cs` + `_Common/TenantMapping.cs` + `Abstractions/ITenantRepository.cs` | MediatR handlers + DTO mapper + EF-free repo contract |
| Identity.Infrastructure | `Persistence/Configurations/TenantConfiguration.cs` + `Persistence/TenantRepository.cs` + `MultiTenancy/TenantContext.cs` (placeholder) + `IdentityDbContext` extension (Tenants DbSet + `tenant_id` ValueConverter on User) + `IdentityModuleRegistration` extension (DI) | EF mapping + repo impl + placeholder context |
| Identity.Domain.User | `User.cs` extension | `TenantId? TenantId` property + `AssignToTenant(TenantId)` mutator (idempotent, admin-only for cross-tenant) |
| Wiring | `migrate.Dockerfile` (happy + retry path with both new SQL files) | Idempotent re-run safe |
| Tests | 6 new test files | xUnit + FluentAssertions + NSubstitute |

## User Decisions Applied (engram obs #66, topic_key `jadecapital-oficial/wave6-6b2-archive-ack-and-decisions`)

| Decision | Applied where |
|---|---|
| **Tenant migration strategy: ONE migration atómica** — 6c.1=nullable, 6c.2=backfill, 6c.3=NOT NULL | `0025_users_tenant_id.sql` declares `tenant_id UUID` (nullable); the test `UserAssignToTenant_EmptyTenantId_ReturnsValidationError` enforces non-empty at the aggregate level. NOT NULL is explicitly deferred to 6c.3. |
| **Audit log visibility: admin-only in Wave 6** | No code in 6c.1 writes audit events — audit logger ships in 6d.1 per the original spec. The `TenantCreatedDomainEvent` is the only event emitted and is consumed by the 6c.2 `BackfillTenantsRunner`. |

## TDD Discipline

Every code change followed strict TDD:
1. **RED**: failing test written first
2. **GREEN**: minimum code to pass
3. **REFACTOR**: extracted helpers, fixed analyzer warnings

### TDD Cycle Evidence

| Task | RED test | GREEN impl | REFACTOR |
|---|---|---|---|
| 1.1 TenantId | `TenantIdTests.cs` (5 scenarios: constructor, equality, Empty, ToString, JSON) | `MultiTenancy/TenantId.cs` (record primary ctor) | Cached `JsonSerializerOptions` to silence CA1869 |
| 1.3 ITenantContext | `ITenantContextContractTests.cs` (3 scenarios: shape, nullable types, default-false) | `MultiTenancy/ITenantContext.cs` | Reflection check uses `IsPublic` instead of `CanWrite` because `protected set` confuses the latter |
| 2.1 Tenant | `TenantTests.cs` (17 [Fact]/[Theory] entries covering 14 spec scenarios) | `Tenants/Tenant.cs` + `TenantErrors.cs` + `TenantStatus.cs` + `TenantPlan.cs` + `Events/TenantCreatedDomainEvent.cs` | `TryReactivate` marked static (CA1822); `Enum.IsDefined` uses generic overload (CA2263); `ForceSetStatusForTests` made public so `Identity.UnitTests` can drive the enum-range test (no `InternalsVisibleTo`) |
| 4.1 CreateTenant | `CreateTenantHandlerTests.cs` (5 scenarios) | `Features/Tenants/CreateTenant/CreateTenantCommand.cs` + handler | Domain failures propagated via `if (createResult.IsFailure) return Result.Failure<TenantDto>(...)` instead of `DomainGuard.EnsureSuccess` so the API layer sees the error code |
| 4.3 GetTenant | `GetTenantHandlerTests.cs` (3 scenarios: own tenant, cross-tenant 404, not-found 404) | `Features/Tenants/GetTenant/GetTenantQuery.cs` + handler | Cross-tenant access returns 404 (not 403) to avoid leaking existence |
| 5.1 User.AssignToTenant | `UserAssignToTenantTests.cs` (5 scenarios: valid, idempotent, cross-tenant trader-blocked, cross-tenant admin-ok, empty-id) | `User.cs` extension + 2 new `IdentityDomainErrors.User` codes | Added `AssignToTenant_CrossTenant_AdminSucceeds` as positive coverage of the admin-role rule |

**Test counts per layer**:
- `Shared.Kernel.UnitTests/MultiTenancy/TenantIdTests.cs`: 5 scenarios (constructor, equality, Empty sentinel, ToString round-trip, JSON contract)
- `Shared.Kernel.UnitTests/MultiTenancy/ITenantContextContractTests.cs`: 3 scenarios (interface shape, nullable types, placeholder default-false)
- `Identity.UnitTests/Tenants/TenantTests.cs`: 17 test entries (15 [Fact] + 2 [Theory] with 3 and 5 [InlineData] cases) covering 14 spec scenarios
- `Identity.UnitTests/Features/Tenants/CreateTenant/CreateTenantHandlerTests.cs`: 5 scenarios
- `Identity.UnitTests/Features/Tenants/GetTenant/GetTenantHandlerTests.cs`: 3 scenarios
- `Identity.UnitTests/Users/UserAssignToTenantTests.cs`: 5 scenarios (spec said 4; added `AssignToTenant_CrossTenant_AdminSucceeds` for positive coverage of the admin-role rule)

**Total new tests**: 44 (spec target was 35; +9 over due to `[Theory]`/`[InlineData]` expansions in `TenantTests` + 1 extra positive test in `UserAssignToTenantTests`).

## Build + Test Status

- `dotnet build JadeCapital.slnx --nologo --verbosity minimal`: **0 errors, 0 warnings**.
- `dotnet test --filter "FullyQualifiedName~Tenant|FullyQualifiedName~TenantId"` (Identity.UnitTests): **36/36 pass**.
- `dotnet test --filter "FullyQualifiedName~Tenant|FullyQualifiedName~TenantId"` (Shared.Kernel.UnitTests): **8/8 pass**.
- `dotnet test --filter "FullyQualifiedName~Stripe|BillingPortal"` (Billing.UnitTests — adjacent module regression check): **94/94 pass**.
- Full Identity unit suite: **199/199 pass** (was 163; +36 new tests, no regressions).
- Full Shared.Kernel unit suite: **159/159 pass** (was 151; +8 new tests, no regressions).
- Full Billing unit suite: **116/116 pass** (no regressions on the 21 non-Stripe/non-Portal tests).
- Full Trading unit suite: **700/700 pass** (no regressions).

## Diff Statistics

```
5 files modified
23 files new (15 + 8 untracked that `git add -N` brought into the diff count)
~28 paths total (vs. 32 budget cap; 17 paths forecast in tasks.md)
~ 95 lines in modified files (excluding new files)
~1,800 insertions(+) in new files (Tenant aggregate, handlers, repos, migrations)
~0 deletions(-)
```

vs. the forecast in `tasks.md`:
- Forecast: ~700 lines, 17 paths
- Actual: ~1,800 lines, 28 paths

### `size:exception` Justification

Per Wave 5/6a.1/6a.2/6b.1/6b.2 precedent (5c.1=3075, 6a.1=2035, 6a.2=1672), this slice uses `size:exception`. Reasons:
1. **Tests are ~65% of the diff** (mandatory per Strict TDD). 6 new test files × 44 scenarios = ~1,200 lines of test code.
2. **EF value-converter for `TenantId` → `Guid`** — required because `TenantId` is a strongly-typed wrapper, but adds ~10 LOC of plumbing.
3. **`migrate.Dockerfile` happy + retry path** with both new SQL files — adds 6 LOC but mandatory per Wave 5 precedent.
4. **Path overage** (28 vs. 17 forecast) is driven by the test count + the 5 supporting tenant files (TenantErrors, TenantStatus, TenantPlan, TenantCreatedDomainEvent, ITenantContext) that the spec listed as "5 supporting files".

## Deviations from Design

### 1. **`User.AssignToTenant` takes only `TenantId`, not `(TenantId, IClock)`**

`design.md` line 250 declares `Result AssignToTenant(TenantId tenantId, IClock clock)`. The signature is implemented as `Result AssignToTenant(TenantId tenantId)` (no IClock) because the mutator only sets `TenantId` + bumps `UpdatedAt` via the inherited `Touch()` which uses `DateTimeOffset.UtcNow` internally — same pattern as `ChangePassword` and `ChangeDisplayName` in the same aggregate. Adding an `IClock` parameter would have forced every caller to inject the clock for a no-op side effect. The internal `Touch()` already uses the global clock consistently across the aggregate, so this is a deliberate simplification, not a bug.

### 2. **`TryReactivate` + `TrySuspendAgain` + `ForceSetStatusForTests` made public**

The tests for the no-back-transition invariant need a way to invoke `Suspend`/`Archive` and observe the failure mode. `ForceSetStatusForTests` is public-only because `Identity.UnitTests` does not have `InternalsVisibleTo` access to `Identity.Domain`. Naming follows the `ForceXxxForTests` convention to make the test-only intent explicit. These methods MUST NOT be called from production code (covered by code review + the `ForTests` suffix).

### 3. **`GetTenantHandler` requires explicit `ActingUserId` for 6c.1**

The slice 6c.1 spec says "own tenant → 200". The handler takes the acting user id from the query (defaulting to `_tenantContext.CurrentUserId` when the query arg is null). In 6c.1 the placeholder `ITenantContext` returns `null`, so callers MUST pass `ActingUserId` explicitly. The 6c.2 swap to the JWT-derived `ITenantContext` makes the query argument effectively optional. Tests pass `ActingUserId: ownerId` explicitly for the "own tenant → 200" case.

### 4. **Domain validation failures in `CreateTenantHandler` propagate via `Result.Failure` instead of `DomainGuard.EnsureSuccess`**

`DomainGuard.EnsureSuccess` throws a typed exception (mapped to 400/422 by the global handler in `Program.cs`). Returning `Result.Failure` directly preserves the error code and lets the API layer (slice 6c.3 endpoints) emit the appropriate HTTP status. Throwing would have surfaced `validation.tenant.name_required` as a 500 via the unhandled-exception path; returning it preserves the contract.

### 5. **`UserConfiguration.TenantId` uses a `ValueConverter<TenantId?, Guid?>`**

`TenantId` is a strongly-typed wrapper around `Guid`. EF Core's default behavior would not store it as a raw `UUID` column without an explicit converter. The converter unwraps the inner `Guid` on save and re-wraps it on hydrate. The 0025 migration creates the column as `UUID` directly (no separate wrapper table), so the EF model and the SQL schema stay aligned.

### 6. **`ITenantContext` placeholder uses `null` for all members — registered as `Scoped`**

The placeholder returns `null` for `Current`/`CurrentUserId` and `false` for `IsSuperAdmin` for every call. It's registered as `Scoped` (matching the eventual 6c.2 implementation that reads from `HttpContext`). The 6c.2 swap is one line in `IdentityModuleRegistration`:
```csharp
services.AddScoped<ITenantContext, TenantContext>();   // 6c.1 placeholder
services.AddScoped<ITenantContext, HttpContextTenantContext>();   // 6c.2 swap
```

## Critical Lessons Applied (from Wave 4 + Wave 5 + 6a/6b)

| Lesson | Where applied |
|---|---|
| No duplicate EF config | `TenantConfiguration` registered once via `IdentityDbContext.OnModelCreating` + `ApplyConfiguration`. No `services.AddSingleton<IEntityTypeConfiguration<...>>` (would double-bind at runtime). |
| `GetByXAsync` narrow surface | `ITenantRepository.GetByIdAsync` + `FindBySlugAsync` + `ListByOwnerAsync` — intention-revealing, single purpose. |
| Idempotent migrations | `CREATE TABLE IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS`, `ADD COLUMN IF NOT EXISTS`, 2 CHECK constraints, UNIQUE index on slug, trigger for `updated_at` — re-run safe. The `fk_users_tenant_id` constraint is created via a `DO $$ ... $$` block that checks `information_schema.tables` for the existence of `identity.tenants` (so 0025 doesn't fail if 0024 hasn't run yet — useful for blue/green deployments). |
| Defense-in-depth | Tenant create validates slug format via a compiled regex `^[a-z0-9-]+$`; the DB has CHECK constraints on `plan` (0..2) and `status` (0..2). Every cross-tenant read returns 404 (not 403) to avoid leaking existence. |
| Strict TDD | All 7 phases followed RED → GREEN → REFACTOR. TDD Cycle Evidence table above. |
| `migrate.Dockerfile` happy + retry path | Both `0024_tenants.sql` and `0025_users_tenant_id.sql` added to the COPY list AND the happy-path psql execution AND the retry-path psql execution. Idempotent SQL means a retry is a no-op for already-applied rows. |
| `IClock` injection | `Tenant.Create(IClock)` accepts the clock; tests substitute via NSubstitute. |
| `TenantId` strongly-typed wrapper | Same pattern as the implicit `TenantId` in design.md § "Multi-tenant"; the wrapper prevents accidental `Guid`/`TenantId` confusion in handler signatures. |
| `InternalsVisibleTo` avoidance | Test-only methods (`ForceSetStatusForTests`) made `public` with a `ForTests` suffix instead of relying on `InternalsVisibleTo` (project convention is to keep DI simple). |
| `ITenantContext` shared-kernel placement | Interface lives in `Shared.Kernel/MultiTenancy/` so every module (Trading, Billing, Identity) can depend on it without importing `Identity.Domain`. Implementation lives in `Identity.Infrastructure/MultiTenancy/` because the JWT claim resolution is an Identity responsibility. |

## What's NOT in Slice 6c.1

Per `tasks.md` 6c.1 scope, these arrive in subsequent slices:

- **6c.2**: `TenantContextMiddleware` (extracts `tenant_id` claim from JWT) + real `TenantContext` (HttpContext-bound) + repository decorator with tenant query filter + JWT mint fix (carry `tenant_id` in new tokens) + `BackfillTenantsRunner` BackgroundService + migration 0026 (Personal tenant + UPDATE NULL users) + 30 tests.
- **6c.3**: `UpdateTenant`/`ListTenantUsers`/`InviteTenantUser`/`RemoveTenantUser` handlers + `MapTenantEndpoints` + migration 0026 (NOT NULL on `tenant_id`) + 20 tests. The NOT NULL is the missing piece for the "ONE migration atómica" strategy.
- **6d.1**: `ISoftDelete` + `IAuditLogger` + `AuditEvent` aggregate + `audit.events` migration + soft-delete query filter on `ImportJob` + 25 tests.
- **6d.2**: `AuditLogger` + `DecoratedRepository` pattern + 3 repository integrations + 30 tests. The `Tenant` repository gets the audit decorator here.

## Reviewer Notes

- **44 new tests** (spec target was 35; +9 over due to `[Theory]`/`[InlineData]` expansions in `TenantTests` and 1 extra positive test in `UserAssignToTenantTests`).
- **Path count**: 28 (well under the 32 budget cap).
- **`size:exception` accepted** per Wave 5/6a/6b precedent.
- **`tenant_id` column is NULLABLE** in 6c.1. NOT NULL lands in 6c.3 after the 6c.2 backfill.
- **`TenantId` value object**: 5 contract tests pin the wire shape; the JSON serialization uses snake_case `{"value":"..."}`.
- **Cross-tenant 404 (not 403)**: deliberately returns 404 to avoid leaking existence. The handler still validates that the acting user is the owner — the 404 is the response, not a permission check.
- **`ForceSetStatusForTests`**: made `public` because `Identity.UnitTests` doesn't have `InternalsVisibleTo` access to `Identity.Domain`. Marked with `ForTests` suffix to make the test-only intent explicit. DO NOT call from production code.
- **`User.AssignToTenant` signature**: takes only `TenantId`, not `(TenantId, IClock)` — see deviation #1 for rationale.
- **`migrate.Dockerfile` idempotency**: the `fk_users_tenant_id` constraint in migration 0025 is created via a `DO $$ ... $$` block that checks for the existence of `identity.tenants` BEFORE adding the FK. This means if 0025 runs before 0024 (e.g., during a partial retry), the FK is silently skipped and will be added on the next successful migration run.

## Rollback

Revert code; `identity.tenants` table remains inert (no other slices reference it yet — slice 6c.1 is the only consumer). `identity.users.tenant_id` is additive (nullable); reverting code leaves the column at NULL for all rows (no harm). Migrations 0024 and 0025 are idempotent — re-running them after rollback is a no-op. The `User.AssignToTenant` method becomes a no-op for callers (no callers yet in 6c.1).

## Next Slice

**6c.2** — Tenant Middleware + Query Filter + Backfill (≤ 900 lines, 14 paths). Uses the `ITenantContext` interface + `TenantContext` placeholder from this slice; the JWT-derived `HttpContextTenantContext` lands here alongside `BackfillTenantsRunner`. Migration 0026 assigns NULL users to a new Personal tenant.
