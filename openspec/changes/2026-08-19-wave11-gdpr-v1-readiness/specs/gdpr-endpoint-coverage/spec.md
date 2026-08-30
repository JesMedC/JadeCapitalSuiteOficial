# Delta for gdpr-compliance — Wave 11 GDPR Endpoint Coverage

**Change**: 2026-08-19-wave11-gdpr-v1-readiness
**Wave**: 11 (GDPR v1 readiness — closure of Wave 10.5 narrower scope)
**Slices**: 11.1 (GDPR cascade xUnit coverage), 11.2b (DELETE endpoint + UI), 11.3 (export endpoint + HardDeleteSweepOptions)
**Status**: DELTA — extends `openspec/specs/gdpr-compliance/spec.md` (Wave 10.5 canonical) with the 13 xUnit coverage scenarios that were deferred from Wave 10.5
**Strict TDD**: ACTIVE — every Requirement + Scenario here MUST be covered by tests in slice 11.1 / 11.2b / 11.3

## MODIFIED Requirements

### Requirement: DELETE /api/users/me triggers GDPR Art. 17 cascade (test coverage delta)

(Previously: the requirement existed in `openspec/specs/gdpr-compliance/spec.md` line 15–34 with 2 scenarios. The behavior was specified but had ZERO xUnit coverage post-Wave 10.5. Wave 11.1 + 11.2b add the 6 coverage scenarios below.)

#### Scenario: xUnit — DELETE returns 202 Accepted + starts cascade (integration)

- GIVEN a registered user U1 with 1 trade + 1 journal entry + 1 risk profile persisted in Testcontainers Postgres
- WHEN U1 calls `DELETE /api/users/me` via the HTTP client
- THEN the response MUST be 202 Accepted with `{ "gracePeriodDays": 30, "hardDeleteScheduledAt": "<UtcNow+30d>" }`
- AND `users.is_deleted` MUST be `true` (verified via `IgnoreQueryFilters()`)
- AND `users.status` MUST equal `ScheduledHardDelete` (2)
- AND `users.scheduled_for_hard_delete_at` MUST be `UtcNow + 30d` ± 1 second
- AND every row in `trading.trades` WHERE `user_id = U1.Id` MUST have `is_deleted = true`
- AND every row in `trading.journal_entries` WHERE `user_id = U1.Id` MUST have `is_deleted = true`
- AND `audit.events` MUST contain a `User/Deleted` row with `user_id = U1.Id`, `changes = { "AnonymizedEmail": { "before": "<original>", "after": "deleted-<userId>@anonymized.local" } }`

#### Scenario: xUnit — 30-day grace period elapses → hard delete (integration + clock injection)

- GIVEN U1 called DELETE on day 0 (Status=ScheduledHardDelete, ScheduledHardDeleteAt=UtcNow+30d)
- WHEN `HardDeleteSweepBackgroundService.RunOnceAsync` runs with `IClock.UtcNow = UtcNow + 31d`
- THEN every row referencing `U1.Id` MUST be physically purged from the DB (verified via `IgnoreQueryFilters()` + `Count == 0`)
- AND `audit.events` MUST retain ONE pseudonymized row (`user_id = NULL`, `entity_type = "User"`, `action = "Deleted"`, `changes.hardDelete.original_user_id_hash = "<sha256(U1.Id)>"`)
- AND the pseudonymized row MUST be queryable by `entity_type = 'User' AND entity_id = 'deleted_user_<sha256>'` (deterministic — same user → same hash)

#### Scenario: xUnit — HardDeleteSweep is idempotent (no double-purge)

- GIVEN U1 was hard-deleted in a previous sweep run
- WHEN `HardDeleteSweepBackgroundService.RunOnceAsync` runs again immediately after
- THEN the query MUST return 0 rows (U1.Id is physically gone — `IdentityDbContext.Users.Where(u => u.Id == U1.Id).CountAsync() == 0`)
- AND NO further `audit.events` rows MUST be written
- AND a Serilog `Debug` log MUST record `HardDeleteSweep: no users due for hard-delete in this cycle.`

#### Scenario: xUnit — HardDeleteSweep per-user transaction isolation

