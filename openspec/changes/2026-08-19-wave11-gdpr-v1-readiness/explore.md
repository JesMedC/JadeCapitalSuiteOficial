# Explore — Wave 11 (GDPR v1 readiness)

## Intent

Wave 11 closes the two **CRITICAL risks** blocking the `v1.0.0` GA tag (`0029_backfill_personal_tenant.sql` FK defect + zero Testcontainers coverage for the GDPR cascade) and ships the deferred GDPR Art. 17 + Art. 20 endpoint surface (DELETE/GET `/api/users/me`), the cookie consent + ToS/Privacy FE work, the welcome email trigger, and the mechanical `HardDeleteSweepOptions` extraction. Target: `v1.0.0` GA tag at end of Wave 11.

## Context

- **Wave 10 archive**: `openspec/changes/2026-08-18-wave10-v1-readiness/archive-report.md` (104 lines, shipped `v1.0.0-rc1` @ `8d40394`)
- **Spec canon already promoted**: `openspec/specs/gdpr-compliance/spec.md` (10 scenarios) + `openspec/specs/account-lifecycle/spec.md` (6 scenarios) — both shipped Wave 10.5 with **narrower scope** (pattern only, no endpoints/tests)
- **Migration count check** (user flagged "verify count is 37 not 38"): **actual count = 32** SQL files in `infrastructure/postgres/migrations/`. The Wave 10 archive claim of "37 migrations" was either pre-renumber or counting sub-files. The 32-file baseline is what `JadeApiFactory.ApplyMigrationAsync` (test fixture) iterates on a fresh DB.
- **Test infrastructure**: `tests/IntegrationTests/JadeCapital.Api.IntegrationTests/Infrastructure/JadeApiFactory.cs` already wires `Testcontainers.PostgreSql` + `Testcontainers.Redis` + `Respawn` (Wave 4 → Wave 10.1 chain). Wave 11 reuses this fixture — no new Testcontainers setup needed.
- **Architecture already in place**: `IUserCascadeDeletor` interface (`src/2.Modules/Identity/JadeCapital.Identity.Application/Abstractions/IUserCascadeDeletor.cs`), `UserCascadeDeleterOrchestrator` (DI-aggregates `IEnumerable<IUserCascadeDeletor>`), `IdentityUserCascadeDeletor` (RefreshTokens + RiskProfiles + PasswordHistory + TemporaryCredentials), `TradingUserCascadeDeletor` (13 trading aggregates), `BillingUserCascadeDeletor` (Subscriptions + StripeCustomer), `GdprAuditAnonymizer` (raw-SQL UPDATE, pseudonyms audit rows), `HardDeleteSweepBackgroundService` (daily + 30-day grace).

## Deferred items validation

| # | Item (Wave 10 archive line 77-78) | Status | Evidence |
|---|---|---|---|
| 1 | `DELETE /api/users/me` endpoint | ❌ truly deferred | `src/2.Modules/Identity/JadeCapital.Identity.Api/Endpoints/` ships only `AuthEndpoints.cs`, `RiskProfileEndpoints.cs`, `TenantEndpoints.cs`. No `UserEndpoints.cs` exists. Spec scenario `gdpr-compliance/spec.md` line 19-27 is uncovered. |
| 2 | `GET /api/users/me/export` endpoint (GDPR Art. 20) | ❌ truly deferred | No export endpoint + no `IUserDataExporter` abstraction. Spec scenario lines 40-52 uncovered. |
| 3 | Cookie consent FE banner + service | ❌ truly deferred | `frontend/src/assets/legal/` does not exist; no `CookieConsentService` in `frontend/src/app/core/`. Spec scenarios lines 62-81 uncovered. |
| 4 | ToS + Privacy Policy FE pages | ❌ truly deferred | `frontend/src/app/features/auth/register/register.page.ts` has placeholder `<a href="#">` links (line 222-223). Spec scenarios lines 87-98 uncovered. |
| 5 | Welcome email trigger on `RegisterUserHandler` | ❌ truly deferred | `RegisterUserHandler.cs` (94 lines) ends after token issuance — no `IEmailSender` call. Spec scenario for SPF/DKIM (line 104-111) + idempotent welcome uncovered. |
| 6 | `HardDeleteSweepOptions.cs` extraction | ❌ truly deferred | `find` returns 0 hits for `HardDeleteSweepOptions*`. `HardDeleteSweepBackgroundService.cs` line 46-47 hardcodes `GracePeriodDays = 30` + `MaxJitterMs = 30min`. Need extraction to `IOptions<HardDeleteSweepOptions>` pattern. |
| 7 | GDPR cascade xUnit tests via Testcontainers | ❌ truly deferred (CRITICAL) | `find` confirms ZERO cascade-related test files. All 10 `gdpr-compliance` scenarios + 6 `account-lifecycle` scenarios = **16 scenarios uncovered**. |
| 8 | Fix `0029_backfill_personal_tenant.sql` FK defect | ❌ truly deferred (CRITICAL) | See **CRITICAL fixes validation §1** below. Blocks fresh-DB apply. |

