# Wave 13 CI/CD Maturity — Apply Progress

> **Canonical apply-progress ledger.** This file is authoritative for cumulative task and evidence state.

## Execution Context

- Branches: Phase 1 `feature/wave13-cicd-maturity-final`; Phase 2 `feature/wave13-e2e-journeys`; Phase 3 `feature/wave13-walg-pitr`; Phase 4 `feature/wave13-audit-partitioning`; Phase 5 `feature/wave13-release-readiness`
- Completed scope: Phase 1 tasks 1.1–1.3, Phase 2 tasks 2.1–2.3, Phase 3 tasks 3.1–3.3, Phase 4 tasks 4.1–4.3, and Phase 5 tasks 5.1–5.3
- Mode: Strict TDD
- Delivery: `auto-chain`, `feature-branch-chain`; work unit 5 / PR5 targets PR4
- Parent tokens: Phase 1 `sha256:bee90b3532da743a02839f31fc6a0cb2cfcea9cee4c8569a1a3df9a185f611d7`; Phase 2 `sha256:41b95b10a4a59c9c91b8774cd53f114e2597ade410d1481808b704bb9080b083`; Phase 3 `sha256:4f1edb9c12e47e0a7236b2d8e08fa7b6ba2520afebf849616f039bbc39f854d2`; Phase 4 `sha256:f39aa19fa81b2278820b656510cc2b1be22d4dbdb2a8cd3474b2484447017fb7`; Phase 5 `sha256:ff20d003a4135f63aaefab9fb04d63d36963c6796eb0aa3e7732e4e6c1ba8e1c`
- Transaction action: none (`acquire`/`settle` explicitly excluded)
- Status: **Phases 1–5 complete; ready for verification**

## Task State

- [x] 1.1 RED — Added `frontend/e2e/auth.spec.ts` for **Registration with consent** and **Login and session** persistence.
- [x] 1.2 GREEN — Added the real-stack Compose harness, external Playwright base URL, and CI auth gate.
- [x] 1.3 REFACTOR — Kept journeys interception-free; added deadlines, quoted CI inputs, isolated teardown, and proof.
- [x] 2.1 RED — Added `frontend/e2e/journeys.spec.ts` for **Create, list, and open trade**, **Authenticated GDPR export**, and **Account deletion grace period**.
- [x] 2.2 GREEN — Added deterministic real-stack fixtures/download assertions and gated all five journeys.
- [x] 2.3 REFACTOR — Deduplicated fixtures without weakening assertions; proved runtime and slice rollback.
- [x] 3.1 RED — Added failing PITR scenario and fail-closed contract tests before WAL-G production code.
- [x] 3.2 GREEN — Added continuous WAL-G archival, base backups, catalog-through-A validation, and isolated A/T/B recovery.
- [x] 3.3 REFACTOR — Centralized fail-closed catalog/deadline/isolation/source-integrity checks; updated CI and the DR runbook.
- [x] 4.1 RED — Added real-PostgreSQL tests for fresh/upgrade/checkpoint/rerun, deterministic routing, exact retention, rejected drafts, and rollback/rerun.
- [x] 4.2 GREEN — Added migration 0040, EF composite identity, and ordered composite-pair retention deletion.
- [x] 4.3 REFACTOR — Centralized catalog/state invariants, exact-copy checks, locks, retained-source rollback, and full-runner proof.
- [x] 5.1 RED — Added release-readiness tests and explicit negative fixtures for archive-unlisted and changelog-claim-without-archive drift, repository roots, delivery presence, and lookup failures.
- [x] 5.2 GREEN — Added the exact archive manifest, changelog claims, validator/evidence builders, ordered CI gate, generated SHA-bound evidence, and operator documentation.
- [x] 5.3 REFACTOR — Centralized canonical root/subprocess handling, atomic evidence output, fail-closed delivery checks, and runtime proof without delivery.

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

## Phase 3 Implementation

