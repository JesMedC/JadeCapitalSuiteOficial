# Proposal: Trader Journal Core — Wave 2 (Journal + Behavioral Analytics)

## Intent and Problem

Wave 1 sentó las bases de métricas server-side, perfil de riesgo, checklist pre-trade y post-trade review. Pero falta el **loop de aprendizaje**: el trader necesita un journal diario (cómo se siente ANTES y DESPUÉS del mercado), detección de patrones conductuales (revenge trading, overtrading), gráficos de MFE/MAE para entender el "qué hubiera pasado", y prompts automáticos de coaching para guiar la mejora.

Sin estos elementos, el SaaS es un cuaderno de trades; con ellos, es una **herramienta de mejora conductual** que justifica el cobro y la retención.

## Goals

- **2a Journal diario**: pre-market plan (intención del día, watchlist, escenarios) + post-market reflection (qué hice bien, qué mejorar, mood) + mood tracker 1-5x3 (antes/durante/después).
- **2b Behavioral analytics**: revenge-trading detector (size ↑ tras loss), overtrading (N trades >> baseline del usuario), tilt detector (3+ losses consecutivos), winning-streak detector (size ↑ tras win = overconfidence), tag-aggregation (P&L por emotionality tag del checklist 1c).
- **2c MFE/MAE charts**: per-trade Maximum Favorable Excursion y Maximum Adverse Excursion, agregado en histogramas por tag/setup.
- **2d Coaching prompts**: reglas determinísticas que sugieren acciones al trader. "3 losses consecutivos a la misma hora → revisar si hay tilt", "tu plan dice no operar noticias y abriste EURUSD 5 min antes del NFP", "no operas hace X días → ¿descanso intencional o falta de disciplina?".

## Scope Boundaries

**Changed**: `trading.journal_entries` (nueva tabla), `trading.behavioral_events` (nueva tabla), `trading.trades.mfe_amount/mae_amount` (nuevas columnas nullable), nuevo módulo UI en `trader/journal/`.

**Unchanged**: Wave 1 features (RiskProfile, Checklist, Position-Size, PostTrade Review, Metrics). Admin shell. Public. Auth.

## Capabilities (new)

- **`journal-daily`** — user-level daily journal con pre-market + post-market + mood 1-5x3 + free-text reflection.
- **`behavioral-analytics`** — server-side analytics que detectan revenge trading, overtrading, tilt, overconfidence, aggregation por emotionality tag.
- **`mfe-mae-charts`** — MFE/MAE per-trade (calculado desde `trades.entry_price` + ticks via market data abstraction stub para V1; fallback: aproximar MFE/MAE desde open/close/P&L).
- **`coaching-prompts`** — rule-based prompts generados server-side desde un `ICoachingRule` registry. Inicial 5 reglas, extensible.

## Capabilities (modified)

Ninguna existente — todas las capabilities nuevas.

## Ownership and Approach

- **Trading.Application** owns los nuevos aggregates (`JournalEntry`, `BehavioralEvent`).
- **Trading.Infrastructure** calcula MFE/MAE. Para Wave 2 usamos **aproximación determinística** desde open/close + P&L (no requiere market data provider real). Wave 4 va a integrar el provider real.
- **Frontend** agrega `/app/journal` como nueva ruta del trader shell con bottom-tab entry.
- **Shared.Infrastructure** agrega `ICoachingRule` interface + registry pattern.
- **Host wiring**: `MapJournalEndpoints` + `MapCoachingPromptsEndpoint`.

## Non-Goals and Later Waves

- NO real-time market data provider (Wave 4).
- NO AI/LLM-based coaching (Wave 5 — Ollama).
- NO multi-day trends / monthly summaries (Wave 3 - Strategies).
- NO export del journal a PDF (Wave 5).
- NO integración con wearables / mood detection (futuro lejano).

## Chained Delivery, Validation, Rollback, and Rollback

