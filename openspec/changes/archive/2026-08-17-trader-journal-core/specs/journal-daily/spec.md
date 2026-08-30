# Journal Daily Specification

## Purpose

User-owned daily trading journal. Captures pre-market intent, post-market reflection, and mood across three temporal dimensions (before / during / after market). The journal is the substrate for behavioral analytics (Wave 2b), coaching prompts (Wave 2d), and post-trade review (Wave 1d).

## Requirements

### Requirement: One journal entry per user per day

The system MUST allow at most one journal entry per `(user_id, local_date)` pair. The local date is computed from the user's timezone (header `X-User-Timezone`, fallback to UTC). A user MAY update an existing entry; the system MUST upsert atomically. Two simultaneous entries for the same user+date MUST NOT exist (partial unique index `ux_journal_user_date`).

#### Scenario: First entry of the day

- GIVEN an authenticated user with no journal entry for today
- WHEN `POST /api/journal/today` succeeds
- THEN exactly one entry MUST be persisted for that user and date
- AND the response MUST include the new entry's id and all fields

#### Scenario: Updating today's entry

- GIVEN an existing entry for today
- WHEN `POST /api/journal/today` is called again with partial fields
- THEN the system MUST upsert atomically and return the merged entry
- AND no second row MAY be created

#### Scenario: Cross-user isolation

- GIVEN user A has an entry for 2026-08-17
- WHEN user B requests `GET /api/journal?date=2026-08-17`
- THEN the response MUST return user B's own entry (or 404), NEVER user A's

### Requirement: Validated mood dimensions

`mood_pre`, `mood_during`, `mood_post` MUST be integers in `[1, 5]` (1 = Fearful, 2 = Anxious, 3 = Neutral, 4 = Confident, 5 = Euphoric). The same scale used in `pre-trade-checklist.emotionality` for consistency. Out-of-range values MUST be rejected with a `validation` error (422).

#### Scenario: Out-of-range mood

- GIVEN any mood field is outside `[1, 5]` (e.g. `mood_pre = 7`)
- WHEN the request is validated
- THEN the system MUST return `422 Unprocessable Entity` with a `validation` error
- AND MUST NOT persist any partial entry

### Requirement: Authenticated CRUD

`GET /api/journal/today`, `GET /api/journal?from=&to=`, `POST /api/journal/today`, and `DELETE /api/journal/{id}` MUST require an authenticated Trader identity (RequireAuthorization). The response MUST include only the requesting user's entries; cross-user reads MUST be impossible.

#### Scenario: Unauthenticated or restricted-scope caller

- GIVEN an anonymous request or a forced-change / restricted-scope token
- WHEN any journal endpoint is requested
- THEN the system MUST deny access before any lookup or persistence

### Requirement: Pre-market + post-market + reflection structure

The entry MUST support three distinct free-text sections:

- `premarket_plan`: string ≤ 2000 chars (intended setup, watchlist, scenarios).
- `postmarket_reflection`: string ≤ 5000 chars (what worked, what to improve, lessons).
- `tags`: array of free-text tags ≤ 32 chars each, max 10 tags per entry (e.g. "fomo", "revenge", "good-execution").

#### Scenario: Oversized pre-market plan

- GIVEN `premarket_plan` length is 2500 chars
- WHEN the request is validated
- THEN the system MUST return 422 with `validation.journal.premarket_plan_too_long`

### Requirement: Timezone-aware date resolution

The journal entry's `local_date` MUST be derived from the user's timezone, not the server's UTC clock. The system MUST accept `X-User-Timezone` header (IANA TZ name, e.g. `America/Argentina/Buenos_Aires`); absent or invalid MUST fall back to UTC.

#### Scenario: Trader in Buenos Aires opens at 22:00 UTC on 2026-08-16

- GIVEN `X-User-Timezone: America/Argentina/Buenos_Aires` (UTC-3)
- WHEN the trader creates an entry at 2026-08-16 19:00 local (2026-08-16 22:00 UTC)
- THEN `local_date = "2026-08-16"`

## Data Model

```
trading.journal_entries
  id                UUID PK
  user_id           UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE
  local_date        DATE NOT NULL
  timezone          VARCHAR(64) NOT NULL DEFAULT 'UTC'
  mood_pre          SMALLINT NULL CHECK (mood_pre BETWEEN 1 AND 5)
  mood_during       SMALLINT NULL CHECK (mood_during BETWEEN 1 AND 5)
  mood_post         SMALLINT NULL CHECK (mood_post BETWEEN 1 AND 5)
  premarket_plan    TEXT NULL CHECK (LENGTH(premarket_plan) <= 2000)
  postmarket_reflection TEXT NULL CHECK (LENGTH(postmarket_reflection) <= 5000)
  tags              TEXT[] NULL CHECK (array_length(tags, 1) <= 10)
  created_at        TIMESTAMPTZ NOT NULL DEFAULT now()
  updated_at        TIMESTAMPTZ NOT NULL DEFAULT now()

Indexes:
  ux_journal_user_date UNIQUE (user_id, local_date)
  ix_journal_user_created (user_id, created_at DESC)
```

## Endpoints

- `GET /api/journal/today` — returns today's entry or 404.
- `GET /api/journal?from=YYYY-MM-DD&to=YYYY-MM-DD` — returns entries in range.
- `POST /api/journal/today` — upsert today's entry (partial body OK).
- `DELETE /api/journal/{id}` — soft or hard delete (decision: hard delete in Wave 2, soft-delete deferred to Fase 6).

All require `RequireAuthorization` and `api-general` rate limit.
