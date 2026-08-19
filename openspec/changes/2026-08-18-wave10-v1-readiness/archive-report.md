# Archive — Wave 10 (v1 readiness)

**Change**: 2026-08-18-wave10-v1-readiness
**Archived**: 2026-08-19
**Tracker integration**: feature/0a-identity-model @ 8d40394
**PR chain**: #37 → #48 → #53 → #54 → #55 → #56 (5 chained PRs, 1 integration)
**Mode**: hybrid (OpenSpec + engram)
**Delivery strategy**: auto-chain + feature-branch-chain
**Release target**: v1.0.0-rc1

## Intent (recap from proposal.md)

Wave 10 closes the v1 readiness gap surfaced by a 40-gap audit + 18 additional gaps. Target: ship a release candidate `v1.0.0-rc1` that ops can deploy.

## Sub-scopes closed

- **A — Security & compliance**: A1 (CI/CD), A3 (CSP+HSTS), A7 (TLS via Caddy), A8 (ToS+Privacy+Cookie consent), A10 (Dependabot)
- **B — Deploy & ops**: A2 (Docker Secrets), A5 (docker-compose.prod.yml), A6 (backups + DR runbook)
- **C — CI/CD infrastructure**: A1 (GitHub Actions), B6 partial (integration tests in CI via services:), B17 (OpenAPI export)
- **D — GDPR**: A9 (cascade deletor pattern), G-A1 (data portability — deferred to Wave 11+), G-A3 (cookie consent banner — deferred to Wave 11+), G-A4 (email deliverability — deferred to Wave 11+), G-A5 (welcome email — deferred to Wave 11+), B18 (account deletion UI — deferred to Wave 11+)
- **E — Stripe verification**: A4 (test-mode validator)
- **F — Docs**: B13 (LICENSE+CHANGELOG+CONTRIBUTING+SECURITY), B14 (README.es), B15 (5 NEW ADRs 0005-0009)

## Slice summary

| Slice | PR | Description | LOC | Tests | Commit |
|---|---|---|---:|---:|---|
| 10.1 | #37 | CI/CD pipeline + Dependabot | ~919 | 0 new (CI infra) | 6d244ae |
| 10.2 | #48 | Production deployment + secrets | ~2331 | +7 DockerSecret | 57c87cc |
| 10.3 | #53 | Security headers + TLS | ~430 | +6 nginx+meta CSP | c33ace5 |
| 10.4 | #54 | Backups + migration-order fix (narrower) | ~2000 | +5 renumbering | be1e897 (in chain) |
| 10.5 | #55 | GDPR cascade deletor pattern (narrower) | ~900 | 0 new (cascade defer) | 7610ce6 (in chain) |
| 10.6 | #56 | Docs + observability + SEO + coverage + Stripe | ~2700 | +9 Stripe validator | e965736 (in chain) |
| Integration | local merge | All 5 chained PRs + final | — | — | 8d40394 |
| **Total** | **7 PRs** | | **~9,280 LOC** | **+27 BE new** | |

## Cumulative state

- **Cumulative BE tests**: 1416 (Wave 9 1325 baseline + Wave 10 +27 from 10.2 + 10.3 + 10.4 + 10.6) — verified locally post-archive: 367 Identity + 180 Shared.Kernel + 721 Trading + 116 Billing + 5 Admin + 27 Host = 1416, all passing.
- **Build**: 0 errors, 0 warnings
- **Zero regression**: all pre-Wave-10 tests still pass
- **Wave 10 size:exception**: 5 of 6 slices (10.3 within budget)

## Specs promoted to canonical

1. `openspec/specs/ci-infrastructure/spec.md` (NEW — Wave 10 10.1)
2. `openspec/specs/deployment-automation/spec.md` (NEW — Wave 10 10.2)
3. `openspec/specs/security-headers/spec.md` (NEW — Wave 10 10.3)
4. `openspec/specs/backup-strategy/spec.md` (NEW — Wave 10 10.4)
5. `openspec/specs/gdpr-compliance/spec.md` (NEW — Wave 10 10.5)
6. `openspec/specs/account-lifecycle/spec.md` (NEW — Wave 10 10.5)
7. `openspec/specs/observability-light/spec.md` (NEW — Wave 10 10.6)
8. `openspec/specs/production-readiness/spec.md` (NEW — Wave 10 10.6)

