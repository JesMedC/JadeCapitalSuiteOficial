# Exploration: Wave 13 CI/CD Maturity and Operations Hardening

## Current State

Wave 12 is complete at `c311ef8`, with 1,590 backend tests, 59 integration tests, 198 frontend tests, and migration `0039_add_existing_user_aggregate_columns.sql`. Therefore Wave 13 MUST start at migration `0040`; the old dirty Wave 13 draft is evidence only and MUST NOT be copied.

| Intended item | Evidence after Wave 12 | Status |
|---|---|---|
| Five Playwright happy paths in CI | Playwright, Chromium installation, `playwright.config.ts`, and an axe-only CI job exist. The sole E2E spec is `frontend/e2e/a11y.spec.ts`; the web server starts Angular only while `/api` proxies to an API that CI does not start. | Partially shipped; full-stack functional E2E is missing. |
| wal-g continuous archiving/PITR | Daily `pg_dump` and dump restore scripts exist. Production Postgres has no wal-g binary/config, `archive_mode`, `archive_command`, `restore_command`, or PITR test. The runbook's RPO claim is conditional and currently unproven. | Not shipped. |
| Audit log partitioning | `audit.events` is unpartitioned with PK `id`; EF maps `HasKey(e => e.Id)`. Retention still materializes up to 100,000 rows and deletes by IDs. | Not shipped. |
| Automated CHANGELOG archive cross-check | Wave 11 added a manual cross-check note, but no CI checker exists. The file still contains repeated `v1.0.0-rc1` release headings and inaccurate early-wave attribution identified by the rejected draft. | Manual partial only; automation and factual repair are missing. |
| `v1.1.0-rc1` readiness | Tags `v1.0.0-rc1` and `v1.0.0` are ancestors of HEAD; no `v1.1.0-rc1` tag or release gate exists. Wave 12 already shipped optional PII-safe OpenTelemetry, so that draft item is out of Wave 13 scope. | Not shipped. |

The rejected draft's partition migration is unsafe: it reuses occupied number `0039`, is not rerunnable, hard-codes only August-November 2026, changes the database PK without the matching EF composite key, drops the source table, and documents destructive recovery. Its claim that PostgreSQL table rename is non-transactional is also incorrect. No draft code should be promoted.

CodeGraph was checked first but its CLI could not run because the local `mise` shim has no configured Node version; this exploration therefore used direct repository evidence.

## Affected Areas

- `.github/workflows/ci.yml` — add a full-stack functional E2E gate without replacing the existing axe job.
- `frontend/playwright.config.ts`, `frontend/e2e/` — support API readiness, isolated test data, failure artifacts, and five functional paths.
- `frontend/src/app/features/auth/`, `frontend/src/app/features/trader/` — stable accessible selectors and the critical user journeys under test.
- `docker-compose.prod.yml`, `infrastructure/postgres/`, `infrastructure/backup/` — wal-g image/config, separate WAL target, base backup, and non-destructive PITR restore verification.
- `docs/runbooks/disaster-recovery.md` — replace conditional recovery assumptions with tested commands and explicit source/restore isolation.
- `infrastructure/postgres/migrations/0040_audit_events_partitioning.sql` — idempotent, transactional partition conversion beginning after Wave 12's `0039`.
- `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/Configurations/AuditEventConfiguration.cs` — align EF's key with the partitioned database key.
- `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/` — prove retention behavior across parent/default/monthly partitions.
- `CHANGELOG.md`, `scripts/`, `.github/workflows/ci.yml` — machine-check archive coverage and release-note structure.
- `openspec/changes/archive/`, release metadata, and release workflow — source evidence and final `v1.1.0-rc1` readiness gate.

## Approaches

1. **Risk-separated feature-branch chain** — deliver independently verified E2E, WAL/PITR, partitioning, and release-evidence slices.
   - Pros: isolates three different failure domains; keeps rollback and review focused; supports strict RED-GREEN-REFACTOR; avoids copying the dirty draft; each child can target its immediate predecessor.
   - Cons: requires shared fixture/release contracts and several PRs; final tag waits for the entire chain.
   - Effort: High

