# Exploration — Wave 9 Audit Finalization

**Change**: `2026-08-19-wave9-audit-finalization`
**Branch**: `feature/0a-identity-model` @ `4f54013` (Wave 8 just archived)
**Stack**: ASP.NET Core 10 / .NET SDK 10.0.400 (via `mise exec -- dotnet …`)
**Mode**: hybrid (OpenSpec + engram); explore is read-only (Strict TDD does not apply to research)

---

## Executive Scope

Wave 9 has three sub-scopes:

| Sub-scope | Description | Verdict |
|---|---|---|
| **A** | Audit decorators on the 5 remaining `src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/` repositories | 4 audited + 1 skipped = 5 decorators total |
| **B** | Admin-only `GET /api/admin/audit/events` query API (paginated + filterable) | NEW feature |
| **C** | 90-day audit retention + auto-purge `AuditRetentionBackgroundService` | NEW feature |

**Effective scope**: 4 new audit decorators + 1 documented SKIP + 2 new features (query API + retention). Total LOC forecast: **~2,250 LOC**, **~32 paths**, **~25 tests**.

The 5 Trading repositories are the "almost there" remainder from Wave 8's 7-decorator expansion. They were intentionally deferred because (a) Wave 5/6/7 had already shipped 8 decorators; (b) Wave 8 picked the highest-priority 7 of 9 user-owned candidates; (c) the 5 Trading repos named here include 2 write-once AI repos (`IAIRiskAdvice` + `ICoachingPrompt`), 1 user-configurable catalog (`IScannerFilter`), 1 system-internal batch sweeper (`IAttachmentSweep`), and 1 read-only aggregate (`ITradeAttachmentUsage`).

---

## Current State

### What Wave 8 shipped (the inheritance)

Wave 8 closed the audit decorator rollout to **15 user-owned aggregates** (`Tenant`, `ImportJob`, `Subscription`, `User`, `Strategy`, `Trade`, `RiskProfile`, `JournalEntry`, `TradeReview`, `PlannerSession`, `PreTradeChecklist`, `Account`, `Instrument`, `Alert`, `StripeCustomer`) + 2 documented SKIPs (`ISubscriptionAdminRepository`, `IStripeWebhookEventRepository`):

- **Generic helper**: `DecoratedRepository<T>` at `src/3.Shared/JadeCapital.Shared.Infrastructure/Persistence/DecoratedRepository.cs` (319 LOC). Wraps `IRepository<T>` (4 methods). `IsTerminated` reflection upgrades `UpdateAsync` → `AuditAction.Deleted` when `IsDeleted == true` OR `Status ∈ {Cancelled, Terminated, Expired}`.
- **Typed decorators** co-located per module: 3 in Identity (Tenant, User, RiskProfile), 7 in Trading (ImportJob, Strategy, Trade, JournalEntry, Account, Instrument, Alert, TradeReview, PlannerSession, PreTradeChecklist), 2 in Billing (Subscription, StripeCustomer). All implement the typed interface directly.
- **Cross-tenant `IsOwner` check** is the Wave 7/8 norm: every typed decorator compares `entity.UserId` to `ITenantContext.CurrentUserId` before allowing mutation. On mismatch: `AuditAction.Denied` audit row + `UnauthorizedAccessException`.
- **`AuditAction.Denied = 4` + `AuditAction.Failed = 5`** added in Wave 7; migration 0029 widened `ck_audit_events_action` to `IN (0,1,2,3,4,5)`.
- **`AuditLogger`** writes to dedicated `AuditDbContext` (write-only, isolated from `IdentityDbContext`). The `audit.events` table has 8 columns + 3 indexes + 1 CHECK constraint (migration 0027). Schema = `audit`, not `identity`.
- **Scrutor** 4.2.2 wired across Trading + Billing + Identity (Wave 7 7a.0). All `services.Decorate<IXxxRepository, XxxAuditDecorator>()` calls follow the canonical registration pattern in `TradingModuleRegistration.cs` and `BillingModuleRegistration.cs`.

### Pattern templates Wave 9 will replicate

| Template | Path | Shape |
|---|---|---|
| Canonical typed decorator (write-once) | `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Audit/StripeCustomerAuditDecorator.cs` (~250 LOC) | Only `AddAsync` audited; reads forwarded; `IsOwner` on AddAsync; aggregate immutable post-Create |
| Bespoke typed decorator (CRUD without Delete) | `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AccountAuditDecorator.cs` (160 LOC) | Generic `DecoratedRepository<Account>` wrapper; `IRepository<Account>` extension; cross-tenant `IsOwner` on `UpdateAsync` |
| Bespoke batch mutation decorator | (NEW — Wave 9 9a.3) | Wraps `SoftDeleteBatchAsync(IReadOnlyList<Guid>, ct)`; emits `AuditAction.Deleted` per id; cross-tenant `IsOwner` per id |

### Operational surface already in place

- **`BackgroundService` pattern** (Identity + Trading precedents):
  - `RefreshTokenCleanupService` (Identity.Infrastructure) — loop with configurable interval + `ExecuteDeleteAsync` + batch limit + per-attempt `try/catch` isolation.
  - `BackfillTenantsHostedService` (Identity.Infrastructure) — one-shot at startup + per-run isolation.
  - `AttachmentLifecycleService` (Trading.Infrastructure) — daily tick with jitter + per-user / per-attachment isolation.
  - `CoachingPromptService` (Trading.Infrastructure) — daily tick with jitter.
- **Admin module** (`src/2.Modules/Admin/`) has skeleton scaffolding: `Admin.Api/Endpoints/AdminSubscriptionEndpoints.cs` (192 LOC, the template for `RequireAuthorization("AdminOnly")` + `MapGroup("/api/admin/...")` + `Results.Problem(...)` + `ISender.Send(query)` pattern); `Admin.Api/Authorization/RequireAdminPolicyHandler.cs` (58 LOC, the explicit Admin role check); `AddAdminOnly()` in `Identity.Api/IdentityApiRegistration.cs`. The Admin module's Application + Infrastructure csprojs exist but the folders are empty (only `bin` + `obj` + csproj — no code yet).
- **`audit.events` query surface**: NONE exists today. `grep -rn "audit.events"` returns ONLY write-side references (AuditLogger, AuditEventConfiguration, decorator docstrings). No `IAuditEventQueryStore`, no `IAuditEventReader`, no `GET /api/audit/...` endpoint. The audit.events table is write-only via `AuditDbContext` (the DbContext exposes `DbSet<AuditEvent> AuditEvents` but no read-side handler exists).
- **Migration**: `audit.events` schema is migration 0027 (idempotent CREATE TABLE IF NOT EXISTS + CREATE INDEX IF NOT EXISTS + CHECK constraint + COMMENT ON). Migration 0029 widened `ck_audit_events_action` to `IN (0,1,2,3,4,5)`. No retention table or column exists; retention is purely a `DELETE FROM audit.events WHERE occurred_at < cutoff` operation.