- GIVEN 3 users (U1, U2, U3) all due for hard-delete; U2's `CascadeHardDeleteAsync` throws `InvalidOperationException("simulated module failure")` mid-cascade
- WHEN `RunOnceAsync` runs
- THEN U1 MUST be hard-deleted successfully (1 audit row, 1 user row purged)
- AND U2's hard-delete MUST fail logged (Serilog `Error` log + exception captured), U2's user row MUST remain in DB
- AND U3 MUST be hard-deleted successfully (the orchestrator continues past U2's failure)
- AND the per-user transaction isolation MUST prevent U2's failure from aborting U1 + U3

#### Scenario: xUnit — GdprAuditAnonymizer preserves queryability (deterministic hash)

- GIVEN U1 has 5 audit.events rows with `user_id = U1.Id`
- WHEN `IGdprAuditAnonymizer.AnonymizeUserAsync(U1.Id, ct)` runs
- THEN all 5 rows MUST have `user_id = NULL`
- AND all 5 rows MUST have `entity_id = 'deleted_user_<sha256(U1.Id)>'` (deterministic — same hash on re-run)
- AND all 5 rows MUST have `changes_json->'hardDelete'->>'original_user_id_hash' = '<sha256(U1.Id)>'`
- AND the rows MUST be queryable by `entity_type = 'User' AND entity_id = 'deleted_user_<sha256>'`

#### Scenario: xUnit — DELETE endpoint cross-tenant returns 403

- GIVEN U1 belongs to tenant T1; an attacker in tenant T2 holds a valid JWT
- WHEN the attacker calls `DELETE /api/users/me` (their own `me`, not U1's) — but the test verifies that even cross-tenant `user_id` lookups return 403
- THEN the response MUST be 403 Forbidden with `error.code = "auth.cross_tenant"`
- AND NO cascade MUST fire (U1's user row in T1 MUST remain untouched)

### Requirement: GET /api/users/me/export streams user data (GDPR Art. 20 — test coverage delta)

(Previously: the requirement existed in `openspec/specs/gdpr-compliance/spec.md` line 36–52 with 2 scenarios. The endpoint was not implemented in Wave 10.5. Wave 11.3 ships the endpoint + the 3 coverage scenarios below.)

#### Scenario: xUnit — export includes 12 entities (integration + JSON parse)

- GIVEN U1 has 5 trades, 3 journal entries, 2 strategies, 1 risk profile, 1 account, 1 subscription
- WHEN U1 calls `GET /api/users/me/export`
- THEN the response Content-Type MUST be `application/json`
- THEN the response Content-Disposition MUST be `attachment; filename="jadecapital-export-{userId}.json"`
- AND the parsed JSON object MUST contain: `trades: [5 items]`, `journals: [3 items]`, `strategies: [2 items]`, `accounts: [1 item]`, `risk_profile: { ... }`, `subscription: { ... }`, and arrays for every other entity listed in the requirement (12 entities total)
- AND the response MUST stream via `Transfer-Encoding: chunked` (verified via response header)

#### Scenario: xUnit — export EXCLUDES audit.events + stripe_webhook_events

- GIVEN `audit.events` has 250 rows for U1, `stripe_webhook_events` has 12 rows for U1
- WHEN U1 calls `GET /api/users/me/export`
- THEN the parsed JSON object MUST NOT contain an `audit_events` field
- AND the parsed JSON object MUST NOT contain a `stripe_webhook_events` field
- AND the response size MUST be < 100 KB (250 audit rows × ~400 bytes each would be ~100 KB — excluding them keeps the response small)

#### Scenario: xUnit — export cross-tenant returns 403

- GIVEN U1 belongs to tenant T1; an attacker in tenant T2 holds a valid JWT
- WHEN the attacker calls `GET /api/users/me/export` (the attacker's `me`)
- THEN the response MUST be 403 Forbidden
- AND the attacker MUST NOT see any of U1's data (verified by JSON parse — `trades: []`, etc.)
- AND NO `audit.events` row MUST be written for the failed attempt (export is read-only; not audited)

### Requirement: UserCascadeDeleterOrchestrator composition (test coverage delta — Wave 11.1)

(Previously: the orchestrator + `IUserCascadeDeletor` interface shipped from Wave 10.5 with zero xUnit coverage. Wave 11.1 adds the 4 coverage scenarios below.)

#### Scenario: xUnit — orchestrator invokes all registered deletors in registration order

- GIVEN 3 mock `IUserCascadeDeletor` implementations registered (Identity, Trading, Billing) — each returns `0` from `CascadeSoftDeleteAsync` + tracks call order
- WHEN `UserCascadeDeleterOrchestrator.CascadeSoftDeleteAsync(userId, ct)` is called
- THEN all 3 deletors MUST be invoked in registration order (Identity → Trading → Billing)
- AND the return value MUST be `0` (sum of all 3)

#### Scenario: xUnit — per-deletor exception is logged + does not abort batch

- GIVEN 3 mock deletors; the Trading mock throws `InvalidOperationException("simulated Trading module failure")`
- WHEN `CascadeSoftDeleteAsync(userId, ct)` is called
- THEN the Identity deletor MUST run successfully
- AND the Trading exception MUST be logged (Serilog `Error` log + exception captured)
- AND the Billing deletor MUST still run after Trading's failure (the batch continues)
- AND the orchestrator MUST NOT re-throw (per-deletor exceptions are swallowed)

#### Scenario: xUnit — orchestrator CascadeHardDeleteAsync invokes audit anonymizer + physical user-row DELETE in correct order

- GIVEN 3 mock deletors + 1 mock `IGdprAuditAnonymizer` + a `physicalUserRowDeleteAsync` delegate
- WHEN `CascadeHardDeleteAsync(userId, physicalUserRowDeleteAsync, ct)` is called
- THEN the 3 deletors MUST run first (in registration order)
- AND `IGdprAuditAnonymizer.AnonymizeUserAsync(userId, ct)` MUST run AFTER all deletors (the per-user audit events remain attributable during the cascade)
- AND `physicalUserRowDeleteAsync(ct)` MUST run LAST (FK constraint requires FK-referencing rows gone first)
- AND each step's exception MUST be logged + swallowed (the orchestrator continues past any single failure)

#### Scenario: xUnit — GDPR cascade end-to-end (integration — the BIG one)

- GIVEN a Testcontainers Postgres with all 32 migrations applied via `JadeApiFactory`
- AND U1 registered with: 1 Trade, 1 JournalEntry, 1 Account, 1 Strategy, 1 RiskProfile, 1 Subscription, 1 StripeCustomer (7 user-owned aggregates across Identity + Trading + Billing)
- WHEN `CascadeSoftDeleteAsync(U1.Id, ct)` is called
- THEN all 7 aggregates MUST have `is_deleted = true` (verified via `IgnoreQueryFilters()`)
- AND `IdentityDbContext.Users.Where(u => u.Id == U1.Id).Count() == 1` (user row not physically deleted yet)
- AND `HardDeleteSweepBackgroundService.RunOnceAsync(ct)` runs with `IClock.UtcNow = UtcNow + 31d`
- THEN all 7 aggregates MUST have 0 rows (physically purged)
- AND `IdentityDbContext.Users.Where(u => u.Id == U1.Id).Count() == 0` (user row physically deleted)
- AND `audit.events` MUST contain 1 pseudonymized row per the GdprAuditAnonymizer scenario above

## Cross-references

- Companion spec: `openspec/specs/gdpr-compliance/spec.md` (canonical Wave 10.5 — the behavior)
- Companion spec: `openspec/specs/account-lifecycle/spec.md` (the lifecycle states + sweep)
- Related Wave 11 spec: `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/specs/hard-delete-sweep-options/spec.md` (11.3's DELTA on HardDeleteSweepOptions)
- Source: `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/explore.md` §"Deferred items validation" items 1, 2, 7 + §"CRITICAL fixes validation §2"
- Test infrastructure: `tests/IntegrationTests/JadeCapital.Api.IntegrationTests/Infrastructure/JadeApiFactory.cs` (Testcontainers Postgres + Respawn — Wave 10.1)

## Out of scope

- Right-to-restrict-processing (GDPR Art. 18) — distinct endpoint, Wave 12+
- DSAR intake form beyond the manual runbook (11.4's `docs/runbooks/gdpr-data-subject-request.md` covers the manual flow)
- Real-time consent revocation via email header (`List-Unsubscribe`) — Wave 12+
- E2E Playwright tests for the full cascade flow (Wave 13+)
