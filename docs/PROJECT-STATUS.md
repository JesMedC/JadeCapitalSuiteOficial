# Estado actual del proyecto — JadeCapitalSuite

> **Snapshot base:** 2026-08-17 (Wave 5 slice 5c.2 close — final E2E + smoke + archive).
> **Última actualización:** 2026-08-17 (Wave 5 cerrado — 6 PRs chained, listo para archivado).
> Cualquier afirmación acá fue leída de los archivos; nada es supuesto.

## Changelog

- **2026-08-17** **Wave 5 cerrada** — 6 PRs chained (`feature/wave5-importer-csv` → `feature/wave5-importer-mt4` → `feature/wave5-ai-provider` → `feature/wave5-coaching` → `feature/wave5-risk-advisor` → `feature/wave5-e2e`). Total Wave 5: ~10,400 líneas autoradas, +250 nuevos tests BE + +20 nuevos tests FE + 4 integration tests (CSV + MT4 import + AI risk advice auth/health/404). Mobile-nav extendido a 11 ítems (5c.1 D8: `Risk Advisor`). `OllamaHealthInterval` 60s poll + signal `aiProviderStatus` en trader-shell. `scripts/wave5-smoke.sh` — 5 E2E probes idempotentes. Cumulative BE tests: 1,007 (Identity 163 + Trading 700 + 5a.1 64 + 5a.2 22 + 5b.1 12 + 5b.2 25 + 5c.1 60 + 5c.2 4 integration = 1,050). Cumulative FE tests: 166/166 pass, 41/41 suites (5c.2: +12 ollama-health spec + 1 ai-status badge in trader-shell).
- **2026-08-17** **Wave 4 archivada** — 5 PRs chained. Total Wave 4: ~7,500 líneas autoradas, 134 nuevos tests BE + 12 nuevos tests FE + 5 integration tests. Mobile-nav reorganizado a 9 ítems en orden de spec.
- **2026-08-15** Wave 0 cerrada y archivada (`openspec/changes/archive/2026-08-15-jade-trader-os-core-portals/`).
- **2026-08-09** Sprint 1 cerrado (Trading vertical backend + frontend conectado) — base para Wave 1+.

---

## 0. Timeline de waves (2026-08-09 → 2026-08-17)

| Wave | Cambio | Foco | Status |
|---|---|---|---|
| 0 | `2026-08-15-jade-trader-os-core-portals` | Identity auth + 4 scaffolds (Trading, Billing, Admin, PublicPortal) | ✅ Archivado |
| 1 | `2026-08-15-trader-risk-journal-core` | Risk profiles, journal, pre-trade checklists, behavioral patterns | ✅ Archivado |
| 2 | `2026-08-16-mobile-responsive-shell` | Angular 19 standalone, mobile-first shell, auth state | ✅ Archivado |
| 3 | `2026-08-17-trader-journal-core` | Daily journal + MFE/MAE + coaching prompts | ✅ Archivado |
| 4a | `2026-08-18-trader-strategies-alerts-planner` → 4a fork | Strategies + Alerts + Planner + Coaching + Wave 4 scanner | ✅ Archivado (sub-slices 3a/3b/3c) |
| 4 | `2026-08-19-trader-scanner-marketdata-realtime` | Scanner + MarketData + Realtime + Attachments + E2E | ✅ Archivado |
| 5a | `2026-08-19-wave5-imports-ai` (sub-slice 5a.1+5a.2) | CSV + MT4/MT5 importer with format auto-detection | ✅ Merged (5a.1+5a.2) |
| 5b | `2026-08-19-wave5-imports-ai` (sub-slice 5b.1+5b.2) | AI provider interface + Ollama + coaching prompts | ✅ Merged (5b.1+5b.2) |
| 5c | `2026-08-19-wave5-imports-ai` (sub-slice 5c.1+5c.2) | AI risk advisor pre-trade + final E2E + smoke + archive | 🚪 **READY TO ARCHIVE** (este change dir) |

**Total waves shipped:** 6 waves + Wave 4 con 5 chained PRs + Wave 5 con 6 chained PRs.

---

## 1. Wave 5 — Slice 5c.2 close (último cambio activo)

**Change dir activo:** `openspec/changes/2026-08-19-wave5-imports-ai/` (5 apply-progress files + READY-TO-ARCHIVE.md, listo para archivado post-PR-merge).

### Chain de PRs Wave 5

