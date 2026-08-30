# Coaching Prompts Specification

## Purpose

Server-side rule-based coaching prompts that nudge traders toward better behavior. Initial five rules. Each rule reads trade + journal history and returns zero or more actionable prompts. The prompts render in the trader dashboard, the journal page, and (optionally) as email/notification triggers in Wave 3+.

## Requirements

### Requirement: Pluggable rule registry

The system MUST evaluate trade + journal history against an `ICoachingRule` registry. Each rule implements `string RuleId`, `int Priority`, and `IReadOnlyList<CoachingPrompt> Evaluate(EvaluationContext ctx)`. New rules MAY be added without touching the rest of the system.

#### Scenario: Five initial rules registered

- GIVEN the application starts
- WHEN the registry is constructed
- THEN it MUST contain exactly these five rules: `RevengeTradeRule`, `OvertradingDayRule`, `TiltSequenceRule`, `LongBreakRule`, `PreMarketPlanMissRule`
- AND the order MUST be stable (Priority ascending: high severity first)

### Requirement: Five initial rules

#### `RevengeTradeRule`

- GIVEN a closed losing trade within the last 24h followed by a trade with volume ≥ 1.5× the loser's volume in the same instrument class
- WHEN evaluated
- THEN it MUST emit a `RevengeTrade` prompt with severity `medium` and copy: "Detectamos revenge trading — ¿estabas emocionalmente afectado por la pérdida anterior? Te recomendamos un break de 30 min."

#### `OvertradingDayRule`

- GIVEN a day with ≥ 12 closed trades where the user's 30-day daily mean ≤ 5
- WHEN evaluated
- THEN it MUST emit a `OvertradingDay` prompt with severity proportional to (count / mean) and copy: "Operaste N veces hoy — tu promedio es M. ¿Estás sobre-operando?"

#### `TiltSequenceRule`

- GIVEN 3+ consecutive closed losing trades within a 90-minute window
- WHEN evaluated
- THEN it MUST emit a `TiltSequence` prompt with severity `high` and copy: "3 pérdidas consecutivas en 90 min — posible tilt. Cierra el terminal y volvé mañana con cabeza fresca."

#### `LongBreakRule`

- GIVEN a user who hasn't opened a trade in 5+ calendar days AND had ≥ 1 trade in the previous 30 days
- WHEN evaluated
- THEN it MUST emit a `LongBreak` prompt with severity `low` and copy: "5 días sin operar — ¿descanso intencional o falta de disciplina? Si es lo segundo, agendá una sesión de trading."

#### `PreMarketPlanMissRule`

- GIVEN a user with a journal entry today with `premarket_plan` non-empty AND ≥ 1 trade today that doesn't reference any of the journal's `tags` or instruments in `premarket_plan`
- WHEN evaluated
- THEN it MUST emit a `PreMarketPlanMissRule` prompt with severity `low` and copy: "Tu plan de hoy mencionaba X e Y, pero operaste Z. ¿Estabas siguiendo tu plan?"

### Requirement: Time-windowed queries

`GET /api/coaching/prompts` MUST accept `period=7d|30d|90d|all` defaulting to `30d`. The evaluation MUST consider only trades + journals within the window.

#### Scenario: 7d period

- GIVEN a `TiltSequenceRule` event from 60 days ago
- WHEN `GET /api/coaching/prompts?period=7d` is requested
- THEN the old event MUST NOT be returned

### Requirement: Severity ordering

Prompts MUST be ordered by severity descending: `high` first, then `medium`, then `low`. Within a severity, newer prompts MUST come first.

#### Scenario: Multiple prompts returned in stable order

- GIVEN the user has 1 `TiltSequence` (high, today) and 1 `OvertradingDay` (medium, today)
- WHEN `GET /api/coaching/prompts?period=30d` is requested
- THEN the `TiltSequence` MUST appear first, followed by the `OvertradingDay`

### Requirement: Authenticated read

`GET /api/coaching/prompts` MUST require an authenticated Trader identity. The response MUST include only the requesting user's prompts; cross-user reads MUST be impossible.

#### Scenario: Unauthenticated caller

- GIVEN an anonymous request
- WHEN `GET /api/coaching/prompts` is called
- THEN the system MUST return 401

### Requirement: No PII in copy

Coaching prompt copy MUST NOT include absolute numbers from the user's P&L (no `+$150.00` or `-50%`). The copy MAY use qualitative language ("pérdida consecutiva", "operaste más de lo habitual") but MUST NOT include raw amounts, percentages, or instrument-specific amounts.

#### Scenario: Copy redaction

- GIVEN a `TiltSequenceRule` event with `totalPnl = -$450`
- WHEN the prompt copy is generated
- THEN the rendered message MUST NOT contain "-$450" or "450 USD"
- AND MUST contain language like "3 pérdidas consecutivas" instead

### Requirement: CTA links

Each prompt MUST include a `cta` object with `{ route, label }`. The route MUST be a valid frontend path. The label MUST be actionable Spanish ("Ir al journal", "Revisar trades de hoy", "Hacer break de 30 min").

#### Scenario: CTA for `TiltSequenceRule`

- GIVEN a `TiltSequenceRule` prompt
- WHEN the response is built
- THEN the CTA MUST be `{ route: "/app/trades", label: "Revisar las operaciones" }` or similar

## Output Shape

```json
{
  "period": "30d",
  "userId": "...",
  "prompts": [
    {
      "ruleId": "TiltSequence",
      "severity": "high",
      "title": "Posible tilt detectado",
      "body": "3 pérdidas consecutivas en 90 min — posible tilt. Cierra el terminal y volvé mañana con cabeza fresca.",
      "cta": { "route": "/app/trades", "label": "Revisar las operaciones" },
      "occurredAt": "2026-08-12T14:32:00Z"
    }
  ]
}
```

## Endpoint

- `GET /api/coaching/prompts?period=7d|30d|90d|all` — returns the object above.
- `api-general` rate limit. RequireAuthorization.

## Architecture

- **Shared.Infrastructure** owns `ICoachingRule` interface + `CoachingPrompt` record.
- **Trading.Application** registers the 5 initial rules in `TradingModuleRegistration`.
- **Trading.Api** exposes `GET /api/coaching/prompts` which iterates the registry and aggregates outputs.
