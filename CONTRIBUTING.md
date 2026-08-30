# Contributing to JadeCapital Suite

## Setup
1. Clone repo
2. `cp .env.example .env` — fill in JWT secrets (≥ 32 chars)
3. `docker compose up -d` — start postgres + redis + minio + mailpit + api + frontend
4. `cd frontend && npm install`

## Dev workflow
1. Create branch off `feature/0a-identity-model`: `git checkout -b feature/wave-N-slug`
2. Strict TDD: RED first (test fails), GREEN (impl), REFACTOR
3. Conventional commits: `type(scope): description`
4. PR title format: `type(wave-N-slug): slice N.M — description`
5. Open PR against `feature/0a-identity-model`
6. CI runs (lint + tests); all green required to merge
7. Merge with `--squash` after approval

## PR checklist
- [ ] Tests added/updated
- [ ] Build clean (0 errors, 0 new warnings)
- [ ] Cumulative BE suite green
- [ ] apply-progress doc updated
- [ ] Deviations documented
- [ ] Conventional commit message
- [ ] Reference to spec(s) + tasks.md phases

## Stack
- Backend: ASP.NET Core 10, EF Core 9, MediatR, FluentValidation
- Frontend: Angular 19 (standalone + Signals), OnPush, SCSS
- DB: PostgreSQL 16, Redis 7, MinIO

## Code style
- BE: C# 12+, file-scoped namespaces, expression-bodied members
- FE: TypeScript strict, OnPush change detection, signals for state
- No Co-Authored-By in commits

## Legal copy placeholders
This repo ships placeholders for `frontend/src/assets/legal/terms-of-service.md` and `privacy-policy.md`. **Do not deploy to production** without legal counsel sign-off. See `docs/email-deliverability.md` and `docs/runbooks/deployment.md` for the deployment checklist.
