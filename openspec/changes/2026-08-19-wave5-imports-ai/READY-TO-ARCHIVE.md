# READY TO ARCHIVE — Wave 5 (`2026-08-19-wave5-imports-ai`)

> **Status**: Wave 5 closed (5/6 PRs merged, 1/6 PR #6 ready for review).
> **Change dir**: `openspec/changes/2026-08-19-wave5-imports-ai/`
> **Archive target**: `openspec/changes/archive/2026-08-19-wave5-imports-ai/`
> **Action by orchestrator**: AFTER PR #6 (`feature/wave5-e2e`) merges into
> `feature/0a-identity-model`, move this directory into `archive/` and promote
> the 3 delta specs (importers, ai-coaching, ai-risk-advisor) to
> `openspec/specs/`.

---

## 1. Manifest — apply-progress files

| Slice | File | Final commit | PR | Status |
|---|---|---|---|---|
| 5a.1 | `apply-progress-wave5-slice-5a-1.md` | `71a8613` + `22b4291` + `b5beaa2` + `c17493f` | [#6](https://github.com/JesMedC/JadeCapitalSuiteOficial/pull/6) | ✅ Merged |
| 5a.2 | `apply-progress-wave5-slice-5a-2.md` | `0e82167` | [#7](https://github.com/JesMedC/JadeCapitalSuiteOficial/pull/7) | ✅ Merged |
| 5b.1 | `apply-progress-wave5-slice-5b-1.md` | `6120b49` + `5170cb1` | [#8](https://github.com/JesMedC/JadeCapitalSuiteOficial/pull/8) | ✅ Merged |
| 5b.2 | `apply-progress-wave5-slice-5b-2.md` | `1d4b025` | [#9](https://github.com/JesMedC/JadeCapitalSuiteOficial/pull/9) | ✅ Merged |
| 5c.1 | `apply-progress-wave5-slice-5c-1.md` | `1ff59a7` | [#10](https://github.com/JesMedC/JadeCapitalSuiteOficial/pull/10) | ✅ Merged |
| 5c.2 | `apply-progress-wave5-slice-5c-2.md` | (this slice, `feature/wave5-e2e`) | [#11](https://github.com/JesMedC/JadeCapitalSuiteOficial/pull/11) | ⏳ **Open** |

## 2. Final commit hashes per slice

| Slice | Branch | Merge commit | Last feature commit |
|---|---|---|---|
| 5a.1 | `feature/wave5-importer-csv` | `3205790` | `c17493f` (R3 fixes) |
| 5a.2 | `feature/wave5-importer-mt4` | `6cc542b` | `0e82167` |
| 5b.1 | `feature/wave5-ai-provider` | `42e077b` | `5170cb1` (R3 fixes) |
| 5b.2 | `feature/wave5-coaching` | `fe5a37e` | `1d4b025` |
| 5c.1 | `feature/wave5-risk-advisor` | `83b88e1` | `1ff59a7` |
| 5c.2 | `feature/wave5-e2e` | (pending) | (this slice) |

`feature/0a-identity-model` HEAD before 5c.2 = **`83b88e1`**.

## 3. PR URLs

| PR | Title | Base | Head | URL |
|---|---|---|---|---|
| #6 | Wave 5 — slice 5a.1 (CSV importer) | `feature/0a-identity-model` | `feature/wave5-importer-csv` | https://github.com/JesMedC/JadeCapitalSuiteOficial/pull/6 |
| #7 | Wave 5 — slice 5a.2 (MT4/MT5 importer) | `feature/0a-identity-model` | `feature/wave5-importer-mt4` | https://github.com/JesMedC/JadeCapitalSuiteOficial/pull/7 |
| #8 | Wave 5 — slice 5b.1 (AI provider + Ollama) | `feature/0a-identity-model` | `feature/wave5-ai-provider` | https://github.com/JesMedC/JadeCapitalSuiteOficial/pull/8 |
| #9 | Wave 5 — slice 5b.2 (AI coaching) | `feature/0a-identity-model` | `feature/wave5-coaching` | https://github.com/JesMedC/JadeCapitalSuiteOficial/pull/9 |
| #10 | Wave 5 — slice 5c.1 (AI risk advisor) | `feature/0a-identity-model` | `feature/wave5-risk-advisor` | https://github.com/JesMedC/JadeCapitalSuiteOficial/pull/10 |
| #11 | Wave 5 — slice 5c.2 (E2E + smoke + archive) | `feature/0a-identity-model` | `feature/wave5-e2e` | https://github.com/JesMedC/JadeCapitalSuiteOficial/pull/11 |

## 4. Total LOC added (per slice)

| Slice | Net LOC | size:exception? | Notes |
|---|---:|---|---|
| 5a.1 | ~3,000 | ✅ Accepted (Wave 4 precedent) | Largest slice; 16 paths |
| 5a.2 | 1,134 | ✅ Accepted | 6 paths |
| 5b.1 | 1,207 | ✅ Accepted | 9 paths |
| 5b.2 | 2,989 | ✅ Accepted | 13 paths |
| 5c.1 | 3,075 | ✅ Accepted | 17 paths |
| 5c.2 | **596** | ❌ **Within budget** | 9 paths |
| **Total** | **~12,001** | 5/6 slices | 70 paths |

## 5. Test counts (cumulative post-Wave 5)

### BE (excluding integration blocked by Wave 4e.D1)

| Project | Pre-Wave 5 | Post-Wave 5 |
|---|---:|---:|
| JadeCapital.Identity.UnitTests | 163 | 163 |
| JadeCapital.Billing.UnitTests | 22 | 22 |
| JadeCapital.Shared.Kernel.UnitTests | 100 | 100 |
| JadeCapital.Trading.UnitTests | 522 | **700** (+178 Wave 5) |
| **BE unit total** | **807** | **985** (+178) |
| BE integration (Testcontainers) | 5 (Wave 4) | 9 (+4 Wave 5; **0 passing** — Wave 4e.D1 blocker) |

### FE (jest)

| State | Pre-Wave 5 | Post-Wave 5 |
|---|---:|---:|
| Suites | 36 | **41** (+5) |
| Tests | 146 | **179** (+33) |
| Pass rate | 100% | 100% |

**Total tests added by Wave 5**: +178 BE unit, +4 BE integration (blocked),
+33 FE unit, +0 FE integration.

## 6. Specs to promote (3 delta specs)

Located at `openspec/changes/2026-08-19-wave5-imports-ai/specs/`:

| Spec | Source slice | New capabilities |
|---|---|---|
| `importers/spec.md` | 5a.1 + 5a.2 | CSV / MT4 / MT5 import endpoints, dedupe, status polling |
| `ai-coaching/spec.md` | 5b.1 + 5b.2 | AI provider interface, Ollama HttpClient, coaching prompts |
| `ai-risk-advisor/spec.md` | 5c.1 + 5c.2 | Pre-trade AI advisory, OpenTradeHandler integration, health polling |

After archive, the orchestrator should `cp -r specs/* ../specs/` (or use
`sdd-archive` skill).

## 7. Known follow-ups (carried forward — NOT 5c.2 blocking)

1. **5a.1 `trading.trades.ticket_id` dedupe column** — add explicit
   `ticket_id VARCHAR(64)` column to `trading.trades` + unique index
   `(user_id, account_id, ticket_id)`. Deferred to Wave 5 hygiene or Wave 6.
2. **Wave 4e.D1 migration-order fix** — `JadeApiFactory.ApplyMigrationAsync`
   sorts with `StringComparer.Ordinal`, which orders `0021_*.sql` before
   `2026*_*.sql`. 1-line fix (parse date prefix from filename). 30 LOC.
   Blocks all 9 integration tests. **Wave 5 hygiene slice.**
3. **OpenAI / Claude provider impls** — `IAIProvider` abstraction in place
   (5b.1); concrete impls deferred to Wave 6.
4. **Streaming tokens in FE** (SSE / chunked) — Wave 7 PWA observability.
5. **Real broker integration** (IBKR, MT5 native) — Wave 6.
6. **Real virus scanner** (ClamAV) — Wave 6.
7. **AI signal generation from scanner results** — Wave 6.
8. **Calendar integration** (Google Calendar) — Wave 7+.
9. **Multi-tenant AI rate limits** — Fase 6.
10. **PWA offline mode** — Fase 7.
11. **Real-time alerts push via SignalR** — Wave 7.

## 8. Archive move checklist (orchestrator only)

After PR #11 (`feature/wave5-e2e`) merges:

- [ ] Verify `feature/0a-identity-model` HEAD includes 83b88e1 + the 5c.2 merge commit
- [ ] `git mv openspec/changes/2026-08-19-wave5-imports-ai openspec/changes/archive/`
- [ ] `cp -r openspec/changes/archive/2026-08-19-wave5-imports-ai/specs/* openspec/specs/`
- [ ] Run `sdd-archive` skill (or equivalent) to sync delta specs
- [ ] Update `docs/PROJECT-STATUS.md` Changelog with archive date
- [ ] Update `openspec/CHANGELOG.md` (if present) with the 6-PR chain summary
- [ ] Close any tracking issues / milestones referencing this change dir

## 9. Pre-archive checks completed (5c.2)

- ✅ `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings
- ✅ `dotnet test --filter "FullyQualifiedName!~JadeCapital.Api.IntegrationTests"` → 985/985 pass
- ✅ `npm test` → 179/179 pass, 41/41 suites
- ✅ `grep -c "^\s*- \[ \]" tasks.md` → 0 (all 65 tasks marked `[x]`)
- ✅ `git diff --name-only` → 9 paths (well under 32-path hard cap)
- ✅ `git diff --stat` → 596 net LOC (under 2,000 hard cap; only Wave 5 slice within budget)
- ✅ `bash -n scripts/wave5-smoke.sh` → SYNTAX_OK
- ⚠ Integration tests blocked by Wave 4e.D1 (out of scope; documented)