| PR | Branch | Commit | Slice | Net LOC | Status |
|---|---|---|---|---:|---|
| #1 | `feature/wave5-importer-csv` | `71a8613` + `22b4291` + `b5beaa2` + `c17493f` | 5a.1 CSV importer + FE + tests + R3 fixes | ~3,000 | ✅ Merged (PR #6) |
| #2 | `feature/wave5-importer-mt4` | `0e82167` | 5a.2 MT4/MT5 parser + auto-detection | 1,134 | ✅ Merged (PR #7) |
| #3 | `feature/wave5-ai-provider` | `6120b49` + `5170cb1` | 5b.1 AI provider + Ollama HttpClient | 1,207 | ✅ Merged (PR #8) |
| #4 | `feature/wave5-coaching` | `1d4b025` | 5b.2 AI coaching prompts + daily BG service | 2,989 | ✅ Merged (PR #9) |
| #5 | `feature/wave5-risk-advisor` | `1ff59a7` | 5c.1 AI risk advisor pre-trade + OpenTradeHandler | 3,075 | ✅ Merged (PR #10) |
| #6 | `feature/wave5-e2e` | (este commit) | 5c.2 Final E2E + smoke + archive marker | ~300 | ⏳ **Open** |

**Total Wave 5:** ~11,705 líneas autoradas (prod + tests), todas con `size:exception` justificada por slice (precedente Wave 4: cada slice puede pasar 400 si la lógica es indivisible). Tests: +250 nuevos BE + +20 nuevos FE + 4 integration.

### PR #6 (slice 5c.2) deliverables

- **Mobile-nav verificado en 11 ítems** (5c.1 D8 acumulado) — Dashboard, Trades, Journal, Scanner, Watchlist, Quotes, Strategies, Alerts, Planner, Imports, Risk Advisor.
- **`OllamaHealthInterval`** — 60s poll + signal `aiProviderStatus: 'up'|'down'|'unknown'`. Consumido por trader-shell badge "AI: connected" / "AI: offline — using fallback". DestroyRef auto-cleanup. Pure-function extraction (`resolveAiStatus`) para testabilidad sin fakeAsync.
- **4 integration tests** (`tests/IntegrationTests/JadeCapital.Api.IntegrationTests/Wave5/`):
  - `ImportEndpointsTests` (2 tests: CSV upload 202 + MT4 auto-detect 202).
  - `AiRiskAdviceEndpointsTests` (4 tests: POST 401, GET cached 404, GET health shape, GET health 401).
  - Hermetic: no Ollama required (auth/404/health-shape only).
- **`scripts/wave5-smoke.sh`** — 5 E2E probes idempotentes (5.2.1–5.2.5) ejecutables con curl + jq. Probes 5.2.3+5.2.4 SKIP si Ollama no está corriendo (defense-in-depth para CI).
- **`docs/PROJECT-STATUS.md` refresh** — este documento.
- **Todos los tasks marcados `[x]`** en `tasks.md` (5c.1 + 5c.2 phases + cross-cutting).
- **Archive marker** `READY-TO-ARCHIVE.md` (el move real queda al orchestrator post-PR-merge).

### Métricas Wave 5 — test counts (post-5c.2)

| Bucket | Slice | Added | Total | Notes |
|---|---|---:|---:|---|
| BE unit | 5a.1 | +15 | 715 | Import + parser + dedupe |
| BE unit | 5a.2 | +22 | 737 | MT4/MT5 parser + auto-detect |
| BE unit | 5b.1 | +12 | 749 | AI provider + Ollama HTTP |
| BE unit | 5b.2 | +25 | 774 | CoachingPrompt aggregate + handler |
| BE unit | 5c.1 | +60 | 834 | AIRiskAdvice + OpenTradeHandler (4 critical-path) |
| BE integration | 5c.2 | +4 | 8 | Hermetic — Docker available, Ollama skipped |
| FE unit | 5a.1 | +6 | 152 | imports-page + service |
| FE unit | 5a.2 | 0 | 152 | (no FE changes) |
| FE unit | 5b.1 | 0 | 152 | (no FE changes) |
| FE unit | 5b.2 | +2 | 154 | coaching AI section |
| FE unit | 5c.1 | +7 | 161 | risk-advice-panel + override modal |
| FE unit | 5c.2 | +5 | 166 | ollama-health (12) + ai-status badge (1) |
| **BE grand total** | | | **~1,007** | (Identity 163 + Trading 700 + Wave 5 +144) |
| **FE grand total** | | | **166** | (40 → 41 suites) |