**Validation totals**: 0 confirmed-shipped, 0 partial, 8 truly-deferred.

## CRITICAL fixes validation

### §1 — `0029_backfill_personal_tenant.sql` FK defect

**Root cause** (verified by reading the migration + `0026_tenants.sql`):

1. `0026_tenants.sql` (line 35) declares: `owner_user_id UUID NOT NULL REFERENCES identity.users(id) ON DELETE RESTRICT`
2. `0029_backfill_personal_tenant.sql` (line 50-52) tries to insert a sentinel `'00000000-0000-0000-0000-000000000002'` when `identity.users` is empty on a fresh DB
3. **Empty-DB failure scenario**: fresh Postgres container → run all 32 migrations in `OrderBy(filename)` order → 0028 sets `tenant_id NOT NULL` on empty users table (passes: 0 NULL rows) → 0029 tries `INSERT INTO identity.tenants` with `owner_user_id = sentinel_guid` → FK constraint rejects (sentinel user does not exist) → `migrate.Dockerfile` exits non-zero → fresh deploy blocked
4. The author's own comment (line 36) acknowledges the case: *"será irrelevante porque no hay usuarios a backfillear"* — but the FK is statement-scoped, not row-scoped; empty users means INSERT fails regardless
5. Compounding bug: `0028_NOT_NULL_tenant_id.sql` (line 53-65) requires `0029` to run BEFORE it (its own error message says so), but the file-order numbering (`0028 < 0029`) runs `0028` first. The numbering was set in Wave 10.4 renumbering — likely a bug carried over from the old numbering (`0026_backfill` → `0029_backfill`).

**Fix options**:

| Option | Approach | Pros | Cons | Complexity |
|---|---|---|---|---|
| **A** | Split `0029` into `0029_sentinel_user.sql` (creates system user with `tenant_id = Personal tenant = NULL` initially, then INSERT Personal tenant, then UPDATE) + `0033_personal_backfill.sql` (existing logic, runs after first user registers via app code OR via separate migration) | Backwards-compatible; each migration does one thing; re-runnable | Splits a single atomic transaction across 2 migrations; renumbering `0028` ↔ `0029` order is a separate fix; complex test surface | Medium |
| **B** | Modify `0029` to: (a) drop `tenant_id NOT NULL` temporarily, (b) INSERT sentinel user into `identity.users` (with `tenant_id = NULL`), (c) INSERT Personal tenant with `owner_user_id = sentinel_id`, (d) UPDATE existing NULL users to Personal, (e) re-apply `SET NOT NULL`. All in one transaction. | Self-contained in 0029; no renumbering needed; explicit + atomic | Re-applies `0028`'s work (acceptable because `0028` is idempotent — it skips if NOT NULL already); sentinel user stays in DB forever (must document) | Medium |
| **C** | Add NEW migration `0029.5_sentinel_system_user.sql` BEFORE current `0029`: (a) INSERT Personal tenant with `owner_user_id = NULL` via `ALTER TABLE ... ALTER COLUMN owner_user_id DROP NOT NULL`, (b) INSERT sentinel user with `tenant_id = Personal_id`, (c) UPDATE tenant's `owner_user_id` to sentinel_id, (d) re-apply NOT NULL. Then existing `0029` runs (its sentinel logic now finds the real sentinel user). | Cleanest separation; explicit sentinel-user migration | Requires renumbering (current `0029` → `0033`); 2 file renames; needs careful ordering to avoid breaking Wave 6 6c.2 references in docs | Medium-High |
| **D** *(additional)* | Convert `tenants.owner_user_id` FK in `0026` to `DEFERRABLE INITIALLY DEFERRED`. Then `0029` can INSERT tenant + user in either order; FK check only fires at COMMIT. | Solves root cause generically (not just for `0029`); benefits future migrations | Modifying `0026` after it shipped — needs idempotent guard so re-running on existing DBs doesn't re-create the constraint | Low-Medium |