- Added a checksum-pinned WAL-G 3.0.9 PostgreSQL 16 image, secret-file wrapper, operations-profile base-backup service, continuous `wal-push`, and production `archive_timeout=3600s`.
- Added an isolated Compose drill with independent source, archive, and initially empty restore volumes. It records marker A timestamp/LSN/timeline/segment, requires a nonempty base catalog and gap-free same-timeline `wal-show --detailed-json` coverage through A, records T, writes B, and restores to T.
- Proved the restored cluster contains A but not B, the source fingerprint remains unchanged, and cutoff-to-latest-recovered RPO is 1 second (≤3600 seconds).
- Added nine fast fail-closed contract checks for catalog gaps/malformed JSON, deadlines, in-place restore, source mutation, missing storage, and unreadable operator secrets.
- Added the PITR CI job and operator-safe disaster-recovery/base-backup/rollback instructions. Local test credentials use a temporary mode-`0600` file removed by teardown.

## Phase 3 TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|---|---|---|---|---|---|---|---|
| 3.1 | `scripts/test-pitr.sh` | Integration | N/A (new) | ✅ Failed because `docker-compose.pitr.yml` did not exist | ✅ 3/3 PITR scenarios passed | ✅ Base/WAL, A/T/B, and measured RPO paths | ✅ Proof assertions and isolated cleanup retained |
| 3.2 | `scripts/test-pitr.sh`, `pitr-drill.sh` | Runtime integration | ✅ RED scenario harness existed first | ✅ Missing WAL-G/Compose implementation | ✅ Real backup, archive, fetch, and recovery passed | ✅ Base catalog plus A and B WAL boundaries | ✅ Pinned image, deadline polling, and exact evidence fields |
| 3.3 | `scripts/test-pitr-contracts.sh` | Contract/integration | ✅ Runtime drill passed before extraction | ✅ Failed on missing `pitr-contract.sh` | ✅ 9/9 fail-closed contracts passed | ✅ Valid and rejected catalog/deadline/path/hash/secret cases | ✅ Shared validators; full drill remained 3/3 green |

## Phase 3 Work Unit Evidence

| Evidence | Result |
|---|---|
| Focused test command and exact result | `bash scripts/test-pitr.sh`: exit 0; 9/9 fail-closed contracts and 3/3 PITR scenarios passed. Catalog showed one base and continuous timeline 1 through A segment `000000010000000000000004`; measured RPO was 1 second. |
| Runtime harness command and exact result | `docker compose -p wave13-pitr-1775182 -f docker-compose.pitr.yml up --build --abort-on-container-exit --exit-code-from drill drill` (executed by the focused runner): exit 0; WAL-G base push/fetch completed; isolated restore contained A, excluded B, and source fingerprint was unchanged. |
| Cleanup/rollback rehearsal | Runner trap executed `down -v --remove-orphans`; no `wave13-pitr` containers or volumes remained. Production WAL-G objects/daily dumps are outside the isolated cleanup boundary and the runbook forbids deleting them or restoring in place. |
| Compose/syntax receipts | Production Compose config, all shell syntax checks, `git diff --check`, and local secret-file handling passed. |
| Review footprint | 516 authored additions/deletions including cumulative artifact updates; below the approved 800-line PR3 budget. |
| Rollback boundary | Revert `docker-compose.pitr.yml`, WAL-G files under `infrastructure/backup/`, production Compose WAL-G/base-backup additions, PITR scripts, CI job, and DR runbook additions. Retain existing daily dump scripts and all remote backup objects; Phases 1–2 and 4–5 remain independent. |

## Phase 4 Implementation

- Added stateful `0040` with immutable UTC anchor, deterministic historical/current/next/DEFAULT partitions, PREPARED checkpoints, locked recopy, count plus bidirectional `EXCEPT ALL` validation, atomic swap, retained source, and ACTIVE catalog/key/coverage reconciliation.
- Added an executable rollback rehearsal that rejects old-key duplicates, recopies and validates post-write rows, atomically restores an unpartitioned canonical table, retains both prior generations, and leaves PREPARED state for a convergent rerun.
- Aligned EF identity to `(id, occurred_at)` and changed PostgreSQL retention to delete bounded ordered composite pairs across parent partitions.
- Made migration 0030's payload comment compatible with both pre-0034 `changes` and post-0034 `changes_json`, allowing the complete migration runner to rerun after partition activation.

