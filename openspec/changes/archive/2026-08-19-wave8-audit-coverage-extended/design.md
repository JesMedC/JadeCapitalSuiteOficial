# Design — Wave 8 (Audit Coverage Extension: 7 More Decorators + 2 Documented SKIPs)

## Architecture Overview

Wave 8 widens the Wave 7 audit decorator pattern from **8 of 15** user-owned aggregates (Tenant, ImportJob, Subscription, User, RiskProfile, Strategy, Trade, JournalEntry) to **15 of 17** (adds TradeReview, PlannerSession, PreTradeChecklist, Account, Instrument, Alert, StripeCustomer; documents 2 explicit SKIPs for `ISubscriptionAdminRepository` and `IStripeWebhookEventRepository`). The wave ships in 5 chained slices (8a.1, 8a.2, 8a.3, 8b.1, 8b.2) totaling ~2,650 LOC, 48 file paths, and 35 new BE tests.

The wave is driven by a single concern — **compliance gap closure** (sub-scopes A + B + C in the proposal):

1. **Coverage extension** — 7 new typed audit decorators reusing Wave 7's generic `DecoratedRepository<T>` from `Shared.Infrastructure` (canonical shape) OR bespoke per-aggregate shapes (when `IRepository<T>` extension is impossible without security regression or where the audit shape diverges from the generic pattern).
2. **Surface surgery** — 2 atomic renames (`IAccountRepository.RemoveAsync` → `DeleteAsync`, `IInstrumentRepository.RemoveAsync` → `DeleteAsync`) matching `IRepository<T>.DeleteAsync(T, ct)`. 1 handler call site each, updated in the same slice.
3. **SKIP reconciliation** — 2 documented SKIPs with explicit rationale baked into the spec REMOVED Requirements section + the proposal's Out of Scope + the tasks.md slice 8b.2 verification.

