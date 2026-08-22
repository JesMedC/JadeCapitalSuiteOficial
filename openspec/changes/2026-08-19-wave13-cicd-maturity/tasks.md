# Tasks: Wave 13 CI/CD Maturity

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines | 1,400–2,000 |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | Five PRs, each ≤400/800 lines |
| Delivery strategy | auto-chain |
| Chain strategy | feature-branch-chain |

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: feature-branch-chain
400-line budget risk: High

### Suggested Work Units

| Unit | Goal/base | Focused test | Runtime harness | Rollback boundary |
|---|---|---|---|---|
| 1 | Auth E2E; PR1→tracker | `npm --prefix frontend run test:e2e -- auth.spec.ts` | `docker compose -f docker-compose.ci.yml up --build --abort-on-container-exit e2e` | Harness/config/CI job |
| 2 | Journeys; PR2→PR1 | `npm --prefix frontend run test:e2e -- journeys.spec.ts` | Same stack; teardown `-v` | Journey specs/fixtures |
| 3 | PITR; PR3→PR2 | `bash scripts/test-pitr.sh` | `docker compose -f docker-compose.pitr.yml up --build --abort-on-container-exit` | PITR files; retain backups |
| 4 | Audit; PR4→PR3 | `dotnet test --filter FullyQualifiedName~AuditPartition` | `docker compose run --rm migrate`; rehearse rollback | `0040`/EF/retention; retained source |
| 5 | Release; PR5→PR4 | `python scripts/test-release-readiness.py` | `GITHUB_SHA=$(git rev-parse HEAD) python scripts/build-rc-evidence.py` | Manifest/scripts/CI/evidence |

## Phase 1: Authenticated E2E Foundation

- [x] 1.1 RED — Add `frontend/e2e/auth.spec.ts` for **Registration with consent** and **Login and session** persistence.
- [x] 1.2 GREEN — Create `docker-compose.ci.yml`, externalize `frontend/playwright.config.ts`, and gate auth E2E in `.github/workflows/ci.yml`.
- [x] 1.3 REFACTOR — Remove interception; add quoted inputs, deadlines, isolated teardown, focused/runtime proof.

## Phase 2: Remaining Full-Stack Journeys

- [ ] 2.1 RED — Add `frontend/e2e/journeys.spec.ts` for **Create, list, and open trade**, **Authenticated GDPR export**, and **Account deletion grace period**.
- [ ] 2.2 GREEN — Add `frontend/e2e/fixtures/` setup/download assertions and gate all five journeys.
- [ ] 2.3 REFACTOR — Deduplicate fixtures without weakening assertions; prove runtime and slice rollback.

## Phase 3: WAL and Isolated PITR

- [ ] 3.1 RED — Add `scripts/test-pitr.sh` failures for **Base/WAL coverage**, **Isolated PITR target**, and **Drill proves the RPO**.
- [ ] 3.2 GREEN — Add `docker-compose.pitr.yml` and `infrastructure/backup/` catalog-through-A and isolated A/T/B restore.
- [ ] 3.3 REFACTOR — Fail closed on catalog gaps, deadlines, in-place restore, or source mutation; update `docs/runbooks/disaster-recovery.md`; rehearse rollback.

## Phase 4: Audit Partitioning and Retention

- [ ] 4.1 RED — Test **Fresh database**, **Historical upgrade**, **Safe rerun**, **Default coverage**, **Future coverage**, **Retained source**, **EF identity**, and **Partition-aware retention**; reject drafts; prove rollback/rerun.
- [ ] 4.2 GREEN — Create `infrastructure/postgres/migrations/0040_partition_audit_events.sql`; update Identity `AuditEventConfiguration.cs` and `AuditRetentionBackgroundService.cs` for composite pairs.
- [ ] 4.3 REFACTOR — Centralize state/catalog checks, exact-copy validation, anchor, locks, retained-source rollback, and runtime proof.

## Phase 5: Archive Gate and Non-Delivering RC

- [ ] 5.1 RED — Test **Archive is unlisted**, **Changelog claim lacks an archive**, and **Readiness evidence is non-delivering** in `scripts/test-release-readiness.py`; cover relative/absolute/missing/outside/wrong roots, local/remote tags, publication, lookup failure.
- [ ] 5.2 GREEN — Add `openspec/archive-manifest.json`, validation/evidence builders under `scripts/`, `CHANGELOG.md` markers, and ordered CI gates bound to `GITHUB_SHA`.
- [ ] 5.3 REFACTOR — Canonicalize `git -C <root>`, quote fail-closed subprocesses, prove runtime, and confirm no delivery.
