# Design — Wave 9 (Audit Finalization: 4 Decorators + Query API + 90-Day Retention)

## Architecture Overview

Wave 9 closes the audit decorator rollout to **19 entity types** (15 from Wave 8 + 4 new Wave 9 aggregates: AIRiskAdvice, CoachingPrompt, ScannerFilter, TradeAttachment via AttachmentSweep) + documents 1 SKIP (`ITradeAttachmentUsageRepository`). The wave also introduces 2 NEW features: the admin-only `GET /api/admin/audit/events` query API + the 90-day `AuditRetentionBackgroundService` auto-purge. The wave ships in 5 chained slices (9a.1, 9a.2, 9a.3, 9b.1, 9b.2) totaling ~2,450 LOC, ~38 file paths, and ~24 new BE tests.

The wave is driven by 3 concerns (per the proposal sub-scopes):

1. **Coverage extension (sub-scope A)** — 4 new typed audit decorators: 2 bespoke write-once (`AIRiskAdvice`, `CoachingPrompt`) mirroring `StripeCustomerAuditDecorator` shape; 1 bespoke CRUD-without-Delete (`ScannerFilter`) extending `IRepository<ScannerFilter>` with a defensive `DeleteAsync` stub; 1 NEW batch soft-delete (`AttachmentSweep`) wrapping `SoftDeleteBatchAsync` and emitting 1 audit row per id (the first "1-call-many-audit-rows" decorator in the codebase).
2. **Admin query API (sub-scope B)** — `GET /api/admin/audit/events` with cursor pagination + filters; lands in `Admin.Api` (NOT Identity.Api); Admin role enforced via `RequireAuthorization("AdminOnly")` + `RequireAdminPolicyHandler` BEFORE any DB lookup.
3. **Audit retention (sub-scope C)** — `IAuditRetentionService` + `AuditRetentionService` (EF Core 9 `ExecuteDeleteAsync`) + `AuditRetentionBackgroundService` (2-min startup settle + 24h interval + jitter + per-attempt isolation) + `AuditRetentionOptions` (`ValidateOnStart`). Idempotent + safe re-run.

