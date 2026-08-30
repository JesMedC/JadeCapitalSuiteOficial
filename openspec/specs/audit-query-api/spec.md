# Audit Query API Specification

## Purpose

Admin-only read surface for the `audit.events` table. Compliance officers and admin tooling query mutation history through `GET /api/admin/audit/events` with structured filters + cursor pagination. The endpoint enforces `AdminOnly` authorization at the boundary before any DB lookup — no trader or anonymous access ever.

This spec covers the endpoint contract, query handler, query store abstraction, cursor pagination semantics, PII exposure contract, and error scenarios. It does NOT cover retention (see `audit-retention-policy`) or the write path (see `soft-delete-audit`).

## Requirements

### Requirement: Admin endpoint authorizes before any DB lookup

The system MUST expose `GET /api/admin/audit/events` at `src/2.Modules/Admin/JadeCapital.Admin.Api/Endpoints/AdminAuditEndpoints.cs`. The endpoint MUST be mounted via `MapGroup("/api/admin/audit/events").RequireAuthorization("AdminOnly").MapGet("/", ListAsync).RequireRateLimiting("api-general")`. The `RequireAdminPolicyHandler` MUST evaluate the `User.IsInRole("Admin")` check BEFORE any DB lookup or MediatR dispatch — anonymous callers MUST receive `401`, callers with the Trader role (but not Admin) MUST receive `403`.

#### Scenario: Anonymous request returns 401

- GIVEN no JWT is attached to the request
- WHEN `GET /api/admin/audit/events` is called
- THEN the endpoint MUST return HTTP 401 (Unauthorized)
- AND NO `AuditEventQueryStore.ListAsync` call MUST occur

#### Scenario: Trader role returns 403

- GIVEN a valid JWT is attached with `role = "Trader"` (no Admin claim)
- WHEN `GET /api/admin/audit/events` is called
- THEN the endpoint MUST return HTTP 403 (Forbidden)
- AND NO `AuditEventQueryStore.ListAsync` call MUST occur

#### Scenario: Admin role returns 200

- GIVEN a valid JWT is attached with `role = "Admin"`
- WHEN `GET /api/admin/audit/events` is called with no filters
- THEN the endpoint MUST return HTTP 200 with `{ "items": [...], "next_cursor": "<base64>", "has_more": <bool> }`
- AND the AdminActor identity MUST be captured in the existing Serilog request log (request URI + JWT subject) — no `audit.events` row is generated for the admin's own query

### Requirement: Structured filter semantics

The endpoint MUST accept the following query-string filters, AND-combined (all optional): `entity_type` (string, exact match on `audit.events.entity_type`), `action` (short enum value: 0=Created, 1=Updated, 2=Deleted, 3=Restored, 4=Denied, 5=Failed), `user_id` (Guid), `tenant_id` (Guid), `from` + `to` (ISO 8601 timestamps, half-open `[from, to)`). Filters MUST translate to EF `.Where()` clauses against `AuditDbContext.AuditEvents`. Compound filters MUST use the most selective single index (Postgres bitmap-ANDs the others); the 3 existing indexes (`ix_audit_events_entity`, `ix_audit_events_tenant_time`, `ix_audit_events_user`) cover the 4 single-filter queries.

#### Scenario: Single filter narrows results

- GIVEN 100 audit events across all entity types
- WHEN `GET /api/admin/audit/events?entity_type=TradeAttachment` is called
- THEN the response MUST contain only rows where `entity_type = "TradeAttachment"`
- AND the EF query MUST hit `ix_audit_events_entity`

#### Scenario: Date range filter applies half-open interval

- GIVEN events occurred at `2026-08-01T10:00Z`, `2026-08-19T00:00Z`, `2026-08-19T23:59:59.999Z`
- WHEN `GET /api/admin/audit/events?from=2026-08-01T00:00:00Z&to=2026-08-19T00:00:00Z` is called
- THEN the response MUST include the `2026-08-01` event and MUST exclude the `2026-08-19T00:00Z` event (half-open `[from, to)`)
- AND the EF query MUST hit `ix_audit_events_tenant_time` when `tenant_id` is also supplied, otherwise scan + filter

#### Scenario: Action enum maps to byte value

- GIVEN an admin calls `?action=4` (Denied)
- WHEN the request is processed
- THEN the EF query MUST apply `e.Action == (AuditAction)4`
- AND the response MUST include only `action = 4` rows

### Requirement: Cursor pagination on (occurred_at DESC, id DESC)