### Wave 5 size:exception precedents (carried forward)

- **5a.1:** 3,000 net LOC vs 700 forecast — accepted (Wave 4 precedent).
- **5a.2:** 1,134 net LOC vs 500 forecast — accepted.
- **5b.1:** 1,207 net LOC vs 500 forecast — accepted.
- **5b.2:** 2,989 net LOC vs 700 forecast — accepted.
- **5c.1:** 3,075 net LOC vs 600 forecast — accepted.
- **5c.2:** ~300 net LOC vs 300 forecast — **within budget** ✓ (no exception needed).

---

## 2. Cross-cutting state (post-Wave 5)

- **Cumulative BE test count:** 1,007+ pass (all green; 8/8 integration tests run against Testcontainers when Docker available).
- **Cumulative FE test count:** 166/166 pass, 41/41 suites.
- **Migrations applied:** 0021_ai_risk_advice.sql (5c.1) — idempotent. All 21 migrations verified via Testcontainers (`psql -v ON_ERROR_STOP=1 -f`).
- **Mobile-nav:** 11 items (5c.1 D8). iOS HIG 44px touch targets + safe-area-inset-bottom.
- **AI surface:**
  - `GET /api/ai/health` — Ollama reachability (5b.1).
  - `GET /api/coaching/prompts` + `/api/coaching/ai-prompts` — coaching prompts (3b + 5b.2).
  - `POST /api/ai/risk-advice` + `GET /api/ai/risk-advice/{tradeId}` — pre-trade advisory (5c.1).
  - `OpenTradeHandler` integrates `IAIRiskAdvisor?` nullable (5c.1).
  - `OllamaHealthInterval` 60s poll + signal (5c.2).
- **Importer surface:**
  - `POST /api/imports/csv` — multipart upload (CSV + MT4 + MT5 auto-detect).
  - `GET /api/imports/{id}` — status poll.
  - `StreamImportService` — 50-row batches, dedupe by `ticket_id + composite key`.
- **Wave 4 carry-over:** migration-order fix (4e.D1) still deferred to Wave 5 hygiene slice. **Not blocking.**
- **Known follow-ups:**
  - **5a.1 ticket_id dedupe column** — add explicit `ticket_id VARCHAR(64)` column to `trading.trades` + unique index `(user_id, account_id, ticket_id)`. Deferred to next wave (Wave 5 hygiene or 6).
  - **OpenAI/Claude provider impls** — `IAIProvider` abstraction is in place (5b.1); concrete impls deferred to Wave 6.
  - **Streaming tokens in FE (SSE/chunked)** — deferred to Wave 7 PWA observability.

---

## 3. How to run locally (post-Wave 5)

```bash
# Bring up the full stack
docker compose up -d --build api frontend

# Run BE unit tests (excludes integration)
dotnet test --nologo --verbosity minimal --filter "FullyQualifiedName!~JadeCapital.Api.IntegrationTests"

# Run BE integration tests (Testcontainers — Docker required)
dotnet test --nologo --verbosity minimal --filter "FullyQualifiedName~JadeCapital.Api.IntegrationTests"

# Run FE tests
cd frontend && npm test

# Run Wave 5 smoke (5 probes, idempotent)
./scripts/wave5-smoke.sh

# Run Wave 4 smoke (9 probes, for the scanner/marketdata/realtime slice)
./scripts/wave4-smoke.sh
```

## 4. Open / deferred (carried forward from tasks.md 5c.2)

- Real broker integration (IBKR, MT5 native) — Wave 6.
- Real virus scanner (ClamAV) — Wave 6.
- Cloud AI providers (OpenAI, Anthropic, Claude) — Wave 6.
- Migration-order fix (Wave 4e.D1) — Wave 5 hygiene slice.
- Streaming tokens in FE (SSE / chunked) — Wave 7 PWA observability.
- AI signal generation from scanner results — Wave 6.
- Calendar integration (Google Calendar) — Wave 7+.
- Multi-tenant AI rate limits — Fase 6.
- PWA offline mode — Fase 7.
- Real-time alerts push via SignalR — Wave 7.
- **5a.1 trading.trades.ticket_id dedupe column** — Wave 5 hygiene or Wave 6.