**No new infrastructure**: no enum extension (Wave 7's `AuditAction.Denied = 4` + `AuditAction.Failed = 5` + migration 0029 already cover all 4 new decorators), no migration, no `Shared.Kernel` change. The `audit.events` table schema is unchanged from Wave 6 (migration 0027) + Wave 7 (migration 0029).

## Module Dependency Diagram

```
AFTER Wave 8 (current state at start of Wave 9):
  Trading.Infrastructure  ──→  Shared.Infrastructure  (for DecoratedRepository<T>)
  Billing.Infrastructure  ──→  Shared.Infrastructure  (for DecoratedRepository<T>)
  Identity.Infrastructure  ──→  Shared.Infrastructure
  Shared.Infrastructure    ──→  Shared.Kernel

AFTER Wave 9 (this delta):
  Trading.Infrastructure  ──→  Shared.Infrastructure  (for DecoratedRepository<T>)
  Billing.Infrastructure  ──→  Shared.Infrastructure  (for DecoratedRepository<T>)
  Identity.Infrastructure  ──→  Shared.Infrastructure
  Admin.Infrastructure    ──→  Identity.Infrastructure  ← NEW EDGE (for AuditDbContext)
  Admin.Application       ──→  (no module edges; depends on Admin.Infrastructure via DI)
  Admin.Api               ──→  Admin.Application       ← NEW MODULE (empty folders filled)
  Shared.Infrastructure    ──→  Shared.Kernel
```

**1 new edge + 1 new module populated.** `Admin.Infrastructure → Identity.Infrastructure` is the new cross-module DI edge for `AuditDbContext` (used by `AuditEventQueryStore`). The `Admin.Application` + `Admin.Infrastructure` csprojs already exist (referenced from `JadeCapital.Host/Program.cs` lines 131, 133, 151); Wave 9 9b.1 fills the empty source folders with the first files. The `Admin.Api` folder already has `AdminSubscriptionEndpoints.cs` (192 LOC, Wave 0) as the canonical template; Wave 9 9b.1 adds `AdminAuditEndpoints.cs` (~120 LOC) following the same pattern.

**Why no circular dep risk**: `Admin.Infrastructure` references `Identity.Infrastructure` for `AuditDbContext` only — it does NOT register `Identity.Infrastructure`'s services. The edge is one-way; `Identity.Infrastructure` does not know about Admin.

## Per-Decorator Decisions (shape + signature)

### 1. `AIRiskAdviceAuditDecorator` (9a.1) — Bespoke write-once

**Pattern**: Bespoke decorator implementing `IAIRiskAdviceRepository` directly. Mirrors a simplified `StripeCustomerAuditDecorator` shape (Wave 8 8b.1). The interface has only `AddAsync(AIRiskAdvice, ct)` + `FindByUserAndTradeAsync(...)` (read). The aggregate is immutable post-Create per the entity docstring.

**Interface surgery**: 0 (the interface shape already matches the audit intent).

**Cross-tenant**: YES — `advice.UserId == _tenant.CurrentUserId`.

**File**: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AIRiskAdviceAuditDecorator.cs` (~120 LOC). Only `AddAsync` wrapped with `Created` audit + `IsOwner` check. `FindByUserAndTradeAsync` forwarded without audit.

**Forecast**: 4 integration scenarios.

### 2. `CoachingPromptAuditDecorator` (9a.1) — Bespoke write-once

**Pattern**: Same as AIRiskAdvice — bespoke write-once. The interface has 3 methods: `AddAsync` (mutation), `FindByUserAndDateAsync` (read), `ListByUserAndWindowAsync` (read). Aggregate immutable post-Create.

**Interface surgery**: 0.

**Cross-tenant**: YES — `prompt.UserId == _tenant.CurrentUserId`.

**File**: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/CoachingPromptAuditDecorator.cs` (~120 LOC). Same shape as AIRiskAdvice.

**Forecast**: 4 integration scenarios.

### 3. `ScannerFilterAuditDecorator` (9a.2) — Bespoke CRUD-without-Delete

**Pattern**: Bespoke decorator implementing `IScannerFilterRepository` directly. The interface has 3 reads (`GetByIdAsync`, `GetByUserAndNameAsync`, `ListByUserAsync`) + 2 mutations (`AddAsync`, `UpdateAsync`). `ScannerFilter` has no `DeleteAsync` (deactivation via `IsActive = false` via `Deactivate(IClock)`). The interface does NOT inherit `IRepository<T>` — adding `DeleteAsync` as a defensive stub requires the canonical 4-method surface, which is added in Wave 9 9a.2.

**Interface surgery**: 1 (extend `IScannerFilterRepository` to `IRepository<ScannerFilter>`; add defensive `DeleteAsync(ScannerFilter, ct)` stub that throws `NotSupportedException` + emits `AuditAction.Failed` — mirrors Wave 7 7a.1 `UserAuditDecorator` precedent).

**Cross-tenant**: YES — `filter.UserId == _tenant.CurrentUserId`.

**File**: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/ScannerFilterAuditDecorator.cs` (~180 LOC). Bespoke shape. `AddAsync` + `UpdateAsync` wrapped. `DeleteAsync` defensive stub emits `Failed` + throws. Reads forwarded without audit.

**Why bespoke instead of generic**: `IScannerFilterRepository.GetByUserAndNameAsync(name, ct)` is a parameter-driven read; extending `IRepository<T>` would force `GetByIdAsync(Guid, ct)` shape that ignores the name-scoped lookup. Bespoke decorator preserves the bespoke read signatures.

**Forecast**: 5 integration scenarios + 1 contract scenario (DeleteAsync defensive stub pin).

### 4. `AttachmentSweepAuditDecorator` (9a.3) — NEW batch soft-delete pattern

**Pattern**: Bespoke decorator implementing `IAttachmentSweepRepository` directly. NEW pattern — wraps `SoftDeleteBatchAsync(IReadOnlyList<Guid>, ct)` which is a batch mutation, NOT the canonical single-entity shape. The decorator emits 1 `AuditAction.Deleted` audit row PER id in the batch, with `EntityType = "TradeAttachment"`, `EntityId = id`, `ChangesJson = { "IsActive": { "before": true, "after": false } }`. Cross-tenant `IsOwner` check is per-id (using `attachment.UserId` from the loaded EF tracked instance the inner repo loaded for the `MarkSwept` call). `InsertAuditAsync(...)` is forwarded WITHOUT audit (the destination `trading.attachments_quota_audit` table IS the audit log for the sweep — emitting on top would be doubly-recorded noise; mirrors Wave 8 8b.2 `IStripeWebhookEventRepository` SKIP rationale). 3 read methods (`GetExpiredBatchAsync`, `GetUserAggregateAsync`, `GetActiveUserIdsAsync`) forwarded without audit.

**Interface surgery**: 0.

**Cross-tenant**: YES (per-id) — `attachment.UserId == _tenant.CurrentUserId`.

**File**: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AttachmentSweepAuditDecorator.cs` (~150 LOC). NEW pattern — first "1-call-many-audit-rows" decorator.

**Why bespoke**: `IAttachmentSweepRepository.SoftDeleteBatchAsync(IReadOnlyList<Guid>, ct)` does not fit `IRepository<T>.DeleteAsync(T, ct)`. Bespoke decorator preserves the batch shape.

**Forecast**: 5 integration scenarios.

## Sub-scope B: Admin Query API Architecture

### Endpoint surface

```
GET /api/admin/audit/events?entity_type=&action=&user_id=&tenant_id=&from=&to=&cursor=&limit=
```

### Authorization boundary

`RequireAuthorization("AdminOnly")` + `RequireAdminPolicyHandler` deny-by-default BEFORE any DB lookup. Anonymous → 401, Trader role → 403, Admin role → 200.

### Read-side architecture

- **New interface**: `IAuditEventQueryStore` in `Admin.Application/Abstractions/` (admin-side abstraction; keeps Admin independent of Identity's internal `AuditDbContext`).
- **EF impl**: `AuditEventQueryStore` in `Admin.Infrastructure/Persistence/` — depends on `AuditDbContext` (already in Identity.Infrastructure + registered in DI). Cross-module reference OK: `Admin.Infrastructure` transitively references `Identity.Infrastructure` via the new `<ProjectReference>` added in 9b.1 Phase 9.
- **Handler**: `ListAuditEventsQuery` + `ListAuditEventsHandler` in `Admin.Application/Features/Audit/ListAuditEvents/`.
- **Endpoint**: `MapAdminAuditEndpoints()` extension in `Admin.Api/Endpoints/AdminAuditEndpoints.cs`.
- **DI wiring**: `services.AddScoped<IAuditEventQueryStore, AuditEventQueryStore>()` + handler registration in `AdminModuleRegistration` (NEW file — csproj already exists).

### Cursor pagination

`(occurred_at DESC, id DESC)` keyset. Cursor format: `base64("{occurred_at_ticks}:{id_guid}")`. Server filters `WHERE (occurred_at, id) < (cursor.occurred_at, cursor.id) ORDER BY occurred_at DESC, id DESC LIMIT $limit + 1`. The `+1` row determines `has_more`. If two events share `occurred_at` (rare batch inserts), the `id` tiebreaker guarantees deterministic ordering. Opaque base64 discourages client tampering.

### Index coverage

3 existing indexes (`ix_audit_events_entity`, `ix_audit_events_tenant_time`, `ix_audit_events_user`) cover the 4 single-filter queries. Compound filters use the most selective single index; Postgres bitmap-AND. For 1M+ row tables, add a covering index in a follow-up migration if profiling shows it (out of Wave 9 scope).

### PII + compliance

- **Admin-only access**: enforced at endpoint boundary. No trader / anonymous access ever.
- **PII exposure**: `user_id` + `tenant_id` are internal Guids (not PII); `changes` JSONB may contain user-owned entity data — admin role sees all by design (compliance contract).
- **No audit-on-audit-query**: the admin's query does NOT generate an `audit.events` row (would be recursive). AdminActor captured in Serilog request log.
- **Rate limiting**: `RequireRateLimiting("api-general")` (matches Wave 8 8b.1 AdminSubscriptionEndpoints).

## Sub-scope C: Audit Retention Architecture

### BackgroundService pattern (inherited from Wave 4/5/6/8)

`AuditRetentionBackgroundService : BackgroundService` co-located with `AuditLogger` + `AuditDbContext` in `Identity.Infrastructure/Audit/`. Pattern precedents: `RefreshTokenCleanupService` (Wave 6 6c.2), `AttachmentLifecycleService` (Wave 4 4d), `BackfillTenantsHostedService` (Wave 5 5a). Loop structure: 2-min startup settle → 24h cycle with `[0, +30min]` jitter → per-cycle `try/catch` isolation → `RunOnceAsync` exposed for tests.

### SQL approach

EF Core 9 `ExecuteDeleteAsync(ct)` translates to a single `DELETE FROM audit.events WHERE id IN (...)` (or the optimized single-statement form). The `Take(batchLimit)` caps the rows per call; the BackgroundService loops within a cycle until `PurgeOldAsync` returns 0 rows (idempotent drain within a single cutoff). The `audit.events` table has no FK relationships (intentional denormalization for append-only semantics), so the DELETE is safe.

### Why `ExecuteDeleteAsync` (not tracked entities)

If the purge loaded tracked entities + `SaveChangesAsync`, the EF change tracker would emit `audit.events` audit rows for its own deletes — recursive noise. `ExecuteDeleteAsync` bypasses the change tracker entirely.

### Config validation

`AuditRetentionOptions.ValidateOnStart` enforces `RetentionDays > 0`, `CleanupIntervalHours > 0`, `BatchLimit > 0`. Invalid config fails the host at startup (matches Wave 6 6c.2 `JwtOptions` precedent).

### Scheduling rationale

- **First run**: 2 minutes after startup — gives the rest of the pipeline time to settle (matches `BackfillTenantsHostedService` precedent scaled for a heavier first run).
- **Subsequent runs**: every `CleanupIntervalHours` (24h default) with `[0, +30min]` jitter to avoid thundering herd across replicas (matches `AttachmentLifecycleService`).
- **Per-run isolation**: `try { PurgeOldAsync + log } catch { LogError + continue }` — never crash the host (matches `RefreshTokenCleanupService`).
- **Logging**: `LogInformation` when rows deleted > 0; `LogDebug` when 0 rows deleted.

## Pattern Decisions Summary

| Repo / Feature | Pattern | LOC estimate | New module? |
|---|---|---:|---|
| AIRiskAdvice | Bespoke write-once (mirror StripeCustomer) | ~120 | — |
| CoachingPrompt | Bespoke write-once (mirror StripeCustomer) | ~120 | — |
| ScannerFilter | Bespoke CRUD-without-Delete (mirror Account without Delete) | ~180 | — |
| AttachmentSweep | NEW bespoke batch soft-delete (1-call-many-audit-rows) | ~150 | — |
| Admin query API | `Admin.Api` endpoint + `Admin.Application` handler + `Admin.Infrastructure` store | ~420 | Admin.Application + Admin.Infrastructure (empty folders filled) |
| Retention | `Identity.Infrastructure` BackgroundService + options | ~200 | — |

**No new infrastructure** — no enum extension, no migration, no Shared.Kernel change. Wave 7's `AuditAction.Denied` + `AuditAction.Failed` + migration 0029 already cover all 4 new decorators' audit-action needs.

## Migration Path

**No schema migration required.** The decorator pattern is additive — `audit.events` table already exists (Wave 6 migration 0027). Wave 7's migration 0029 already covers `Denied = 4` + `Failed = 5`. No new columns. No new table. No new index.

**No data migration required.** The `audit.events` table starts receiving `AIRiskAdvice` / `CoachingPrompt` / `ScannerFilter` / `TradeAttachment` rows from the moment the deployment completes (slice 9a.1, 9a.2, 9a.3 each adds 1+ decorator). Historical mutations (pre-Wave 9) are not backfilled.

**Retention is purely a DELETE.** No retention column, no partition strategy, no TTL. The first retention run happens 2 minutes after the first deploy of slice 9b.1.

**1 surface change**: `IScannerFilterRepository` gains a defensive `DeleteAsync` stub that throws `NotSupportedException`. Verified via `git grep` BEFORE the extension — expected 0 call sites in production handlers.

## Affected Areas Summary

| Area | Impact | Slice |
|---|---|---|
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AIRiskAdviceAuditDecorator.cs` | New | 9a.1 |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/CoachingPromptAuditDecorator.cs` | New | 9a.1 |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/ScannerFilterAuditDecorator.cs` | New | 9a.2 |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AttachmentSweepAuditDecorator.cs` | New | 9a.3 |
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/IScannerFilterRepository.cs` | Modified | 9a.2 |
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/ITradeAttachmentUsageRepository.cs` | Modified (XML doc) | 9b.2 |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs` | Modified | 9a.1 + 9a.2 + 9a.3 |
| `src/2.Modules/Admin/JadeCapital.Admin.Application/Abstractions/IAuditEventQueryStore.cs` | New | 9b.1 |
| `src/2.Modules/Admin/JadeCapital.Admin.Application/Features/Audit/ListAuditEvents/*` | New | 9b.1 |
| `src/2.Modules/Admin/JadeCapital.Admin.Infrastructure/Persistence/AuditEventQueryStore.cs` | New | 9b.1 |
| `src/2.Modules/Admin/JadeCapital.Admin.Infrastructure/DependencyInjection/AdminModuleRegistration.cs` | New | 9b.1 |
| `src/2.Modules/Admin/JadeCapital.Admin.Infrastructure/JadeCapital.Admin.Infrastructure.csproj` | Modified | 9b.1 |
| `src/2.Modules/Admin/JadeCapital.Admin.Api/Endpoints/AdminAuditEndpoints.cs` | New | 9b.1 |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/IAuditRetentionService.cs` | New | 9b.1 |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/AuditRetentionService.cs` | New | 9b.1 |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/AuditRetentionBackgroundService.cs` | New | 9b.1 |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/Configuration/AuditRetentionOptions.cs` | New | 9b.1 |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/DependencyInjection/IdentityModuleRegistration.cs` | Modified | 9b.1 |
| `src/2.Modules/Host/JadeCapital.Host/Program.cs` | Modified | 9b.1 |
| `appsettings.json` | Modified | 9b.1 |
| 4 new test files in `tests/UnitTests/JadeCapital.Trading.UnitTests/Persistence/` | New | 9a.1 + 9a.2 + 9a.3 |
| 3 new test files in `tests/UnitTests/JadeCapital.Admin.UnitTests/` + 1 endpoint test | New | 9b.1 |
| 2 new test files in `tests/UnitTests/JadeCapital.Identity.UnitTests/Audit/` | New | 9b.1 |
| `openspec/specs/soft-delete-audit/spec.md` (main, archive-time merge) | Modified | sdd-archive |

## Out of Scope (deferred to Wave 10+)

- Admin write-back API for `audit.events` (POST to annotate, PATCH to tag for compliance).
- `AuditAction.Restored` end-to-end support (soft-delete restore command + admin tooling).
- Soft-delete cascade propagation beyond `ImportJob` for the 4 newly-decorated aggregates.
- Retention configuration UI for admin (per-tenant override, dashboard).
- Audit log export (CSV/JSON) for compliance officers.
- User-facing read API (`GET /api/audit/me`).
- Free-text search on `changes` JSONB.
- `audit.events` partitioning strategy (Postgres native partitioning by `tenant_id` or month).
- Initial backlog drain one-shot script (for tenants with > 1M accumulated rows at first deploy).
- Per-tenant retention override (`AuditRetentionOptions.PerTenantRetentionDays` dictionary).
- Migration to a different audit sink (Kafka, S3, external SIEM).
- Cross-tenant audit log access (today: tenant-isolated; future: admin tooling for cross-tenant forensics).
- Bulk audit events for `AddRangeAsync` (currently 1 row per entity; bulk path emits 1 batch row with `EntityCount = N`).
