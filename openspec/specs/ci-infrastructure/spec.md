# CI Infrastructure Specification

**Change**: 2026-08-18-wave10-v1-readiness
**Wave**: 10 (v1 readiness)
**Slice**: 10.1 — CI/CD pipeline + dependency scanning
**Status**: NEW spec (no prior canonical)
**Strict TDD**: ACTIVE — every Requirement + Scenario here must be covered by tests in slice 10.1

## Purpose

Define the merge-gate that gates every PR from `feature/*` into `feature/0a-identity-model`. The CI pipeline MUST run lint, backend unit tests, integration tests against Testcontainers Postgres/Redis, and frontend lint/test/build on every PR + push to the default branch. A nightly scheduled workflow MUST scan for vulnerable NuGet + npm packages and run an OWASP ZAP baseline. Dependabot MUST open weekly PRs for outdated packages with auto-merge for patch versions only. Without this slice, every later slice ships unverified: bugs land in `feature/0a-identity-model` undetected, and no v1.0.0-rc1 tag is credible.

## ADDED Requirements

### Requirement: GitHub Actions CI workflow on PR + push

The system MUST define `.github/workflows/ci.yml` triggered on `pull_request` and `push` to `feature/0a-identity-model`. The workflow MUST run 4 jobs in parallel: `lint-backend` (`dotnet format --verify-no-changes` + Roslynator), `test-backend` (`dotnet test --nologo --verbosity minimal` excluding integration), `test-integration` (Testcontainers Postgres + Redis via `services: postgres, redis` block), and `test-frontend` (`npm --prefix frontend test -- --ci` + `npm --prefix frontend run build`). A job MUST fail the workflow on any non-zero exit. The workflow MUST upload NuGet + npm caches keyed on `Directory.Build.props` hash + `package-lock.json` hash to keep runs under 10 minutes.

#### Scenario: backend lint + test passes

- GIVEN a PR with no backend code changes
- WHEN the workflow runs `lint-backend` + `test-backend`
- THEN both jobs MUST exit 0 within 5 minutes total
- AND the workflow summary MUST show 2 green checks

#### Scenario: integration tests use Testcontainers Postgres

- GIVEN `test-integration` starts
- WHEN GitHub Actions provides the `postgres:16-alpine` + `redis:7-alpine` services
- THEN Testcontainers MUST spin up ephemeral Postgres + Redis containers
- AND `WebApplicationFactory<Program>` MUST connect via the service hostnames
- AND all 33+ integration tests MUST pass against the live DB

#### Scenario: frontend lint + test + build passes

- GIVEN a PR with no frontend changes
- WHEN `test-frontend` runs `npm test -- --ci` + `npm run build`
- THEN jest MUST report 0 failures and `ng build` MUST exit 0
- AND the `dist/` artifact MUST be uploaded for downstream deploy jobs

#### Scenario: nightly cron scans for vulnerabilities

- GIVEN `.github/workflows/nightly.yml` triggers on `cron: '0 3 * * *'` + `workflow_run`
- WHEN the nightly job runs at 03:00 UTC
- THEN `dotnet list package --vulnerable --include-transitive` MUST exit 0 (no critical/high CVEs)
- AND `npm audit --audit-level=high` in `frontend/` MUST exit 0
- AND an OWASP ZAP baseline scan MUST complete within 30 minutes

### Requirement: Dependabot config updates dependencies weekly

The system MUST define `.github/dependabot.yml` with two ecosystems: `nuget` (directory `/`, schedule `weekly`, open PRs ≤ 10, groups patches together for auto-merge) and `npm` (directory `/frontend`, schedule `weekly`, open PRs ≤ 10). Dependabot MUST open PRs with the `dependencies` label and assign to the maintainers team. Patch updates for `nuget` MUST auto-merge after CI green via `.github/workflows/dependabot-auto-merge.yml`. Major + minor updates MUST require manual review.

#### Scenario: Dependabot opens PRs for outdated NuGet packages

- GIVEN `dotnet list package --outdated` reports 3 outdated packages
- WHEN the weekly cron fires
- THEN Dependabot MUST open 3 PRs (or 1 grouped PR if `groups:` consolidates them)
- AND each PR MUST trigger `ci.yml` and MUST NOT merge until CI is green

#### Scenario: Dependabot opens PRs for outdated npm packages

- GIVEN `npm outdated` in `frontend/` reports 2 outdated packages
- WHEN the weekly cron fires
- THEN Dependabot MUST open PRs labeled `dependencies` for each
- AND the PR MUST list the new version + the changelog link

#### Scenario: Dependabot alerts on vulnerable packages

- GIVEN GitHub's advisory database reports CVE-2026-XXXX for `Microsoft.Extensions.*`
- WHEN Dependabot detects the vulnerable version in `Directory.Packages.props`
- THEN an alert MUST open as a security advisory + a PR
- AND the PR MUST reference the CVE in its body

### Requirement: Branch protection rules require CI green

The repository MUST configure branch protection on `feature/0a-identity-model` requiring: (a) `ci/lint-backend`, `ci/test-backend`, `ci/test-integration`, `ci/test-frontend` checks to be green, (b) 1 approving review, (c) linear history. A PR with failing CI MUST NOT be mergeable via the GitHub UI or the API.

#### Scenario: PR with failing CI cannot be merged

- GIVEN a PR with `test-backend` failing
- WHEN the author attempts `gh pr merge --auto`
- THEN the merge MUST be rejected with `branch is not mergeable` error
- AND the PR status MUST show 1 red + 3 green checks

## Cross-references

- Closes gaps A1 (no CI/CD), A10 (no vulnerability scanning), B6 partial (integration tests in CI)
- Related Wave 9 spec: `openspec/specs/soft-delete-audit/spec.md` (CI runs the audit cascade tests)
- Related Wave 10 specs: all six slices depend on the CI merge-gate
- Source: `openspec/changes/2026-08-18-wave10-v1-readiness/explore.md` §A1, §A10

## Out of scope

- Playwright E2E tests in CI (Wave 11+, gap B4)
- Mutation testing (Stryker.NET, Wave 11+, gap B7)
- Performance / load testing in CI (k6/Locust, Wave 11+, gap B8 — needs prod compose from 10.2)
- Self-hosted runners (Wave 11+ — uses GitHub-hosted free tier for v1.0)
- Auto-merge for major + minor npm/NuGet updates (manual review required)
- Coverlet coverage gate (lives in 10.6)