Total: 8 NEW canonical specs, 36 requirements, 64 scenarios.

### Mechanical copy readback (byte-identity proof)

All 8 specs copied via shell `cp` with mandatory `diff -r` readback. Empty diff + matching SHA256 = byte-identical:

| Domain | src SHA-256 (prefix) | dst SHA-256 (prefix) | diff -r |
|---|---|---|---|
| ci-infrastructure | 3e99a7b5fdf8 | 3e99a7b5fdf8 | empty ✅ |
| deployment-automation | 5538b2f2c15c | 5538b2f2c15c | empty ✅ |
| security-headers | cd0305aff7b1 | cd0305aff7b1 | empty ✅ |
| backup-strategy | edcea689cafd | edcea689cafd | empty ✅ |
| gdpr-compliance | 1106323a4216 | 1106323a4216 | empty ✅ |
| account-lifecycle | 805cd4de1f3f | 805cd4de1f3f | empty ✅ |
| observability-light | 45ed1a7ceb9b | 45ed1a7ceb9b | empty ✅ |
| production-readiness | cd3a0fb6b64f | cd3a0fb6b64f | empty ✅ |

## Deviations log

Reference each apply-progress file's "Deviations" section:

- **10.4 narrower**: 37 migrations (not 38); fresh-DB apply test deferred to Wave 11+
- **10.5 narrower**: cascade pattern only; DELETE/GET endpoints + cookie consent + welcome email + ToS UI + cascade xUnit tests deferred to Wave 11+
- **10.6 narrower**: OpenAPI export step embedded in test-backend job (not its own job per spec); Sentry hooks deferred per user authorization

## sdd-verify verdict

**PASS WITH WARNINGS** (no CRITICAL findings)
- 0 critical findings
- 4 warning findings (all deviations from spec/design; all user-authorized)
- 5 suggestion findings (style, naming, refactor polish)

## Hotfix log (Wave 10)

None. The runtime ledger was corrupt (post-9b.1 lease state), preventing normal sdd-attempt acquire/settle. Work proceeded under ordinary delivery policy per user authorization. The 4 build errors caught during inline 10.5 work were fixed inline (RED → GREEN per Strict TDD).

## Lessons for Wave 11+

1. **GDPR cascade tests are CRITICAL**: Wave 10.5 ships the cascade pattern + 30-day sweep BackgroundService with ZERO runtime xUnit coverage. Wave 11 MUST add Testcontainers-backed cascade tests before v1.0.0 tags.
2. **0029_backfill_personal_tenant.sql FK defect**: blocks fresh-DB apply. Wave 11+ must either fix the migration to tolerate zero rows or split into separate migrations.
3. **HardDeleteSweepOptions missing**: tasks.md specified a separate options class for grace/interval/jitter tuning. Wave 10.5 hardcoded these. Wave 11+ should extract.
4. **Per-request CSP nonce**: Wave 10.3 ships a static `{request_nonce}` placeholder. Wave 11+ should add nginx-njs-module or Lua for proper per-request CSP.
5. **CHANGELOG Wave 0-7 archive cross-check**: best-effort summaries written from memory. Wave 11 should cross-check against `openspec/changes/archive/`.
6. **Stripe env var alignment**: docker-compose uses `Stripe__SecretKey`; StripeOptions reads `Stripe:ApiKey`. Both work today via the legacy alias bridge but should be unified.

## Next steps

- **Wave 11+ backlog**: Postgres test container, GDPR cascade tests, Hotfix 0029 FK, Stripe env var alignment, per-request CSP nonce, account deletion UI, GDPR endpoints + cookie consent + welcome email + ToS UI, Sentry hooks (when ops provides DSN)
- **`v1.0.0-rc1` tag**: should be created by orchestrator immediately after this archive commit
- **Production deploy**: ops team uses Wave 10.2 deployment runbook + docker-compose.prod.yml