**Recommendation**: **Option B + sentinel user documentation** (single-file fix, atomic, no renumbering). The sentinel user becomes a permanent "System Admin" record with `role = 0` (Trader) + `email = 'system@jadecapital.internal'` + random PBKDF2 hash that cannot be guessed + the `tenant_id` points to the Personal tenant. The `users` table's GDPR cascade already covers deleting this user (it's just a row). Document the sentinel in the migration header + add an `AuditEvent` for the sentinel creation so the compliance trail records it. **Effort: Low-Medium, ~80 LOC + 1 apply-progress note.**

**Backwards-compat**: Option B's `SET NOT NULL` re-apply is safe on existing DBs because by the time `0029` runs there, all users have `tenant_id` (either because of an earlier run of `0029` or because the Personal tenant exists). On a DB where `0029` has already run, the sentinel user check is a no-op (sentinel id doesn't match any user id, so the UPDATE finds 0 rows).

### §2 — GDPR cascade xUnit tests gap (16 scenarios uncovered)

**Spec scenarios to cover**:

| Spec | Scenarios | Count |
|---|---|---:|
| `openspec/specs/gdpr-compliance/spec.md` | DELETE returns 202 + starts cascade; 30-day grace elapses → hard delete; export includes 12 entities; export EXCLUDES `audit.events` + `stripe_webhook_events`; banner appears on first visit; user choice persists in `localStorage`; analytics gated by consent (×2 cases); ToS + policy required; acceptance timestamp persisted; SPF/DKIM/DMARC pass | **10** |
| `openspec/specs/account-lifecycle/spec.md` | Active user state; SoftDeleted state; ScheduledHardDelete state; HardDeleted state (rows purged + audit row); sweep finds due users; idempotent re-run | **6** |
| **Total** | | **16** |

**Test file estimate** (following `tests/IntegrationTests/JadeCapital.Api.IntegrationTests/Wave5/ImportEndpointsTests.cs` precedent + Wave 4 scanner/quotes pattern):
- `Auth/UserDeleteEndpointTests.cs` — DELETE cascade, 30-day grace, idempotent re-run, HardDeleted pseudonymized audit row → ~6 scenarios in 1 file
- `Auth/UserExportEndpointTests.cs` — export includes 12 entities, excludes `audit.events` + `stripe_webhook_events`, cross-tenant 403 → ~3 scenarios in 1 file
- `Auth/CookieConsentTests.cs` — banner appears, persists in `localStorage`, gated by consent (unit test on `CookieConsentService` + integration test on `/api/auth/consent`) → ~3 scenarios in 1 file
- `Auth/RegisterTermsAcceptanceTests.cs` — ToS required, acceptance timestamp persisted → ~2 scenarios in 1 file
- `BackgroundServices/HardDeleteSweepTests.cs` — sweep finds due users, idempotent re-run, audit pseudonymization → ~3 scenarios in 1 file (direct orchestrator calls + Timer-driven test skipped)
- `Email/EmailDeliverabilityDocsTests.cs` — checks `docs/runbooks/setup-email-deliverability.md` exists + content sanity (DNS record names match) → ~1 scenario (no DNS dig at test time; smoke via doc presence)

**Test infrastructure reuse**: All GDPR cascade tests use `JadeApiFactory` (already spins Postgres + Redis containers). Zero new Testcontainers setup. Stripe webhook event idempotency verified indirectly via existing `Wave5/ImportEndpointsTests.cs` pattern.