## Phase 4 TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|---|---|---|---|---|---|---|---|
| 4.1 | `AuditPartitionMigrationTests.cs` | Real PostgreSQL integration | ✅ retention 3/3; anonymizer 3/3 | ✅ 5/5 failed before 0040/rehearsal existed | ✅ 5/5 passed | ✅ Empty, historical, checkpoint, current/next/DEFAULT, duplicate-id, retention, rollback paths | ✅ Shared DB/SQL helpers; 5/5 remained green |
| 4.2 | migration, EF mapping, retention service | PostgreSQL/EF integration | ✅ existing mapping/retention baselines | ✅ Migration absent and id-only EF/retention behavior rejected | ✅ 5/5 passed against PostgreSQL 16 | ✅ Equal id at different timestamps plus monthly/DEFAULT expiry | ✅ Composite deletion centralized in one bounded SQL statement |
| 4.3 | migration and rollback rehearsal | Runtime integration | ✅ Phase 4 focused tests green | ✅ Full migration rerun failed at 0030's stale payload comment | ✅ Fresh and second full runner both completed | ✅ Rollback equality, post-write recopy, and rerun convergence | ✅ State/catalog/equality gates centralized; focused tests stayed 5/5 |

## Phase 4 Work Unit Evidence

| Evidence | Result |
|---|---|
| Focused test command and exact result | `mise exec dotnet@10.0.400 -- dotnet test tests/IntegrationTests/JadeCapital.Api.IntegrationTests/JadeCapital.Api.IntegrationTests.csproj --filter FullyQualifiedName~AuditPartition --maxcpucount:1`: exit 0; 5/5 passed against Testcontainers PostgreSQL 16. |
| Runtime harness command and exact result | `docker compose -p wave13-phase4 -f docker-compose.ci.yml run --rm migrate` executed twice against one fresh volume: both exit 0; final catalog `p|ACTIVE|3` (partitioned parent, ACTIVE state, current/next/DEFAULT children). Teardown removed the volume/network. |
| Supporting verification | Audit retention unit tests 3/3; migration-order tests 5/5; solution build succeeded with 0 errors and 3 pre-existing CA2263 warnings; `git diff --check` passed. |
| Review footprint | 538 authored additions/deletions including cumulative artifact updates; below the approved 800-line PR4 budget. |
| Rollback boundary | Revert migration `0040`, its rehearsal/test, EF composite mapping, composite retention SQL, migration-count updates, and the 0030 rerun guard. The retained source and partitioned generation are never automatically deleted; Phases 1–3 and 5 remain independent. |

## Phase 5 Implementation

- Added an exact three-way set gate across OpenSpec archive directories, the sorted versioned manifest, and machine-readable changelog claims; mismatch diagnostics name every offending key.
- Added deterministic negative fixtures for both specified drift directions and fail-closed tests for malformed sets, repository roots, SHA mismatch, local/remote tags, GitHub publication, and unavailable lookups.
- Added non-delivering `v1.1.0-rc1` evidence generation bound to current `GITHUB_SHA`, exact remote tag ref, authoritative GitHub release lookup, and manifest digest. Evidence is written atomically only after all checks pass.
- Added an ordered CI job after all existing gates plus an operator runbook. No tag, release, publication, commit, push, archive, acquire, or settle action was performed.

