# Design: Wave 11 — GDPR v1 Readiness (5 slices, ~2,680 LOC, 6 DELTA specs)

**Change**: `2026-08-19-wave11-gdpr-v1-readiness`
**Base branch**: `feature/wave10-v1-readiness @ 8d40394` (Wave 10 archived — v1.0.0-rc1 tagged)
**Mode**: hybrid (OpenSpec + engram)
**Strategy**: `feature-branch-chain` with `size:exception` ONLY for slice 11.2b (~730 LOC: DELETE endpoint + UI + cascade audit row + 6 tests)
**Strict TDD**: ACTIVE — every spec scenario is RED-tested before GREEN impl
**Reference docs**: `explore.md` (231 LOC) · `proposal.md` · 6 DELTA specs in `specs/`
**Release target**: **`v1.0.0` GA** at the end of Wave 11 (no `rc` suffix — this IS the GA tag)

---

## 1. Architecture overview

### 1.1 What Wave 11 adds

Wave 11 is the **closure of Wave 10.5's narrower scope** + the **2 CRITICAL risks** flagged in the Wave 10 archive. Wave 11 is NOT new product features — it's the GDPR + cascade + consent surface that Wave 10.5 deferred, plus the test coverage + FK defect fix that blocks the `v1.0.0` GA tag.

```
                      ┌──────────────────────────── v1.0.0 GA ────────────────────────────┐
                      │                                                                   │
   spec/ canonical ─►  ┌────────────────┐    ┌──────────────────┐    ┌────────────────┐    │
   (Wave 10.5)         │ gdpr-compliance│    │account-lifecycle │    │ hard-delete-   │    │
   ↓ DELTA specs       │  (10 scenarios)│    │  (6 scenarios)   │    │ sweep-options  │    │
                       └────────────────┘    └──────────────────┘    └────────────────�    │
                       │                                                                   │
   src/2.Modules ───► ┌────────────────┐    ┌──────────────────┐    ┌────────────────┐    │
   (Identity exists)  │ IUserCascade   │    │ HardDeleteSweep  │    │ DeleteAccount  │    │
                      │ Deletor (10.5) │───►│ BGService (10.5) │    │ Handler (11.2b)│    │
                      └────────────────┘    └──────────────────┘    └────────────────┘    │
                       │                                                                   │
   infrastructure ───► ┌────────────────┐    ┌──────────────────┐    ┌────────────────┐    │
   (migrations)        │ 0029 sentinel  │    │ 0038 welcome     │    │ 0039 consent + │    │
                      │ user fix (11.2a)│    │ email col (11.3) │    │ 0040 cookie    │    │
                      └────────────────┘    └──────────────────┘    └────────────────┘    │
                       │                                                                   │
   frontend/ ────────► ┌────────────────�    ┌──────────────────┐    ┌────────────────┐    │
   (new feature area)  │ Settings tab + │    │ Cookie bottom    │    │ ToS + Privacy  │    │
                      │ Delete confirm │    │ bar + localStore │    │ pages (TODO)   │    │
                      └────────────────┘    └──────────────────┘    └────────────────┘    │
                       │                                                                   │
   tests/  ──────────► ┌────────────────┐    ┌──────────────────┐    ┌────────────────┐    │
   (NEW)               │ GDPR cascade   │    │ HardDeleteSweep  │    │ Export endpoint│    │
                      │ Testcontainers │    │ IOptionsMonitor  │    │ IAsyncEnumerable│   │
                      └────────────────┘    └──────────────────┘    └────────────────┘    │
                       └───────────────────────────────────────────────────────────────────┘
```

### 1.2 Wave 11 builds on Wave 10.5