**Effort**: ~5 test files × ~50-80 LOC + factory helper for `IClock` deterministic time-travel (~30 LOC). Total ~300-400 LOC of test code + ~50 LOC of test-infra.

## New gaps identified

| # | Gap | Evidence | Wave |
|---|---|---|---|
| G-1 | **0029 sentinel user creation must be auditable** | The new sentinel user needs an `audit.events` row with `action = "Created"` for compliance trail. Wave 11 should add a `CreateSentinelUser` migration step that emits the audit row directly via raw SQL insert (bypassing the append-only `AuditDbContext` invariant? — actually the `GdprAuditAnonymizer` already does this). Better: have `0029` INSERT into `audit.events` directly with `entity_type = "User"`, `entity_id = sentinel_id`, `action = 0` (Created). | 11 |
| G-2 | **Welcome email idempotency column missing** | `gdpr-compliance/spec.md` line 102 says *"skip if the same user already received a welcome email in the last 7 days, verified via `users.welcome_email_sent_at`"*. But the column does NOT exist in `users` (verified by reading `RegisterUserHandler` + the migration list — no `users_welcome_email_sent_at.sql` migration). Wave 11 must add a new migration `0033_users_welcome_email_sent_at.sql` with the column + an index. | 11 |
| G-3 | **Cookie consent column already exists on users** | `gdpr-compliance/spec.md` line 56 mentions `users.cookie_consent_accepted_at` + `users.cookie_consent_choice`. Need to verify these columns exist in `identity.users` (likely from Wave 10 10.4 schema consolidation). If missing → Wave 11 adds migration. | 11 |
| G-4 | **ToS acceptance columns missing** | `gdpr-compliance/spec.md` line 85 says `users.terms_accepted_at`, `users.privacy_accepted_at`, `users.consent_ip`. None of these exist (no migration for them). Wave 11 adds migration `0034_users_terms_acceptance.sql`. | 11 |
| G-5 | **GDPR audit anonymization integrity test** | After `GdprAuditAnonymizer` runs, the audit row's `entity_id` is a deterministic hash — the spec requires queryability. Wave 11 should add a unit test that runs the anonymizer + asserts the resulting row can still be queried by `entity_type = 'User' AND entity_id = '<hash>'`. Not on the deferred list but adjacent. | 11 |
| G-6 | **Stripe webhook event idempotency table is append-only — verify no gap** | `src/2.Modules/Billing/JadeCapital.Billing.Application/Stripe/IStripeWebhookEventRepository.cs` confirms append-only design + idempotency via `FindByEventIdAsync`. The Wave 10 archive flagged this as "verify no gap". Confirmed: NO GAP. No action. | (none) |
| G-7 | **Account deletion UI** | `gdpr-compliance/spec.md` references `B18 (account deletion UI)` closure. Wave 11 should add an Angular "Delete my account" button in the user settings page (`frontend/src/app/features/trader/settings/`) that POSTs to a confirmation modal → calls `DELETE /api/users/me`. | 11 |
| G-8 | **GDPR docs runbook** | `gdpr-compliance/spec.md` line 124 mentions `docs/runbooks/gdpr-data-subject-request.md`. The `find` confirms it does NOT exist. Wave 11 should create it (10-20 steps: user requests DSAR → ops triages → calls DELETE endpoint → confirms cascade → 30-day grace → hard-delete sweep → audit trail pseudonymization). | 11 |
| G-9 | **CHANGELOG cross-check** | Wave 10 archive lesson #5 (line 97): "CHANGELOG Wave 0-7 archive cross-check: best-effort summaries written from memory. Wave 11 should cross-check against `openspec/changes/archive/`". The CHANGELOG.md entries for Waves 0-7 are inferred (no archive cross-check was done). Wave 11 should grep each archive's `proposal.md` or `apply-progress` and adjust the CHANGELOG if needed. | 11 |
| G-10 | **Stripe env var alignment** | `src/1.Api/JadeCapital.Host/Configuration/StripeOptions.cs` (line 22-28) explicitly says: *"Wave 11+ should align the two (either add `Stripe__ApiKey` to compose, or rename `StripeOptions.ApiKey` to `StripeOptions.SecretKey` — pick one)"*. The legacy alias bridge works but is tech debt. | 11 |
| G-11 | **`<auto-generated-by>` marker cleanup** | Wave 10.6 already removed the marker from `StripeOptions.cs` (apply-progress line 71). Only `src/1.Api/JadeCapital.Host/Configuration/DockerSecretConfigurationProvider.cs` line 1 still has it (Wave 10.2 + C# compiler treats it as auto-generated). Wave 11 removes the marker. | 11 |
| G-12 | **WCAG/a11y baseline** | Out of scope — already in Wave 12 backlog. | 12 |
| G-13 | **Per-request CSP nonce** | Wave 10.3 ships `{request_nonce}` placeholder. Per-request nonce is Wave 12. | 12 |
| G-14 | **Sentry hooks** | Deferred per user authorization (Wave 10.6 narrower). Wave 12 when ops provides DSN. | 12 |
| G-15 | **OpenTelemetry tracing** | Wave 12 backlog. | 12 |
| G-16 | **E2E tests (Playwright)** | Wave 13 backlog. | 13 |
| G-17 | **wal-g continuous WAL archiving** | Wave 13 backlog. | 13 |
| G-18 | **Audit partitioning by month** | Wave 13 backlog. | 13 |

## Proposed slice breakdown

**Recommended pattern: Pattern B (cascade test infrastructure first)**. Rationale: Strict TDD is active + 16 scenarios have ZERO coverage. Building endpoints first and tests second means a partial-impl merge that breaks `git bisect`. Putting test infrastructure in 11.1 guarantees every subsequent slice lands with its tests written first (RED → GREEN).

| Slice | Sub-scope | Items covered | Effort | Dependencies | Target LOC | size:exception |
|---|---|---|---|---|---:|---|
| **11.1** | Test infrastructure + GDPR cascade xUnit tests | Item 7 (GDPR cascade xUnit tests via Testcontainers Postgres — 16 spec scenarios) | Med | Wave 10.5 cascade pattern (already shipped) | ~400 tests + ~50 test-infra | No (under 800) |
| **11.2** | 0029 FK fix + DELETE `/api/users/me` endpoint | Item 8 (FK fix — sentinel user migration) + Item 1 (DELETE endpoint + `DeleteUserHandler` + `UserEndpoints.cs`) + G-1 (sentinel user audit row) + G-7 (account deletion UI — basic modal) | High | 11.1 (tests must exist for DELETE behavior) | ~500 BE + ~150 FE + ~80 migration | **size:exception** (auth-critical + cascade-critical; could split into 11.2a migration + 11.2b endpoint if 800-line budget blows) |
| **11.3** | GET `/api/users/me/export` endpoint + `HardDeleteSweepOptions` extraction | Item 2 (export endpoint + `IUserDataExporter` abstraction + `ExportUserHandler`) + Item 6 (`HardDeleteSweepOptions` class + DI wiring + verify behavior unchanged) + G-2 (`welcome_email_sent_at` column migration) | Med | 11.2 (depends on UserEndpoints.cs scaffold + cascade contract verified) | ~400 BE | No |
| **11.4** | Cookie consent + ToS/Privacy + welcome email + GDPR docs | Item 3 (cookie consent FE banner + service + `POST /api/auth/consent` endpoint) + Item 4 (ToS + Privacy Policy FE pages with legal copy placeholders) + Item 5 (welcome email trigger in `RegisterUserHandler` + SPF/DKIM/DMARC runbook `docs/runbooks/setup-email-deliverability.md`) + G-4 (terms acceptance columns migration) + G-8 (`docs/runbooks/gdpr-data-subject-request.md`) + G-9 (CHANGELOG Wave 0-7 cross-check) + G-10 (Stripe env var alignment) + G-11 (`<auto-generated-by>` marker cleanup on `DockerSecretConfigurationProvider.cs`) | High | 11.3 (registration columns must exist before welcome email trigger wires up) | ~600 BE + ~250 FE + ~100 docs | No |
| **Total** | | | | | ~2,500 LOC | 1 size:exception (11.2) |

## Specs to write

| Spec | NEW or DELTA | Slug | Description | Scenarios |
|---|---|---|---|---:|
| Endpoint tests + cascade test coverage | DELTA | `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/specs/gdpr-endpoint-coverage/spec.md` | NEW delta for GDPR endpoint test coverage — covers scenarios 1-2, 3-4, 7-10 of `gdpr-compliance` spec + all 6 of `account-lifecycle`. Uses JadeApiFactory pattern. | 13 |
| Cookie consent + ToS acceptance | DELTA | `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/specs/consent-acceptance/spec.md` | NEW delta — covers scenarios 5-9 of `gdpr-compliance` (banner + ToS required + acceptance persisted). | 5 |
| Welcome email + email deliverability | DELTA | `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/specs/email-deliverability/spec.md` | NEW delta — covers welcome email idempotency + SPF/DKIM/DMARC runbook presence. | 2 |
| Account deletion UI | DELTA | `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/specs/account-deletion-ui/spec.md` | NEW delta — covers FE "Delete my account" button + confirmation modal + cascade status display. | 3 |
| GDPR docs runbook | DELTA | `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/specs/gdpr-ops-runbook/spec.md` | NEW delta — covers `docs/runbooks/gdpr-data-subject-request.md` content (DSAR intake → DELETE → grace → sweep → audit). | 1 |
| `HardDeleteSweepOptions` extraction | DELTA | `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/specs/hard-delete-sweep-options/spec.md` | NEW delta — covers extraction of `GracePeriodDays` + `IntervalHours` + `InitialDelayMinutes` + `MaxJitterMinutes` into `IOptions<HardDeleteSweepOptions>` with default values matching current hardcoded behavior. | 4 |

**Total**: 6 DELTA specs, ~28 scenarios. (Note: the canonical `gdpr-compliance` + `account-lifecycle` specs from Wave 10 already cover the behavioral surface — Wave 11 deltas layer on test coverage + the additional gaps.)

## Chained PR order

| PR | Slice | Title | LOC | Branch base | Merge order |
|---|---|---|---|---|---|
| **#57** | 11.1 | `test(gdpr): GDPR cascade xUnit coverage via Testcontainers (16 scenarios)` | ~450 | `feature/wave10-v1-readiness` @ `8d40394` | 1 (independent) |
| **#58** | 11.2a | `fix(migrations): 0029 FK defect — sentinel system user + Personal tenant atomic insert` | ~150 | #57 | 2 (after #57; unblocks fresh-DB apply verifier) |
| **#59** | 11.2b | `feat(gdpr): DELETE /api/users/me endpoint + DeleteUserHandler + account deletion UI` | ~580 | #58 | 3 (after #58) |
| **#60** | 11.3 | `feat(gdpr): GET /api/users/me/export + HardDeleteSweepOptions extraction` | ~400 | #59 | 4 (after #59) |
| **#61** | 11.4 | `feat(consent+email+docs): cookie consent + ToS/Privacy + welcome email + GDPR runbooks + CHANGELOG cross-check + Stripe env var alignment` | ~950 | #60 | 5 (last) |

**Chain HEAD**: `feature/wave11-gdpr-v1-readiness` @ `+` after #61 merge. Tag `v1.0.0` after #61 ships + sdd-verify passes.

## Risks

### Per-slice risks

| Slice | Risk | Mitigation |
|---|---|---|
| 11.1 | Testcontainers may not be available in CI sandbox (Wave 4 precedent: some tests skipped when Docker unavailable). The `JadeApiFactory` already handles this via `Task<...>` lazy init + per-fixture retry. | Use `Skip = !DockerAvailable` xUnit trait on the 16 new tests so they degrade gracefully on sandbox runners but run in CI. |
| 11.2a | Migration split could break an existing dev DB mid-update. | The migration is forward-only; existing DBs that already passed `0029` see the sentinel INSERT as a no-op (the `WHERE tenant_id IS NULL` finds 0 rows after the first run). |
| 11.2b | DELETE endpoint regresses Wave 6 6c.2 tenant backfill tests if cascade contract leaks into register flow. | Test isolation: `DeleteUserHandler` does NOT touch `tenants` — only `identity.users` + per-module deletors + audit. Wave 6 tests untouched. |
| 11.2b | The 30-day sweep BackgroundService is real-time; xUnit cannot test time-travel without `IClock` abstraction (which exists per `src/3.Shared/JadeCapital.Shared.Kernel/Time/IClock.cs`). | 11.1 tests use `IClock` injection to simulate grace period elapsing. |
| 11.3 | Export endpoint memory-buffers the entire dataset → contradicts `gdpr-compliance/spec.md` line 46 (`MUST stream without buffering`). | Use `Results.Stream` with `IAsyncEnumerable<T>` from the repository layer; xUnit verifies `Transfer-Encoding: chunked` header. |
| 11.3 | `HardDeleteSweepOptions` extraction changes default values accidentally. | Wave 10 hardcodes `GracePeriodDays = 30`, `IntervalHours = 24`, `InitialDelay = 2 min`, `MaxJitter = 30 min`. The options class defaults MUST match these exactly — verified by a "behavior parity" test that runs `RunOnceAsync` with both old + new config and asserts identical behavior. |
| 11.4 | Cookie consent banner blocks first-paint (UX risk). | Banner is rendered in `app.component.html` AFTER main shell loads; uses CSS `position: fixed; bottom: 0` so it doesn't reflow above-the-fold. The spec doesn't require blocking — only appearing. |
| 11.4 | Welcome email trigger in `RegisterUserHandler` regresses Wave 6 6c.2 register tests (which assert a user is created WITHOUT sending email — the new email-send is additional). | The Wave 6 tests assert DB state, not email-sent state. The new trigger is additive (sends AFTER DB commit). |
| 11.4 | `<auto-generated-by>` removal on `DockerSecretConfigurationProvider.cs` may re-introduce CS8669 (nullable annotations) if the file isn't carefully regenerated. | The file is hand-written code (the marker was a copy-paste from Wave 10.2). Removing the marker + keeping the file as-is works (verified by Wave 10.6's same fix on `StripeOptions.cs`). |