## Phase 5 TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|---|---|---|---|---|---|---|---|
| 5.1 | `scripts/test-release-readiness.py`, two JSON fixtures | Unit/contract | N/A — new release gate | ✅ Import failed with `ModuleNotFoundError: release_readiness` before production code | ✅ 11/11 tests passed | ✅ Exact match plus both mismatch directions and malformed ordering | ✅ Fixture setup centralized; 11/11 remained green |
| 5.2 | release-readiness suite and current-repo validator | Unit/runtime | ✅ Existing CI contract validator passed all checks | ✅ Validator/build APIs were absent | ✅ 11/11 tests and 12/12 current archive claims passed | ✅ SHA mismatch plus local/remote/publication absence and presence | ✅ Shared module with thin CLIs; all checks remained green |
| 5.3 | release-readiness suite | Contract/runtime | ✅ 11/11 green before final root/fixture cleanup | ✅ Root and fail-closed command expectations existed before implementation | ✅ 11/11 passed after canonicalization | ✅ Relative/absolute success; missing/outside/wrong root and lookup failures | ✅ Atomic output and canonical `git -C`; runtime remained green |

## Phase 5 Work Unit Evidence

| Evidence | Result |
|---|---|
| Focused test command and exact result | `python3 scripts/test-release-readiness.py`: exit 0; 11/11 tests passed, including two persisted negative fixtures and five delivery/lookup failure subcases. |
| Runtime harness command and exact result | `GITHUB_SHA="$(git rev-parse HEAD)" python3 scripts/build-rc-evidence.py --root . --output artifacts/release/v1.1.0-rc1-readiness.json`: exit 0; evidence built for `12e72caf35bbc0930f17d601386a2cc3c5465ca2`; 12 archive claims exact; local tag absent, exact remote tag absent, GitHub release lookup returned authoritative HTTP 404. |
| Supporting verification | Current-repo validator passed 12/12; existing CI validator passed; solution build succeeded with 0 errors and 3 pre-existing CA2263 warnings; `git diff --check` passed. |
| Review footprint | 563 authored additions/deletions including cumulative artifact updates and generated readiness evidence; below the approved 800-line PR5 budget. |
| Rollback boundary | Revert the release-readiness Python files/fixtures, archive manifest, changelog claims, CI job, generated evidence, and release-readiness runbook. Phases 1–4 and all archived OpenSpec changes remain unchanged. |

## Issues Found

- Phase 1: Host .NET SDK was unavailable. A containerized broad Identity run passed 434 tests but 10 Testcontainers cases failed from nested-Docker/Ryuk initialization; the focused registration/login backend set passed 20/20, and the real API image published and served both E2E journeys.
- Phase 1: The existing app emits unrelated SignalR/CSP console noise on the dashboard; it does not affect the specified auth persistence assertions.
- Phase 2: The deletion route was declared as `settings/delete-account` plus a nested `delete-account`; changing the child path to empty made `/app/settings/delete-account` reachable as documented.
- Phase 2: The trade journey uses authenticated real API fixture setup/create and verifies the persisted result through the real UI; no endpoint is mocked or intercepted.
- Phase 3: WAL-G 3.0.9 exposes `wal-show --detailed-json` as a timeline array rather than the older documented `--json` object; validation targets the installed, checksum-pinned binary and fails on unknown/malformed shapes.
- Phase 4: The first complete-chain rerun exposed migration 0030's unconditional comment on the pre-0034 `changes` column. A fail-closed column-aware comment guard fixed reruns without changing data.
- Phase 5: CodeGraph could not run because the local `mise` shim has no configured CodeGraph runtime; artifact-guided filesystem inspection was used after the required CodeGraph status attempt failed.

## Deviations from Design

None in service reality, persistence, download, authorization, CI gating, PITR isolation, audit partition safety, archive consistency, or non-delivering RC evidence. Minimal Phase 1 build/runtime repairs were required so the designed Compose-built real stack could start. Phase 2 fixture setup uses authenticated real endpoints to keep the journey deterministic while the user-visible persisted trade is verified in the UI. Phase 3 uses WAL-G 3.0.9's current `--detailed-json` catalog shape while preserving the designed fail-closed continuity contract. Phase 4 places composite-pair deletion in `AuditRetentionService`, where deletion identity is resolved, while the background scheduler continues to supply cutoff and batch policy.

## Remaining Work

All planned Wave 13 implementation tasks are complete. Verification and archive remain separate parent-orchestrated phases.
