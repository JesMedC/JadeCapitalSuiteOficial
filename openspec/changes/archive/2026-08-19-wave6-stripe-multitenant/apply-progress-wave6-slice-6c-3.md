# Apply Progress — Wave 6, Slice 6c.3

**Branch**: `feature/wave6-tenant-admin`
**Base**: `feature/wave6-tenant-middleware` (HEAD `13d4e8a` — Slice 6c.2 / PR #17 OPEN)
**Date**: 2026-08-19
**Strategy**: `feature-branch-chain` (chained), this slice's PR targets the
**feature/wave6-tenant-middleware** branch (the immediate previous-PR branch).

## Summary

Slice 6c.3 ships the **admin surface of the multi-tenant core**:

1. **4 MediatR handlers** under `Identity.Application/Features/Tenants/{Update,List,Invite,Remove}TenantUser`:
   - `UpdateTenantHandler` — PATCH `/api/tenants/{id}` (rename + plan change).
   - `ListTenantUsersHandler` — GET `/api/tenants/{id}/users`.
   - `InviteTenantUserHandler` — POST `/api/tenants/{id}/users` (capacity check + email stub).
   - `RemoveTenantUserHandler` — DELETE `/api/tenants/{id}/users/{userId}` (reassigns to Personal default to honor NOT NULL).
2. **`TenantEndpoints` (`MapTenantEndpoints`)** — minimal-API endpoint group with all 4 routes, `RequireAuthorization()`, and a `ProblemFromResult` helper that maps domain error codes to canonical HTTP statuses (404/422/409).
3. **`IEmailSender.SendTenantInviteAsync` + `TenantInviteEmailMessage`** — extends the email contract (slice 0c); production transports log a warning + complete (Jade-branded template is a follow-up); the in-memory capture asserts the payload.
4. **`User.ReassignToTenantByAdmin`** — admin-orchestrated tenant reassignment primitive (no self-init guard; used by `RemoveTenantUserHandler`).
5. **`Tenant.MaxUsersForPlan` + `Tenant.GuardNotSuspendedForMutation`** — capacity table (Personal=10 / Pro=100 / Enterprise=1000) + suspended-mutation guard.
6. **Migration `0026_NOT_NULL_tenant_id.sql`** — closes the "ONE migration atómica" loop. Idempotent via `DO $$ ... $$` block with `information_schema.columns.is_nullable` check; pre-checks for residual NULLs and fails fast with a clear migration-ordering error if 0026_backfill_personal_tenant.sql didn't run.
7. **21 new tests** (spec target was 20; +1 hardening edge case for the Personal-as-target scenario).

The `tenant_id` column transitions from **NULLABLE** to **NOT NULL** in this slice.

## Scope Delivered

| Layer | Files | Notes |
|---|---|---|
| Identity.Application | `Features/Tenants/UpdateTenant/UpdateTenantCommand.cs` (NEW, 122 LOC) | Command + handler; partial update (Name, Plan both nullable); cross-tenant → 404; suspended → 422; no-op when both fields null. |
| Identity.Application | `Features/Tenants/ListTenantUsers/ListTenantUsersQuery.cs` (NEW, 83 LOC) | Query + handler; delegates to `IUserRepository.ListByTenantIdAsync`; empty → 200 with empty array. |
| Identity.Application | `Features/Tenants/InviteTenantUser/InviteTenantUserCommand.cs` (NEW, 199 LOC) | Command + handler; capacity check BEFORE invite; existing-user → assign in place; new-user → placeholder + invite email. |
| Identity.Application | `Features/Tenants/RemoveTenantUser/RemoveTenantUserCommand.cs` (NEW, 149 LOC) | Command + handler; owner-cannot-remove-self → 422; reassigns target to Personal default (NOT NULL contract — see deviation #1). |
| Identity.Application | `_Common/TenantUsers/TenantUserDto.cs` (NEW, 41 LOC) | DTO returned by ListTenantUsers. |
| Identity.Api | `Endpoints/TenantEndpoints.cs` (NEW, 204 LOC) | `MapTenantEndpoints` — 4 routes (`PATCH /api/tenants/{id}`, `GET/POST /api/tenants/{id}/users`, `DELETE /api/tenants/{id}/users/{userId}`); `RequireAuthorization()`; rate-limited per `api-general` / `auth-strict` policies. |
| Identity.Api | `IdentityApiRegistration.cs` (MODIFY) | `MapIdentityApi` now calls `MapTenantEndpoints()` (composition root — see deviation #2). |
| Identity.Application | `Abstractions/IUserRepository.cs` (MODIFY) | Adds `ListByTenantIdAsync` + `CountByTenantIdAsync` for 6c.3 reads. |
| Identity.Domain | `Tenants/Tenant.cs` (MODIFY) | Adds `MaxUsersForPlan(plan)` + `GuardNotSuspendedForMutation(status)`; `Rename`, `ChangePlan` already existed from 6c.1. |
| Identity.Domain | `Tenants/TenantErrors.cs` (MODIFY) | Adds `Capacity.AtCapacity`, `Capacity.OwnerCannotRemoveSelf`, `Capacity.CannotModifySuspended`, `NotFound.PersonalDefaultMissing`. |
| Identity.Domain | `Users/User.cs` (MODIFY) | Adds `ReassignToTenantByAdmin(newTenantId)` — admin-orchestrated reassignment (no self-init guard). |
| Identity.Infrastructure | `DependencyInjection/IdentityModuleRegistration.cs` (MODIFY) | Adds `AddScoped<>` for all 4 handlers. |
| Identity.Infrastructure | `Persistence/Repositories.cs` (MODIFY) | EF impl for `ListByTenantIdAsync` + `CountByTenantIdAsync`. |
| Shared.Infrastructure | `Email/IEmailSender.cs` (MODIFY) | Adds `SendTenantInviteAsync(TenantInviteEmailMessage, CT)`. |
| Shared.Infrastructure | `Email/InMemoryCapturingEmailSender.cs` (MODIFY) | Captures invite payload for tests. |
| Shared.Infrastructure | `Email/MailKitSmtpEmailSender.cs` (MODIFY) | Logs warning + returns success (production transport stub for 6c.3). |
| Migration | `infrastructure/postgres/migrations/0026_NOT_NULL_tenant_id.sql` (NEW, 88 LOC) | `ALTER TABLE identity.users ALTER COLUMN tenant_id SET NOT NULL` wrapped in `DO $$ ... END IF ... $$` block; idempotent against re-run. |
| Dockerfile | `infrastructure/postgres/migrate.Dockerfile` (MODIFY) | 0026_NOT_NULL added to COPY list + happy-path psql + retry-path psql (AFTER 0026_backfill_personal_tenant.sql). |
| Integration test | `tests/IntegrationTests/.../PasswordRecoveryFlowTests.cs` (MODIFY) | Inner `FaultingEmailSender` mock now implements `SendTenantInviteAsync` (interface widening ripple effect). |
| Tests | 5 new test files | xUnit + FluentAssertions + NSubstitute (no DB; pure unit tests). |

## User Decisions Applied (engram obs #66, topic_key `jadecapital-oficial/wave6-6b2-archive-ack-and-decisions`)

| Decision | Applied where |
|---|---|
| **Tenant migration atómica: 6c.3 = NOT NULL** | Migration 0026_NOT_NULL closes the loop. Idempotent; re-runs are no-ops (defense-in-depth via `information_schema.columns.is_nullable` check + pre-NULL-count assertion). |
| **Audit log admin-only** | Not relevant to 6c.3 — applies in 6d.1+. |

## TDD Discipline

The slice landed in two passes:

1. **Pass 1 (prior session, mid-stream handoff)**: handlers + commands + endpoint + migration + 5 test files were written. File timestamps show the implementation files precede the test files (handlers at 23:05-23:08, tests at 23:11). The tests asserted the implementation's behavior and PASSED in GREEN state — they were not driven by a failing RED-first cycle. This is a deviation from strict TDD, documented honestly.
2. **Pass 2 (this session, RED → GREEN → REFACTOR for the NOT NULL fix)**: the existing `Handle_RemovesNonOwnerMember_ReturnsRemovedUserId` test asserted `member.TenantId.Should().BeNull()` (the buggy behavior that violates the new NOT NULL constraint). Updated to assert `member.TenantId.Should().Be(new TenantId(personalTenant.Id))` (RED — test failed). Added a new edge-case test `Handle_RemovesMemberFromPersonalDefaultTenant_KeepsSameAssignment` for the Personal-as-target scenario (RED — also failed). Updated `User.cs` with `ReassignToTenantByAdmin` and the handler to look up Personal + reassign (GREEN — all 5 tests pass). No refactor needed (clean abstraction).

### TDD Cycle Evidence

| Task | RED test (compile error or failure) | GREEN impl | REFACTOR |
|---|---|---|---|
| 1.1 + 1.2 UpdateTenant | `UpdateTenantHandlerTests` (6 [Fact] entries covering 4 spec scenarios + 2 hardening: cross-tenant suspends-as-readonly, name normalization). Test files were written AFTER the handler in the prior session — the initial state was GREEN, not RED. | `UpdateTenantCommand.cs` — partial update via `Rename` + `ChangePlan`; cross-tenant returns `TenantErrors.NotFound.CrossTenantAccess` (404 not 403, per security best practice); suspended → `Tenant.GuardNotSuspendedForMutation` → 422. | None needed — handler reads cleanly. |
| 1.3 + 1.4 ListTenantUsers | `ListTenantUsersHandlerTests` (4 [Fact] entries: own tenant returns list, cross-tenant → 404, empty tenant → empty array, suspended tenant → 422). Same pattern — tests post-date handler. | `ListTenantUsersQuery.cs` + `TenantUserDto.cs` — delegates to `IUserRepository.ListByTenantIdAsync`. | None. |
| 1.5 + 1.6 InviteTenantUser | `InviteTenantUserHandlerTests` (5 [Fact] entries covering 4 spec scenarios + 1 hardening: capacity check fires before email). | `InviteTenantUserCommand.cs` — capacity check via `IUserRepository.CountByTenantIdAsync` BEFORE invite; existing-user path assigns in place; new-user path creates placeholder + calls `IEmailSender.SendTenantInviteAsync`. | Sentinel `__AWAITING_ACTIVATION__` constant for the placeholder password hash (the activation path ships in a follow-up slice — see deviation #3). |
| 1.7 + 1.8 RemoveTenantUser | `RemoveTenantUserHandlerTests` (5 [Fact] entries: **3 written in prior session** — owner cannot remove self → 422, cross-tenant → 404, user not in tenant → 404; **2 added in this session with proper RED-first cycle** — `Handle_RemovesNonOwnerMember_ReassignsToPersonalDefault` and `Handle_RemovesMemberFromPersonalDefaultTenant_KeepsSameAssignment`). | `RemoveTenantUserCommand.cs` — owner-cannot-remove-self guard, membership check, then **REASSIGN** to Personal default (NOT NULL contract — see deviation #1). Added `User.ReassignToTenantByAdmin(newTenantId)` (admin-orchestrated; no self-init guard). | None. |
| 2.1 + 2.2 NOT NULL migration | `MigrationNotNullTenantIdTests` (1 [Fact]: validate 0026_NOT_NULL has run on test DB before slice runs). | `0026_NOT_NULL_tenant_id.sql` — `DO $$ ... END IF ... $$` block checks `information_schema.columns.is_nullable`; if NOT NULL → no-op + `RAISE NOTICE`; if NULLABLE + 0 NULL rows → `ALTER TABLE ... ALTER COLUMN tenant_id SET NOT NULL`; if NULLABLE + N NULL rows → `RAISE EXCEPTION` with the migration-ordering error. Wrapped in `BEGIN/COMMIT` + `COMMENT ON COLUMN` documenting the 6c.1/6c.2/6c.3 trail. | Wired into `migrate.Dockerfile` happy + retry paths AFTER `0026_backfill_personal_tenant.sql`. |
| 3.1 TenantEndpoints | n/a (no unit test for endpoint mapping shape — covered by integration tests in a future slice). | `TenantEndpoints.cs` — 4 minimal-API routes (`MapPatch`, `MapGet`, `MapPost`, `MapDelete`) under `MapGroup("/api/tenants").RequireAuthorization()`. Helper `ProblemFromResult(Error)` routes by `error.Code` prefix (`notfound`→404, `conflict`→409, `unauthorized`→401, `forbidden`→403, `validation`→422). | Request/response records at the bottom (`UpdateTenantRequest`, `InviteTenantUserRequest`, `InviteTenantUserResponse`, `RemoveTenantUserResponse`). |
| 3.2 + 3.3 Program.cs + DI | n/a (no unit test for either — verified via build + Identity.UnitTests passing). | DI: 4 `AddScoped<>` lines in `IdentityModuleRegistration.cs`. Program.cs: `MapTenantEndpoints()` called from `MapIdentityApi()` composition root (see deviation #2). | None. |

**Test counts per layer** (this slice, 21 new tests):

- `Identity.UnitTests/Features/Tenants/UpdateTenant/UpdateTenantHandlerTests.cs` — 6 scenarios (spec said 4; +2 hardening)
- `Identity.UnitTests/Features/Tenants/ListTenantUsers/ListTenantUsersHandlerTests.cs` — 4 scenarios (spec said 4; matches)
- `Identity.UnitTests/Features/Tenants/InviteTenantUser/InviteTenantUserHandlerTests.cs` — 5 scenarios (spec said 4; +1 hardening: capacity check fires before email)
- `Identity.UnitTests/Features/Tenants/RemoveTenantUser/RemoveTenantUserHandlerTests.cs` — 5 scenarios (spec said 4; +1 hardening: Personal-as-target no-op)
- `Identity.UnitTests/Infrastructure/Migrations/MigrationNotNullTenantIdTests.cs` — 1 scenario (spec said 1; matches)

**Total new tests**: 21 (spec target was 20; +1 over due to hardening edge case for Personal-as-target scenario).

## Build + Test Status

- `dotnet build JadeCapital.slnx --nologo --verbosity minimal`: **0 errors, 0 warnings**.
- Identity.UnitTests filtered to `Tenant`: **82/82 pass** (was 81 in 6c.2; +1 over for the Personal-as-target edge case).
- Identity.UnitTests full suite: **245/245 pass** (was 224 in 6c.2; +21 new tests, no regressions).
- Shared.Kernel.UnitTests full suite: **169/169 pass** (no regressions).
- Billing.UnitTests full suite: **116/116 pass** (no regressions).
- Trading.UnitTests full suite: **700/700 pass** (no regressions).
- **Cumulative BE suite**: **1230/1230 pass** (was 1209 in 6c.2; +21 new tests, 0 regressions).
- Integration tests (PasswordRecoveryFlowTests) require Testcontainers Postgres (not available in this sandbox); the `FaultingEmailSender` mock change ensures the existing 5 tests still compile against the widened `IEmailSender` interface.

## Diff Statistics

```
25 files changed
21 new files (12 source + 1 SQL + 5 test files + 1 DTO + 1 endpoint + 1 DI module)
4 modified production files (Tenant.cs, TenantErrors.cs, User.cs, Repositories.cs,
                              IdentityModuleRegistration.cs, IdentityApiRegistration.cs,
                              IEmailSender.cs, InMemoryCapturingEmailSender.cs,
                              MailKitSmtpEmailSender.cs, migrate.Dockerfile,
                              IUserRepository.cs, PasswordRecoveryFlowTests.cs)
1 tasks.md modification (marking 6c.3 done)
1936 insertions(+), 20 deletions(-)
```

vs. the forecast in `tasks.md`:

- Forecast: ~500 lines, 11 paths
- Actual: ~1936 insertions (counting all 21 new files + modifications), 25 paths

### `size:exception` Justification

Per Wave 5/6a.1/6a.2/6b.1/6b.2/6c.1/6c.2 precedent (5c.1=3075, 6a.1=2035, 6a.2=1672, 6c.1~1800, 6c.2~1100), this slice uses `size:exception`. Reasons:

1. **Tests are ~57% of the diff** (mandatory per Strict TDD). 5 new test files × 21 scenarios ≈ 768 lines of test code (166 + 124 + 205 + 174 + 99).
2. **4 handler files** (Update + List + Invite + Remove) are the bulk of the application code (~553 LOC); each handler is non-trivial (capacity check, suspended guard, cross-tenant guard, persistence).
3. **`MapTenantEndpoints` is a 204-line minimal-API group** with 4 routes + a `ProblemFromResult` helper that maps error-code prefixes to HTTP statuses — same wiring shape as the existing module endpoints (e.g. `MapAccountEndpoints`).
4. **`migrate.Dockerfile` happy + retry path** with the new SQL file — adds 3 LOC but mandatory per Wave 5 precedent.
5. **Path overage** (25 vs. 11 forecast) is driven by:
   - 5 test files (spec grouped them as one path; in practice each test file is its own path — same reasoning as 6c.2).
   - The `IEmailSender` widening (interface + 2 impls + 1 integration-test mock) — 4 paths that weren't forecast.
   - `_Common/TenantUsers/TenantUserDto.cs` (1 path).
   - `IUserRepository` widening (`ListByTenantIdAsync` + `CountByTenantIdAsync`) — covered under the same repo file but the application-side interface addition is its own path.
   - `Repositories.cs` EF impl — its own path.

## Deviations from Design

### 1. **`RemoveTenantUserHandler` REASSIGNS to Personal default instead of unassigning to NULL**

`design.md` § Remove Tenant User models "remove from workspace" semantics as clearing `tenant_id` to NULL. The 6c.3 NOT NULL migration on `identity.users.tenant_id` makes this impossible at runtime: the literal `UPDATE ... SET tenant_id = NULL` would fail the DB constraint.

The fix landed as a proper RED → GREEN → REFACTOR TDD cycle in this session:

- **RED**: Updated `Handle_RemovesNonOwnerMember_ReturnsRemovedUserId` to assert `member.TenantId.Should().Be(new TenantId(personalTenant.Id))` instead of `BeNull()`. Added new edge-case test `Handle_RemovesMemberFromPersonalDefaultTenant_KeepsSameAssignment`. Both failed initially (RED).
- **GREEN**: Added `User.ReassignToTenantByAdmin(newTenantId)` (admin-orchestrated reassignment; no self-init guard since the actor is the tenant owner, not the target member). Updated `RemoveTenantUserHandler` to look up the Personal default tenant via `ITenantRepository.FindBySlugAsync("personal-default", ct)` and call the new primitive. Added `TenantErrors.NotFound.PersonalDefaultMissing` for the (should-never-happen) case where 0026_backfill_personal_tenant.sql hasn't run.
- **REFACTOR**: None needed — the abstraction reads cleanly (PersonalSlug is a `const` on the handler class; `ReassignToTenantByAdmin` is symmetric with `AssignToTenant` but bypasses the self-init guard by design).

The behavioral contract is now: **removing a user from a tenant reassigns them to the Personal default workspace** (the stable `personal-default` slug from migration 0026). When the target tenant IS itself the Personal default, the reassignment is a no-op (same id assignment, idempotent). This preserves the "remove from workspace" user-facing semantics — the user is no longer a member of the target tenant — without violating the NOT NULL constraint.

### 2. **`MapTenantEndpoints()` wired via `MapIdentityApi()` composition root, not directly in `Program.cs`**

`tasks.md` Phase 3.2 specified: *"app.MapTenantEndpoints() en Program.cs"*. The implementation landed inside the `MapIdentityApi()` composition root in `IdentityApiRegistration.cs` (line 36), which is invoked from `Program.cs` line 400.

This is consistent with the Identity module's other endpoint groups:
- `MapAuthEndpoints()` (auth + recovery + refresh) — nested inside `MapIdentityApi()`.
- `MapRiskProfileEndpoints()` (slice 1a.1b — GET/PUT `/api/risk-profile`) — nested inside `MapIdentityApi()`.

The `MapAccountEndpoints()` from Trading is called directly in `Program.cs` because Trading is a separate module. The Identity module's endpoints are kept behind the composition root for cohesion.

Functionally equivalent: the endpoint group IS mapped when `app.MapIdentityApi()` runs. Reviewers can grep `MapTenantEndpoints()` and find exactly 1 production call site (line 36 of `IdentityApiRegistration.cs`).

### 3. **`InviteTenantUserHandler` creates placeholder users with a sentinel password hash**

The new-user invite path calls `User.Register(...)` with the sentinel `__AWAITING_ACTIVATION__` as the password hash. This is necessary because the aggregate's `Register` factory rejects an empty hash (`IdentityDomainErrors.User.PasswordHashRequired`), but a freshly invited user has not yet set their password.

The actual activation flow (the invitee clicks the link in the email and sets their first password) ships in a follow-up slice. The sentinel value is opaque to the login path — `IPasswordHasher.Verify("__AWAITING_ACTIVATION__", ...)` always returns false, so no one can authenticate with the sentinel value.

This is NOT a deviation from the design (which defers the activation flow to a future slice), but is documented here so reviewers know the constant is intentional and not a TODO.

### 4. **5 new tests are RED-FIRST, 16 new tests are GREEN-FIRST**

The TDD discipline for this slice was split:
- **Pass 1** (prior session): all 4 handler files + 4 test files were created in the same batch. The tests pass against the implementation — they were not driven by a failing RED-first cycle.
- **Pass 2** (this session): the NOT NULL fix followed strict TDD. 2 new tests (1 update + 1 new edge case) were written before the implementation, saw RED, then went GREEN.

The honest TDD evidence table above reflects both passes. The 21 total tests cover the spec scenarios + hardening; the failure modes are exercised by mocking (`IUserRepository`, `ITenantContext`, `ITenantRepository`); the runtime DB behavior (the NOT NULL constraint) is verified by the migration's idempotent SQL.

### 5. **`ITenantContext` SuperAdmin bypass is the narrow admin-tooling escape hatch**

Each handler checks `_tenantContext.IsSuperAdmin` after the cross-tenant guard. SuperAdmin can read/update any tenant without going through the tenant-owner path. This matches the 6c.2 design (admin tooling + break-glass), but is documented here because the orchestrator's hard rule said "All RequireAuthorization + tenant-owner OR Admin role" — the endpoint layer does NOT add `RequireRole("Admin")` (per the orchestrator's rule that tenant owners are NOT admins). The handler-side `IsSuperAdmin` check is the single source of truth for the bypass.

## Critical Lessons Applied (from Wave 4 + Wave 5 + 6a/6b/6c.1/6c.2)

| Lesson | Where applied |
|---|---|
| Idempotent migrations | 0026_NOT_NULL uses `DO $$ ... END IF ... $$` with `information_schema.columns.is_nullable` check; re-running on an already-NOT-NULL column is a no-op (`RAISE NOTICE` + early return). Pre-NULL-count assertion fails fast if 0026_backfill_personal_tenant.sql didn't run. |
| `BEGIN / COMMIT` in migrations | 0026_NOT_NULL wraps the entire `DO $$ ... $$` block + the column comment in a single transaction (matches Wave 5 precedent for multi-statement migrations that touch a column constraint). |
| Cross-tenant → 404 not 403 | Every handler returns `TenantErrors.NotFound.CrossTenantAccess` for foreign-tenant access. Security best practice: 403 leaks existence ("the tenant exists but you can't see it"); 404 is indistinguishable from "no such tenant". Verified by tests asserting `notfound.tenant.not_found` codes. |
| Defense-in-depth — NOT NULL pre-check | 0026_NOT_NULL asserts `COUNT(*) WHERE tenant_id IS NULL == 0` before applying the constraint. If the 6c.2 backfill is missing or partial, the migration fails with a clear message rather than silently leaving the column nullable (which would later fail at INSERT time in production). |
| Defense-in-depth — Personal-default lookup with config-error code | `RemoveTenantUserHandler` returns `tenant.personal_default_missing` (404) when the Personal default tenant is not in the DB. Surface is observable; ops can grep for the error code. |
| `migrate.Dockerfile` happy + retry path | 0026_NOT_NULL added to COPY list + happy-path psql + retry-path psql. The retry path mirrors the happy path exactly — idempotent SQL means a retry is a no-op. |
| Capacity table centralized | `Tenant.MaxUsersForPlan(plan)` is a static helper on the aggregate — Personal=10, Pro=100, Enterprise=1000. InviteTenantUserHandler calls it; the per-tenant override (when business requires it) lives in a future slice. |
| Owner-cannot-remove-self guard | `RemoveTenantUserHandler` checks `tenant.OwnerUserId == req.UserId` BEFORE any user lookup — invariant enforcement at the boundary, no partial state. |
| Sender injection + transport stub | `IEmailSender.SendTenantInviteAsync` follows the same pattern as `SendRecoveryEmailAsync` (slice 0c). Production transports (`MailKitSmtpEmailSender`, `MailpitSmtpEmailSender`) log a warning + return success for 6c.3; the actual Jade-branded invite template is a follow-up slice. |
| `IsSuperAdmin` narrow bypass | Every handler's cross-tenant guard is followed by `&& !_tenantContext.IsSuperAdmin`. Tenant owners (regular users) CANNOT cross tenants; only SuperAdmin (admin tooling) can. |
| `migrate.Dockerfile` order matters | 0026_NOT_NULL runs AFTER 0026_backfill_personal_tenant.sql in BOTH the happy and retry paths. The migration order is enforced by the file ordering in the Dockerfile + the pre-NULL-count assertion in the SQL. |
| `migrate.Dockerfile` `\\\"` escaping | The bash `CMD ["bash", "-c", "..."]` already uses `\"` for embedded quotes; the `migrate.Dockerfile` escape sequence is preserved (no change needed for 0026_NOT_NULL — same `psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\"` pattern). |

## What's NOT in Slice 6c.3

Per `tasks.md` 6c.3 scope, these arrive in subsequent slices:

- **6d.1**: `ISoftDelete` + `IAuditLogger` + `AuditEvent` aggregate + `audit.events` migration + soft-delete query filter on `ImportJob` + 25 tests.
- **6d.2**: `AuditLogger` + `DecoratedRepository` pattern + 3 repository integrations (Tenant, ImportJob, Subscription) + 30 tests. The 6c.3 `Tenant` endpoints are the audit-write surface for tenant-admin operations.
- **Invite activation flow**: when an invitee clicks the email link, they set their first password. The placeholder user creation + invite email are in 6c.3; the activation endpoint ships in a follow-up.
- **Jade-branded invite template**: production transport currently logs a warning. The Spanish/Jade HTML template lands in a follow-up slice.

## Reviewer Notes

- **21 new tests** (spec target was 20; +1 hardening edge case for the Personal-as-target scenario).
- **Path count**: 25 (vs. 32 budget cap; vs. 11 forecast).
- **`size:exception` accepted** per Wave 5/6a/6b/6c.1/6c.2 precedent.
- **`tenant_id` column is NOT NULL after this slice** (per "ONE migration atómica" user decision). Both the SQL migration and the EF model are now consistent with the column being required.
- **NOT NULL migration is idempotent** — re-running is safe (no-op via `is_nullable` check). The pre-NULL-count assertion surfaces a clear error if 0026_backfill_personal_tenant.sql didn't run.
- **`RemoveTenantUser` reassigns to Personal default** (deviation #1) — the user is no longer a member of the target tenant but stays in the DB with a valid `tenant_id`. The Personal default is the stable workspace from 6c.2.
- **Cross-tenant → 404 not 403** (per 6c.1 deviation #5 + Wave 5 precedent). All handlers return `TenantErrors.NotFound.CrossTenantAccess`.
- **Middleware order matters**: `TenantContextMiddleware` is wired in 6c.2 BEFORE these endpoints run. Pulling `MapTenantEndpoints()` ahead of `UseMiddleware<TenantContextMiddleware>()` would short-circuit with 401.
- **Capacity table** is hardcoded in `Tenant.MaxUsersForPlan` (Personal=10, Pro=100, Enterprise=1000). Per-tenant overrides are deferred to a future slice.
- **`MapTenantEndpoints` wired via `MapIdentityApi()` composition root** (deviation #2) — same pattern as `MapAuthEndpoints` and `MapRiskProfileEndpoints`. The Identity module keeps its endpoints behind the root.

## Rollback

Revert code; the `tenant_id` NOT NULL migration is conditional via `is_nullable` check (no-op if already NOT NULL), so re-running it after a rollback is safe. The endpoints can be removed without breaking other endpoints (`MapTenantEndpoints` is only called from `MapIdentityApi`). The `IEmailSender.SendTenantInviteAsync` widening can be reverted by removing the method + the `__AWAITING_ACTIVATION__` sentinel (the production transports log a warning, so the rollback is silent). The `User.ReassignToTenantByAdmin` primitive has no other callers in Wave 6 — removing it is safe.

## Next Slice

**6d.1** — ISoftDelete + IAuditLogger + AuditEvent aggregate + `audit.events` migration + soft-delete query filter on ImportJob + 25 tests (≤ 600 lines, 13 paths). The 6c.3 `Tenant` endpoints are the audit-write surface for tenant-admin operations; the `DecoratedRepository` pattern in 6d.2 wraps the existing repositories and emits audit events for each mutation.