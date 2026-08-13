# Tasks: Jade Trader OS Core Portals — Wave 0

## Review Workload Forecast

| Slice | Boundary | Lines | Base |
|---|---|---:|---|
| 0a | Identity model + SQL `0006` + domain tests (existing `JadeCapital.Identity.UnitTests`) | 360 | tracker |
| 0b | Recovery handlers + app tests (same `Identity.UnitTests`) | 332 | 0a |
| 0c | SMTP/transport + API/Host + integration (`JadeCapital.Api.IntegrationTests`) + Mailpit compose | 286 | 0b |
| 0d | Angular recovery state/guards/pages + minimal Jest harness + 1st `.spec.ts` | 384 | 0c |
| 0e | Billing aggregate + SQL `0007` + new `JadeCapital.Billing.UnitTests` csproj | 394 | 0d |
| 0f | Billing handlers + new `JadeCapital.Admin.Api` csproj + `IUserOwnerProjection` + Host/authz | 338 | 0e |
| 0g | Angular Admin list/detail/history/state/routes + additional `.spec.ts` | 386 | 0f |
| **Total** | 7 chained slices | **2480** | tracker→main |

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: feature-branch-chain
400-line budget risk: Low

User confirmed `feature-branch-chain` (Engram obs #395); `auto`+`force-chained`. Tracker `feature/wave-0-core-portals`; bases 0a→tracker, 0b→0a,…,0g→0f. Per-PR `git diff --stat` <400, re-split if exceeded.

**Build**: `dotnet build JadeCapital.slnx --nologo --verbosity minimal`. **SQL harness**: `PGPASSWORD=$POSTGRES_PASSWORD psql -h postgres -U $POSTGRES_USER -d $POSTGRES_DB -v ON_ERROR_STOP=1 -f infrastructure/postgres/migrations/<file>.sql`; idempotent re-run (same script twice → exit 0); additive DDL only (`CREATE TABLE IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS`, `ALTER TABLE … ADD COLUMN IF NOT EXISTS`); no `DROP`, no destructive `ALTER`. The repo uses hand-authored SQL migrations — **no `dotnet ef` is used**.

### Work Units (PR → test → runtime → rollback)
- 0a: `dotnet test tests/UnitTests/JadeCapital.Identity.UnitTests/JadeCapital.Identity.UnitTests.csproj --nologo --verbosity minimal` · `psql -v ON_ERROR_STOP=1 -f migrations/2026MMDD_0006_PasswordRecovery.sql` against `jade-postgres` · revert domain + repository; keep SQL applied (inert)
- 0b: `dotnet test .../JadeCapital.Identity.UnitTests/JadeCapital.Identity.UnitTests.csproj --nologo --verbosity minimal` · handler tests with `InMemoryCapturingEmailSender` fakes · drop handlers; keep domain
- 0c: `dotnet test tests/IntegrationTests/JadeCapital.Api.IntegrationTests/JadeCapital.Api.IntegrationTests.csproj --nologo --verbosity minimal` · `docker compose up postgres mailpit` + `dotnet run` host + curl `/api/auth/forgot-password` · unmount endpoints + `Mail__*` env
- 0d: `cd frontend && npx jest --testPathPattern=auth` + `npm --prefix frontend run build` · `npm start` forgot/forced-change flows · remove routes/guard/state fields
- 0e: `dotnet test tests/UnitTests/JadeCapital.Billing.UnitTests/JadeCapital.Billing.UnitTests.csproj --nologo --verbosity minimal` (new project) · `psql -v ON_ERROR_STOP=1 -f migrations/2026MMDD_0007_BillingSubscriptions.sql` · revert domain; keep SQL applied
- 0f: `dotnet test .../JadeCapital.Billing.UnitTests .../JadeCapital.Api.IntegrationTests --nologo --verbosity minimal` · `dotnet run` + curl `/api/admin/subscriptions/*` with Admin/non-Admin tokens · unmap Admin API; keep data
- 0g: `cd frontend && npx jest --testPathPattern=admin` + `npm --prefix frontend run build` · `npm start` admin routes · unroute admin pages

## Slice 0a — Identity Model + SQL 0006 + Domain Tests + Application Reuse Checker (≤360)
- [x] 0a.1 🔴 RED: `tests/UnitTests/JadeCapital.Identity.UnitTests/Authentication/TemporaryCredentialTests.cs` + `PasswordHistoryTests.cs` + `PasswordChangeReuseCheckerTests.cs` + `PasswordHistoryHydrationOrderTests` — latest-only activation (`Generation != latestGeneration` ⇒ superseded), history `(changed_at DESC, id DESC)`, 5-newest retention with hydration-order defense, `SessionVersionDefaultsToZero`, `TempFailuresShareLockoutCounter` (exercises `User.RecordFailedLogin` boundary), salted-hash reuse detection at the Application boundary.
- [x] 0a.2 🟢 GREEN: add `TemporaryCredential`, `PasswordHistoryEntry`, `User.SessionVersion`; VOs `CrockfordCredential(128-bit,26-char)`, `CredentialHash` in `src/2.Modules/Identity/JadeCapital.Identity.Domain/Authentication/`. Domain does NOT claim plaintext reuse validation — that responsibility lives in `JadeCapital.Identity.Application/Authentication/PasswordChangeReuseChecker`.
- [x] 0a.3 🟢 EF configs + indexes (`unique(user,generation)`, partial-unique `(user_id) WHERE status='Activated'`, `(user_id, generation DESC)` sweep, `password_history(user,changed_at DESC,id DESC)`) in `JadeCapital.Identity.Infrastructure/Persistence/Configurations/`. EF/SQL parity verified.
- [x] 0a.4 🟡 REFACTOR: dedupe ordering helpers via `PasswordHistoryEntry.OrderNewestFirst`; reused `IClock`/no `ITemporaryClock` invented (existing `User` lockout already takes the existing `DateTimeOffset.UtcNow` path; the recovery lifecycle uses an injected `DateTimeOffset utcNow` parameter, keeping the temporary credential free of clock state). `User.ChangePasswordPreservingHistory` normalizes the backing list BEFORE prepend/evict so EF hydration order cannot evict the wrong hash.
- [x] 0a.5 Hand-author `infrastructure/postgres/migrations/20260811_0006_PasswordRecovery.sql` (idempotent, additive only — `CREATE TABLE IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS`, `ALTER TABLE … ADD COLUMN IF NOT EXISTS`); append `COPY` + `psql -v ON_ERROR_STOP=1 -f` invocation in `infrastructure/postgres/migrate.Dockerfile`. SQL corrected in place to match EF parity (unique partial active, generation-desc index, history with id DESC tie-breaker).
- [x] 0a.6 Verify: `dotnet build JadeCapital.slnx` (0 errors) + `dotnet test .../JadeCapital.Identity.UnitTests --nologo --verbosity minimal` (114/114 passing); run `psql -v ON_ERROR_STOP=1 -f …0006_PasswordRecovery.sql` twice — both exit 0 (idempotent re-run, exit code 0 confirmed on first and second run against the live `jade-postgres` container); `git diff --stat` for original 0a exceeded 400 (~1006 lines), recorded as a discovery; correction-run authored lines within the 400 cap.
- [x] 0a.7 Security: reject current+5 prior (via Application `PasswordChangeReuseChecker` using `IPasswordHasher.Verify` — NOT domain string comparison); deterministic tie-breaker (`(changed_at DESC, id DESC)`); lockout-shared (`User.RecordFailedLogin` exercised by both regular and temp credential paths); 128-bit entropy on the temporary credential (16-byte CSPRNG seed, 26 Crockford Base32 chars, no byte truncation); latest-only activation enforced at model AND DB (`UNIQUE(user_id) WHERE status='Activated'`).
- [x] 0a.8 Rollback: revert domain + repository + Application files; keep `0006` applied (inert columns). Slice 0b owns the atomic DB transaction that supersedes older `Activated` rows when a new recovery email is sent — documented in design.md and apply-progress.md.

## Slice 0b — Recovery Handlers + App Tests (≤332)
- [x] 0b.1 RED tests + 0b.2 GREEN handlers — 6 spec names, code diff = 394.
- [x] 0b.3 REFACTOR: shared abstractions in `RecoveryAbstractions.cs`.
- [x] 0b.4 Verify: 120/120 tests pass.
- [x] 0b.5 Security: SessionVersion bumped (pre-change tokens rejected); revoke under per-user lock.
- [x] 0b.6 Rollback: drop handlers + `RecoveryAbstractions.cs`; keep Domain + `0006` schema.

## Slice 0c — SMTP/Transport + API/Host + Integration/Config (≤286)
- [ ] 0c.1 🔴 RED: `tests/IntegrationTests/JadeCapital.Api.IntegrationTests/Auth/PasswordRecoveryFlowTests.cs` — `ForgotPassword_Always200Generic`, `TimingBodyStatusIndistinguishable`, `Throttle5PerHourPerIp`, `InMemorySender_NeverLogsBody`, `SmtpFailure_DoesNotActivate`.
- [ ] 0c.2 🟢 GREEN: `IEmailSender` w/ `MailKitSmtpEmailSender`, `MailpitSmtpEmailSender`, `InMemoryCapturingEmailSender` in `src/3.Shared/JadeCapital.Shared.Infrastructure/Email/`.
- [ ] 0c.3 🟢 GREEN: minimal-API endpoints `POST /api/auth/forgot-password`, `POST /api/auth/change-password` in `JadeCapital.Identity.Api/Endpoints/AuthEndpoints.cs`; RFC7807 `auth.recovery_invalid|auth.password_reused|concurrent_update`.
- [ ] 0c.4 🟢 GREEN Host: `AddIdentityInfrastructure`, `MapIdentityApi`, `AddMailOptions`, `AddAdminOnly`, restricted-scope middleware; uniform-timing 14s±250ms w/ dummy PBKDF2; throttle 5/hour/IP in `src/1.Api/JadeCapital.Host/Program.cs`.
- [ ] 0c.5 🟢 `docker-compose.yml`: add `mailpit` service (`axllent/mailpit:latest`, ports 1025/8025); `.env.example`: add `Mail__Host/Port/Username/Password/From/UseStartTls`.
- [ ] 0c.6 🟡 REFACTOR: `IUniformTimingGate`; trim mapper allocations.
- [ ] 0c.7 Verify: `dotnet test .../JadeCapital.Api.IntegrationTests --nologo --verbosity minimal` w/ Testcontainers Postgres+Mailpit; `git diff --stat` <400.
- [ ] 0c.8 Security: log-scrubber (no plaintext password/token/hash/body); Serilog config assertion.
- [ ] 0c.9 Rollback: unmap endpoints; unset `Mail__*` env.

## Slice 0d — Angular Recovery State/Guards/Pages + Jest Harness (≤384)
- [ ] 0d.1 🟢 PRE: add minimal Jest harness in `frontend/`: `package.json` devDeps `jest@29 ts-jest@29 @types/jest@29 jest-preset-angular@14`; `frontend/jest.config.js` (ts-jest preset, `testEnvironment:'jsdom'`, `setupFilesAfterEach`); `frontend/src/setup-jest.ts`. This is the *only* slice that adds frontend test infra.
- [ ] 0d.2 🔴 RED: `frontend/src/app/core/auth/__tests__/auth.state.spec.ts` + `core/guards/__tests__/recovery.guard.spec.ts` + `forced-change.guard.spec.ts` — `AuthState_MarksPasswordChangeRequired`, `RedirectsWhenNoChangeRequired`, `AllowsOnlyChangeRoute`.
- [ ] 0d.3 🔴 RED: `features/auth/recovery/__tests__/forgot-password.page.spec.ts` + `forced-change.page.spec.ts` — `DisablesSubmitWhileLoading`, `AnnouncesStatus_AriaLive`, `KeyboardOrder_MobileViewport_360px`.
- [ ] 0d.4 🟢 GREEN: extend `frontend/src/app/core/state/auth.state.ts` Signals (`passwordChangeRequired`, `recoveryGrant`, `generation`); `recovery.guard.ts`, `forced-change.guard.ts`.
- [ ] 0d.5 🟢 GREEN: `forgot-password.page.ts`, `forced-change.page.ts` in `frontend/src/app/features/auth/recovery/`; strict TS, standalone, Signals, OnPush, SCSS; ARIA labels, focus mgmt, `aria-live`, responsive.
- [ ] 0d.6 🟡 REFACTOR: `AccessibleStatusComponent`; helpers to `shared/forms/`.
- [ ] 0d.7 Verify: `cd frontend && npx jest --testPathPattern=auth` + `npm --prefix frontend run build`; `git diff --stat` <400.
- [ ] 0d.8 Regression: `npx jest --testPathPattern=public|trader` (PublicPortal/Trader unchanged after slice).
- [ ] 0d.9 A11y: keyboard-only (Tab/Enter/Esc); visible focus; `aria-live=polite` success / `assertive` error.
- [ ] 0d.10 Rollback: remove routes/guard/state fields; revert Jest harness.

## Slice 0e — Billing Aggregate + SQL 0007 + New Test Project (≤394)
- [ ] 0e.1 🟢 PRE: create `tests/UnitTests/JadeCapital.Billing.UnitTests/JadeCapital.Billing.UnitTests.csproj` (xUnit 2.9.2, FluentAssertions 7.0.0, NSubstitute 5.3.0; refs `JadeCapital.Billing.Domain` + `JadeCapital.Billing.Application`) + `GlobalUsings.cs`. Add to `JadeCapital.slnx`.
- [ ] 0e.2 🔴 RED: `tests/UnitTests/JadeCapital.Billing.UnitTests/Subscriptions/SubscriptionTests.cs` — `ChangesTierAppendsHistory`, `Cancel_FromCancellableState`, `ExtendTrial_RejectsExpiredDateOrNonActiveTrial`, `NoOp_Rejection_NoHistory`, `ConcurrentMutation_ExactlyOneWins_ViaVersion`, `HistoryOrder_NewestFirst_StableTieBreaker`, `EligiblePlanOnly`.
- [ ] 0e.3 🟢 GREEN: `Plan`, `Subscription` aggregates, VOs `PlanCode`, `SubscriptionPeriod`; events `SubscriptionTierChanged/Cancelled/TrialExtended` in `JadeCapital.Billing.Domain/Subscriptions/`.
- [ ] 0e.4 🟢 EF configs + indexes (`subscriptions.user UNIQUE`, `(status,updated_at DESC)`, `version`); append-only `subscription_history(subscription_id,occurred_at DESC,id DESC)` in `JadeCapital.Billing.Infrastructure/Persistence/Configurations/`.
- [ ] 0e.5 🟡 REFACTOR: `ISubscriptionMutator`; dedupe Tier/Cancel/Extend rules.
- [ ] 0e.6 Hand-author `infrastructure/postgres/migrations/2026MMDD_0007_BillingSubscriptions.sql` (idempotent, additive only); append `COPY` + `psql -v ON_ERROR_STOP=1 -f` invocation in `migrate.Dockerfile`.
- [ ] 0e.7 Verify: `dotnet test .../JadeCapital.Billing.UnitTests --nologo --verbosity minimal`; `psql -v ON_ERROR_STOP=1 -f …0007_BillingSubscriptions.sql` twice (both exit 0); `git diff --stat` <400.
- [ ] 0e.8 Rollback: revert domain files + delete `JadeCapital.Billing.UnitTests` project; keep `0007` applied (inert columns).

## Slice 0f — Billing Handlers + Admin.Api + IUserOwnerProjection + Host/Authz (≤338)
- [ ] 0f.1 🔴 RED: `tests/UnitTests/JadeCapital.Billing.UnitTests/Features/Subscriptions/{List,ChangeTier,Cancel,ExtendTrial}Tests.cs` + `tests/IntegrationTests/JadeCapital.Api.IntegrationTests/AdminAuthorizationTests.cs` — `ListSubscriptions_PagedByQuery`, `ChangeTier_RequiresVersion`, `Cancel_RecordsActorAndTimestamp`, `ExtendTrial_RejectsIfNotActiveTrial`, `AdminEndpoint_RejectsNonAdmin_BeforeLookup`, `AdminEndpoint_RejectsForcedChangeToken`, `OwnerProjection_ExposesOnlyEmailAndDisplayName`.
- [ ] 0f.2 🟢 GREEN: handlers in `JadeCapital.Billing.Application/Features/Subscriptions/`; contracts DTOs in `JadeCapital.Billing.Contracts/Subscriptions/SubscriptionDtos.cs`.
- [ ] 0f.3 🟢 PRE: create new csproj `src/2.Modules/Admin/JadeCapital.Admin.Api/JadeCapital.Admin.Api.csproj` (refs `JadeCapital.Billing.Application` + `JadeCapital.Identity.Contracts`); add to `JadeCapital.slnx`.
- [ ] 0f.4 🟢 GREEN: thin `JadeCapital.Admin.Api/Endpoints/AdminSubscriptionEndpoints.cs` (list/search, detail+owner+history, tier, cancel, trial extend); `IUserOwnerProjection` in `JadeCapital.Identity.Contracts/Projections/IUserOwnerProjection.cs` (read-only, exposes only `Email` + `DisplayName`).
- [ ] 0f.5 🟢 GREEN Host: `AddBillingInfrastructure`, `MapAdminApi`, `AddAdminOnly` policy in `src/1.Api/JadeCapital.Host/Program.cs`; reuse restricted-scope middleware.
- [ ] 0f.6 🟡 REFACTOR: `RequireAdminPolicyHandler`; collapse DTO duplication.
- [ ] 0f.7 Verify: `dotnet test .../JadeCapital.Billing.UnitTests .../JadeCapital.Api.IntegrationTests --nologo --verbosity minimal`; runtime `dotnet run` + curl Admin API with Admin/non-Admin/forced-change tokens; `git diff --stat` <400.
- [ ] 0f.8 Security: denial-before-lookup (no subscription data leak); projection narrowing; narrow-scope (no role/suspend/impersonate routes).
- [ ] 0f.9 Rollback: unmap Admin API endpoints; remove `JadeCapital.Admin.Api` csproj from `JadeCapital.slnx`; keep data.

## Slice 0g — Angular Admin List/Detail/History/State/Routes/Tests (≤386)
- [ ] 0g.1 🔴 RED: `frontend/src/app/features/admin/__tests__/admin.guard.spec.ts` + `state/__tests__/admin.state.spec.ts` + `subscriptions/__tests__/{list,detail,history}.spec.ts` — `BlocksNonAdmin_AndForcedChange`, `ListSearch_DedupesSignals`, `RefreshesHistoryAfterMutation`, `LoadingEmptyErrorStates_NonOverlapping`, `StaleConflict_PreservesInput`, `HistoryTable_NewestFirst_StableTieBreaker`.
- [ ] 0g.2 🟢 GREEN: `admin.guard.ts`, `admin-api.service.ts`, `admin.state.ts` in `frontend/src/app/features/admin/state/`.
- [ ] 0g.3 🟢 GREEN: `admin-subscriptions-list.page.ts`, `admin-subscription-detail.page.ts`, `admin-subscription-history.component.ts` in `frontend/src/app/features/admin/subscriptions/`; strict TS, standalone, Signals, OnPush, SCSS; loading/error/empty/conflict/mutation-pending states; responsive desktop/tablet/mobile; ARIA labels, logical keyboard order, visible focus, `aria-live` status.
- [ ] 0g.4 🟢 GREEN: routes in `frontend/src/app/app.routes.ts`; AdminOnly lazy load; e2e for forbidden Trader `/admin`.
- [ ] 0g.5 🟡 REFACTOR: `MutationStatusBannerComponent`; table helpers to `shared/tables/`.
- [ ] 0g.6 Verify: `cd frontend && npx jest --testPathPattern=admin` + `npm --prefix frontend run build`; regression `npx jest --testPathPattern=public|trader|auth`; `git diff --stat` <400.
- [ ] 0g.7 A11y/Responsive: keyboard-only (Tab/Shift+Tab/Enter) @360px viewport; focus moves predictably after errors/confirmations; tables understandable when narrow.
- [ ] 0g.8 Rollback: unroute admin pages; drop guard.

## Per-PR Exit Criteria (non-checkbox, validated before merge)
- `dotnet build JadeCapital.slnx --nologo --verbosity minimal` — zero errors
- `dotnet test <slice-specific csproj(s)> --nologo --verbosity minimal` — zero failures
- `npx jest` (frontend slices 0d/0g) — zero failures
- `npm --prefix frontend run build` — succeeds
- `psql -v ON_ERROR_STOP=1 -f <migration>.sql` (slices 0a/0e) — exits 0; second run also exits 0 (idempotent)
- `git diff --stat` <400 authored lines
- Migration files: only `CREATE/ALTER ADD` (no `DROP`, no destructive `ALTER`)
