# Proposal: Wave 13 CI/CD Maturity

## Intent

Establish verified gates for five full-stack journeys, PostgreSQL RPO ≤1 hour, safe audit partitioning, archive/changelog consistency, and `v1.1.0-rc1` readiness without publishing.

## Scope

### In Scope
- Run five real Playwright paths against the migrated stack: registration with consent; login/session; create/list/open trade; authenticated GDPR export; account deletion entering grace period.
- Configure wal-g, base backups, and an isolated PITR drill proving RPO ≤1 hour.
- Add rerunnable migration `0040` for monthly `audit.events` partitions with EF composite-key alignment, DEFAULT/current/future coverage, retention compatibility, validated atomic swap, and deferred cleanup.
- Cross-check `CHANGELOG.md` against an OpenSpec archive manifest in CI.
- Produce exact-commit `v1.1.0-rc1` readiness evidence; tagging/publishing requires an explicit delivery step.

### Out of Scope
- Shipped accessibility/axe work and Wave 12 OpenTelemetry.
- Payment E2E, multi-region failover, tagging, or publishing.

## Capabilities

### New Capabilities
- `audit-partition-management`: Partition migration, coverage, EF alignment, and rerun behavior.

### Modified Capabilities
- `ci-infrastructure`: Functional Playwright and archive-validation gates.
- `backup-strategy`: wal-g archival and isolated PITR evidence.
- `audit-retention-policy`: Retention across parent, monthly, and DEFAULT partitions.
- `production-readiness`: Changelog/archive consistency and RC evidence.

## Approach

Use five strict-TDD slices: E2E harness/auth, remaining journeys, WAL/PITR, audit partitioning, then release evidence. Never promote the rejected draft. `0040` uses a shadow table, count validation, transactional rename, retained source, and state-aware reruns. Identity changes stay in Infrastructure; Host only composes services.

## Affected Areas

| Area | Impact | Description |
|---|---|---|
| `.github/workflows/ci.yml`, `frontend/e2e/` | Modified | Full-stack gates |
| `docker-compose.prod.yml`, `infrastructure/{postgres,backup}/`, `docs/runbooks/` | Modified | WAL/PITR proof |
| `infrastructure/postgres/migrations/0040_*`, `src/2.Modules/Identity/.../Infrastructure/` | Modified | Partitions, EF, retention |
| `CHANGELOG.md`, `scripts/`, `openspec/changes/archive/` | Modified | RC evidence |

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| False-green E2E/PITR | Medium | Real services; restore into separate empty Postgres |
| Lock/storage pressure during partition swap | High | Maintenance sizing, transactional swap, retained source |
| DEFAULT partition hides maintenance lag | Medium | Monitor occupancy and precreate future partitions |

## Rollback Plan

Disable gates independently; revert wal-g settings while retaining daily dumps; transactionally restore the retained table and prior EF key; revert evidence files. Never restore over the source database or publish during rollback.

## Dependencies

- Separate WAL backup endpoint/credentials; CI containers; migration `0039`.

## Success Criteria

- [ ] All five named journeys pass in CI against the real stack.
- [ ] An isolated PITR drill proves a target boundary and RPO ≤1 hour.
- [ ] Fresh, upgrade, rerun, retention, historical, DEFAULT, and future-partition tests pass for `0040` with EF alignment.
- [ ] CI rejects archive/changelog drift, and exact-commit RC evidence exists without a tag or publication.
