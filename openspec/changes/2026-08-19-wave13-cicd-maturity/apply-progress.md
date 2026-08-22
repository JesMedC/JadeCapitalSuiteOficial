# Wave 13 CI/CD Maturity — Apply Progress

> **Canonical apply-progress ledger.** This file is authoritative for cumulative task and evidence state.

## Execution Context

- Branches: Phase 1 `feature/wave13-cicd-maturity-final`; Phase 2 `feature/wave13-e2e-journeys`
- Completed scope: Phase 1 tasks 1.1–1.3 and Phase 2 tasks 2.1–2.3
- Mode: Strict TDD
- Delivery: `auto-chain`, `feature-branch-chain`; work unit 2 / PR2 targets PR1
- Parent tokens: Phase 1 `sha256:bee90b3532da743a02839f31fc6a0cb2cfcea9cee4c8569a1a3df9a185f611d7`; Phase 2 `sha256:41b95b10a4a59c9c91b8774cd53f114e2597ade410d1481808b704bb9080b083`
- Transaction action: none (`acquire`/`settle` explicitly excluded)
- Status: **Phases 1–2 complete; later phases untouched**

## Task State

- [x] 1.1 RED — Added `frontend/e2e/auth.spec.ts` for **Registration with consent** and **Login and session** persistence.
- [x] 1.2 GREEN — Added the real-stack Compose harness, external Playwright base URL, and CI auth gate.
- [x] 1.3 REFACTOR — Kept journeys interception-free; added deadlines, quoted CI inputs, isolated teardown, and proof.
- [x] 2.1 RED — Added `frontend/e2e/journeys.spec.ts` for **Create, list, and open trade**, **Authenticated GDPR export**, and **Account deletion grace period**.
- [x] 2.2 GREEN — Added deterministic real-stack fixtures/download assertions and gated all five journeys.
- [x] 2.3 REFACTOR — Deduplicated fixtures without weakening assertions; proved runtime and slice rollback.

## Phase 1 Previous Attempt Evidence

The first attempt stopped before RED because the accessibility baseline had 3/6 failures. The second attempt made the authorized prerequisite correction: the billing-frequency selector is now an ARIA group of toggle buttons (`aria-pressed`) rather than an invalid tablist. The baseline then passed 6/6.

## Phase 1 Implementation

- Added two UI-only Playwright journeys with unique accounts; registration remains disabled until both consent controls are accepted, and both journeys authenticate through the real API/database.
- Added `docker-compose.ci.yml` with migrated PostgreSQL, Redis, MinIO, API, production frontend, and Playwright runner services.
- Externalized `E2E_BASE_URL`; external-stack runs disable Playwright's local web server.
- Added isolated CI project naming, a 30-minute job deadline, fail-on-E2E exit propagation, and `down -v --remove-orphans` under `if: always()`.
- Repaired pre-existing real-stack blockers discovered by RED/GREEN: the production API Dockerfile now reuses its restore stage, obsolete missing root-file copies were removed, Docker build artifacts are excluded, and frontend runtime Sentry configuration consumes the generated build-time module.

## Phase 1 TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|---|---|---|---|---|---|---|---|
| 1.1 | `frontend/e2e/auth.spec.ts` | E2E | ✅ a11y 6/6 after authorized prerequisite | ✅ 2 journeys failed before real-stack wiring | ✅ 2/2 passed against migrated stack | ✅ Consent-disabled state, persisted login, and reload path | ✅ Helpers/unique accounts; 2/2 remained green |
| 1.2 | `auth.spec.ts`, Compose/CI/config | E2E/infrastructure | ✅ Playwright and frontend baselines | ✅ Auth journeys failed without external stack | ✅ API/frontend images built and 2/2 journeys passed | ➖ Structural harness; one expected exit path | ✅ Explicit deadlines and writable isolated outputs |
| 1.3 | `frontend/e2e/auth.spec.ts` | E2E/infrastructure | ✅ 2/2 GREEN before cleanup | ✅ Existing journey assertions guarded behavior | ✅ 2/2 passed after refactor | ✅ Both independent accounts exercised | ✅ No route interception; final runtime 2/2 |

## Phase 1 Work Unit Evidence

| Evidence | Result |
|---|---|
| Focused test command and exact result | Compose runner executes `npm run test:e2e -- auth.spec.ts`: exit 0; 2 passed in 11.2s. |
| Runtime harness command and exact result | `timeout 30m docker compose -p wave13-phase1 -f docker-compose.ci.yml up --build --abort-on-container-exit --exit-code-from e2e e2e`: exit 0; migrations completed, API/frontend healthy, API and frontend images built, 2/2 journeys passed. |
| Isolated teardown | `docker compose -p wave13-phase1 -f docker-compose.ci.yml down -v --remove-orphans`: exit 0; containers, network, and all three named volumes removed. |
| Rollback boundary | Revert `.dockerignore`, `.gitignore`, `docker-compose.ci.yml`, `frontend/e2e/auth.spec.ts`, Playwright/CI config, the two runtime build repairs, and the billing ARIA prerequisite; later Wave 13 phases remain untouched. |