| Wave 10.5 artifact | Wave 11 reuse |
|---|---|
| `IUserCascadeDeletor` interface (`src/2.Modules/Identity/JadeCapital.Identity.Application/Abstractions/IUserCascadeDeletor.cs`) with `CascadeSoftDeleteAsync(userId, ct)` + `CascadeHardDeleteAsync(userId, physicalUserRowDeleteAsync, ct)` | 11.1 tests the interface contract. 11.2b's `DeleteAccountHandler` calls `CascadeSoftDeleteAsync`. 11.3's `HardDeleteSweepBackgroundService.RunOnceAsync` calls `CascadeHardDeleteAsync` (unchanged). **No new interface methods.** |
| `UserCascadeDeleterOrchestrator` (`src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Cascade/UserCascadeDeleterOrchestrator.cs`) | 11.1's 4 orchestrator tests. 11.2b's `DeleteAccountHandler` injects the orchestrator. |
| `IdentityUserCascadeDeletor` + `TradingUserCascadeDeletor` + `BillingUserCascadeDeletor` (per-module) | 11.1's GDPR cascade integration test exercises all 3. **No new deletors.** |
| `IGdprAuditAnonymizer` (raw-SQL UPDATE, pseudonymizes audit rows) | 11.1's audit anonymization integrity test (deterministic hash queryability). |
| `HardDeleteSweepBackgroundService` (`src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/BackgroundServices/HardDeleteSweepBackgroundService.cs`) with hardcoded `GracePeriodDays = 30` + `MaxJitterMs = 30min` + `await Task.Delay(TimeSpan.FromMinutes(2))` + `await Task.Delay(TimeSpan.FromHours(24).Add(...))` | 11.3 replaces hardcoded constants with `IOptionsMonitor<HardDeleteSweepOptions>.CurrentValue.*`. Defaults match Wave 10.5 EXACTLY (behavior parity test). |
| `users` lifecycle columns from `infrastructure/postgres/migrations/0039_user_lifecycle.sql` (status, scheduled_for_hard_delete_at, etc.) | 11.2b reads/writes `users.status` + `users.scheduled_for_hard_delete_at`. 11.3 adds `users.welcome_email_sent_at` via `0038_add_welcome_email_sent_at.sql`. 11.4 adds consent columns via `0039_add_consent_columns.sql` + `0040_add_cookie_consent_columns.sql`. |
| `JadeApiFactory` (Testcontainers Postgres + Redis + Respawn) at `tests/IntegrationTests/JadeCapital.Api.IntegrationTests/Infrastructure/JadeApiFactory.cs` | 11.1's `GdprCascadeIntegrationTests` uses this fixture. Zero new Testcontainers setup. |
| `audit.events` + `AuditAction.Deleted` (used since Wave 9's `ImportJob` cascade) | 11.2b emits 1 `User/Deleted` row per DELETE. 11.2a emits 1 `User/Created` row for sentinel user. 11.3's `HardDeleteSweepBackgroundService` triggers `GdprAuditAnonymizer` which pseudonymizes rows. |
| `multi-tenant` spec (`tenant_id` JWT claim + `TenantContextMiddleware` + `ITenantContext`) | 11.3's `GET /api/users/me/export` reads `ITenantContext.CurrentUserId` to scope the export — cross-tenant reads return 403. |
| `soft-delete-audit` spec (`ISoftDelete` + EF global query filter) | 11.1's tests verify `IsDeleted = true` on cascade. |
| CI infra from Wave 10.1 (`services: postgres, redis` in `test-integration` job) | 11.1's integration test runs in CI. Sandbox: tests use `Skip = !DockerAvailable` trait. |

### 1.3 Module dependency graph — no new edges from Wave 11

Wave 11 adds **zero new project-to-project references** in the .NET solution. All new module-level dependencies are interface-only:

```
┌──────────────────────────────────────────────────────────────────────────────�
│                    Identity.Application (existing — Wave 10.5)                 │
│  IUserCascadeDeletor · UserCascadeDeleterOrchestrator · IGdprAuditAnonymizer  │
└───────────────┬──────────────────┬─────────────────────┬──────────────────────┘
                │                  │                     │
        implements           implements           implements
                │                  │                     │
        ┌───────▼──────┐   ┌───────▼────────┐   ┌───────▼──────────┐
        │   Identity   │   │    Trading     │   │     Billing      │
        │   (own       │   │   (own 13 aggs)│   │   (own Subs +    │
        │    deletor)  │   │                │   │   StripeCust)    │
        └──────┬───────┘   └───────┬────────┘   └───────┬──────────┘
               │                  │                     │
               └──────────────────┼─────────────────────┘
                                  ▼
               ┌─────────────────────────────────────┐
               │  UserCascadeDeleterOrchestrator    │
               │  (composed in IdentityModule-      │
               │   Registration, Wave 10.5)         │
               └────────────────┬────────────────────┘
                                ▼
               ┌─────────────────────────────────────┐
               │  HardDeleteSweepBackgroundService   │
               │  (Wave 10.5 + 11.3 IOptionsMonitor) │
               └─────────────────────────────────────┘
```

Wave 11 11.2b's `DeleteAccountHandler` (in `Identity.Application/Features/Auth/DeleteAccount/`) is the only new handler — it resolves `UserCascadeDeleterOrchestrator` from DI (the orchestrator collects all 3 deletors). Wave 11 does NOT add new cross-module edges.

---

## 2. Slice designs

### 2.1 Slice 11.1 — GDPR cascade xUnit coverage (`feature/wave11-gdpr-cascade-tests`, PR #57, ~450 LOC)

**Closes gaps**: Critical #2 (GDPR cascade xUnit tests — 16 spec scenarios) + G-5 (audit anonymization integrity test)
**Spec delta**: `specs/gdpr-endpoint-coverage/spec.md` (13 scenarios) + `specs/hard-delete-sweep-options/spec.md` (4 scenarios for `IOptionsMonitor`)
**Target LOC**: 450 (tests only — no production code changes) — **NO `size:exception`**

#### Test infrastructure reuse

All 10 new tests use the existing `JadeApiFactory` from Wave 10.1 (`tests/IntegrationTests/JadeCapital.Api.IntegrationTests/Infrastructure/JadeApiFactory.cs`). No new Testcontainers setup. The factory spins Postgres + Redis containers per test class + `Respawn` checkpoint between tests. Unit tests use `NSubstitute` for mocking `IUserCascadeDeletor` + `IGdprAuditAnonymizer` + `IUserRepository` (Wave 4+ precedent).

#### Test files

| Path | Purpose | Scenarios |
|---|---|---|
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Cascade/UserCascadeDeleterOrchestratorTests.cs` | Orchestrator composition + per-deletor exception isolation | 4 scenarios: invokes all in order; exception logged + continues; hard-delete invokes anonymizer + physical delete in order; CascadeSoftDeleteAsync sum returns |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Cascade/GdprAuditAnonymizerTests.cs` | Audit pseudonymization integrity (deterministic hash) | 2 scenarios: pseudonymizes all rows with deterministic hash; rows queryable by `entity_type = 'User' AND entity_id = 'deleted_user_<hash>'` |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/BackgroundServices/HardDeleteSweepBackgroundServiceTests.cs` | `IClock`-based time-travel + idempotent re-run | 3 scenarios: finds users where Status=ScheduledHardDelete AND ScheduledHardDeleteAt <= UtcNow; idempotent re-run finds 0; per-user transaction isolation |
| `tests/IntegrationTests/JadeCapital.Api.IntegrationTests/Gdpr/GdprCascadeIntegrationTests.cs` | End-to-end Testcontainers Postgres cascade | 1 scenario: register U1 → 7 aggregates across 3 modules → DELETE → assert IsDeleted=true; RunOnceAsync with clock+31d → assert 0 rows; 1 pseudonymized audit row |

#### Test stack

- **Unit tests**: NSubstitute for mocks + `FakeClock` (Wave 6 6d.2 precedent) for time travel + `Respawn` for DB checkpoint.
- **Integration tests**: `JadeApiFactory` (Testcontainers Postgres + Redis) + xUnit `[IClassFixture<JadeApiFactory>]` + `[RetryFact(3)]` on the integration test for sandbox resilience.

#### Sandbox gating

All new tests use `[Trait("Category", "RequiresDocker")]` + `Skip = !DockerAvailable`. The `JadeApiFactory` checks `DockerClient.Instance.IsAvailable()` at fixture init and throws `SkipException` if unavailable. CI runs all tests (Docker is available via `services: postgres, redis`); sandbox skips them gracefully.

#### Files

| Path | Action | Purpose |
|---|---|---|
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Cascade/UserCascadeDeleterOrchestratorTests.cs` | Create | 4 orchestrator scenarios |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Cascade/GdprAuditAnonymizerTests.cs` | Create | 2 anonymizer scenarios |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/BackgroundServices/HardDeleteSweepBackgroundServiceTests.cs` | Create | 3 BackgroundService scenarios (IClock-based) |
| `tests/IntegrationTests/JadeCapital.Api.IntegrationTests/Gdpr/GdprCascadeIntegrationTests.cs` | Create | 1 end-to-end cascade scenario (Testcontainers) |
| `tests/IntegrationTests/JadeCapital.Api.IntegrationTests/Infrastructure/JadeApiFactory.cs` | Modify (no functional change) | Add `RequiresDocker` trait support (Wave 4 precedent) |

---

### 2.2 Slice 11.2a — 0029 FK fix (`feature/wave11-0029-fk-fix`, PR #58, ~150 LOC)

**Closes gaps**: Critical #1 (0029 FK defect) + G-1 (sentinel user audit row)
**Spec delta**: None (the fix is mechanical — the FK defect is implementation-level, not spec-level)
**Target LOC**: 150 (single migration file + 1 apply-progress note) — **NO `size:exception`**

#### Root cause (recap)

`infrastructure/postgres/migrations/0029_backfill_personal_tenant.sql` line 50–52 references a sentinel user `00000000-0000-0000-0000-000000000002` for the Personal tenant's `owner_user_id` FK. The migration's own comment (line 36) acknowledges "será irrelevante porque no hay usuarios a backfillear" but the FK is statement-scoped — on a fresh Postgres container with `identity.users` empty, the INSERT into `identity.tenants` fails because the sentinel user doesn't exist.

#### Fix strategy (Option B per preflight Q10)

Modify `0029` to insert the sentinel user BEFORE the Personal tenant INSERT, all in one atomic transaction:

```sql
-- infrastructure/postgres/migrations/0029_backfill_personal_tenant.sql (modified)

BEGIN;

-- 1) Insert the sentinel "System" user (idempotent via ON CONFLICT).
--    This row is the FK target for the Personal tenant's owner_user_id.
--    The role enum value 3 ("System") is a placeholder; the sentinel
--    cannot login (random unguessable PBKDF2 hash). The audit row in
--    step 4 records the creation for compliance trail.
INSERT INTO identity.users (
    id, email, display_name, password_hash, role, status,
    is_deleted, created_at, updated_at, tenant_id
)
VALUES (
    '00000000-0000-0000-0000-000000000002'::uuid,
    'system@anonymized.local',
    'System',
    '$pbkdf2-sha256$29000$<random-base64>$<random-base64>', -- random unguessable
    3,                  -- UserRole.System
    0,                  -- UserStatus.Active
    false,
    now(),
    NULL,
    NULL                -- tenant_id set in step 3 after Personal tenant exists
)
ON CONFLICT (id) DO NOTHING;

-- 2) Insert the Personal tenant (unchanged behavior; the sentinel user exists now).
DO $$
DECLARE
    v_personal_id UUID := '11111111-1111-1111-1111-111111111111';
BEGIN
    INSERT INTO identity.tenants (id, name, slug, owner_user_id, plan, status, created_at, updated_at)
    VALUES (v_personal_id, 'Personal', 'personal-default',
            '00000000-0000-0000-0000-000000000002'::uuid, -- sentinel user FK
            0, 0, now(), NULL)
    ON CONFLICT (slug) DO NOTHING;

    -- 3) Backfill NULL users to Personal (unchanged).
    UPDATE identity.users
    SET tenant_id = v_personal_id, updated_at = now()
    WHERE tenant_id IS NULL;
END
$$;

-- 4) Audit row for sentinel user creation (G-1 closure).
--    Written AFTER the sentinel INSERT to ensure the row exists.
--    Uses raw SQL because AuditDbContext is in a separate DbContext
--    and we don't have a DbContext available in a raw migration.
INSERT INTO audit.events (
    id, entity_type, entity_id, action, tenant_id, user_id,
    changes_json, occurred_at
)
VALUES (
    gen_random_uuid(),
    'User',
    '00000000-0000-0000-0000-000000000002'::uuid,
    0,                  -- AuditAction.Created
    NULL,               -- no tenant (the sentinel is system-level)
    NULL,               -- no user (the system actor)
    '{"sentinelUser": true, "reason": "GDPR FK defect fix — Wave 11.2a"}'::jsonb,
    now()
);

-- 5) Re-apply 0028's NOT NULL constraint check (idempotent — the check
--    exits early if the column is already NOT NULL on existing DBs).
DO $$
DECLARE
    v_is_nullable TEXT;
BEGIN
    SELECT is_nullable INTO v_is_nullable
    FROM information_schema.columns
    WHERE table_schema = 'identity'
      AND table_name = 'users'
      AND column_name = 'tenant_id';

    IF v_is_nullable = 'NO' THEN
        RAISE NOTICE '0029_sentinel: tenant_id is already NOT NULL; skipping.';
        RETURN;
    END IF;

    ALTER TABLE identity.users ALTER COLUMN tenant_id SET NOT NULL;
END
$$;

COMMENT ON COLUMN identity.users.id IS
    'User GUID. Sentinel user 00000000-0000-0000-0000-000000000002 (role=System) '
    'is a permanent FK target for the Personal tenant (created in 0029). Cannot login.';

COMMIT;
```

#### Why Option B (single-file atomic)

- No renumbering required (0029 stays at 0029; 0028 stays at 0028).
- The sentinel user + Personal tenant + NOT NULL re-apply are atomic (single transaction).
- On existing DBs: the sentinel INSERT is a no-op (`ON CONFLICT DO NOTHING`), the Personal tenant INSERT is a no-op (`ON CONFLICT (slug) DO NOTHING`), the audit row INSERT is appended (no conflict — new `gen_random_uuid()`), the NOT NULL re-apply exits early (`v_is_nullable = 'NO'`).
- On a fresh DB: the sentinel is inserted first → the Personal tenant's FK is satisfied → the backfill works → the NOT NULL constraint is applied.

#### Sentinel user documentation

The migration header documents:
- `role = 3` (UserRole.System) — placeholder, no real role.
- Random unguessable PBKDF2 hash — cannot login (the system never presents a login form with email `system@anonymized.local`).
- The user row is permanent (never soft-deleted or hard-deleted; the GDPR cascade explicitly excludes it).
- The audit row records the creation timestamp + reason.

#### Verification

The fix is verified by the existing `scripts/verify-migration-order.sh` (Wave 10.4) which runs against a fresh Testcontainers Postgres + applies all 32 migrations in order + asserts every expected table exists. After the fix, this verifier MUST succeed on a fresh DB (where it currently fails at `0029`). Additionally, a new xUnit test `Migration_0029_Fresh_ApplyTests` verifies the migration applies on a fresh DB + asserts the sentinel user + Personal tenant exist + asserts the NOT NULL constraint is in place.

#### Files

| Path | Action | Purpose |
|---|---|---|
| `infrastructure/postgres/migrations/0029_backfill_personal_tenant.sql` | **Modify** | Insert sentinel user in atomic transaction + audit row + NOT NULL re-apply |
| `tests/IntegrationTests/JadeCapital.Api.IntegrationTests/Migrations/Migration_0029_Fresh_ApplyTests.cs` | Create | Verifies fresh-DB apply succeeds (1 scenario) |

---

### 2.3 Slice 11.2b — DELETE endpoint + UI (`feature/wave11-delete-account`, PR #59, ~730 LOC, **`size:exception`**)

**Closes gaps**: Item 1 (DELETE endpoint) + G-7 (account deletion UI)
**Spec deltas**: `specs/gdpr-endpoint-coverage/spec.md` (DELETE scenarios) + `specs/account-deletion-ui/spec.md` (3 FE scenarios)
**Target LOC**: 730 — **`size:exception`** (auth-critical + cascade-critical + multi-module blast radius)

#### BE endpoint flow

```
HTTP DELETE /api/users/me
   │
   ▼
UserEndpoints.MapDeleteAccount (RequireAuthorization)
   │
   ▼
MediatR → DeleteAccountCommand(userId from ITenantContext.CurrentUserId)
   │
   ▼
DeleteAccountHandler.HandleAsync
   │
   ├── 1. Load user from IdentityDbContext.Users (IgnoreQueryFilters)
   ├── 2. Validate user is Active (Status = 0, IsDeleted = false)
   ├── 3. Anonymize: user.Email = "deleted-<userId:N>@anonymized.local"
   │                  user.DisplayName = "Deleted User"
   │                  user.Status = ScheduledHardDelete (2)
   │                  user.ScheduledHardDeleteAt = clock.UtcNow + 30 days
   │                  user.IsDeleted = true
   │                  user.DeletedAtUtc = clock.UtcNow
   │                  user.DeletedByUserId = user.Id (self-delete recorded)
   ├── 4. Call UserCascadeDeleterOrchestrator.CascadeSoftDeleteAsync(userId, ct)
   │      → returns int totalTouched (sum of rows across 3 deletors)
   ├── 5. Revoke all refresh tokens for userId
   ├── 6. Emit 1 audit.events row: User/Deleted (changes = { anonymizedEmail, cascadeSoftDeletedRows })
   ├── 7. Persist via IUserRepository.UpdateAsync(user, ct) (the only allowed
   │      path through UserAuditDecorator that bypasses DeleteAsync's NotSupportedException)
   │
   ▼
Return Result.Success with { GracePeriodDays = 30, HardDeleteScheduledAt, CascadeSoftDeletedRows }
   │
   ▼
HTTP 202 Accepted with { gracePeriodDays: 30, hardDeleteScheduledAt: "<iso8601>", cascadeSoftDeletedRows: <int> }
```

#### Files

| Path | Action | Purpose |
|---|---|---|
| `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/DeleteAccount/DeleteAccountCommand.cs` | Create | MediatR command + `DeleteAccountResult` record |
| `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/DeleteAccount/DeleteAccountHandler.cs` | Create | Handler (anonymize + orchestrator + revoke + audit) |
| `src/2.Modules/Identity/JadeCapital.Identity.Api/Endpoints/UserEndpoints.cs` | Create | `MapDelete("/api/users/me")` + `.RequireAuthorization()` (NEW file — no UserEndpoints existed in Wave 10) |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Application/DeleteAccountHandlerTests.cs` | Create | 3 unit tests (anonymization correctness, orchestrator called, audit row emitted) |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Endpoints/DeleteAccountEndpointTests.cs` | Create | 1 endpoint test (202 + body shape) |
| `tests/IntegrationTests/JadeCapital.Api.IntegrationTests/Gdpr/DeleteAccountFlowTests.cs` | Create | 2 integration tests (full flow + cross-tenant 403) |
| `frontend/src/app/features/settings/settings-routing.module.ts` | Create | New settings feature module (lazy-loaded) |
| `frontend/src/app/features/settings/settings.page.ts` | Create | Tab shell + "Account" tab default |
| `frontend/src/app/features/settings/account-deletion/account-deletion.page.ts` | Create | The "Delete my account" page |
| `frontend/src/app/features/settings/account-deletion/account-deletion-confirmation.component.ts` | Create | Confirmation modal (email input + Confirm button) |
| `frontend/src/app/shared/services/account-deletion.service.ts` | Create | Angular service wrapping `DELETE /api/users/me` (Signal-based) |

#### Cross-tenant guard

`DeleteAccountCommand` resolves `userId` from `ITenantContext.CurrentUserId` (NOT from the request body). The endpoint reads `me` from the JWT (the `sub` claim). Cross-tenant attacks (attacker in T2 calls DELETE on their own `me` — which is T2User, not T1's U1) return 403 if the `ITenantContext.CurrentUserId` doesn't match the path's `{id = "me"}` resolution. Defense in depth.

#### Cascade contract

`DeleteAccountHandler` calls `UserCascadeDeleterOrchestrator.CascadeSoftDeleteAsync(userId, ct)` — the existing Wave 10.5 method. The orchestrator delegates to 3 per-module deletors (`IdentityUserCascadeDeletor`, `TradingUserCascadeDeletor`, `BillingUserCascadeDeletor`) in registration order with try/catch isolation. No new contract surface.

#### Audit row

The handler emits ONE `audit.events` row directly via `IAuditLogger.LogAsync` (NOT through `IUserRepository.UpdateAsync`'s `UserAuditDecorator` path which would emit `User/Updated` for the anonymization). The direct emission allows the `action = AuditAction.Deleted` (not `Updated`) and a custom `changes_json` shape:
```json
{
  "AnonymizedEmail": { "before": "u1@example.com", "after": "deleted-<userId>@anonymized.local" },
  "CascadeSoftDeletedRows": { "before": null, "after": <int> }
}
```

---

### 2.4 Slice 11.3 — Export endpoint + HardDeleteSweepOptions (`feature/wave11-export-options`, PR #60, ~400 LOC)

**Closes gaps**: Item 2 (export endpoint) + Item 6 (`HardDeleteSweepOptions` extraction) + G-2 (welcome email idempotency column)
**Spec deltas**: `specs/gdpr-endpoint-coverage/spec.md` (export scenarios) + `specs/hard-delete-sweep-options/spec.md` (4 scenarios)
**Target LOC**: 400 — **NO `size:exception`**

#### Export endpoint streaming flow

```
HTTP GET /api/users/me/export
   │
   ▼
ExportAccountDataEndpoint.MapGet("/api/users/me/export")
   │
   ▼
MediatR → ExportAccountDataQuery(userId from ITenantContext.CurrentUserId)
   │
   ▼
ExportAccountDataHandler.HandleAsync
   │
   ├── 1. Get the response body stream (HttpContext.Response.Body)
   ├── 2. Open Utf8JsonWriter on the stream
   ├── 3. WriteStartObject() + write each array:
   │      ├── "users": [1 item — the user record]
   │      ├── "accounts": IAsyncEnumerable<Account> from ITradingExportRepository.GetAccountsForUserAsync(userId, ct)
   │      ├── "trades": IAsyncEnumerable<Trade> from ITradingExportRepository.GetTradesForUserAsync(userId, ct)
   │      ├── "journals": IAsyncEnumerable<JournalEntry> from ITradingExportRepository.GetJournalsForUserAsync(userId, ct)
   │      ├── "trade_reviews": IAsyncEnumerable<TradeReview> from ITradingExportRepository.GetTradeReviewsForUserAsync(userId, ct)
   │      ├── "strategies": IAsyncEnumerable<Strategy> from ITradingExportRepository.GetStrategiesForUserAsync(userId, ct)
   │      ├── "alerts": IAsyncEnumerable<Alert> from ITradingExportRepository.GetAlertsForUserAsync(userId, ct)
   │      ├── "planner_sessions": IAsyncEnumerable<PlannerSession> from ITradingExportRepository.GetPlannerSessionsForUserAsync(userId, ct)
   │      ├── "pre_trade_checklists": IAsyncEnumerable<PreTradeChecklist> from ITradingExportRepository.GetPreTradeChecklistsForUserAsync(userId, ct)
   │      ├── "risk_profile": [1 item — the risk profile]
   │      ├── "subscription": [1 item — the subscription]
   │      ├── "stripe_customer": [1 item — the Stripe customer record]
   │      └── "settings": [1 item — the user settings]
   ├── 4. WriteEndObject() + flush Utf8JsonWriter + dispose
   │      (audit.events + stripe_webhook_events are EXCLUDED — compliance trail + system)
   │
   ▼
HTTP 200 OK with Content-Type: application/json + Content-Disposition: attachment
   + Transfer-Encoding: chunked (no buffering — IAsyncEnumerable streams)
```

#### Why IAsyncEnumerable (not buffered list)

GDPR Art. 20: "MUST stream without buffering the entire dataset in memory" (`gdpr-compliance/spec.md` line 46). A user with 10,000 trades should not OOM the API. The `IAsyncEnumerable<T>` per aggregate + `Utf8JsonWriter` flushes each entity as it's enumerated. The handler doesn't materialize any list.

#### `IUserDataExporter` abstraction (new interface)

```csharp
namespace JadeCapital.Identity.Application.Abstractions;

/// <summary>
/// Wave 11.3 — per-module user data exporter for GDPR Art. 20.
/// Each module that owns user-owned aggregates registers one implementation
/// of GetXxxForUserAsync (IAsyncEnumerable<T>) per aggregate. The
/// ExportAccountDataHandler aggregates them.
/// </summary>
public interface IUserDataExporter
{
    /// <summary>
    /// Returns a stream of entities owned by userId, filtered by tenant context.
    /// MUST bypass ISoftDelete filters (IgnoreQueryFilters) so the user sees
    /// their own soft-deleted data (per Art. 20 "all data concerning them").
    /// </summary>
    IAsyncEnumerable<T> GetXxxForUserAsync<T>(Guid userId, CancellationToken ct)
        where T : class;
}
```

Trading module registers `TradingUserDataExporter : IUserDataExporter` with implementations for Account, Trade, JournalEntry, TradeReview, Strategy, Alert, PlannerSession, PreTradeChecklist. Billing module registers `BillingUserDataExporter : IUserDataExporter` with Subscription + StripeCustomer. Identity module ships the single-record writers (user + risk_profile + settings) directly in the handler.

#### HardDeleteSweepOptions extraction

```csharp
// src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Configuration/HardDeleteSweepOptions.cs
namespace JadeCapital.Identity.Infrastructure.Configuration;

public sealed class HardDeleteSweepOptions
{
    public const string SectionName = "HardDeleteSweep";

    public int InitialDelaySeconds { get; set; } = 120;   // 2 min (matches Wave 10.5)
    public int IntervalHours { get; set; } = 24;          // 24h (matches Wave 10.5)
    public int MaxJitterMinutes { get; set; } = 30;       // 30 min (matches Wave 10.5)
    public int GracePeriodDays { get; set; } = 30;        // 30 days (matches Wave 10.5)
    public int BatchLimit { get; set; } = 100;            // 100 users/cycle (matches Wave 10.5)
}

public sealed class HardDeleteSweepOptionsValidator : IValidateOptions<HardDeleteSweepOptions>
{
    public ValidateOptionsResult Validate(string? name, HardDeleteSweepOptions options)
    {
        var errors = new List<string>();
        if (options.GracePeriodDays < 1 || options.GracePeriodDays > 365)
            errors.Add($"HardDeleteSweep:GracePeriodDays must be >= 1 and <= 365 (got {options.GracePeriodDays}).");
        if (options.IntervalHours < 1 || options.IntervalHours > 168)
            errors.Add($"HardDeleteSweep:IntervalHours must be >= 1 and <= 168 (got {options.IntervalHours}).");
        if (options.BatchLimit < 1 || options.BatchLimit > 10000)
            errors.Add($"HardDeleteSweep:BatchLimit must be >= 1 and <= 10000 (got {options.BatchLimit}).");
        if (options.MaxJitterMinutes < 0 || options.MaxJitterMinutes > 1440)
            errors.Add($"HardDeleteSweep:MaxJitterMinutes must be >= 0 and <= 1440 (got {options.MaxJitterMinutes}).");
        if (options.InitialDelaySeconds < 0 || options.InitialDelaySeconds > 3600)
            errors.Add($"HardDeleteSweep:InitialDelaySeconds must be >= 0 and <= 3600 (got {options.InitialDelaySeconds}).");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
```

#### HardDeleteSweepBackgroundService modification

Replace hardcoded constants with `IOptionsMonitor<HardDeleteSweepOptions>` reads:

```csharp
// BEFORE (Wave 10.5):
public const int GracePeriodDays = 30;
private const double MaxJitterMs = 30d * 60d * 1000d;
// ...
await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
// ...
await Task.Delay(TimeSpan.FromHours(24).Add(TimeSpan.FromMilliseconds(jitterMs)), stoppingToken);

// AFTER (Wave 11.3):
private readonly IOptionsMonitor<HardDeleteSweepOptions> _options;

public HardDeleteSweepBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<HardDeleteSweepOptions> options,
    ILogger<HardDeleteSweepBackgroundService> logger)
{
    _scopeFactory = scopeFactory;
    _options = options;
    _logger = logger;
}

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var opts = _options.CurrentValue;
    _logger.LogInformation("HardDeleteSweep started; first run in {Delay}s, grace={Days}d, jitter=[0,+{Jitter}min].",
        opts.InitialDelaySeconds, opts.GracePeriodDays, opts.MaxJitterMinutes);

    try
    {
        await Task.Delay(TimeSpan.FromSeconds(opts.InitialDelaySeconds), stoppingToken);
    }
    catch (OperationCanceledException) { return; }

    while (!stoppingToken.IsCancellationRequested)
    {
        var cycleOpts = _options.CurrentValue; // read fresh each cycle (hot reload support)
        await RunOnceAsync(cycleOpts, stoppingToken);
        if (stoppingToken.IsCancellationRequested) break;

        var jitterMs = Random.Shared.NextDouble() * cycleOpts.MaxJitterMinutes * 60d * 1000d;
        try
        {
            await Task.Delay(
                TimeSpan.FromHours(cycleOpts.IntervalHours).Add(TimeSpan.FromMilliseconds(jitterMs)),
                stoppingToken);
        }
        catch (OperationCanceledException) { break; }
    }
}

public async Task RunOnceAsync(CancellationToken ct)
    => await RunOnceAsync(_options.CurrentValue, ct);

private async Task RunOnceAsync(HardDeleteSweepOptions opts, CancellationToken ct)
{
    // ... use opts.GracePeriodDays, opts.BatchLimit, etc.
}
```

The defaults match Wave 10.5 EXACTLY — verified by `HardDeleteSweepBackgroundServiceOptionsTests` behavior-parity test.

#### Files

| Path | Action | Purpose |
|---|---|---|
| `src/2.Modules/Identity/JadeCapital.Identity.Application/Abstractions/IUserDataExporter.cs` | Create | Streaming exporter interface |
| `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/ExportAccountData/ExportAccountDataQuery.cs` | Create | MediatR query + result type |
| `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/ExportAccountData/ExportAccountDataHandler.cs` | Create | Streaming JSON writer (IAsyncEnumerable) |
| `src/2.Modules/Identity/JadeCapital.Identity.Api/Endpoints/ExportAccountDataEndpoint.cs` | Create | `MapGet("/api/users/me/export")` + `Results.Stream` |
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Export/TradingUserDataExporter.cs` | Create | 8 aggregates via IAsyncEnumerable |
| `src/2.Modules/Billing/JadeCapital.Billing.Application/Export/BillingUserDataExporter.cs` | Create | Subscription + StripeCustomer via IAsyncEnumerable |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Configuration/HardDeleteSweepOptions.cs` | Create | Options POCO + validator |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/BackgroundServices/HardDeleteSweepBackgroundService.cs` | Modify | Replace hardcoded constants with `IOptionsMonitor` |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/DependencyInjection/IdentityModuleRegistration.cs` | Modify | Wire `services.Configure<HardDeleteSweepOptions>(...)` + `ValidateOnStart` |
| `infrastructure/postgres/migrations/0038_add_welcome_email_sent_at.sql` | Create | `ALTER TABLE identity.users ADD COLUMN IF NOT EXISTS welcome_email_sent_at TIMESTAMPTZ NULL` (idempotent) |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Application/ExportAccountDataHandlerTests.cs` | Create | 3 export scenarios (includes 12 entities, excludes audit/stripe_webhook, cross-tenant 403) |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Configuration/HardDeleteSweepOptionsValidatorTests.cs` | Create | 2 scenarios (defaults valid + out-of-range rejected) |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/BackgroundServices/HardDeleteSweepBackgroundServiceOptionsTests.cs` | Create | 2 scenarios (parity + hot reload via `IOptionsMonitor.OnChange`) |

---

### 2.5 Slice 11.4 — Cookie consent + ToS + welcome email + docs (`feature/wave11-consent-docs`, PR #61, ~950 LOC)

**Closes gaps**: Item 3 (cookie consent FE) + Item 4 (ToS + Privacy FE) + Item 5 (welcome email trigger) + G-4 (ToS acceptance columns) + G-8 (GDPR docs runbook) + G-9 (CHANGELOG cross-check) + G-10 (Stripe env var alignment) + G-11 (auto-gen marker cleanup)
**Spec deltas**: `specs/consent-acceptance/spec.md` + `specs/email-deliverability/spec.md` + `specs/account-deletion-ui/spec.md` + `specs/gdpr-ops-runbook/spec.md`
**Target LOC**: 950 — **NO `size:exception`** (close to 800-line cap but work is mostly docs + FE which is lower-risk per Wave 10 precedent; the 4 new tests are small)

#### Cookie consent FE architecture

```
app.component.html
   │
   ├── <router-outlet> (main app shell)
   │
   └── <app-cookie-consent-banner /> (standalone component, lazy-rendered)
            │
            └── CookieConsentService (Signal-based)
                  ├── canLoadAnalytics: Signal<boolean> (derived from choice)
                  ├── choice: WritableSignal<'all' | 'essential' | null>
                  │
                  ├── On init: read localStorage.jade.consent
                  │   - if present: choice.set(consent.choice)
                  │   - if absent: choice.set(null) → banner shows
                  │
                  ├── setChoice(choice: 'all' | 'essential'):
                  │   ├── POST /api/auth/consent { choice, consentIp }
                  │   ├── write localStorage.jade.consent = { choice, acceptedAt }
                  │   └── choice.set(choice) → banner hides
                  │
                  └── canLoadAnalytics: computed(() => choice() === 'all')
```

The bottom-bar uses CSS `position: fixed; bottom: 0; left: 0; right: 0` so it doesn't reflow the main app shell above-the-fold. The banner is non-blocking init (after main shell loads).

#### ToS + Privacy Policy FE pages

Both pages follow the same shape:
- `frontend/src/app/features/legal/terms-of-service.page.ts` (~50 LOC) — Angular standalone component with `<!-- TODO: legal copy -->` placeholder + visible banner "LEGAL COPY PLACEHOLDER — DO NOT DEPLOY TO PRODUCTION WITHOUT LEGAL REVIEW".
- `frontend/src/app/features/legal/privacy-policy.page.ts` (~50 LOC) — same shape.
- `frontend/src/assets/legal/terms-of-service.md` (TODO placeholders) + `frontend/src/assets/legal/privacy-policy.md` (TODO placeholders).
- `frontend/src/app/features/legal/legal-routing.module.ts` (lazy-loaded routes `/legal/terms`, `/legal/privacy`).

The pages are gated on the new `AcceptTerms` + `AcceptPrivacy` flags from `RegisterUserCommand` (the BE rejects registration without both flags).

#### Welcome email trigger in RegisterUserHandler

```csharp
// src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/Register/RegisterUserHandler.cs (extend)

public async Task<Result<RegisterUserResponse>> HandleAsync(RegisterUserCommand cmd, CancellationToken ct)
{
    // ... existing registration logic (validation + AddAsync + token issuance) ...

    // After DB commit, send welcome email (idempotent via WelcomeEmailSentAt).
    try
    {
        if (user.WelcomeEmailSentAt == null || user.WelcomeEmailSentAt < _clock.UtcNow - TimeSpan.FromDays(7))
        {
            await _emailSender.SendAsync(
                to: user.Email,
                subject: "Welcome to Jade Capital",
                htmlBody: WelcomeEmailTemplate.Html(user.DisplayName),
                textBody: WelcomeEmailTemplate.Text(user.DisplayName),
                ct);

            user.WelcomeEmailSentAt = _clock.UtcNow;
            await _userRepository.UpdateAsync(user, ct);
            _logger.LogInformation("Welcome email sent for user {UserId}.", user.Id);
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Welcome email failed for user {UserId}; registration NOT rolled back.", user.Id);
    }

    return Result.Success(new RegisterUserResponse(user.Id, /* ... */));
}
```

`IEmailSender` is wired in `Program.cs` line ~97 (existing — uses Mailpit dev / SMTP prod). The handler is the ONLY new dependency on `IEmailSender`; the welcome email template is inline (`WelcomeEmailTemplate.cs` in `Identity.Application/Features/Auth/Register/`).

#### Consent columns migrations

```sql
-- infrastructure/postgres/migrations/0039_add_consent_columns.sql
-- Wave 11.4 — ToS + Privacy acceptance columns.

BEGIN;

ALTER TABLE identity.users
    ADD COLUMN IF NOT EXISTS terms_accepted_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS privacy_accepted_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS consent_ip INET NULL;

COMMENT ON COLUMN identity.users.terms_accepted_at IS
    'Timestamp of ToS acceptance on register. NULL = not accepted (backfill safe — existing users are pre-ToS).';
COMMENT ON COLUMN identity.users.privacy_accepted_at IS
    'Timestamp of Privacy Policy acceptance on register. NULL = not accepted.';
COMMENT ON COLUMN identity.users.consent_ip IS
    'IP address of the request that captured the acceptance (audit trail).';

COMMIT;
```

```sql
-- infrastructure/postgres/migrations/0040_add_cookie_consent_columns.sql
-- Wave 11.4 — Cookie consent columns.

BEGIN;

ALTER TABLE identity.users
    ADD COLUMN IF NOT EXISTS cookie_consent_accepted_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS cookie_consent_choice SMALLINT NULL;

COMMENT ON COLUMN identity.users.cookie_consent_accepted_at IS
    'Timestamp of the most recent cookie consent decision. NULL = never consented.';
COMMENT ON COLUMN identity.users.cookie_consent_choice IS
    'Cookie consent choice enum: 0=Essential, 1=AcceptAll. NULL = no decision.';

COMMIT;
```

#### Runbooks

- `docs/runbooks/gdpr-data-subject-request.md` (~100 LOC) — DSAR intake → cascade deletion → 30-day grace → restoration procedure → sweep → audit pseudonymization. Per `specs/gdpr-ops-runbook/spec.md` Scenario.
- `docs/runbooks/email-deliverability.md` (~120 LOC) — SPF / DKIM / DMARC DNS records + DKIM rotation cadence + Mailgun / SES / SendGrid / Postmark env-var mappings.
- `docs/email-deliverability.md` (~50 LOC) — resurrected from Wave 10.5 deferral; high-level summary that links to the runbook.

#### CHANGELOG cross-check

`CHANGELOG.md` Wave 0-7 entries are validated against `openspec/changes/archive/2026-08-15-*` through `2026-08-18-wave10-v1-readiness/` proposal.md files. Factual corrections only — `[Corrected]` markers appended to changed entries. No full rewrite.

#### Stripe env var alignment

`src/1.Api/JadeCapital.Host/Configuration/StripeOptions.cs`:
- Rename `ApiKey` property to `SecretKey` (matches `Stripe__SecretKey` env var).
- Add `[Obsolete("Use StripeOptions.SecretKey instead. The ApiKey alias bridge will be removed in v1.1.")]` on a static `ApiKey` property that delegates to `SecretKey` for backwards compat.
- `IValidateOptions<StripeOptions>` already validates `SecretKey` (Wave 10.6) — no change.

#### `<auto-generated-by>` marker cleanup

`src/1.Api/JadeCapital.Host/Configuration/DockerSecretConfigurationProvider.cs` line 1: remove the marker. The file is hand-written code (the marker was a copy-paste from Wave 10.2). Verified by Wave 10.6's same fix on `StripeOptions.cs`.

#### Files

| Path | Action | Purpose |
|---|---|---|
| `frontend/src/app/shared/cookie-consent/cookie-consent.component.ts` | Create | Bottom-bar standalone component |
| `frontend/src/app/shared/cookie-consent/cookie-consent.service.ts` | Create | Signal-based service + localStorage + POST |
| `frontend/src/app/shared/cookie-consent/cookie-consent.component.spec.ts` | Create | 3 jest unit tests |
| `frontend/src/app/features/legal/legal-routing.module.ts` | Create | Lazy-loaded routes |
| `frontend/src/app/features/legal/terms-of-service.page.ts` | Create | Placeholder + TODO marker + warning banner |
| `frontend/src/app/features/legal/privacy-policy.page.ts` | Create | Same shape |
| `frontend/src/app/features/settings/settings-routing.module.ts` | Create | Settings feature area |
| `frontend/src/app/features/settings/settings.page.ts` | Create | Tab shell + Account tab |
| `frontend/src/assets/legal/terms-of-service.md` | Create | Legal copy placeholder |
| `frontend/src/assets/legal/privacy-policy.md` | Create | Legal copy placeholder |
| `infrastructure/postgres/migrations/0039_add_consent_columns.sql` | Create | ToS acceptance columns (idempotent) |
| `infrastructure/postgres/migrations/0040_add_cookie_consent_columns.sql` | Create | Cookie consent columns (idempotent) |
| `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/Consent/ConsentCommand.cs` | Create | MediatR command |
| `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/Consent/ConsentHandler.cs` | Create | Updates `users.cookie_consent_*` |
| `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/Consent/WelcomeEmailTemplate.cs` | Create | Inline HTML + text template |
| `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/Register/RegisterUserHandler.cs` | Modify | Add welcome email trigger |
| `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/Register/RegisterUserCommand.cs` | Modify | Add `AcceptTerms` + `AcceptPrivacy` + `ConsentIp` fields |
| `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/Register/RegisterUserCommandValidator.cs` | Create (or modify if exists) | FluentValidation rules |
| `src/1.Api/JadeCapital.Host/Configuration/StripeOptions.cs` | Modify | Rename `ApiKey` → `SecretKey` + obsolete alias bridge |
| `src/1.Api/JadeCapital.Host/Configuration/DockerSecretConfigurationProvider.cs` | Modify | Remove `<auto-generated-by>` marker |
| `docs/runbooks/gdpr-data-subject-request.md` | Create | DSAR runbook |
| `docs/runbooks/email-deliverability.md` | Create | SPF/DKIM/DMARC runbook |
| `docs/email-deliverability.md` | Create | Resurrected high-level summary |
| `CHANGELOG.md` | Modify | Wave 0-7 archive cross-check (factual corrections) |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Application/ConsentHandlerTests.cs` | Create | 5 scenarios (cookie + ToS + privacy + consent_ip + cross-tenant) |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Application/RegisterWelcomeEmailTests.cs` | Create | 3 scenarios (first send + idempotent re-send + 7-day suppression) |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Application/RegisterTermsAcceptanceTests.cs` | Create | 2 scenarios (ToS required + acceptance persisted) |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Docs/EmailDeliverabilityDocsTests.cs` | Create | 1 content-sanity test (DNS record substrings present) |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Docs/GdprOpsRunbookTests.cs` | Create | 1 content-sanity test (DSAR runbook structure) |

---

## 3. Cross-cutting patterns

### 3.1 GDPR cascade + audit anonymization (no change)

The Wave 10.5 pattern (`IUserCascadeDeletor` + `UserCascadeDeleterOrchestrator` + `IGdprAuditAnonymizer`) is the canonical contract. Wave 11 11.2b's `DeleteAccountHandler` is the only new consumer; it does NOT add new methods to the interface. Wave 11 11.3's `HardDeleteSweepBackgroundService` modification preserves the existing `RunOnceAsync(CancellationToken)` signature (now delegates to `RunOnceAsync(HardDeleteSweepOptions, CancellationToken)` internally — public signature unchanged for backwards compat with existing tests).

### 3.2 HardDeleteSweepOptions (mirrors Wave 9 9b.1 AuditRetentionOptions)

```csharp
// Wave 9 9b.1 AuditRetentionOptions pattern (the precedent):
services.Configure<AuditRetentionOptions>(configuration.GetSection("AuditRetention"));
services.AddOptions<AuditRetentionOptions>()
    .ValidateOnStart()
    .Validate(/* validator */);
```

Wave 11 11.3 mirrors this pattern for `HardDeleteSweepOptions`. The validator is `HardDeleteSweepOptionsValidator : IValidateOptions<HardDeleteSweepOptions>` — same shape as `AuditRetentionOptionsValidator`. The BackgroundService uses `IOptionsMonitor<T>` (NOT `IOptions<T>`) for hot-reload support via `IConfigurationRoot.Reload()`.

### 3.3 Cookie consent persistence (localStorage + DB column sync)

The FE service reads `localStorage.jade.consent` first (fast — no network round-trip). On first visit (no localStorage), the banner shows. On click, the service writes to localStorage + POSTs to `/api/auth/consent` (writes to `users.cookie_consent_*` columns). On subsequent visits, the localStorage read returns the persisted choice; the banner hides.

The DB column sync is fire-and-forget — if the POST fails, the localStorage choice still persists (the user has indicated their choice; the server just hasn't logged it yet). A separate retry mechanism is out of scope for v1.0.

### 3.4 Welcome email idempotency (`users.welcome_email_sent_at` + 7-day suppression)

The handler checks `user.WelcomeEmailSentAt` BEFORE calling `IEmailSender.Send`:
- `null` → send (first register).
- `< UtcNow - 7d` → send (suppression expired; re-register is allowed).
- `>= UtcNow - 7d` → skip (suppressed; re-register within 7 days does NOT re-send).

After a successful send, the handler updates `user.WelcomeEmailSentAt = clock.UtcNow` + persists via `_userRepository.UpdateAsync(user, ct)`. The email-send is wrapped in `try { ... } catch (Exception ex) { _logger.LogError(...) }` so email failure does NOT roll back the registration.

---

## 4. Risks + mitigations

| Risk | Likelihood | Mitigation |
|---|---|---|
| 11.1 Testcontainers may not be available in CI sandbox | Med | `Skip = !DockerAvailable` xUnit trait + `[RetryFact(3)]` on the integration test. The existing `JadeApiFactory` throws `SkipException` if Docker is unavailable. CI has `services: postgres, redis` per Wave 10.1. |
| 11.2a 0029 migration split could break an existing dev DB mid-update | Low | Forward-only; existing DBs see the sentinel INSERT as a no-op (`ON CONFLICT (id) DO NOTHING`). The 0028 NOT NULL re-apply exits early if already NOT NULL. |
| 11.2b DELETE endpoint regresses Wave 6 6c.2 tenant backfill tests | Low | `DeleteAccountHandler` does NOT touch `tenants` — only `identity.users` + per-module deletors + audit. The 6c.2 tests are untouched. |
| 11.2b Multi-module cascade blast radius (auth-critical + cascade-critical) | High | The behavior-parity test in 11.1 + the GDPR cascade integration test (full cascade + 30-day grace + audit anonymization) catch regressions BEFORE merge. PR description explicitly justifies `size:exception` per Wave 5/6/7/8/9/10 precedent. |
| 11.3 Export endpoint memory-buffers the entire dataset | Med | `Results.Stream` + `IAsyncEnumerable<T>` + `Utf8JsonWriter`. xUnit verifies `Transfer-Encoding: chunked` header. The handler never materializes a list. |
| 11.3 `HardDeleteSweepOptions` extraction changes default values accidentally | Low | Behavior-parity test asserts identical behavior between hardcoded Wave 10.5 values + `IOptions<HardDeleteSweepOptions>` defaults. The validator rejects out-of-range values at startup. |
| 11.4 Cookie consent banner blocks first-paint | Low | Banner is rendered in `app.component.html` AFTER main shell loads; uses CSS `position: fixed; bottom: 0` so it doesn't reflow above-the-fold. |
| 11.4 ToS acceptance must not break Wave 0 register flow | Low | Backwards-compatible: missing acceptance is "implicit decline" (registration fails with 422 `auth.terms_required`). Existing Wave 0 register flow used a placeholder flag (always `true` in tests) — verify existing tests pass with the new validator (Wave 0 register tests assert registration succeeds; update them to include `AcceptTerms = true, AcceptPrivacy = true`). |
| 11.4 Export endpoint could leak PII if module mapping is incomplete | Low | Explicit per-aggregate JSON serialization (typed `IAsyncEnumerable<T>` per entity). `audit.events` + `stripe_webhook_events` are EXCLUDED via the handler's explicit array list (NOT via filtering — explicit). |
| 11.4 `<auto-generated-by>` removal on `DockerSecretConfigurationProvider.cs` may re-introduce CS8669 | Low | The file is hand-written code; verified by Wave 10.6's same fix on `StripeOptions.cs`. |

---

## 5. Open architectural questions

**None remaining** — all 10 questions answered in the preflight (sentinel role, audit anonymization, ToS placeholders, cookie UI position, export format, export streaming, ToS columns, welcome email idempotency, cookie consent columns, 0029 FK fix strategy) + the 6 design decisions in the proposal (`hard-delete-sweep-options` spec §7.2).

The only remaining decision is operational (not architectural):
- **Stripe email provider for v1.0 GA**: Mailgun / SES / SendGrid / Postmark? Ops team decides at deploy time. The env-var mapping in `docs/runbooks/email-deliverability.md` covers all 4 providers.

---

## 6. Migration / Rollout

**No phased rollout** — Wave 11 ships as a single feature branch chain (5 PRs) into `feature/wave10-v1-readiness`. The 32 existing migrations + 4 new migrations (0029 modified, 0038 + 0039 + 0040 new) land as a single forward-only chain. The Testcontainers integration test in 11.1 verifies all 32 + 4 new migrations apply in order on a fresh DB.

**Rollback**: `git revert` the merge commit per slice. The cascade pattern + endpoints + UI revert independently (each slice is a separate PR). The 0029 sentinel user + audit row are forward-only (a revert removes the sentinel INSERT but the user row may already exist — manually delete via `DELETE FROM identity.users WHERE id = '00000000-0000-0000-0000-000000000002';` after revert).

**`v1.0.0` GA tag**: lands after PR #61 (11.4) merges AND sdd-verify PASS. No `rc` suffix.

---

## 7. Out of scope (post-v1.0)

Wave 12+ (a11y + OTel + Sentry + per-request CSP nonce + Art. 18 right-to-restrict + pre-hard-delete warning email + multi-region coordination).

Wave 13+ (E2E Playwright + wal-g continuous WAL archiving + audit partitioning by month + bulk admin hard-delete + DSAR intake form).

All explicitly listed in `proposal.md` §4.