2. **Single Wave 13 implementation PR** — combine CI, storage, schema conversion, documentation, and release work.
   - Pros: fewer branches and one integration point.
   - Cons: likely exceeds the 800-line budget; mixes browser, database-recovery, schema, and release risks; makes a failed partition/PITR review block unrelated work; recreates the rejected draft's unsafe coupling.
   - Effort: High

## Recommendation

Use approach 1 with `auto-chain` and `feature-branch-chain`. Forecast five autonomous slices, each targeting no more than 800 authored changed lines:

1. **13.1 — Full-stack E2E harness and auth paths**: start Postgres/Redis/API plus Angular in CI; use an ephemeral migrated database and unique users; add registration-with-consent and login/session tests. Keep axe separate. RED evidence is the current proxy failure when no API runs.
2. **13.2 — Three remaining critical paths**: create/list/open a trade, download the authenticated GDPR export, and delete an isolated account into its grace-period state. These cover the core trading and compliance surfaces without an external Stripe dependency. Lock these five paths in the proposal/spec before apply.
3. **13.3 — wal-g continuous archive and PITR**: provide a pinned wal-g/Postgres image or sidecar contract, `archive_mode=on`, `archive_timeout=3600`, explicit WAL credentials/prefix, and a base-backup workflow. The integration test MUST write a before/after marker, archive WAL, restore into a separate empty Postgres instance to a target timestamp, and prove the boundary. Never restore over the source database. A same-host MinIO target does not prove disaster durability; require a separately configured backup endpoint/failure domain for production.
4. **13.4 — Audit partitioning migration `0040`**: create a shadow RANGE-partitioned table by `occurred_at`, include a DEFAULT partition plus current/future monthly partitions, copy and count-validate rows, atomically swap names in one transaction, and retain the old table until post-deploy verification. Reruns MUST detect completed/intermediate states safely. Update EF to `HasKey(e => new { e.Id, e.OccurredAt })` in the same PR and test fresh apply, upgrade with historical/future rows, rerun, indexes, writes, reads, and retention. Do not hard-code a finite 2026-only horizon or use destructive `CASCADE` recovery.
5. **13.5 — Changelog evidence and release readiness**: add a machine-readable wave/archive manifest and CI checker for archive existence, one-to-one wave coverage, valid release headings, and tag/version consistency; repair factual drift from that evidence. Add a `v1.1.0-rc1` release checklist/gate requiring all tests, five E2E paths, PITR drill evidence, migration upgrade evidence, and a clean exact commit. Create the tag only after final SDD verification, never from a PR test job.

Strict TDD applies to every slice: executable tests/checkers first, minimum implementation second, then refactor. Migration and recovery work require real PostgreSQL integration tests rather than text-presence tests. The 800-line budget supersedes the chained-PR skill's default 400-line threshold because the user explicitly set 800 for this change, but each slice still needs a clear start, finish, verification, and rollback boundary.

## Risks

- Functional E2E will remain false-green if CI starts only Angular or mocks the API; it must exercise the real migrated backend against ephemeral services.
- `archive_timeout=3600` bounds an idle WAL segment's archive delay but does not by itself prove recoverability or off-host durability; only a successful isolated PITR drill proves the RPO path.
- PostgreSQL uniqueness on a partitioned table requires the partition key in the PK; database and EF key changes are inseparable.
- Partition conversion can lock a busy append-only table and temporarily double storage; proposal/design must define maintenance-window sizing and post-verify cleanup.
- A DEFAULT partition prevents outage when maintenance lags, but monitoring must detect rows accumulating there and future partitions must be created automatically.
- Automated CHANGELOG validation needs a small explicit manifest; attempting to infer factual prose directly from arbitrary Markdown will be brittle.
- The release candidate is not ready merely because a tag can be created; the tag must point to the fully verified chain head.

## Ready for Proposal

Yes. The proposal should state that only Playwright/axe infrastructure, manual CHANGELOG notes, daily dumps, and prior release tags already exist; none satisfy the new Wave 13 acceptance criteria. It should lock the five paths and five-slice chain above, reserve migration `0040`, explicitly reject the dirty draft's migration/runbook/recovery assumptions, and make isolated PITR plus upgrade/rerun migration tests release blockers.
