# Design: Wave 13 CI/CD Maturity

## Technical Approach

Add five strict-TDD slices using existing Compose, Playwright, SQL-migration, and Identity Infrastructure patterns. CI uses real services; recovery uses a second cluster; release evidence follows all gates. Domain and Host composition remain unchanged.

## Architecture Decisions

| Decision | Choice | Alternative / rationale |
|---|---|---|
| Functional E2E | Compose-built stack; external `E2E_BASE_URL`, no interception | Mocks produce false-green persistence/session results |
| `0040` activation | Catalog-checked `ABSENT → PREPARED → ACTIVE`; exact copy validation and rename in one locked transaction | Unlocked/non-idempotent conversion rejected: writes can be lost |
| PITR proof | wal-g catalog evidence plus restore into a new empty PostgreSQL volume/network | Archive-command success or in-place restore cannot prove recoverability/isolation |
| Release truth | Archive keys and evidence bound to `GITHUB_SHA`; fail if delivery exists | Prose/pre-existing releases are ambiguous |

## Data Flow

```text
CI → migrated real stack → five Playwright journeys → teardown -v
source PG → base backup → marker A/LSN → archived WAL/catalog → empty PITR PG
archive dirs ↔ manifest ↔ CHANGELOG markers → RC evidence(SHA, no delivery)
```

## `0040` and Fail-Closed Boundaries

`audit.migration_0040_state` stores `PREPARED|ACTIVE` and immutable UTC `partition_anchor`. Each run locks and reconciles state with `pg_class`; contradictions abort.

- **ABSENT**: persist `partition_anchor = date_trunc('month', transaction_timestamp() AT TIME ZONE 'UTC')`. Create a RANGE shadow, composite PK, DEFAULT, and monthly `[start,end)` partitions from `coalesce(month(min(occurred_at)), anchor)` through `greatest(month(max(occurred_at))+1 month, anchor+2 months)`. Empty sources deterministically receive anchor/current and anchor+1/future; no finite horizon is hardcoded.
- **PREPARED**: in one transaction, lock canonical source, truncate/copy shadow, require equal counts and empty bidirectional `EXCEPT ALL`, rename source aside and shadow canonical, then mark `ACTIVE`. Failure remains `PREPARED`; rerun recopies.
- **ACTIVE**: require partitioned canonical parent, `(id, occurred_at)` PK, persisted bounds/current/next/DEFAULT coverage, and retained unpartitioned source; otherwise fail.

The gate rejects any occupied/non-idempotent `0039` partition migration, fixed August–November 2026 horizon, database/EF key mismatch, source `DROP`/`CASCADE`, in-place restore, or destructive recovery instruction. Detection or uncertainty stops apply; rejected drafts are evidence only. EF ships with the composite key. Retention deletes bounded ordered composite pairs.

## Recovery and Evidence Contracts

After writing A, capture its timestamp, LSN, timeline, and WAL segment; force a switch and poll with a deadline. `wal-g backup-list --json` must identify the base, and WAL catalog output must prove an unbroken same-timeline range through A's segment. Missing/malformed data fails before restore. PITR proves A present, B absent, source unchanged, and RPO ≤3600s.

RC generation fails on local `refs/tags/v1.1.0-rc1`, its exact `git ls-remote --tags origin` ref, or authoritative GitHub Release/publication lookup. Query/auth/network failure is not absence. Only then may the builder write evidence; it never delivers.

## File Changes

| Files | Action | Description |
|---|---|---|
| `.github/workflows/ci.yml`, `docker-compose{.ci,.pitr}.yml`, `frontend/{playwright.config.ts,e2e/}` | Modify/Create | Real-stack gates and five journeys |
| `infrastructure/{postgres,backup}/`, `docs/runbooks/disaster-recovery.md` | Modify/Create | wal-g catalog/PITR and non-destructive rollback |
| `infrastructure/postgres/migrations/0040_*`, Identity Infrastructure audit mapping/retention, migration tests | Create/Modify | Stateful partition conversion and EF alignment |
| `openspec/archive-manifest.json`, `scripts/`, `CHANGELOG.md` | Create/Modify | Archive validation and non-delivering RC evidence |

## Post-Write Rollback Algorithm

Executable rehearsal: (1) quiesce writers; (2) create an unpartitioned rollback shadow shaped like retained source; (3) under advisory/`ACCESS EXCLUSIVE` locks, recopy canonical rows; (4) fail if duplicate `id` violates the old PK; (5) require equal counts and empty bidirectional `EXCEPT ALL`; (6) atomically rename partitioned data aside and shadow canonical; (7) deploy prior EF key before resuming. Invariants: no concurrent writes, exact equality, old-key validity, no dropped table. Rehearsal upgrades seeded data, adds current/DEFAULT rows, rolls back, proves equality/writability, then reruns `0040`.

## Testing Strategy and Delivery

RED tests individually gate: **Registration with consent**, **Login and session**, **Create, list, and open trade**, **Authenticated GDPR export**, and **Account deletion grace period**. Integration covers catalog gaps, A/T/B, migration states, rejected-draft signatures, EF/retention, rollback, archive drift, and existing local tag, remote tag, or publication. Use five chained slices, ≤400 authored lines each.

## Threat Matrix

| Boundary | Applicability | Design response / RED tests |
|---|---|---|
| Documentation-like paths | N/A: no executable classification | No task |
| Git repository selection | Applicable | Canonical `git -C <root>`; reject missing/outside roots; test relative, absolute, wrong roots |
| Commit state | N/A: no commit/index mutation | No task |
| Push state | N/A: no push | No task |
| PR commands | N/A: no PR automation | No task |

Shell/process scripts use quoted variables, fixed Compose files, deadlines, isolated project names, fail-closed subprocess status, and teardown traps.

## Migration / Rollout

Rehearse capacity, locks, catalog proof, and rollback before the maintenance window. Retain daily dumps, wal-g objects, and all renamed source tables. Gates roll back independently; tagging/publishing remains out of scope.

## Open Questions

None.