### Cross-slice risks

- **0029 FK fix is BLOCKING the Testcontainers fresh-DB apply verifier** → slice order matters. 11.2a must run BEFORE 11.1's tests can pass on a fresh DB.
- **GDPR cascade xUnit tests require Testcontainers + Docker in CI** → Wave 10.1 already added `services: postgres` to `.github/workflows/test-backend.yml`. Confirmed by reading the workflow YAML.
- **Angular FE components require locale handling for legal text** (Spanish vs English). The existing register.page.ts is all Spanish — ToS/Privacy pages can follow the same convention.
- **Welcome email trigger touches `RegisterUserHandler`** — should NOT regress Wave 6 6c.2 tests. The trigger is wrapped in `try { ... } catch { log + continue }` so email failure does not roll back registration.
- **`HardDeleteSweepOptions` extraction is mechanical** but Wave 10's BackgroundService hardcodes values — must keep behavior identical. The behavior-parity test in 11.3 is the safety net.

## Dependencies between slices

```
11.1 (tests)
  ↓
11.2a (FK fix — unblocks 11.1's fresh-DB test path)
  ↓
11.2b (DELETE endpoint — depends on 11.1 tests + 11.2a FK fix)
  ↓
11.3 (export + HardDeleteSweepOptions — depends on 11.2 UserEndpoints.cs scaffold)
  ↓
11.4 (consent + ToS + email + docs + CHANGELOG + Stripe + auto-gen cleanup)
```