The endpoint MUST paginate via opaque base64 cursor = `base64("{occurred_at_ticks}:{id_guid}")`. Server applies keyset filter `WHERE (occurred_at, id) < (cursor.occurred_at, cursor.id) ORDER BY occurred_at DESC, id DESC LIMIT $limit + 1`. The `+1` row determines `has_more`; if present, the response MUST include `next_cursor` (base64 of the last item's `(occurred_at, id)`); if absent, `has_more = false` and `next_cursor = null`. The `id` tiebreaker MUST guarantee deterministic ordering when two events share `occurred_at` (rare batch inserts). Default `limit = 50`; the value MUST be clamped to `[1, 200]`; values outside the range MUST return HTTP 400.

#### Scenario: First page returns newest-first

- GIVEN 5 audit events occurred at descending timestamps
- WHEN `GET /api/admin/audit/events?limit=2` is called (no cursor)
- THEN the response MUST contain the 2 newest events (newest first)
- AND `has_more = true`
- AND `next_cursor` MUST be a base64 string encoding the second item's `(occurred_at, id)`

#### Scenario: Subsequent page follows the cursor

- GIVEN the first page returned `next_cursor = base64("t1:i2")`
- WHEN `GET /api/admin/audit/events?limit=2&cursor=<base64("t1:i2")>` is called
- THEN the response MUST contain events strictly newer-than-no-including the cursor key (events with `(occurred_at, id) < (t1, i2)`)
- AND the ordering MUST remain `occurred_at DESC, id DESC`

#### Scenario: Tiebreaker is deterministic

- GIVEN 2 events with the same `occurred_at = T` but different `id = A, B` (A < B alphabetically)
- WHEN the page boundary falls on `(T, A)`
- THEN the next page MUST include `(T, B)` and NOT `(T, A)` — the `id` tiebreaker guarantees deterministic ordering

#### Scenario: Limit clamping rejects out-of-range values

- GIVEN a request includes `limit = 0` or `limit = 500`
- WHEN the request is processed
- THEN the endpoint MUST return HTTP 400 with a ProblemDetails body explaining the valid range

### Requirement: Malformed cursor returns 400

The endpoint MUST validate the cursor before applying the keyset filter. If base64 decoding fails OR the decoded payload is not in the format `{long}:{guid}` OR the resulting `(occurred_at, id)` tuple would cause SQL injection, the endpoint MUST return HTTP 400 with a ProblemDetails body (`code = "invalid_cursor"`). The cursor format is opaque to discourage client tampering.

#### Scenario: Base64 decode fails

- GIVEN a request includes `cursor = "!!not-base64!!"`
- WHEN the request is processed
- THEN the endpoint MUST return HTTP 400 with `code = "invalid_cursor"`
- AND NO `AuditEventQueryStore.ListAsync` call MUST occur

#### Scenario: Decoded payload has wrong shape

- GIVEN a request includes `cursor = base64("not-a-guid")`
- WHEN the request is processed
- THEN the endpoint MUST return HTTP 400 with `code = "invalid_cursor"`

### Requirement: PII exposure contract — admin sees all fields

The response DTO MUST include `id`, `entity_type`, `entity_id`, `action` (as enum name string), `tenant_id`, `user_id`, `changes` (full JSONB payload), and `occurred_at`. Admin role sees all fields by design — the `AdminOnly` policy + handler enforce role check BEFORE any DB lookup, so no trader or anonymous access ever reaches the DTO mapping. `user_id` + `tenant_id` are internal Guids (not PII like email/name); the `changes` JSONB may contain user-owned entity data (e.g. trade price, journal text) and is admin-visible by the compliance contract.

#### Scenario: Response includes all fields

- GIVEN an admin calls `GET /api/admin/audit/events` with valid filters
- WHEN the response is built
- THEN each item MUST include `id`, `entity_type`, `entity_id`, `action`, `tenant_id`, `user_id`, `changes`, `occurred_at`
- AND the `changes` field MUST preserve the JSONB structure (no redaction)

### Requirement: IAuditEventQueryStore abstracts the read side

The interface `IAuditEventQueryStore` MUST be declared at `src/2.Modules/Admin/JadeCapital.Admin.Application/Abstractions/IAuditEventQueryStore.cs`. The EF impl `AuditEventQueryStore` MUST be declared at `src/2.Modules/Admin/JadeCapital.Admin.Infrastructure/Persistence/AuditEventQueryStore.cs` and depend on `AuditDbContext` from `Identity.Infrastructure`. The store MUST expose `Task<PagedAuditEventsDto> ListAsync(ListAuditEventsQuery query, CancellationToken ct)`. The handler `ListAuditEventsHandler` MUST live at `src/2.Modules/Admin/JadeCapital.Admin.Application/Features/Audit/ListAuditEvents/ListAuditEventsHandler.cs` and call `IAuditEventQueryStore.ListAsync`. The `Admin.Infrastructure.csproj` MUST add `<ProjectReference Include="..\..\Identity\JadeCapital.Identity.Infrastructure\JadeCapital.Identity.Infrastructure.csproj" />` if missing — this is the new cross-module DI edge (Admin.Infrastructure → Identity.Infrastructure for `AuditDbContext`).

#### Scenario: Query store depends on AuditDbContext

- GIVEN `AuditEventQueryStore` is constructed via DI
- WHEN the constructor resolves dependencies
- THEN it MUST receive `AuditDbContext` (registered in `IdentityModuleRegistration`)
- AND the DI registration `services.AddScoped<IAuditEventQueryStore, AuditEventQueryStore>()` MUST be present in `AdminModuleRegistration`

#### Scenario: Handler emits DTO with cursor + items

- GIVEN the handler processes `ListAuditEventsQuery` with `Limit = 50, Cursor = null`
- WHEN it calls `_queryStore.ListAsync(query, ct)`
- THEN the resulting `PagedAuditEventsDto` MUST contain `Items` (≤ 50 items newest-first), `NextCursor` (base64 of last item's `(occurred_at, id)` if `has_more`), `HasMore` (bool)

### Requirement: Endpoint returns 500 on unhandled errors

Unhandled exceptions in the query store (e.g., DB connectivity loss) MUST surface as HTTP 500 with a generic ProblemDetails body. The full exception MUST be logged via Serilog at `Error` level with the request URI + AdminActor identity, but MUST NOT include PII in the response.

#### Scenario: DB unavailable returns 500

- GIVEN the DB is unreachable when `AuditEventQueryStore.ListAsync` runs
- WHEN the handler catches the exception
- THEN the endpoint MUST return HTTP 500 with `code = "internal_error"`
- AND the Serilog log MUST include the request URI + AdminActor + exception detail