---

## Affected Areas (5 candidate repos — current surface)

| Interface | Inherits `IRepository<T>`? | Mutation surface | ISoftDelete | Status enum (for `IsTerminated`) | EF query filter? | Concrete impl |
|---|---|---|---|---|---|---|
| `IAIRiskAdviceRepository` | **No** | `AddAsync(AIRiskAdvice)` only — aggregate immutable post-Create | No | No | none | `AIRiskAdviceRepository` ✓ (in `Persistence/`) |
| `IAttachmentSweepRepository` | **No** | `SoftDeleteBatchAsync(IReadOnlyList<Guid>)` (mutation!), `InsertAuditAsync(...)` (writes own audit table — system-internal) | n/a (operates on `TradeAttachment`) | n/a | none | `AttachmentSweepRepository` ✓ |
| `ICoachingPromptRepository` | **No** | `AddAsync(CoachingPrompt)` only — aggregate immutable post-Create | No | No | none | `CoachingPromptRepository` ✓ (in `Persistence/`) |
| `IScannerFilterRepository` | **No** | `AddAsync(ScannerFilter)` + `UpdateAsync(ScannerFilter)` — no `DeleteAsync` (deactivate via `IsActive = false` instead) | No (uses `IsActive bool`) | No | none | `ScannerFilterRepository` ✓ |
| `ITradeAttachmentUsageRepository` | **No** | **No mutation methods** — only `GetUsageAsync(Guid userId, ct)` returning `(long TotalBytes, int Count)` | n/a | n/a | none | `TradeAttachmentUsageRepository` ✓ |

### Entity docstring scan (cross-user / per-user / append-only)

