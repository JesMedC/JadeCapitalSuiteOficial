# Wave 13 CI/CD Maturity — Apply Progress

> **Canonical apply-progress ledger.** This file is authoritative for cumulative task and evidence state.

## Execution Context

- Branch: `feature/wave13-cicd-maturity-final`
- Assigned scope: Phase 1 tasks 1.1–1.3 only
- Mode: Strict TDD
- Delivery: `auto-chain`, `feature-branch-chain`, work unit 1
- Parent token: `sha256:bee90b3532da743a02839f31fc6a0cb2cfcea9cee4c8569a1a3df9a185f611d7`
- Transaction action: none (`acquire`/`settle` explicitly excluded)
- Status: **Phase 1 complete**

## Task State

- [x] 1.1 RED — Added `frontend/e2e/auth.spec.ts` for **Registration with consent** and **Login and session** persistence.
- [x] 1.2 GREEN — Added the real-stack Compose harness, external Playwright base URL, and CI auth gate.
- [x] 1.3 REFACTOR — Kept journeys interception-free; added deadlines, quoted CI inputs, isolated teardown, and proof.

## Previous Attempt Evidence

The first attempt stopped before RED because the accessibility baseline had 3/6 failures. The second attempt made the authorized prerequisite correction: the billing-frequency selector is now an ARIA group of toggle buttons (`aria-pressed`) rather than an invalid tablist. The baseline then passed 6/6.

## Implementation

- Added two UI-only Playwright journeys with unique accounts; registration remains disabled until both consent controls are accepted, and both journeys authenticate through the real API/database.
- Added `docker-compose.ci.yml` with migrated PostgreSQL, Redis, MinIO, API, production frontend, and Playwright runner services.
- Externalized `E2E_BASE_URL`; external-stack runs disable Playwright's local web server.
- Added isolated CI project naming, a 30-minute job deadline, fail-on-E2E exit propagation, and `down -v --remove-orphans` under `if: always()`.
- Repaired pre-existing real-stack blockers discovered by RED/GREEN: the production API Dockerfile now reuses its restore stage, obsolete missing root-file copies were removed, Docker build artifacts are excluded, and frontend runtime Sentry configuration consumes the generated build-time module.

## TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|---|---|---|---|---|---|---|---|
| 1.1 | `frontend/e2e/auth.spec.ts` | E2E | ✅ a11y 6/6 after authorized prerequisite | ✅ 2 journeys failed before real-stack wiring | ✅ 2/2 passed against migrated stack | ✅ Consent-disabled state, persisted login, and reload path | ✅ Helpers/unique accounts; 2/2 remained green |
| 1.2 | `auth.spec.ts`, Compose/CI/config | E2E/infrastructure | ✅ Playwright and frontend baselines | ✅ Auth journeys failed without external stack | ✅ API/frontend images built and 2/2 journeys passed | ➖ Structural harness; one expected exit path | ✅ Explicit deadlines and writable isolated outputs |
| 1.3 | `frontend/e2e/auth.spec.ts` | E2E/infrastructure | ✅ 2/2 GREEN before cleanup | ✅ Existing journey assertions guarded behavior | ✅ 2/2 passed after refactor | ✅ Both independent accounts exercised | ✅ No route interception; final runtime 2/2 |

## Work Unit Evidence

| Evidence | Result |
|---|---|
| Focused test command and exact result | Compose runner executes `npm run test:e2e -- auth.spec.ts`: exit 0; 2 passed in 11.2s. |
| Runtime harness command and exact result | `timeout 30m docker compose -p wave13-phase1 -f docker-compose.ci.yml up --build --abort-on-container-exit --exit-code-from e2e e2e`: exit 0; migrations completed, API/frontend healthy, API and frontend images built, 2/2 journeys passed. |
| Isolated teardown | `docker compose -p wave13-phase1 -f docker-compose.ci.yml down -v --remove-orphans`: exit 0; containers, network, and all three named volumes removed. |
| Rollback boundary | Revert `.dockerignore`, `.gitignore`, `docker-compose.ci.yml`, `frontend/e2e/auth.spec.ts`, Playwright/CI config, the two runtime build repairs, and the billing ARIA prerequisite; later Wave 13 phases remain untouched. |

## Additional Verification

- `npm --prefix frontend run test:e2e -- a11y.spec.ts`: exit 0; 6/6 passed.
- `npm --prefix frontend test -- --runInBand src/app/core/observability/sentry-deployment.spec.ts`: exit 0; 7/7 passed.
- Focused Identity registration/login unit filter in the .NET 10 SDK container: exit 0; 20/20 passed.
- `docker compose ... config -q`: exit 0.
- `git diff --check`: exit 0.
- No `page.route`, fulfillment, continuation, or interception appears in `frontend/e2e/auth.spec.ts`.

## Issues Found

- Host .NET SDK is unavailable. A containerized broad Identity run passed 434 tests but 10 Testcontainers cases failed from nested-Docker/Ryuk initialization; the focused registration/login backend set passed 20/20, and the real API image published and served both E2E journeys.
- The existing app emits unrelated SignalR/CSP console noise on the dashboard; it does not affect the specified auth persistence assertions.

## Deviations from Design

None in behavior or architecture. Minimal build/runtime repairs were required so the designed Compose-built real stack could start; they do not add later-phase capability.

## Remaining Work

Phase 2 and all later phases remain unchecked and untouched.
