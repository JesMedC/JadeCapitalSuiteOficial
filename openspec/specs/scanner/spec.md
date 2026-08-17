# Scanner Specification

## Purpose

User-owned instrument scanner. The trader defines filters by spread, volume, historical risk-reward, volatility window, and active hours; runs them ad-hoc or repeatedly; receives a ranked list of instruments that match the user's opportunity profile. Filters are persisted per-user (no global templates) and reusable across sessions.

The scanner complements the watchlist (Wave 4c) by surfacing NEW instruments that match the trader's profile — it does not subscribe to live quotes; that is the watchlist's job.

## Requirements

### Requirement: Scanner filter aggregate

A `ScannerFilter` MUST be a user-owned aggregate with `Id, UserId, Name, MinSpread, MaxSpread, MinVolume, MinRiskReward, VolatilityWindow, ActiveHours, IsActive, CreatedAt, UpdatedAt`. The `ActiveHours` field is a JSON object `{ dayOfWeek: 0..6, startHour: 0..23, endHour: 0..23 }[]` allowing zero or more windows per filter.

#### Scenario: Create valid filter

- GIVEN an authenticated trader
- WHEN `POST /api/scanner/saved` with `{ name: "Low spread majors", minSpread: 0.5, maxSpread: 2.0, minVolume: 100000, minRiskReward: 1.5, volatilityWindow: "D1", activeHours: [] }`
- THEN the system MUST return 201 with the created `ScannerFilterDto` and a new `Id`
- AND the `UserId` MUST equal the JWT `NameIdentifier` claim
- AND `IsActive` MUST default to `true`

#### Scenario: Filter name uniqueness per user

- GIVEN user A has a filter named "London Break"
- WHEN user A creates another filter with `name: "London Break"`
- THEN the system MUST return 409 Conflict

#### Scenario: Cross-user name collision allowed

- GIVEN user A has a filter named "London Break"
- WHEN user B creates a filter with `name: "London Break"`
- THEN the system MUST return 201 (no global uniqueness)

### Requirement: Filter validation

`MinSpread` MUST be >= 0 and `MaxSpread` MUST be > `MinSpread` if both set. `MinVolume` MUST be > 0. `MinRiskReward` MUST be > 0. `VolatilityWindow` MUST be one of `D1, W1, MN`. `ActiveHours` MUST be empty or an array of valid window objects.

#### Scenario: Invalid spread range

- GIVEN a filter request with `minSpread: 5.0, maxSpread: 1.0`
- WHEN `POST /api/scanner/saved` is called
- THEN the system MUST return 422 with `error.code = "validation.failed"` and a message naming `maxSpread < minSpread`

#### Scenario: Empty volume

- GIVEN a filter request with `minVolume: 0`
- WHEN `POST /api/scanner/saved` is called
- THEN the system MUST return 422

### Requirement: Run scanner with a saved filter

`POST /api/scanner/run` with `{ filterId: Guid, limit: 1..100 }` MUST execute the saved filter against `trading.instruments` (joined with cached metrics) and return `ScanResult[]` ordered by `MatchScore` descending. The `MatchScore` is a composite: weighted blend of how well the instrument's current spread fits the filter window (40%), volume percentile vs filter threshold (30%), and historical R-R percentile (30%).

#### Scenario: Run with saved filter returns ranked matches

- GIVEN user A has a filter `id = F1` with `minSpread: 0.5, maxSpread: 2.0, minVolume: 100000, minRiskReward: 1.5`
- AND `trading.instruments` contains EURUSD (spread=1.0, vol=200000, rr=2.0), GBPJPY (spread=3.0, vol=150000, rr=1.8), BTCUSD (spread=1.5, vol=50, rr=2.5)
- WHEN `POST /api/scanner/run { filterId: F1, limit: 10 }` is called
- THEN the response MUST contain at least EURUSD
- AND GBPJPY MUST be excluded (spread 3.0 > maxSpread 2.0)
- AND BTCUSD MUST be excluded (volume 50 < minVolume 100000)
- AND the order MUST be by MatchScore descending

#### Scenario: Empty result (no matches)

- GIVEN a filter that nothing matches
- WHEN `POST /api/scanner/run` is called
- THEN the response MUST be `[]` (empty array, 200 OK, not 404)

#### Scenario: Filter from another user