| Slice (≤400 líneas) | Deliverable | Validate | Rollout / rollback |
|---|---|---|---|
| 2a | Journal domain + application + migration 0013 + endpoint `POST/GET /api/journal/today` + UI `/app/journal` con form + 8 tests | unit + integration + smoke 200 | Additive migration; revert code |
| 2b | Behavioral analytics handler (5 rules) + endpoint `GET /api/trades/behavioral?period=30d` + UI dashboard "Patrones" + 10 tests | unit + smoke | Revert code; data inalterada |
| 2c | MFE/MAE migration 0014 (`trading.trades.mfe_amount/mae_amount`) + calculator + endpoint `GET /api/trades/{id}/mfe-mae` + chart UI + 6 tests | unit + smoke | Additive migration + nullable columns |
| 2d | `ICoachingRule` registry + 5 initial rules + endpoint `GET /api/coaching/prompts` + UI integration con dashboard + 8 tests | unit + smoke | Revert code; rules isolated |

Pre-PR forecast: ~2480 líneas autoradas, 9-10 chained PRs feature-branch-chain. **size:exception justificado por slice** (Wave 0/1 precedente: cada slice puede pasar 400 si la lógica es indivisible, justificada en apply-progress).

## Dependencies and Risks

- **Migration 0013 + 0014**: nullable columns / new tables, additive only, idempotent, no NOT NULL sin backfill.
- **MFE/MAE approximation**: por simplicidad, Wave 2 calcula MFE = `max(open - low, high - open)` aproximado con datos del propio trade (entry_price, exit_price, pnl). Si P&L > 0 → MFE ≥ P&L, MAE ≤ 0. La calidad del cálculo mejora con Wave 4 (market data provider real).
- **Behavioral rules**: heurísticas que pueden generar falsos positivos. Mitigate: thresholds configurables via constants en domain layer (no magic numbers en queries).
- **Coaching prompts tone**: copy en español, neutral, sin shaming. Nunca "perdiste otra vez" — siempre "3 pérdidas consecutivas a las 14hs — ¿hay un patrón de cansancio al final del día?".

## Success Criteria

1. Trader puede escribir un journal diario (pre + post + mood) desde `/app/journal` y verlo persistido en BD.
2. `/app/patterns` (o tab dentro de dashboard) muestra revenge-trading events, overtrading days, tilt sequences con count y severidad.
3. Cada trade en `/app/trades` muestra MFE/MAE en un mini-chart inline (o en el detail).
4. Dashboard muestra al menos 1 coaching prompt activo cuando aplica una regla, con CTA hacia `/app/journal`.
5. Build verde, 0 warnings nuevos, tests verdes. Mobile responsive mantiene pattern Wave 1.5.

## Architectural Decisions

- **MFE/MAE approximation strategy**: usar `(entry_price, exit_price, pnl)` del propio trade para Wave 2. Si exit_price > entry_price (ganador) → MFE ≥ pnl_amount; MAE ≤ 0. Si exit_price < entry_price (perdedor) → MAE ≥ abs(pnl); MFE ≤ 0. Esto es aproximado pero da información útil sin market data provider.
- **Mood 1-5x3 dimensions**: `mood_pre`, `mood_during`, `mood_post`. Misma escala 1-5 que emotionality del checklist (consistencia). Mapeo: 1=Fearful, 2=Anxious, 3=Neutral, 4=Confident, 5=Euphoric.
- **Coaching rule registry**: `ICoachingRule { RuleId, Evaluate(IReadOnlyList<Trade> trades, IReadOnlyList<JournalEntry> journals) -> IReadOnlyList<CoachingPrompt> }`. 5 reglas: `RevengeTradeRule`, `OvertradingDayRule`, `TiltSequenceRule`, `LongBreakRule`, `PreMarketPlanMissRule`.
- **Mobile-first UI**: `/app/journal` debe funcionar en mobile (Wave 1.5 patterns), bottom-nav entry "Diario".

## Chained Strategy

Feature-branch-chain:
- 2a → main (tracker)
- 2b → 2a
- 2c → 2b
- 2d → 2c

OJO: cada slice produce 1-3 PRs targeteando al anterior. El primer PR de cada slice targetea `feature/0a-identity-model` (o la branch del último slice shipped).