**No new infrastructure**: no enum extension (Wave 7's `AuditAction.Denied = 4` + `AuditAction.Failed = 5` already cover all 7 new decorators), no migration (migration 0029 already widened the CHECK constraint to `IN (0,1,2,3,4,5)`), no `Shared.Kernel` change. Wave 7's `DecoratedRepository<T>.IsTerminated` reflection rule (`Status ∈ {Cancelled, Terminated, Expired}`) already covers `PlannerSession` via `PlannerStatus.Cancelled` — the bespoke `PlannerSessionAuditDecorator` re-implements the same reflection locally for consistency.

## Module Dependency Diagram (unchanged from Wave 7)

```
AFTER Wave 7 (current state at start of Wave 8):
  Trading.Infrastructure  ──→  Shared.Infrastructure  (for DecoratedRepository<T>)
  Billing.Infrastructure  ──→  Shared.Infrastructure  (for DecoratedRepository<T>)
  Identity.Infrastructure  ──→  Shared.Infrastructure
  Shared.Infrastructure    ──→  Shared.Kernel

AFTER Wave 8 (this delta):
  Trading.Infrastructure  ──→  Shared.Infrastructure  (for DecoratedRepository<T>)
  Billing.Infrastructure  ──→  Shared.Infrastructure  (for DecoratedRepository<T>)
  Identity.Infrastructure  ──→  Shared.Infrastructure
  Shared.Infrastructure    ──→  Shared.Kernel
```

**Zero new edges.** All 7 new typed decorators import `DecoratedRepository<T>` from `Shared.Infrastructure/Persistence/DecoratedRepository.cs` (Wave 7 7a.0 location). Trading → Shared and Billing → Shared are the only cross-module edges used by the decorators. No Identity.Infrastructure → Trading.Infrastructure circular dep risk.

**Why no edges are added**: the 7 new decorators live in their own module's `Audit/` folder (Trading → Trading for 6 decorators, Billing → Billing for StripeCustomer). Each decorator imports `DecoratedRepository<T>` (a public class in `Shared.Infrastructure`) and `IAuditLogger` + `ITenantContext` (from `Shared.Kernel`). No cross-module Application references, no circular dependency risk.

## Pattern Templates Reused (Wave 6/7 inheritance, no new patterns)

| Template | Source | Reuse scope in Wave 8 |
|---|---|---|
| Canonical typed decorator (generic `DecoratedRepository<T>`) | `src/3.Shared/JadeCapital.Shared.Infrastructure/Persistence/DecoratedRepository.cs` (319 LOC, Wave 7 7a.0) | Account (8a.1), Instrument (8a.1) — instantiate `DecoratedRepository<Account>` + `DecoratedRepository<Instrument>` directly |
| Bespoke typed decorator (cross-tenant `IsOwner`) | `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/TradeAuditDecorator.cs` (321 LOC, Wave 7 7b.1) | TradeReview (8a.2) — mirrors cross-tenant shape + bespoke read method forwarding |
| Bespoke typed decorator (conditional `AddAsync` return semantics) | n/a (Wave 7 RiskProfile supersession is the closest analog: bespoke aggregate-specific mutation) | Alert (8a.2) — bespoke; conditional audit on `AddAsync` returning `bool` |
| Bespoke typed decorator (write-once) | n/a (RiskProfile supersession is the closest analog) | PreTradeChecklist (8a.3) — only `AddAsync` wrapped |
| Bespoke typed decorator (immutable aggregate) | `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/TenantAuditDecorator.cs` (73 LOC, Wave 6 6d.2) — simplified canonical shape | StripeCustomer (8b.1) — only `AddAsync` wrapped, simplified pattern |
| Bespoke typed decorator (`IsTerminated` reflection) | `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/TradeAuditDecorator.cs` (Wave 7 7b.1) — uses `TradeStatus.Cancelled` upgrade | PlannerSession (8a.3) — re-implements the same reflection locally for `PlannerStatus.Cancelled` |
| Cross-tenant `IsOwner` check | Wave 6 6d.2 (`ImportJob`, `Subscription`) + Wave 7 7a.1 (`User`, `RiskProfile`) + 7b.1 (`Strategy`, `Trade`) + 7b.2 (`JournalEntry`) | TradeReview, PlannerSession, Alert, PreTradeChecklist, Account, StripeCustomer (6 of 7) |
| `AuditAction.Denied` + `AuditAction.Failed` enum values | Wave 7 7a.1 (added `Denied = 4` + `Failed = 5`; migration 0029 widened the CHECK constraint) | All 7 new decorators reuse these values; NO new enum extension |
| `ResolveBefore` via EF `ChangeTracker.OriginalValues` with JSON snapshot fallback | Wave 7 7b.1 + 7b.2 (Trade + JournalEntry bespoke decorators) | TradeReview + PlannerSession bespoke decorators |

## Per-Decorator Decisions (shape + signature)

### 1. `AccountAuditDecorator` (8a.1) — Standard generic shape

**Pattern**: Canonical generic `DecoratedRepository<Account>` wrapper (mirrors `UserAuditDecorator` from Wave 7 7a.1). After the `RemoveAsync → DeleteAsync` rename + `IRepository<Account>` extension, the interface fits the generic shape.

**Interface surgery**: 1 (rename `RemoveAsync` → `DeleteAsync`). 1 handler call site updated atomically (`DeleteAccountHandler.cs:47`).

**Cross-tenant**: YES — `account.UserId == _tenant.CurrentUserId` (mirrors `UserAuditDecorator`). Defense-in-depth for user-owned account data.

**File**: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AccountAuditDecorator.cs` (~100 LOC). Imports `DecoratedRepository<T>` from `JadeCapital.Shared.Infrastructure.Persistence`. Forwards `FindByIdAsync` + `ListByUserIdAsync` to `_inner` (no audit). Forwards `AddAsync` + `UpdateAsync` + `DeleteAsync` to `_decorated` (the `DecoratedRepository<Account>` instance).

**Forecast**: 10 integration scenarios + 4 contract scenarios (rename pin: `RemoveAsync_IsNotOnInterface_AfterRename` + `DeleteAsync_Exists_OnInterface` × 2 interfaces).

### 2. `InstrumentAuditDecorator` (8a.1) — Standard generic shape, NO `IsOwner`

**Pattern**: Same as Account — standard generic `DecoratedRepository<Instrument>` wrapper. After the `RemoveAsync → DeleteAsync` rename + `IRepository<Instrument>` extension, the interface fits the generic shape.

**Interface surgery**: 1 (rename `RemoveAsync` → `DeleteAsync`). 1 handler call site updated atomically (`DeleteInstrumentHandler.cs:45`).

**Cross-tenant**: NO. Instrument is a catalog entity — "NO es Aggregate Root: es una Entity compartida por todos los usuarios." All users see the same instrument catalog; admin mutations on the catalog are legitimate. Mirrors `TenantAuditDecorator` precedent (Tenant IS the tenant boundary; Instrument is catalog data, not user-scoped).

**File**: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/InstrumentAuditDecorator.cs` (~100 LOC). Same imports + wiring as Account, minus the `IsOwner` check.

**Forecast**: 10 integration scenarios + 4 contract scenarios (rename pin × 2 interfaces).

### 3. `AlertAuditDecorator` (8a.2) — Bespoke, conditional `AddAsync` audit

**Pattern**: Bespoke decorator implementing `IAlertRepository` directly. The critical detail: `AddAsync(Alert)` returns `bool` (`true` = inserted, `false` = deduped by `ux_alerts_user_rule_day` UNIQUE INDEX). The decorator MUST check the return value after the inner call:
- When `true`: emit `AuditAction.Created`.
- When `false`: emit NO audit row (the row was not created; the existing row's audit history is preserved).

**Interface surgery**: 0 (the interface shape already supports the decorator — `AddAsync` returning `bool` is just a signature detail; no rename needed).

**Cross-tenant**: YES — `alert.UserId == _tenant.CurrentUserId`.

**File**: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AlertAuditDecorator.cs` (~200 LOC). Bespoke shape mirroring `TradeAuditDecorator` (Wave 7 7b.1) — implements `IAlertRepository` directly. Forwards `ListByUserAsync` + `GetByIdAsync` to `_inner` (no audit). Forwards `AddAsync` (with return-value check) + `UpdateAsync` to `_decorated` (the bespoke `DecoratedRepository<Alert>` instance with `IsOwner` check on `UpdateAsync`). Uses EF `ChangeTracker.OriginalValues` for the pre-mutation diff on `UpdateAsync` (Acknowledge transition: `AcknowledgedAt: null → now`).

**Why bespoke instead of generic**: `IAlertRepository` exposes `ListByUserAsync(userId, ct)` — a parameter that scopes the read to a specific user. Extending `IRepository<Alert>` would force a `ListByUserAsync()` (no userId) signature that ignores cross-user scope — a security regression. Bespoke decorator preserves the userId-scoped reads.

**Forecast**: 5 integration scenarios (Created on insert=true, NO audit on dedup=false, Updated with diff on Acknowledge, FindByIdAsync not audited, cross-tenant Denied).

### 4. `TradeReviewAuditDecorator` (8a.2) — Bespoke, forward attachment ops without audit

**Pattern**: Bespoke decorator implementing `ITradeReviewRepository` directly. Mirrors the `JournalEntryAuditDecorator` (Wave 7 7b.2) shape — the interface has cross-user scope on every read (`FindByTradeIdAsync` takes an explicit `tradeId` and is internally scoped to the review's user; the bespoke shape preserves this). Cross-tenant `IsOwner` check on `review.UserId` for `UpdateAsync`.

**Interface surgery**: 0. The interface shape already supports the decorator (no `DeleteAsync` per the entity docstring "no delete" product decision; no rename needed).

**Cross-tenant**: YES — `review.UserId == _tenant.CurrentUserId`.

**Attachment ops** — `AddAttachmentAsync` / `UpdateAttachmentAsync` / `RemoveAttachmentAsync` MUST be forwarded to `_inner` WITHOUT emitting audit rows. Rationale: `TradeAttachment` is a child entity of the review, not a separately-audited aggregate. The review's Update events + handler-side MinIO cleanup log already capture attachment lifecycle. Adding per-attachment audit rows would be noisy without proportional compliance value. Per orchestrator preflight decision 7.

**File**: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/TradeReviewAuditDecorator.cs` (~250 LOC). Bespoke shape mirroring `JournalEntryAuditDecorator` (Wave 7 7b.2). Forwards all reads + attachment ops without audit. Forwards `AddAsync` (Created) + `UpdateAsync` (Updated with diff + `IsOwner`).

**Why bespoke instead of generic**: `ITradeReviewRepository` exposes attachment operations (`AddAttachmentAsync` / `UpdateAttachmentAsync` / `RemoveAttachmentAsync`) as first-class methods. Extending `IRepository<TradeReview>` would lose these signatures; the decorator pattern's forwarding chain would break the MinIO cleanup logging path. Bespoke decorator preserves the full interface surface.

**Forecast**: 5 integration scenarios (Created, Updated with diff + IsOwner, attachment ops forwarded without audit, FindByIdAsync not audited, FindByTradeIdAsync not audited).

### 5. `PlannerSessionAuditDecorator` (8a.3) — Bespoke, `IsTerminated` reflection on `PlannerStatus.Cancelled`

**Pattern**: Bespoke decorator implementing `IPlannerSessionRepository` directly. Mirrors `TradeAuditDecorator` (Wave 7 7b.1) shape with the `IsTerminated` reflection rule. The decorator re-implements the `IsTerminated` reflection locally: `entity.GetType().GetProperty("Status")?.GetValue(entity)?.ToString() is "Cancelled"` (matches the Wave 6 6d.2 rule `Status ∈ {Cancelled, Terminated, Expired}`).

**Interface surgery**: 0. The interface already exposes `UpdateAsync(PlannerSession, ct)` — no rename needed.

**Cross-tenant**: YES — `session.UserId == _tenant.CurrentUserId`.

**File**: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/PlannerSessionAuditDecorator.cs` (~200 LOC). Bespoke shape with the `IsTerminated` reflection re-implemented locally (the generic `DecoratedRepository<T>.IsTerminated` covers the same case but the bespoke decorator needs the same logic for consistency with the Wave 7 7b.1 Trade pattern). Forwards `GetByIdAsync` + `ListByUserAndWeekAsync` + `ExistsForDateAsync` + `GetWeekComparisonAsync` without audit. Forwards `AddAsync` (Created) + `UpdateAsync` (Updated by default; Deleted when `Status == PlannerStatus.Cancelled` per reflection).

**Why bespoke instead of generic**: `IPlannerSessionRepository` exposes bespoke read methods (`ListByUserAndWeekAsync`, `ExistsForDateAsync`, `GetWeekComparisonAsync`) that don't fit the canonical `IRepository<T>.GetByIdAsync(Guid, ct)` shape. Bespoke decorator preserves the full interface.

**Forecast**: 5 integration scenarios (Created, Updated with diff, Cancelled transition upgrades to Deleted via `IsTerminated` reflection, IsOwner check, reads not audited).

### 6. `PreTradeChecklistAuditDecorator` (8a.3) — Bespoke write-once (only `AddAsync` audited)

**Pattern**: Smallest decorator in the wave. The interface has only `AddAsync` + `ListByUserIdAsync`. The checklist is write-once per the entity docstring ("UNA fila por trade — enforced por UNIQUE INDEX sobre trade_id en la DB. La API no expone UPDATE del checklist"). No `UpdateAsync`, no `DeleteAsync` to wrap.

**Interface surgery**: 0 (the interface shape already matches the audit intent).

**Cross-tenant**: YES — `checklist.UserId == _tenant.CurrentUserId`.

**File**: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/PreTradeChecklistAuditDecorator.cs` (~100 LOC). Bespoke shape mirroring a simplified `RiskProfileAuditDecorator` (Wave 7 7a.1) — minimal: only `AddAsync` wrapped. Forwards `ListByUserIdAsync` without audit. `AttachAIRiskAdvisory` is an in-memory mutation called within the same UoW (NOT a repo-level `UpdateAsync`); no audit wrapping needed.

**Forecast**: 3 integration scenarios (Created on AddAsync, ListByUserIdAsync not audited, no Update/Delete methods exist — contract pin).

### 7. `StripeCustomerAuditDecorator` (8b.1) — Bespoke immutable (only `AddAsync` audited)

**Pattern**: Bespoke decorator mirroring a simplified `TenantAuditDecorator` shape. The interface has only `AddAsync` + 2 reads (`GetByUserIdAsync`, `GetByStripeCustomerIdAsync`). The `StripeCustomer` aggregate is immutable post-Create per the entity docstring ("Aggregate is immutable after Create — no mutators").

**Interface surgery**: 0 (the interface shape already matches the audit intent).

**Cross-tenant**: YES — `customer.UserId == _tenant.CurrentUserId`.

**File**: `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Audit/StripeCustomerAuditDecorator.cs` (~120 LOC). Bespoke shape mirroring a simplified `TenantAuditDecorator`. Forwards reads without audit. `AddAsync` emits `Created` + `IsOwner` check. No `UpdateAsync` or `DeleteAsync` to wrap.

**Forecast**: 3 integration scenarios (Created on AddAsync + IsOwner, reads not audited, no Update/Delete methods exist — contract pin).

## Documented SKIPs (the 2 reconciliation items in 8b.2)

### `ISubscriptionAdminRepository` — SKIP

**Rationale** (canonical 4-line statement): The interface has **NO mutation methods** — only reads + loads. All subscription mutations flow through `ISubscriptionAdminUnitOfWork.AddHistoryEntry` + `SaveChangesAsync` + the `Subscription` aggregate's own `ChangeTier` / `Cancel` / `ExtendTrial` / `SyncFromStripe` methods. The `Subscription` aggregate itself is **already audited** by Wave 6's `SubscriptionAuditDecorator` (`src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Audit/SubscriptionAuditDecorator.cs`), which wraps `ISubscriptionRepository` — a different interface that the admin write path doesn't even use.

**Verification**: `git grep -E "Task (Add|Update|Delete)Async" src/2.Modules/Billing/JadeCapital.Billing.Application/Features/Subscriptions/ISubscriptionAdminRepository.cs` → no matches (proves the absence of mutation methods).

**Alternative considered + rejected**: Wrap `ISubscriptionAdminUnitOfWork.SaveChangesAsync` instead. Rejected because the `SubscriptionAuditDecorator` already captures the mutations at the aggregate level. Wrapping the UoW would emit duplicate audit rows for the same transitions.

### `IStripeWebhookEventRepository` — SKIP

**Rationale** (canonical 4-line statement): Append-only per Wave 6's `ISoftDelete` docstring ("ISoftDelete is opt-in per aggregate: not all aggregates are soft-deleteable (e.g., `AuditEvent`, `StripeWebhookEvent` are append-only and never deleted)"). The entity itself IS the audit log equivalent: the table `billing.stripe_webhook_events` records every received webhook with `EventId`, `EventType`, `PayloadJson`, `ReceivedAt`, `ProcessedAt`, `ProcessingError`. Adding an `AuditEvent` row for each `StripeWebhookEvent` row would be doubly-recorded noise.

**Verification**: `grep -n "append-only" src/2.Modules/Billing/JadeCapital.Billing.Domain/Stripe/StripeWebhookEvent.cs` → matches (proves the entity docstring still asserts append-only).

The `UpdateAsync` path (`MarkProcessed` / `MarkFailed`) is system-internal dispatch bookkeeping, not a user mutation. It does NOT carry user intent and does NOT warrant an audit row.

## Architectural Decisions (consolidated)

| Decision | Resolution | Rationale |
|---|---|---|
| Bespoke vs. generic per repo | Per the table above; mix of 2 generic (Account, Instrument) + 5 bespoke (TradeReview, PlannerSession, PreTradeChecklist, Alert, StripeCustomer) | Match the interface shape; avoid forcing `IRepository<T>` extension that would break cross-user scope or aggregate-specific mutations |
| `IAccountRepository.RemoveAsync` rename | `DeleteAsync(Account, ct)`; breaking; atomic in slice 8a.1 | Mirrors Wave 7 7b.1 Trade rename; matches `IRepository<T>.DeleteAsync(T, ct)` |
| `IInstrumentRepository.RemoveAsync` rename | `DeleteAsync(Instrument, ct)`; breaking; atomic in slice 8a.1 | Same as Account |
| `IInstrumentRepository` `IsOwner` check | NO (catalog entity) | Mirrors `TenantAuditDecorator` precedent; admin mutations are legitimate |
| `IAlertRepository.AddAsync` returns `bool` audit semantics | Conditional: `true` → `Created`; `false` (deduped) → NO audit | Per orchestrator preflight decision 6; preserves existing row's audit history |
| `TradeAttachment` separately audited? | NO (child entity of TradeReview) | Per orchestrator preflight decision 7; forwarded without audit |
| `PlannerSession.IsTerminated` reflection rule | YES — `PlannerStatus.Cancelled` matches Wave 6 6d.2 rule | Per orchestrator preflight decision 8 |
| 8a.0 collapses to verified-no-op | YES (Wave 7 7a.0 already added explicit Scrutor) | Verified via `git diff --stat` in slice 8a.1 Phase 0 |
| `Shared.Infrastructure` ownership unchanged | YES — `DecoratedRepository<T>` at `Shared.Infrastructure/Persistence/DecoratedRepository.cs` | Wave 7 7a.0 |
| Cross-tenant `IsOwner` check | YES for 6 of 7 (TradeReview, PlannerSession, Alert, PreTradeChecklist, Account, StripeCustomer); NO for Instrument | Catalog entity exception |
| Test fixture per aggregate | Focused helper `DbContext` mirroring Wave 7's `TestTradingDbContext` pattern (Npgsql-specific array converters fail on SQLite) | No Testcontainers Postgres in this sandbox (carry-forward WARNING) |
| `size:exception` | Likely needed for 8a.1 + 8a.2 + 8a.3; 8b.1 borderline; 8b.2 doc-only | Wave 5/6/7 precedent — all accepted |
| 8a.1 = 12 paths, 8a.2 = 10, 8a.3 = 12, 8b.1 = 12, 8b.2 = 2; total 48 paths ≤ 32 per slice (Wave 7 used 33 paths split across 4 slices; Wave 8's 5 slices at 48 total = avg 10 per slice, all under 32) | OK per bounded review contract |

## Migration Path

**No schema migration required.** The decorator pattern is additive — `audit.events` table already exists (Wave 6 migration 0027). Wave 7's migration 0029 (widening the `ck_audit_events_action` CHECK constraint to `IN (0,1,2,3,4,5)`) already covers all 7 new decorators' audit-action needs. No new columns.

**No data migration required.** The `audit.events` table starts receiving TradeReview / PlannerSession / PreTradeChecklist / Account / Instrument / Alert / StripeCustomer rows from the moment the deployment completes. Historical mutations (pre-Wave 8) are not backfilled — the audit log is forward-only.

**No interface-level deprecation period.** The 2 renames (`RemoveAsync` → `DeleteAsync` on Account + Instrument) are atomic in slice 8a.1. No consumers outside the 1 trading handler each are affected (verified via `git grep` BEFORE the rename). The 7 new decorators register via `services.Decorate<IXxxRepository, XxxAuditDecorator>()` — no DB schema changes.

## Affected Areas Summary

| Area | Impact | Slice |
|---|---|---|
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/Repositories/IAccountRepository.cs` | Modified | 8a.1 |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Persistence/Repositories.cs:147` | Modified | 8a.1 |
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Accounts/DeleteAccount/DeleteAccountHandler.cs:47` | Modified | 8a.1 |
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/Repositories/IInstrumentRepository.cs` | Modified | 8a.1 |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Persistence/Repositories.cs:194` | Modified | 8a.1 |
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Instruments/DeleteInstrument/DeleteInstrumentHandler.cs:45` | Modified | 8a.1 |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AccountAuditDecorator.cs` | New | 8a.1 |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/InstrumentAuditDecorator.cs` | New | 8a.1 |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AlertAuditDecorator.cs` | New | 8a.2 |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/TradeReviewAuditDecorator.cs` | New | 8a.2 |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/PlannerSessionAuditDecorator.cs` | New | 8a.3 |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/PreTradeChecklistAuditDecorator.cs` | New | 8a.3 |
| `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Audit/StripeCustomerAuditDecorator.cs` | New | 8b.1 |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs` | Modified | 8a.1 + 8a.2 + 8a.3 (5× `Decorate` calls) |
| `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/DependencyInjection/BillingModuleRegistration.cs` | Modified | 8b.1 (1× `Decorate` call) |
| 7 new test files in `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/` | New | 8a.1 + 8a.2 + 8a.3 + 8b.1 |
| `openspec/specs/soft-delete-audit/spec.md` (main, archive-time merge) | Modified | sdd-archive |

## Out of Scope (deferred to Wave 9+)

- Audit decorator coverage for `RiskConfiguration`, `PlanVersion`, `JournalDaily`, `BacktestRun`, `StrategyVersion`, `TradeTag`, `Note`, `Mood`, `BehavioralMetric` — Wave 9.
- Admin-only `GET /api/audit/events` query API — Wave 9.
- User-facing read API (`GET /api/audit/me`) — Wave 9.
- Audit log retention policy (90-day default) + auto-purge hosted service — Wave 9.
- Audit log export (CSV / JSON) for compliance officers — Wave 9.
- Soft-delete cascade propagation for the 7 newly-decorated aggregates — Wave 9.
- `AuditAction.Restored` end-to-end support — Wave 9.
- Bulk audit events for `AddRangeAsync` — out of scope (Wave 6/7 precedent).
- Migration to a different audit sink (Kafka, S3, external SIEM) — post-1.0.