## Phase 1 Additional Verification

- `npm --prefix frontend run test:e2e -- a11y.spec.ts`: exit 0; 6/6 passed.
- `npm --prefix frontend test -- --runInBand src/app/core/observability/sentry-deployment.spec.ts`: exit 0; 7/7 passed.
- Focused Identity registration/login unit filter in the .NET 10 SDK container: exit 0; 20/20 passed.
- `docker compose ... config -q`: exit 0.
- `git diff --check`: exit 0.
- No `page.route`, fulfillment, continuation, or interception appears in `frontend/e2e/auth.spec.ts`.

## Phase 2 Implementation

- Added `frontend/e2e/journeys.spec.ts` with three independent deterministic accounts and retry-safe worker/retry suffixes.
- Created trading prerequisites and the open trade through authenticated real API calls, then verified persisted status, direction, price, volume, strategy, and notes in the real UI list.
- Exercised authenticated `/api/users/me/export` through the browser, generated a real Playwright download, parsed its JSON, and asserted the caller profile and export metadata.
- Exercised account deletion through the UI, asserted HTTP 202, exact 30-day response boundary, pending-success copy, and corrected the accidentally doubled lazy child route.
- Updated Compose and CI to execute `auth.spec.ts` plus `journeys.spec.ts`; retained `a11y.spec.ts` unchanged.
- No route interception was added.

## Phase 2 TDD Cycle Evidence

| Task | RED | GREEN | REFACTOR |
|---|---|---|---|
| 2.1 | **Written** — `frontend/e2e/journeys.spec.ts` defined all three Phase 2 journeys first; execution failed on missing `./fixtures/real-stack` before fixture implementation. | **Passed** — the focused real-stack journey run completed 3/3 in 15.1s. | **Passed** — helper extraction retained trade persistence, export-content, and deletion-boundary assertions. |
| 2.2 | **Written** — the all-five functional gate and its setup/download expectations existed before the supporting fixture, route, Compose, and CI behavior was complete. | **Passed** — the migrated real-stack functional run completed 5/5 in 23.2s. | **Passed** — Compose and CI use one five-journey gate with deterministic accounts and isolated outputs. |
| 2.3 | **Written** — the five functional journey assertions were retained as characterization tests before fixture/auth deduplication. | **Passed** — all five functional journeys remained green after deduplication. | **Passed** — cleanup, no-interception, Compose validation, and diff-check receipts passed without weakening assertions. |

## Phase 2 Work Unit Evidence

| Evidence | Result |
|---|---|
| Focused test command and exact result | Real-stack focused runner executed `npm run test:e2e -- journeys.spec.ts`: exit 0; 3/3 passed in 15.1s. |
| Runtime harness command and exact result | `timeout 30m docker compose -p wave13-phase2-proof -f docker-compose.ci.yml up --build --abort-on-container-exit --exit-code-from e2e e2e`: exit 0; migrated services healthy; functional journeys passed 5/5 in 23.2s. |
| Accessibility receipt | Accessibility passed 6/6 after Phase 2. |
| Frontend receipt | Frontend Jest passed 45 suites / 198 tests. |
| Compose receipt | `docker compose ... config --quiet`: passed. |
| Cleanup receipt | `docker compose -p wave13-phase2-proof -f docker-compose.ci.yml down -v --remove-orphans`: passed; Phase 2 containers, network, and three named volumes were removed. |
| No-interception receipt | No-interception search passed; no Playwright route interception or mock pattern was present. |
| Diff-check receipt | `git diff --check`: passed. |
| Review footprint | 330 authored additions/deletions, below the approved 800-line PR2 budget and the planned 400-line work-unit guard. |
| Rollback boundary | Revert `frontend/e2e/journeys.spec.ts`, `frontend/e2e/fixtures/real-stack.ts`, shared-helper edits in `auth.spec.ts`, the functional Compose/CI gate edits, and the deletion child-route correction. Phase 1 implementation and later phases remain independent. |

## Issues Found

- Phase 1: Host .NET SDK was unavailable. A containerized broad Identity run passed 434 tests but 10 Testcontainers cases failed from nested-Docker/Ryuk initialization; the focused registration/login backend set passed 20/20, and the real API image published and served both E2E journeys.
- Phase 1: The existing app emits unrelated SignalR/CSP console noise on the dashboard; it does not affect the specified auth persistence assertions.
- Phase 2: The deletion route was declared as `settings/delete-account` plus a nested `delete-account`; changing the child path to empty made `/app/settings/delete-account` reachable as documented.
- Phase 2: The trade journey uses authenticated real API fixture setup/create and verifies the persisted result through the real UI; no endpoint is mocked or intercepted.

## Deviations from Design

None in service reality, persistence, download, authorization, or CI gating. Minimal Phase 1 build/runtime repairs were required so the designed Compose-built real stack could start. Phase 2 fixture setup uses authenticated real endpoints to keep the journey deterministic while the user-visible persisted trade is verified in the UI.

## Remaining Work

Phases 3–5 remain unchecked and untouched.
