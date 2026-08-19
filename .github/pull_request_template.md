<!--
  .github/pull_request_template.md — Jade Capital Suite PR template

  Wave 10 slice 10.1 — checklist enforced by CODEOWNERS review + the
  ci.yml `lint-backend` / `test-backend` / `test-integration` /
  `test-frontend` gate.

  Mirror the Spanish translation in `pull_request_template.es.md`
  (Wave 10.6 docs slice).
-->

## What

<!-- One paragraph: what changed and why. Reference the spec(s) + tasks.md phases in the "References" section below. -->

## Why

<!-- Link to the issue / change / spec that motivates this PR. -->

## References

- Spec(s): <!-- e.g., `openspec/changes/.../specs/<name>/spec.md` -->
- Tasks phase(s): <!-- e.g., "Phase 2: ..." -->
- ADR(s): <!-- required if this is an architecture change; otherwise N/A -->

## Checklist

- [ ] Tests added / updated (and `dotnet test` is green locally)
- [ ] Build clean (`dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings)
- [ ] Cumulative backend suite green (1389 baseline + slice delta)
- [ ] Frontend suite green if applicable (`npm test -- --watch=false --browsers=ChromeHeadlessCI`)
- [ ] `apply-progress-...md` doc updated (under `openspec/changes/<change>/`)
- [ ] Deviations from design documented (in apply-progress doc)
- [ ] Conventional commit message (no "Co-Authored-By: AI")
- [ ] No secrets / `.env` / credentials committed

## Test plan

<!-- How a reviewer can verify this PR locally. List commands + expected results. -->

## Migration notice

<!-- Required for any change that touches `infrastructure/postgres/migrations/`, `appsettings*.json`, or the Docker Compose stack. Otherwise N/A. -->

## Risk + rollback

<!-- Risk class (low / medium / high) + the exact revert command / behavior if this PR breaks prod. -->