**Parallelizable pairs**: None — strict waterfall because every slice tests the previous slice's behavior.

**Critical path**: 11.1 → 11.2a → 11.2b → 11.3 → 11.4 (all sequential).

## Out of scope for Wave 11 (deferred to Wave 12/13)

### Wave 12 (observability + a11y + Sentry)
- **G-12**: WCAG 2.1 AA baseline (axe-core + screen reader pass on auth + public + trader shells)
- **G-13**: Per-request CSP nonce via nginx-njs-module or Lua (Wave 10.3 placeholder)
- **G-14**: Sentry hooks (when ops provides DSN)
- **G-15**: OpenTelemetry tracing (spans for cascade + sweep + audit)
- **API versioning strategy**: `/api/v1/*` prefix + OpenAPI versioning
- **Multi-tenant AI rate limits** (per-tenant + per-user)
- **Per-user rate limiting** (vs per-IP)
- **GDPR Art. 18 (right to restrict processing)** — distinct endpoint
- **Pre-hard-delete warning email** (7-day notice) — requires `pre_hard_delete_warning_sent_at` column
- **Multi-region hard-delete coordination**

### Wave 13 (hardening + production)
- **G-16**: E2E Playwright tests for full cascade flow
- **G-17**: wal-g continuous WAL archiving (replaces Wave 10.4 daily pg_dump)
- **G-18**: Audit partitioning by month (Postgres native partitioning on `audit.events.occurred_at`)
- **Anonymized-but-not-deleted tier** ("freeze my account" mode)
- **Bulk admin hard-delete** (admin-triggered multi-user purge)
- **DSAR intake form** beyond the manual runbook (FE form → ticket creation)