- GIVEN user A has a filter `id = F1`
- WHEN user B calls `POST /api/scanner/run { filterId: F1, limit: 10 }`
- THEN the system MUST return 404 (filter not in user B's namespace)

### Requirement: Run scanner with ad-hoc filter

`POST /api/scanner/run` MUST also accept a transient filter body (`{ filter: ScannerFilterSpec, limit: 1..100 }`) without `filterId`. The system evaluates without persisting. This is the "try it before saving" path.

#### Scenario: Ad-hoc run without filterId

- GIVEN a transient filter `{ minSpread: 1.0, maxSpread: 5.0, minVolume: 50000, minRiskReward: 1.0 }`
- WHEN `POST /api/scanner/run { filter: ..., limit: 20 }` is called
- THEN the response MUST contain matching instruments ranked
- AND no filter MUST be persisted

### Requirement: List and delete saved filters

`GET /api/scanner/saved` MUST return the user's filters (paginated, default page=1, pageSize=50). `DELETE /api/scanner/saved/{id}` MUST soft-delete (set `IsActive = false`) and return 204. Soft-deleted filters MUST NOT appear in `GET /api/scanner/saved`.

#### Scenario: List own filters

- GIVEN user A has 3 active filters
- WHEN `GET /api/scanner/saved` is called by user A
- THEN the response MUST be `ScannerFilterDto[]` of length 3

#### Scenario: Delete filter

- GIVEN user A has filter `id = F1`
- WHEN `DELETE /api/scanner/saved/F1` is called
- THEN the response MUST be 204
- AND `GET /api/scanner/saved` MUST NOT include F1

#### Scenario: Cross-user delete

- GIVEN user A has filter `id = F1`
- WHEN user B calls `DELETE /api/scanner/saved/F1`
- THEN the system MUST return 404

### Requirement: Cross-user isolation

All scanner endpoints MUST scope to the calling user's `UserId`. The repository MUST filter by `user_id` on every read. There is no admin endpoint in Wave 4.

#### Scenario: Unauthenticated caller

- GIVEN an anonymous request
- WHEN `GET /api/scanner/saved` is called
- THEN the system MUST return 401

## Data Model

```
trading.scanner_filters
  id                 UUID PK
  user_id            UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE
  name               VARCHAR(64) NOT NULL
  min_spread         NUMERIC(10,5) NOT NULL DEFAULT 0
  max_spread         NUMERIC(10,5) NULL
  min_volume         NUMERIC(24,8) NOT NULL DEFAULT 0
  min_risk_reward    NUMERIC(6,2) NOT NULL DEFAULT 0
  volatility_window  SMALLINT NOT NULL DEFAULT 0  -- 0=Unspecified, 7=D1, 8=W1, 9=MN
  active_hours       JSONB NOT NULL DEFAULT '[]'
  is_active          BOOLEAN NOT NULL DEFAULT TRUE
  created_at         TIMESTAMPTZ NOT NULL DEFAULT now()
  updated_at         TIMESTAMPTZ NOT NULL DEFAULT now()

Indexes:
  ux_scanner_filters_user_name UNIQUE (user_id, lower(name)) WHERE is_active = true
  ix_scanner_filters_user_active (user_id) WHERE is_active = true
```

## Endpoints

- `GET /api/scanner/saved?page=1&pageSize=50` — list user's saved filters.
- `POST /api/scanner/saved` — create a filter.
- `GET /api/scanner/saved/{id}` — single filter detail.
- `PATCH /api/scanner/saved/{id}` — update a filter (rename, change thresholds, toggle `IsActive`).
- `DELETE /api/scanner/saved/{id}` — soft-delete a filter.
- `POST /api/scanner/run { filterId | filter, limit }` — run a scan.

All require `RequireAuthorization` and `api-general` rate limit.

## Output Shape

```json
{
  "results": [
    {
      "symbol": "EURUSD",
      "currentSpread": 1.0,
      "dailyVolume": 200000,
      "historicalRiskReward": 2.0,
      "volatility": 0.012,
      "matchScore": 0.87,
      "matchedCriteria": ["spread", "volume", "riskReward"]
    }
  ]
}
```

## Architecture

- **Trading.Domain/Scanner/ScannerFilter.cs** — aggregate root.
- **Trading.Application/Features/Scanner/** — handlers (CreateScannerFilter, UpdateScannerFilter, SoftDeleteScannerFilter, GetScannerFilters, GetScannerFilterById, RunScan).
- **Trading.Infrastructure** — `ScannerFilterConfiguration` (EF), `ScannerFilterRepository`, `ScannerRunner` (encapsulates the join + scoring).
- **Trading.Api** — `MapScannerEndpoints`.
- **Frontend** — `trader/scanner/scanner-page.ts` standalone Signals OnPush SCSS with: list of saved filters, create form (inline), run button, results table with match score column.