| Entity | Docstring says |
|---|---|
| `AIRiskAdvice.cs:32-37` | "Immutability: the aggregate has no public setters. EF rehydration uses the Rehydrate factory which is reserved for the repository." |
| `TradeAttachment.cs:70-80` | `IsActive` bool (sweep flips to false); `SweptAt` timestamp; soft-delete only. |
| `CoachingPrompt.cs:32-37` | "Immutability: the aggregate has no public setters. EF rehydration uses the Rehydrate factory." |
| `ScannerFilter.cs` | "Named set of filters the trader saves to run scans against the instrument universe." Has `Activate(IClock)` + `Deactivate(IClock)` mutators + `Update(...)` mutator + `IsActive bool`. User-owned (UserId FK). |
| (none for `ITradeAttachmentUsageRepository` — it's a query projection, not an aggregate) | n/a |

### DI registration snapshot (current — pre-Wave 9)

```csharp
// TradingModuleRegistration.cs (relevant excerpts)
services.AddScoped<IScannerFilterRepository, ScannerFilterRepository>();           // line 56
services.AddScoped<ITradeAttachmentUsageRepository, TradeAttachmentUsageRepository>(); // line 102
services.AddScoped<IAttachmentSweepRepository, AttachmentSweepRepository>();      // line 104
services.AddScoped<ICoachingPromptRepository, CoachingPromptRepository>();        // line 243
services.AddScoped<IAIRiskAdviceRepository, AIRiskAdviceRepository>();            // line 249
```

None of the 5 are decorated today. All 5 will need `services.Decorate<IXxxRepository, XxxAuditDecorator>()` registration per the Wave 7/8 precedent.

---

## Decisions per target repo

### 1. `IAIRiskAdviceRepository` — AUDIT (bespoke write-once)

**Pattern**: Bespoke write-once decorator mirroring `StripeCustomerAuditDecorator` (Wave 8 8b.1). The interface has only `AddAsync(AIRiskAdvice, ct)` + `FindByUserAndTradeAsync(...)` (read). The aggregate is immutable post-Create per the entity docstring (no `UpdateAsync` / `DeleteAsync` to wrap):

- Forward `FindByUserAndTradeAsync` — no audit (reads are never audited per Wave 6 + 7 + 8 precedent).
- `AddAsync(AIRiskAdvice, ct)` → `Created` audit row. Cross-tenant `IsOwner` check on `advice.UserId` against `ITenantContext.CurrentUserId`; on mismatch emit `AuditAction.Denied` + throw `UnauthorizedAccessException` (matches the Wave 8 8a.3 PreTradeChecklist IsOwner pattern).

**Interface surgery**: 0. The interface shape already supports the decorator (no rename, no additive overload needed).

**Forecast**: ~4 integration scenarios + 0 contract = **4 scenarios**.

### 2. `ICoachingPromptRepository` — AUDIT (bespoke write-once)

**Pattern**: Same as #1 — bespoke write-once decorator mirroring `StripeCustomerAuditDecorator` + the Wave 8 8a.3 PreTradeChecklist IsOwner pattern. The interface has 3 methods: `AddAsync` (mutation), `FindByUserAndDateAsync` (read), `ListByUserAndWindowAsync` (read). The aggregate is immutable post-Create.

- Forward the 2 read methods — no audit.
- `AddAsync(CoachingPrompt, ct)` → `Created` audit row. Cross-tenant `IsOwner` check on `prompt.UserId`; on mismatch emit `AuditAction.Denied` + throw `UnauthorizedAccessException`.

**Interface surgery**: 0. Shape already supports the decorator.

**Forecast**: ~4 integration scenarios + 0 contract = **4 scenarios**.

### 3. `IScannerFilterRepository` — AUDIT (bespoke CRUD without Delete)

**Pattern**: Bespoke typed decorator mirroring `AccountAuditDecorator` (Wave 8 8a.1) but without `DeleteAsync`. The interface has 3 read methods (`GetByIdAsync`, `GetByUserAndNameAsync`, `ListByUserAsync`) + 2 mutation methods (`AddAsync`, `UpdateAsync`). ScannerFilter has no `DeleteAsync` (delete is done via `Deactivate(IClock)` mutator which sets `IsActive = false`).

The interface does NOT inherit `IRepository<T>` — the canonical generic shape requires `DeleteAsync(T, ct)` which ScannerFilter forbids. Bespoke decorator is mandatory.

- Forward 3 read methods — no audit.
- `AddAsync(ScannerFilter, ct)` → `Created` audit row. `IsOwner` check on `filter.UserId`.
- `UpdateAsync(ScannerFilter, ct)` → `Updated` audit with before/after diff (via EF `ChangeTracker.OriginalValues` + JSON diff fallback). `IsOwner` check on `filter.UserId`; on mismatch emit `AuditAction.Denied` + throw `UnauthorizedAccessException`.
- **NO `DeleteAsync` to wrap** — the contract forbids hard-delete. The `IsActive = false` transition via `Deactivate(IClock)` happens INSIDE `UpdateAsync` and is captured as a normal `Updated` event (no `IsTerminated` reflection rule matches `ScannerFilter.IsActive`).

**Interface surgery**: 0. Shape already supports the decorator.

**Forecast**: ~5 integration scenarios + 0 contract = **5 scenarios**.

### 4. `IAttachmentSweepRepository` — AUDIT (bespoke batch soft-delete)

**Pattern**: NEW bespoke decorator template — wraps a batch soft-delete method, not the canonical single-entity shape. The interface has 5 methods: 3 reads (`GetExpiredBatchAsync`, `GetUserAggregateAsync`, `GetActiveUserIdsAsync`) + 2 mutations (`SoftDeleteBatchAsync(IReadOnlyList<Guid>, ct)`, `InsertAuditAsync(...)`).

`SoftDeleteBatchAsync` soft-deletes a batch of `TradeAttachment` rows by setting `IsActive = false` via the domain method `TradeAttachment.MarkSwept(now)`. This IS a user-impacting mutation (the user's attachments are deleted). The aggregate docstring confirms it: "Soft-delete flag. The AttachmentLifecycleService daily sweep flips this to false when ExpiresAt < now()."

`InsertAuditAsync` writes to `trading.attachments_quota_audit` — a SEPARATE audit table owned by the sweep itself. This is system-internal bookkeeping, not a user mutation. The recommendation per the orchestrator's preflight: **DO NOT emit `audit.events` rows for `InsertAuditAsync`** (that table IS the audit log for the sweep; emitting on top of it would be doubly-recorded noise — mirrors the Wave 8 8b.2 StripeWebhookEvent SKIP rationale).

The decorator implements `IAttachmentSweepRepository` directly:

- Forward 3 read methods — no audit.
- `SoftDeleteBatchAsync(IReadOnlyList<Guid> ids, ct)` → emit `AuditAction.Deleted` audit row **per id** (one audit row per attachment in the batch). The `EntityType = "TradeAttachment"`, `EntityId = id`, `ChangesJson = { "IsActive": { "before": true, "after": false } }`. Cross-tenant `IsOwner` check on `attachment.UserId` (loaded via the same EF tracked instance the inner already loaded for the MarkSwept call); on any cross-tenant id: emit `AuditAction.Denied` + throw `UnauthorizedAccessException` (matches the Wave 8 8a.3 PreTradeChecklist IsOwner pattern).
- `InsertAuditAsync(...)` → forward without audit (the `attachments_quota_audit` row IS the audit log for the sweep; emitting `audit.events` rows on top would be doubly-recorded noise — matches the Wave 8 8b.2 StripeWebhookEvent SKIP precedent).

**CRITICAL**: `SoftDeleteBatchAsync` is called by `AttachmentLifecycleService.RunOnceAsync` — the only caller. The decorator wraps the inner repo's existing implementation; the BackgroundService doesn't need to change.

**Interface surgery**: 0. Shape already supports the decorator (no rename, no additive overload).

**Forecast**: ~5 integration scenarios + 0 contract = **5 scenarios**.

### 5. `ITradeAttachmentUsageRepository` — **SKIP**

**Rationale**: The interface has **NO mutation methods** — only `GetUsageAsync(Guid userId, ct)` returning `(long TotalBytes, int Count)`. The repository is a pure read-side aggregate query (a `SUM(bytes) + COUNT(*)` projection over `trade_attachments`).

Adding an audit decorator on `ITradeAttachmentUsageRepository` would be a no-op (no `AddAsync` / `UpdateAsync` / `DeleteAsync` to decorate) — at most it would forward reads without audit. The right answer: **skip with documented rationale**.

**Skip with documented rationale** via XML `<remarks>` on the interface (mirrors Wave 8 8b.2 SKIP pattern for `ISubscriptionAdminRepository` + `IStripeWebhookEventRepository`). No code change; no test added.

**Alternative considered + rejected**: Audit `TradeAttachment` aggregate mutations via `IAttachmentSweepRepository.SoftDeleteBatchAsync` (sub-scope A #4 above). Rejected because the right seam is the sweep repo (user-impacting soft-delete happens there), not the usage repo.

---

## Sub-scope B: Admin `GET /api/admin/audit/events` query API

### Current state (the gap)

- **`audit.events` is write-only today.** `AuditDbContext` exposes `DbSet<AuditEvent> AuditEvents` but no read-side handler, no `IAuditEventQueryStore`, no admin endpoint.
- **No admin endpoint** matches `/api/audit/events` or `/api/admin/audit/events` today (confirmed via `grep -rn "/api/audit"`).
- **Admin module** (`src/2.Modules/Admin/`) has:
  - `Admin.Api/Endpoints/AdminSubscriptionEndpoints.cs` (192 LOC) — the canonical template for `MapGroup("/api/admin/...")` + `RequireAuthorization("AdminOnly")` + `Results.Problem(...)` + `ISender.Send(query)`.
  - `Admin.Api/Authorization/RequireAdminPolicyHandler.cs` (58 LOC) — explicit `User.IsInRole("Admin")` check.
  - `Admin.Api/JadeCapital.Admin.Api.csproj` + `Admin.Application/Admin.Application.csproj` + `Admin.Infrastructure/Admin.Infrastructure.csproj` — 3 csproj files exist, but Application + Infrastructure folders are empty (only `bin/`, `obj/`, csproj).

### Recommended API shape

```
GET /api/admin/audit/events?entity_type=&action=&user_id=&tenant_id=&from=&to=&cursor=&limit=
```

Filters (all optional, AND-combined):
- `entity_type` — string (e.g. `ImportJob`, `Tenant`). Maps to `audit.events.entity_type` (= `AuditEvent.EntityType`). Uses `ix_audit_events_entity`.
- `action` — short enum value (0=Created, 1=Updated, 2=Deleted, 3=Restored, 4=Denied, 5=Failed). Maps to `audit.events.action`.
- `user_id` — Guid. Maps to `audit.events.user_id`. Uses `ix_audit_events_user`.
- `tenant_id` — Guid. Maps to `audit.events.tenant_id`. Uses `ix_audit_events_tenant_time`.
- `from`, `to` — ISO 8601 timestamps. Half-open `[from, to)`. Uses `ix_audit_events_tenant_time` (when `tenant_id` filter is present; otherwise a full scan over the partition).
- `cursor` — opaque pagination cursor (recommended: `(occurred_at DESC, id DESC)` keyset).
- `limit` — page size (default 50, max 200).

Response shape:

```json
{
  "items": [
    {
      "id": "uuid",
      "entity_type": "Trade",
      "entity_id": "uuid",
      "action": "Updated",
      "tenant_id": "uuid",
      "user_id": "uuid",
      "changes": { ... JSONB ... },
      "occurred_at": "2026-08-19T12:34:56Z"
    }
  ],
  "next_cursor": "base64(...)",
  "has_more": true
}
```

### Module placement: Admin.Api

**Recommendation**: place the new endpoint in `JadeCapital.Admin.Api/Endpoints/AdminAuditEndpoints.cs`. Justification:
1. **Admin role gate already exists** — `RequireAuthorization("AdminOnly")` + `RequireAdminPolicyHandler` deny-by-default (no existence / data leak before MediatR dispatch — matches the Wave 8 8b.2 StripeCustomer precedent + AdminSubscriptionEndpoints template).
2. **Module csprojs exist** — `Admin.Api`, `Admin.Application`, `Admin.Infrastructure` are already referenced from `JadeCapital.Host/Program.cs` (lines 131, 133, 151 — Admin handlers + admin write-path repos registered). The endpoint just needs to land in the empty `Admin.Api/Endpoints/` folder + a handler in `Admin.Application/Features/Audit/...` + a query store in `Admin.Infrastructure/Persistence/`.
3. **PII concerns**: `user_id` + `tenant_id` are visible to Admin role only. The `AdminOnly` policy + handler enforces this BEFORE any DB lookup — no info leak about user existence, tenant scope, or audit-event content.
4. **Identity.Api is for trader-facing endpoints** (auth, risk-profile, tenant). Audit logs are operational / compliance tooling, not trader UX — they belong in Admin.
5. **Alternative rejected**: `Identity.Api` (cross-cutting concerns like `IAuditLogger` are in Identity.Infrastructure, but the audit query surface is admin-only UX, not identity UX).

### Read-side architecture

- **New interface**: `IAuditEventQueryStore` in `JadeCapital.Admin.Application/Abstractions/` (admin-side abstraction; keeps the Admin module independent of Identity's internal `AuditDbContext`).
- **EF impl**: `AuditEventQueryStore` in `JadeCapital.Admin.Infrastructure/Persistence/` — depends on `AuditDbContext` (which already lives in Identity.Infrastructure and is registered in DI). The cross-module reference is OK: `Admin.Infrastructure` already references `Identity.Infrastructure` transitively (the csproj chain via `JadeCapital.Host.csproj` + the `Admin.Api.csproj` reference list).
- **Handler**: `ListAuditEventsQuery` + `ListAuditEventsHandler` in `Admin.Application/Features/Audit/ListAuditEvents/`.
- **Endpoint**: `MapAdminAuditEndpoints()` extension in `Admin.Api/Endpoints/AdminAuditEndpoints.cs`.
- **DI wiring**: `services.AddScoped<IAuditEventQueryStore, AuditEventQueryStore>()` + handler registration in `AdminModuleRegistration` (NEW csproj pattern — the file doesn't exist yet, but the csproj does; Wave 9 9b.1 creates it).

### Performance + pagination strategy

- **`audit.events` will grow unbounded** (no retention today). Without Wave 9's retention sub-scope C, the table grows by ~N mutations/day across all user-owned aggregates. For 100 users × 50 mutations/day = 5000 rows/day = ~1.8M rows/year. Cursor-based pagination is mandatory.
- **Cursor format**: `base64("{occurred_at_ticks}:{id_guid}")`. Server filters `WHERE (occurred_at, id) < (cursor.occurred_at, cursor.id) ORDER BY occurred_at DESC, id DESC LIMIT $limit + 1`. The `+1` row determines `has_more`.
- **Index coverage**: The 3 existing indexes (`ix_audit_events_entity`, `ix_audit_events_tenant_time`, `ix_audit_events_user`) cover the 4 single-filter queries. The compound filter (e.g. `entity_type = X AND user_id = Y AND occurred_at > Z`) will use the most selective single index; Postgres will bitmap-AND. For 1M+ row tables, add a covering index in a follow-up migration if profiling shows it.
- **`changes` JSONB**: full payload returned in the response (already a small column — typical diff is < 2 KB). No redaction at the API layer (admin role sees all fields per the AdminOnly policy).

### PII + compliance considerations

- **Admin-only access**: enforced at endpoint boundary via `RequireAuthorization("AdminOnly")` + `RequireAdminPolicyHandler`. No trader / anonymous access ever.
- **PII exposure**: `user_id` + `tenant_id` are internal Guids (not PII like email/name); the `changes` JSONB may contain user-owned entity data (e.g. trade price, journal text) — admin role sees all of it. This is the explicit compliance contract: admin can see all audit data.
- **No logging of audit queries themselves**: the admin's query of audit data does NOT generate an audit row (would be recursive). The AdminActor is captured in the existing Serilog request log (request URI + JWT subject) for accountability.
- **Rate limiting**: `RequireRateLimiting("api-general")` matches the Wave 8 8b.1 AdminSubscriptionEndpoints pattern (no special quota for audit queries — they're not the hot path).

---

## Sub-scope C: 90-day audit retention + auto-purge

### Current state

- **No retention exists today.** `audit.events` grows unbounded. Migration 0027 has no retention column, no partition strategy, no TTL.
- **Pattern precedent**: `RefreshTokenCleanupService` (Identity.Infrastructure, 100 LOC) — uses EF Core 9 `ExecuteDeleteAsync` with batch limit + configurable interval + per-attempt try/catch isolation.
- **Wave 6 / Wave 7 / Wave 8 design docs all explicitly defer retention to Wave 9** — confirmed via `grep -rn "Wave 9\|retention"` in the archive folder.

### Recommended implementation

- **New service**: `IAuditRetentionService` in `JadeCapital.Identity.Application/Abstractions/` (cross-cutting concern; Identity owns the audit infrastructure per Wave 6 6d.1).
- **EF impl**: `AuditRetentionService` in `JadeCapital.Identity.Infrastructure/Audit/`.
- **Hosted service**: `AuditRetentionBackgroundService : BackgroundService` in `JadeCapital.Identity.Infrastructure/Audit/` (co-located with `AuditLogger` + `AuditDbContext` per the Wave 6 6d.2 + Wave 7 7a.1 audit-folder convention).
- **Config**: `AuditRetentionOptions` in `JadeCapital.Identity.Infrastructure/Audit/Configuration/`:

```csharp
public sealed class AuditRetentionOptions
{
    public int RetentionDays { get; set; } = 90;             // default 90
    public int CleanupIntervalHours { get; set; } = 24;     // daily tick
    public int BatchLimit { get; set; } = 10000;             // per-batch delete cap
    public TimeSpan? InitialDelay { get; set; } = TimeSpan.FromMinutes(2); // startup settle
}
```

Binding: `services.Configure<AuditRetentionOptions>(Configuration.GetSection("AuditRetention"))` in `IdentityModuleRegistration.cs`. Config keys in `appsettings.json`:

```json
{
  "AuditRetention": {
    "RetentionDays": 90,
    "CleanupIntervalHours": 24,
    "BatchLimit": 10000
  }
}
```

### SQL approach

```csharp
// AuditRetentionService.PurgeOldAsync
public async Task<int> PurgeOldAsync(DateTimeOffset cutoff, int batchLimit, CancellationToken ct)
{
    var deleted = await _db.AuditEvents
        .Where(e => e.OccurredAt < cutoff)
        .Take(batchLimit)
        .ExecuteDeleteAsync(ct);
    return deleted;
}
```

EF Core 9 `ExecuteDeleteAsync` translates to a single DELETE statement (`DELETE FROM audit.events WHERE id IN (SELECT id FROM audit.events WHERE occurred_at < $1 LIMIT $2)` — or the optimized single-statement form). The `Take(batchLimit)` caps the rows per call; the BackgroundService loops until 0 rows deleted (idempotent).

**Why idempotent**: re-running the loop with the same cutoff is a no-op (rows already deleted match no rows in the next iteration). The `BatchLimit` ensures no single transaction holds a long lock; the daily interval ensures the cumulative delete per day stays well under the table's write rate.

### Schedule + isolation

- **First run**: 2 minutes after startup (Identity hosts the audit infrastructure — gives the rest of the pipeline time to settle, matching the `BackfillTenantsHostedService` 15s precedent scaled for a heavier first run).
- **Subsequent runs**: every `CleanupIntervalHours` (24h default) with `[0, +30min]` jitter to avoid thundering herd across replicas (matches `AttachmentLifecycleService` precedent).
- **Per-run isolation**: `try { PurgeOldAsync + log } catch { LogError + continue }` — never crash the host (matches `RefreshTokenCleanupService` precedent).
- **Logging**: `LogInformation` when rows deleted > 0; `LogDebug` when 0 rows deleted.

### No schema change

- The retention is purely a DELETE — no new column, no new table, no migration. The 0027 migration's `CREATE TABLE IF NOT EXISTS` already declared the schema; the 0029 migration widened the CHECK constraint.
- The `audit.events` table has no FK relationships (the `entity_id` is not an FK to enforce referential integrity — the audit log is intentionally denormalized for append-only semantics). The DELETE is safe.

---

## Pattern decisions summary

### Decorator count + shape

| Slice | Decorator | Pattern | LOC estimate |
|---|---|---|---|
| 9a.1 | `AIRiskAdviceAuditDecorator` | Bespoke write-once (mirror StripeCustomer) | ~120 LOC |
| 9a.1 | `CoachingPromptAuditDecorator` | Bespoke write-once (mirror StripeCustomer) | ~120 LOC |
| 9a.2 | `ScannerFilterAuditDecorator` | Bespoke CRUD without Delete (mirror AccountAuditDecorator without Delete) | ~180 LOC |
| 9a.3 | `AttachmentSweepAuditDecorator` | NEW bespoke batch soft-delete (wraps `SoftDeleteBatchAsync`) | ~150 LOC |

**Total decorator LOC**: ~570 LOC. Tests + DI wiring + comments add another ~1,000 LOC. **Sub-scope A total LOC: ~1,570 LOC**.

### Sub-scope B + C LOC

| Slice | File | LOC estimate |
|---|---|---|
| 9b.1 | `IAuditEventQueryStore` + `AuditEventQueryStore` (EF impl) | ~150 LOC |
| 9b.1 | `ListAuditEventsQuery` + `ListAuditEventsHandler` + DTOs | ~150 LOC |
| 9b.1 | `AdminAuditEndpoints` (endpoint mapping + handler) | ~120 LOC |
| 9b.1 | `IAuditRetentionService` + `AuditRetentionService` + `AuditRetentionBackgroundService` + `AuditRetentionOptions` | ~200 LOC |
| 9b.1 | Tests (handler + endpoint + retention service + background service) | ~300 LOC |
| 9b.1 | DI wiring + config | ~80 LOC |

**Sub-scope B + C total LOC: ~1,000 LOC**.

### Sub-scope A: bespoke vs. generic per repo

| Repo | Pattern | Why |
|---|---|---|
| AIRiskAdvice | **Bespoke** (write-once) | Only `AddAsync` + 1 read; aggregate immutable post-Create; extending `IRepository<T>` would force `UpdateAsync` + `DeleteAsync` that the contract forbids |
| CoachingPrompt | **Bespoke** (write-once) | Same as AIRiskAdvice |
| ScannerFilter | **Bespoke** (CRUD without Delete) | Interface lacks `DeleteAsync` (deactivate via `IsActive = false`); the canonical `IRepository<T>` requires `DeleteAsync(T, ct)` |
| AttachmentSweep | **Bespoke** (batch soft-delete) | NEW pattern — `SoftDeleteBatchAsync(IReadOnlyList<Guid>, ct)` emits 1 audit row per id; cross-tenant `IsOwner` per id; doesn't fit `IRepository<T>` |
| TradeAttachmentUsage | **SKIP** | Read-only repo with no mutations to audit |

### Wave 6/7/8 patterns reused (no new patterns introduced)

- Generic `DecoratedRepository<T>` from `Shared.Infrastructure` (Wave 7 7a.0) — NOT used in Wave 9 (all 4 audited repos are bespoke-shape).
- Cross-tenant `IsOwner` check (Wave 6 6d.2 + Wave 7 7a.1 + Wave 8 8a.3) — used in all 4 new decorators.
- `AuditAction.Denied` + `AuditAction.Failed` enum values (Wave 7 7a.1; migration 0029) — used in all 4 new decorators.
- Bespoke `DeleteAsync` defensive stub for non-deletable aggregates (Wave 7 7a.1) — NOT needed (none of the 4 audited aggregates expose Delete).
- `ResolveBefore` via EF `ChangeTracker.OriginalValues` with JSON snapshot fallback (Wave 7 7b.1 + 7b.2) — used in ScannerFilter only (the only Update-emitting decorator).
- Batch soft-delete with per-row `MarkSwept` domain method (Wave 4 4d AttachmentLifecycleService) — wrapped by `AttachmentSweepAuditDecorator`.
- Admin endpoint pattern (Wave 0 0f `AdminSubscriptionEndpoints`) — replicated for `AdminAuditEndpoints`.
- `RequireAuthorization("AdminOnly")` + `RequireAdminPolicyHandler` (Wave 0 0f + Wave 8 8b.1) — replicated.
- `BackgroundService` with jitter + per-attempt isolation + `ExecuteDeleteAsync` batch limit (Wave 4 4d + Wave 5 5a + Wave 6 6c.2 + Wave 6 6e) — replicated for `AuditRetentionBackgroundService`.

**No new infrastructure** — no enum extension, no migration, no Shared.Kernel change. Wave 7's `AuditAction.Denied` + `AuditAction.Failed` + migration 0029 already cover all 4 new decorators' audit-action needs.

---

## Slicing strategy

### Forecast summary

Per Wave 7 verify-report SUGGESTION #2 (`forecast = integration_scenarios + 2 * contract_scenarios_per_interface_surgery`):

| Slice | Decorators / Features | Integration scenarios | Contract scenarios | Surgery | Total |
|---|---|---:|---:|---:|---:|
| **9a.1** | AIRiskAdvice + CoachingPrompt (write-once) | 8 | 0 | 0 | **8** |
| **9a.2** | ScannerFilter (bespoke CRUD without Delete) | 5 | 0 | 0 | **5** |
| **9a.3** | AttachmentSweep (bespoke batch soft-delete) | 5 | 0 | 0 | **5** |
| **9b.1** | Audit query API + AuditRetention BackgroundService | 10 | 0 | 0 | **10** |
| **9b.2** | Reconciliation doc — SKIP TradeAttachmentUsage with documented rationale | 0 | 2 (rationale tests / source-inspection) | 0 | **2** |
| **Total** | 4 audited decorators + 2 features + 1 SKIP | 28 | 2 | 0 | **30 scenarios / ~2,570 LOC** |

This is ~30 spec scenarios vs. the orchestrator's ~31 estimate. The delta is because (a) all 4 audited repos are bespoke shape (no `RemoveAsync → DeleteAsync` renames); (b) Wave 9 has no enum extension or migration (no migration scenarios).

### Proposed slice decomposition

#### Slice 9a.1 — Trading: AIRiskAdvice + CoachingPrompt (bespoke write-once) (~600 LOC, ~10 paths, ~8 tests)

- Phase 1: Decorators (TDD).
  - [ ] 1.1 RED: `AIRiskAdviceRepositoryIntegrationTests` (4 scenarios — Created on AddAsync, FindByUserAndTradeAsync is not audited, IsOwner cross-tenant check, no Update/Delete methods exist — contract pin).
  - [ ] 1.2 GREEN: `AIRiskAdviceAuditDecorator.cs` in `Trading.Infrastructure/Audit/`. Bespoke write-once. Only AddAsync wrapped; reads forwarded without audit. `IsOwner` check on `advice.UserId`.
  - [ ] 1.3 RED: `CoachingPromptRepositoryIntegrationTests` (4 scenarios — same shape as 1.1).
  - [ ] 1.4 GREEN: `CoachingPromptAuditDecorator.cs` in `Trading.Infrastructure/Audit/`. Bespoke write-once. Same shape as 1.2.
- Phase 2: DI wiring.
  - [ ] 2.1 `services.Decorate<IAIRiskAdviceRepository, AIRiskAdviceAuditDecorator>()`.
  - [ ] 2.2 `services.Decorate<ICoachingPromptRepository, CoachingPromptAuditDecorator>()`.
- Phase 3: Validate.

#### Slice 9a.2 — Trading: ScannerFilter (bespoke CRUD without Delete) (~450 LOC, ~8 paths, ~5 tests)

- Phase 1: Decorator (TDD).
  - [ ] 1.1 RED: `ScannerFilterRepositoryIntegrationTests` (5 scenarios — Created on AddAsync with IsOwner, Updated with before/after diff + IsOwner, ListByUserAsync/GetByIdAsync/GetByUserAndNameAsync are not audited, no DeleteAsync method exists — contract pin).
  - [ ] 1.2 GREEN: `ScannerFilterAuditDecorator.cs` in `Trading.Infrastructure/Audit/`. Bespoke. Implements `IScannerFilterRepository` directly. Wraps AddAsync + UpdateAsync via `DecoratedRepository<ScannerFilter>` (after `IScannerFilterRepository` is extended with `IRepository<ScannerFilter>` shape — note: the `DeleteAsync` requirement of `IRepository<T>` is a no-op stub that throws `NotSupportedException` per the Wave 7 7a.1 UserAuditDecorator precedent, since ScannerFilter forbids hard-delete).
  - [ ] 1.3 RED: Contract tests pinning the `DeleteAsync_NotOnInterface_SinceDeactivateOnly` behavior (the `IRepository<ScannerFilter>` extension's `DeleteAsync(ScannerFilter, ct)` is a defensive stub).
- Phase 2: DI wiring.
  - [ ] 2.1 `services.Decorate<IScannerFilterRepository, ScannerFilterAuditDecorator>()`.
- Phase 3: Validate.

**Note**: The `IRepository<ScannerFilter>` extension requires the interface to declare `GetByIdAsync(Guid, ct)` — currently it has `GetByIdAsync(Guid, ct)` (✓) but the decorator's `DecoratedRepository<ScannerFilter>` shape requires the canonical 4-method surface. Verify: `IScannerFilterRepository.GetByIdAsync` already exists. `AddAsync` + `UpdateAsync` exist. `DeleteAsync` is missing — added as a defensive stub throwing `NotSupportedException` + emitting `AuditAction.Failed` (matches the Wave 7 7a.1 UserAuditDecorator precedent).

#### Slice 9a.3 — Trading: AttachmentSweep (bespoke batch soft-delete) (~350 LOC, ~6 paths, ~5 tests)

- Phase 1: Decorator (TDD).
  - [ ] 1.1 RED: `AttachmentSweepRepositoryIntegrationTests` (5 scenarios — SoftDeleteBatchAsync emits Deleted per id with diff, SoftDeleteBatchAsync with cross-tenant id emits Denied + throws, GetExpiredBatchAsync/GetUserAggregateAsync/GetActiveUserIdsAsync are not audited, InsertAuditAsync is not audited — contract pin).
  - [ ] 1.2 GREEN: `AttachmentSweepAuditDecorator.cs` in `Trading.Infrastructure/Audit/`. Bespoke batch soft-delete. Wraps `SoftDeleteBatchAsync` only; emits one audit row per id. Cross-tenant `IsOwner` check on the loaded `attachment.UserId`. `InsertAuditAsync` forwarded without audit (per the doubly-recorded-noise rationale). 3 reads forwarded without audit.
- Phase 2: DI wiring.
  - [ ] 2.1 `services.Decorate<IAttachmentSweepRepository, AttachmentSweepAuditDecorator>()`.
- Phase 3: Validate.

#### Slice 9b.1 — Identity + Admin: Audit query API + retention BackgroundService (~1,000 LOC, ~12 paths, ~10 tests)

- Phase 1: Query store (TDD).
  - [ ] 1.1 RED: `AuditEventQueryStoreTests` (4 scenarios — ListAsync with no filters returns newest-first, ListAsync with `entity_type` filter, ListAsync with `user_id` + `tenant_id` compound filter, cursor pagination produces keyset-correct next page).
  - [ ] 1.2 GREEN: `IAuditEventQueryStore` in `Admin.Application/Abstractions/` + `AuditEventQueryStore` in `Admin.Infrastructure/Persistence/`. EF query against `AuditDbContext.AuditEvents`.
  - [ ] 1.3 DI wiring: `services.AddScoped<IAuditEventQueryStore, AuditEventQueryStore>()` in `AdminModuleRegistration`.
- Phase 2: Query handler + DTOs (TDD).
  - [ ] 2.1 RED: `ListAuditEventsHandlerTests` (3 scenarios — DTO mapping, cursor encoding/decoding, limit clamping).
  - [ ] 2.2 GREEN: `ListAuditEventsQuery` + `ListAuditEventsHandler` + `AuditEventDto` + `PagedAuditEventsDto` in `Admin.Application/Features/Audit/ListAuditEvents/`. Cursor = `base64("{occurred_at_ticks}:{id_guid}")`.
- Phase 3: Endpoint (TDD — WebApplicationFactory + Testcontainers).
  - [ ] 3.1 RED: `AdminAuditEndpointsIntegrationTests` (3 scenarios — Anonymous request returns 401, Trader role returns 403, Admin role with filters returns 200 + paginated payload).
  - [ ] 3.2 GREEN: `AdminAuditEndpoints.cs` in `Admin.Api/Endpoints/`. `MapGroup("/api/admin/audit/events").RequireAuthorization("AdminOnly").MapGet("/", ListAsync)`.
- Phase 4: Retention BackgroundService (TDD — frozen clock).
  - [ ] 4.1 RED: `AuditRetentionServiceTests` (3 scenarios — cutoff = now - retentionDays, idempotent re-run returns 0 rows, batch limit caps the delete).
  - [ ] 4.2 RED: `AuditRetentionBackgroundServiceTests` (2 scenarios — first run after InitialDelay, exception in RunOnceAsync is logged + does not crash host).
  - [ ] 4.3 GREEN: `IAuditRetentionService` + `AuditRetentionService` + `IAuditRetentionBackgroundService` (alias) + `AuditRetentionBackgroundService` + `AuditRetentionOptions` in `Identity.Infrastructure/Audit/`.
- Phase 5: DI + config wiring.
  - [ ] 5.1 `services.Configure<AuditRetentionOptions>(Configuration.GetSection("AuditRetention"))` in `IdentityModuleRegistration`.
  - [ ] 5.2 `services.AddHostedService<AuditRetentionBackgroundService>()`.
  - [ ] 5.3 `app.MapAdminAuditEndpoints()` in `Program.cs`.
- Phase 6: Validate.

#### Slice 9b.2 — Reconciliation doc (~50 LOC, ~2 paths, 0 tests)

Final reconciliation slice that documents the 1 SKIP with explicit rationale.

- [ ] 1.1 Verify `ITradeAttachmentUsageRepository` has no mutation methods: `git grep -E "Task (Add|Update|Delete)Async" src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/ITradeAttachmentUsageRepository.cs` → no matches.
- [ ] 1.2 Add the SKIP rationale to `IScannerFilterRepository.cs` (if applicable — it lacks `DeleteAsync` by design) + `ITradeAttachmentUsageRepository.cs` (read-only) + `openspec/changes/2026-08-19-wave9-audit-finalization/proposal.md` §"Out of Scope" + `specs/soft-delete-audit/spec.md` §"## REMOVED Requirements" (with `Reason:` block per OpenSpec convention).

**Forecast guard lines (per `sdd-phase-common.md` §E)**:

- Decision needed before apply: **Yes** (size:exception expected per Wave 5/6/7/8 precedent; explicit user acceptance per slice recommended).
- Chained PRs recommended: **Yes** (5 PRs via `feature-branch-chain`; each targets the immediate previous PR branch).
- 400-line budget risk: **High** — each slice will exceed the 400-line PR review budget per Wave 7 7a.1 (1412 LOC) / 7b.1 (1699 LOC) / Wave 8 8a.1 (~700 LOC) / 8b.1 (~700 LOC) precedent. `size:exception` per slice is the expected resolution (the `feature-branch-chain` strategy keeps each PR's reviewer-load bounded at the PR-level scope of work, not the cumulative chain scope).

### Test strategy

- **In-memory SQLite** for all integration tests (Wave 5/6/7/8 precedent; Testcontainers Postgres not in sandbox per verify-report carry-forward WARNING).
- **Per-decorator test fixture** mirrors the Wave 8 pattern: wire the typed `DbContext` + `AuditDbContext` + `IAuditLogger` + `ITenantContext` + `IClock`.
- **Endpoint tests** use `WebApplicationFactory` + Testcontainers Postgres (Wave 6 6f precedent). For 9b.1, the integration test depends on Docker/Postgres — same carry-forward WARNING as Wave 8; sandbox uses per-project test runs.
- **BackgroundService tests** use the Wave 4 4d `AttachmentLifecycleService.RunOnceAsync` exposed-for-tests pattern: `public async Task RunOnceAsync(CancellationToken ct)` on the hosted service so unit tests drive the loop deterministically.
- **Frozen clock** for retention: tests inject `FakeClock` (Wave 6 6d.2 precedent) and assert `PurgeOldAsync` uses the injected `IClock.UtcNow` to compute the cutoff.

---

## Constraints + Risks

1. **size:exception precedent**: Wave 5/6/7/8 all needed `size:exception` per slice. Expect Wave 9 to need it for each apply slice — explicit user acceptance per slice recommended. The 400-line PR review budget is exceeded by every Wave 7/8 slice.

2. **1 SKIP needs explicit rationale**: `ITradeAttachmentUsageRepository` — no mutation methods; pure read-side aggregate query. Wrapping would be a no-op. SKIP with documented rationale.

3. **`IScannerFilterRepository` extension to `IRepository<ScannerFilter>`**: requires adding a defensive `DeleteAsync(ScannerFilter, ct)` stub that throws `NotSupportedException` + emits `AuditAction.Failed` (matches the Wave 7 7a.1 UserAuditDecorator precedent). The `ScannerFilter` aggregate forbids hard-delete — deactivation is via `IsActive = false`. This adds 1 method to the interface + 1 contract test scenario.

4. **`AttachmentSweepAuditDecorator` NEW pattern**: batch soft-delete emits 1 audit row per id (not 1 row per batch). This is the first decorator that emits N audit rows for a single repo call. The pattern is straightforward (loop over `_db.TradeAttachments` tracked instances, call `MarkSwept` on each, emit `AuditAction.Deleted` per id) but it's the only "one-call-many-audit-rows" decorator in the codebase. Worth a design.md section to document the rationale.

5. **`audit.events` grows unbounded today**: until Wave 9 9b.1 ships the retention BackgroundService, the table accumulates. The Wave 8 archive has ~1366 BE tests + 5 user-owned aggregates audited × ~5 mutations/user/day × 100 active users = ~2500 rows/day = ~3M rows over 3 years. Sub-scope B's admin query API will be the FIRST consumer of this table — performance characteristics (query latency, index usage) should be observed at the first deploy.

6. **PII exposure in admin query**: `user_id` + `tenant_id` are visible to Admin role only; the `changes` JSONB may contain user-owned entity data. The `AdminOnly` policy + handler enforce this BEFORE any DB lookup. No new compliance surface vs. existing admin endpoints (the `AdminSubscriptionEndpoints` already expose similar PII).

7. **Cursor pagination correctness**: the `(occurred_at DESC, id DESC)` keyset must be stable across all queries. If two events have the same `occurred_at` (rare but possible with batch inserts), the `id` tiebreaker guarantees deterministic ordering. The cursor format must be opaque + base64 to discourage client tampering.

8. **Retention `BatchLimit` config**: the daily purge can delete up to `BatchLimit × CleanupIntervalHours / 24` = `10000 × 1` = 10k rows/day. For 3M accumulated rows, the purge takes 300 days to drain the backlog. Initial backlog drain may need a one-shot script (NOT in Wave 9 scope) — flag for the orchestrator.

9. **`AuditRetentionOptions` config validation**: `RetentionDays > 0`, `CleanupIntervalHours > 0`, `BatchLimit > 0`. Use `ValidateOnStart` (Wave 6 6c.2 JwtOptions precedent) to fail fast at startup if the config is invalid.

10. **Cross-module DI edges**: `Admin.Infrastructure` references `AuditDbContext` from `Identity.Infrastructure` (the audit.events table is Identity-owned per Wave 6 6d.1). This is OK — `Admin.Infrastructure` already transitively references Identity via the existing `Admin.Api` → `Identity.Api` chain. Verify the `Admin.Infrastructure.csproj` has `<ProjectReference Include="..\..\Identity\JadeCapital.Identity.Infrastructure\JadeCapital.Identity.Infrastructure.csproj" />` (likely needs to be added in 9b.1 Phase 5).

11. **`Admin.Application` + `Admin.Infrastructure` empty folders**: today only `JadeCapital.Host/Program.cs` references the 3 Admin csprojs (lines 131, 133, 151 — for admin subscription handlers + admin write-path repos). The csprojs exist but the source folders have no code yet. Wave 9 9b.1 creates the first files in these folders (`Admin.Application/Abstractions/IAuditEventQueryStore.cs`, `Admin.Application/Features/Audit/ListAuditEvents/`, `Admin.Infrastructure/Persistence/AuditEventQueryStore.cs`). The pattern precedent is `JadeCapital.Admin.Api/Endpoints/AdminSubscriptionEndpoints.cs` which lives in `Admin.Api` (already populated); the new code lands in `Admin.Application` + `Admin.Infrastructure` (empty today).

12. **No new Shared.Kernel change**: no enum extension, no migration. Wave 7's `AuditAction.Denied` + `AuditAction.Failed` + migration 0029 already cover all 4 new decorators' audit-action needs. The new query API + retention service don't touch the AuditAction enum (the query API returns existing actions; the retention service DELETEs rows).

13. **Forecast uncertainty**: ~2,570 LOC + ~30 scenarios is a lower bound. The 4 bespoke Wave 9 decorators may overrun their initial estimates (the bespoke pattern typically adds 30-50% LOC vs. the generic `DecoratedRepository<T>` shape — Wave 7 7b.1 TradeAuditDecorator was 321 LOC vs. the ~150 LOC generic estimate). Explicit per-slice LOC tracking with `size:exception` per Wave 8 precedent.

---

## Ready for Proposal

**Yes.** The scope is bounded (4 decorators + 1 documented SKIP + 2 new features), the slicing strategy respects module boundaries (3 Trading + 1 Identity/Admin + 1 reconciliation), the per-slice forecast respects the 32-path review contract, and the patterns are inherited verbatim from Wave 7/8's bespoke templates + the canonical `DecoratedRepository<T>` + the Wave 0/8 admin endpoint pattern + the Wave 4/6 BackgroundService pattern.

The orchestrator should tell the user:
- Effective scope is **4 decorators + 1 SKIP + 2 features** (not 5 decorators + 2 features as the user prompt phrased it).
- The 5-PR chain (9a.1 → 9a.2 → 9a.3 → 9b.1 → 9b.2) follows the Wave 8 `feature-branch-chain` precedent.
- Each slice will need `size:exception` per Wave 5/6/7/8 precedent.
- The new admin query API lands in `Admin.Api` (NOT Identity.Api) — admin-only UX, not trader UX.
- The retention BackgroundService lands in `Identity.Infrastructure` (NOT a new module) — Identity owns the audit infrastructure per Wave 6 6d.1.
- Test strategy: in-memory SQLite for decorators; Testcontainers Postgres for the admin endpoint (sandbox carry-forward WARNING).
- The `AttachmentSweepAuditDecorator` is the first batch-soft-delete decorator in the codebase — its 1-call-many-audit-rows pattern warrants a design.md section.