## Recommendation

**GO for Wave 11.** All 8 deferred items + 18 new gaps have clear ownership. The 2 CRITICAL fixes (FK defect + 16 cascade xUnit scenarios) are the highest-priority work and slot naturally into 11.1 + 11.2a.

**Effort estimate**: 4 chained PRs (#57-#61), ~2,500 LOC, 6 DELTA specs (~28 scenarios), 1 size:exception (11.2b due to DELETE endpoint + UI + migration combined).

**Release target**: `v1.0.0` GA tag at the merge of #61 + sdd-verify PASS.

**Tag-at-end trigger**: After #61 merges to `feature/0a-identity-model`, the orchestrator creates the `v1.0.0` annotated tag (no `rc1` suffix — this IS the GA tag). The Wave 10 `v1.0.0-rc1` tag is superseded.

## Open questions

1. **Sentinel user in 0029 — what role?** Option B's sentinel user needs a role. Options: (a) reuse `Trader = 0`, (b) reuse `Admin = 2`, (c) introduce new `System = 3`. Recommend (a) for minimal migration surface — the role is irrelevant because the sentinel can't login (random unguessable hash). Decision needed before 11.2a lock.

2. **`welcome_email_sent_at` index — needed?** Spec implies "skip if sent in last 7 days" → query is `WHERE welcome_email_sent_at > UtcNow - 7d`. With ~1k users expected for v1.0, a B-tree index is cheap. Recommend: yes, add the index. Decision needed before 11.3 lock.

3. **ToS legal copy — placeholder or empty?** Spec says *"placeholder pages ship with `<!-- TODO: legal copy -->` markers"*. Wave 11 ships placeholders (no real legal text). The user/legal counsel delivers actual copy in a later PR. Confirm before 11.4 lock.

4. **Cookie consent UI position — bottom bar vs modal?** Spec says "banner". Recommend bottom-bar (less intrusive + doesn't block flow). Decision needed before 11.4 lock.

5. **GDPR cascade test parallelism** — run 16 tests in parallel via xUnit collection fixture, or serially? Each test spins a fresh Postgres schema (`Respawn` checkpoint between tests). Recommend parallel (xUnit default). Decision: no action needed — default works.

6. **Export endpoint format — single JSON file or ZIP?** Spec says `Content-Disposition: attachment; filename="jadecapital-export-{userId}.json"` — single JSON. Recommend stick to single JSON for v1.0 (simpler + satisfies Art. 20). Decision: no action needed.

7. **HardDeleteSweepOptions — keep defaults exact-match to Wave 10 hardcoded values?** Yes, otherwise behavior changes silently. Recommendation encoded in the parity test. No decision needed.

8. **Should 11.2b's account deletion UI be a new Angular page or modal-on-existing-settings-page?** Spec doesn't dictate. Recommend modal-on-settings-page (lower surface area + matches Wave 7 settings pattern). Decision needed before 11.2b lock.

9. **Welcome email template — copy from existing template? `docs/templates/welcome-email.html`?** Need to check if a template exists. If yes, reuse + customize. If no, ship minimal HTML+text version. Decision needed during 11.4 apply.

10. **CHANGELOG Wave 0-7 cross-check — limit to factual corrections or full rewrite?** Recommend: factual corrections only (preserve Wave 10's prose + add `[Corrected]` note where archive conflicts). Decision: no action needed — recommended approach.