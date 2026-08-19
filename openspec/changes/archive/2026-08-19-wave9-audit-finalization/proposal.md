# Proposal: Wave 9 — Audit Finalization (4 Decorators + Query API + 90-Day Retention)

**Change**: `2026-08-19-wave9-audit-finalization`
**Branch**: `feature/0a-identity-model` @ `4f54013` (Wave 8 just archived)
**Stack**: ASP.NET Core 10 / .NET SDK 10.0.400 via `mise exec -- dotnet …`
**Strategy**: `feature-branch-chain` (carries the Wave 8 chain)
**Mode**: hybrid (OpenSpec + engram)

---

## Intent and Problem

Wave 8 closed audit decorator rollout to **15 user-owned aggregates** + 2 documented SKIPs. Wave 8's verify-report (`verify-report-wave8-final.md` SUGGESTION #1) flagged **5 remaining Trading candidate repositories**. Of those 5:

- **4 need audit decorators** (2 bespoke write-once + 1 bespoke CRUD-without-Delete + 1 NEW batch soft-delete pattern).
- **1 is a documented SKIP** (`ITradeAttachmentUsageRepository` — read-only, no mutations to audit).

Two additional gaps from Wave 6/7/8 deferral also land in Wave 9:

- **Sub-scope B**: admin-only `GET /api/admin/audit/events` query API (paginated + filterable) — the audit log is write-only today; no read surface exists for compliance officers or admin tooling.
- **Sub-scope C**: 90-day audit retention + auto-purge `AuditRetentionBackgroundService` — `audit.events` grows unbounded; Wave 6/7/8 design docs all explicitly deferred retention to Wave 9.

**Compliance + operational gap** (real and bounded):
1. Every mutation on `AIRiskAdvice` / `CoachingPrompt` / `ScannerFilter` / `TradeAttachment` (via `AttachmentSweep`) is invisible to `audit.events` today.
2. Compliance officers cannot query `audit.events` from any UI/API today (no `IAuditEventQueryStore`, no admin endpoint).
3. `audit.events` will grow unbounded — at ~2500 rows/day (100 users × ~5 audited mutations/user/day × 5 aggregates), that's ~3M rows over 3 years without retention.

**No new infrastructure**: no enum extension, no migration, no `Shared.Kernel` change. Wave 7's `AuditAction.Denied = 4` + `AuditAction.Failed = 5` + migration 0029 already cover all 4 new decorators' audit-action needs.

---

## Goals

- **Decorator coverage (Sub-scope A)**: 4 new typed audit decorators spanning Trading (4 — AIRiskAdvice, CoachingPrompt, ScannerFilter, AttachmentSweep) + 1 documented SKIP (`ITradeAttachmentUsageRepository`). Every user-facing mutation in the 5-repo remainder now emits `AuditEvent` writes through the Wave 8 pattern.
- **Admin query API (Sub-scope B)**: `GET /api/admin/audit/events` with cursor pagination + filters (`entity_type`, `action`, `user_id`, `tenant_id`, date range). Lives in `JadeCapital.Admin.Api/Endpoints/AdminAuditEndpoints.cs`. Admin role enforced via `RequireAuthorization("AdminOnly")` + `RequireAdminPolicyHandler`.
- **Audit retention (Sub-scope C)**: `IAuditRetentionService` + `AuditRetentionService` + `AuditRetentionBackgroundService` + `AuditRetentionOptions` in `Identity.Infrastructure/Audit/`. 90-day default retention, daily tick with jitter, `ExecuteDeleteAsync` batch limit + per-attempt isolation. Idempotent + safe re-run.
- **Zero regression**: all Wave 8 tests still pass after the wave.

---

## Scope Boundaries

### In Scope

| Sub-scope | Boundary | Deliverable |
|---|---|---|
| **A. Trading coverage (4 decorators)** | 2 bespoke write-once + 1 bespoke CRUD-without-Delete + 1 NEW batch soft-delete | `AIRiskAdviceAuditDecorator` + `CoachingPromptAuditDecorator` (9a.1, write-once); `ScannerFilterAuditDecorator` (9a.2, CRUD-no-Delete); `AttachmentSweepAuditDecorator` (9a.3, NEW batch soft-delete pattern) |
| **B. Admin query API (new feature)** | `Admin.Api` endpoint + `Admin.Application` query handler + `Admin.Infrastructure` query store | `AdminAuditEndpoints.cs` + `ListAuditEventsQuery` + `ListAuditEventsHandler` + `IAuditEventQueryStore` + `AuditEventQueryStore` (9b.1) |
| **C. Audit retention (new feature)** | `Identity.Infrastructure` BackgroundService + options | `IAuditRetentionService` + `AuditRetentionService` + `AuditRetentionBackgroundService` + `AuditRetentionOptions` (9b.1) |
| **D. SKIP reconciliation** | Doc-only | Explicit rationale in proposal Out of Scope + spec REMOVED Requirements for `ITradeAttachmentUsageRepository` (9b.2) |
| **Tests** | SQLite-in-memory for decorators; Testcontainers Postgres for admin endpoint | ~25 new tests: 8 (9a.1) + 5 (9a.2) + 5 (9a.3) + 7 (9b.1: 3 store + 3 handler + 1 retention) |

### Out of Scope (deferred to Wave 10+)

- **Admin write-back API** for `audit.events` (e.g. POST to annotate, PATCH to tag for compliance) — Wave 10+. The query surface is read-only in Wave 9.
- **`AuditAction.Restored` end-to-end support** — the enum value is reserved (Wave 6); restore command + admin tooling is Wave 10+.
- **Soft-delete cascade propagation** beyond `ImportJob` (Wave 6 precedent) for the 4 newly-decorated aggregates — Wave 10+.
- **Retention configuration UI** for admin (per-tenant override, dashboard) — Wave 10+. Wave 9 is purely config-driven via `appsettings.json` (`AuditRetention:RetentionDays`).
- **Audit log export (CSV/JSON)** for compliance officers — Wave 10+. Query API is the first compliance surface; export builds on top.
- **User-facing read API** (`GET /api/audit/me`) — "my mutation history" tab in mobile is Wave 10+.
- **Free-text search on `changes` JSONB** — Wave 10+. Wave 9 query API supports structured filters only.
- **`audit.events` partitioning strategy** (Postgres native partitioning by `tenant_id` or month) — Wave 10+ if retention backlog drain shows the need.
- **Initial backlog drain one-shot script** — for backlogs > 1M rows, `BatchLimit × 24h` cannot drain fast enough. A separate operational script (NOT in Wave 9 scope) handles this on first deploy.
- **Migration to a different audit sink** (Kafka / S3 / external SIEM) — post-1.0.

### Out of Scope (Wave 9 SKIP — documented, not deferred)

- **`ITradeAttachmentUsageRepository`**: NO mutation methods (`Task (Add|Update|Delete)Async` — none on the interface). The repo is a pure read-side aggregate query — `GetUsageAsync(Guid userId, ct)` returning `(long TotalBytes, int Count)`. The right seam for `TradeAttachment` audit IS `IAttachmentSweepRepository.SoftDeleteBatchAsync` (sub-scope A #4) — the user-impacting soft-delete happens there, not at the usage projection. Adding an audit decorator here would be a no-op on the audit-write path; the only thing left to forward is reads (which Wave 6/7/8 precedent says are never audited). **Skip with documented rationale. No code change; no test added.**

---

## Capabilities (modified)

- **`soft-delete-audit`** (Wave 6/7/8, `openspec/specs/soft-delete-audit/spec.md`) — MODIFIED. The Wave 8 spec covers `ISoftDelete` + `IAuditLogger` + `DecoratedRepository<T>` + the 15 typed decorators. Wave 9 extends the same requirement:

  > **Requirement: Decorator coverage for the remaining 4 Trading aggregates**
  > Every `IXxxRepository` that mutates state on a user-owned aggregate MUST have a typed audit decorator registered in its module's `*ModuleRegistration`. The decorator MUST forward `AddAsync` / `UpdateAsync` / `SoftDeleteBatchAsync` (or the aggregate-specific mutation method) and emit an `AuditEvent` with `EntityType = typeof(T).Name` (or `EntityType = "TradeAttachment"` for `AttachmentSweep` since the aggregate is the child, not the sweep). Covered aggregates after Wave 9: `Tenant`, `ImportJob`, `Subscription`, `User`, `Strategy`, `Trade`, `RiskProfile`, `JournalEntry`, `TradeReview`, `PlannerSession`, `PreTradeChecklist`, `Account`, `Instrument`, `Alert`, `StripeCustomer`, `AIRiskAdvice`, `CoachingPrompt`, `ScannerFilter`, `TradeAttachment`. New aggregates added in Wave 10+ extend this list.

  The delta spec also adds a **REMOVED Requirements** section per OpenSpec convention:
  - `### REMOVED: ITradeAttachmentUsageRepository` — Reason: interface has no mutation methods; `TradeAttachment` aggregate mutations already audited via `IAttachmentSweepRepository.SoftDeleteBatchAsync`.

  This is a **capability expansion**, NOT a new capability. The sdd-spec phase writes a delta spec for `soft-delete-audit` (~18 sub-scenarios across the 4 new decorators + 1 SKIP reconciliation).

## Capabilities (new)

- **`audit-query-api`** (NEW, `openspec/specs/audit-query-api/spec.md`) — admin-only read surface for `audit.events`. Covers the GET endpoint contract, cursor pagination semantics, filter semantics (entity_type / action / user_id / tenant_id / from / to), `AdminOnly` policy enforcement at the endpoint boundary, PII exposure contract (admin sees all fields), rate limiting, response DTO shape (`items[]`, `next_cursor`, `has_more`).

- **`audit-retention-policy`** (NEW, `openspec/specs/audit-retention-policy/spec.md`) — 90-day retention + auto-purge background service. Covers the `AuditRetentionOptions` config contract (`RetentionDays`, `CleanupIntervalHours`, `BatchLimit`, `InitialDelay`), the `IAuditRetentionService.PurgeOldAsync(cutoff, batchLimit, ct)` contract, the `AuditRetentionBackgroundService` scheduling contract (2-min startup settle + 24h interval + jitter), idempotency + per-attempt isolation, `ValidateOnStart` config validation.

  **Why separate specs**: query API + retention are different capabilities with different owners, different compliance surfaces, and different change cadences. Keeping them in `soft-delete-audit` would conflate the write path (decorators) with the read path (query API) and the lifecycle path (retention) — three orthogonal concerns. Wave 6/7/8 precedent treats `soft-delete-audit` as the write-path spec; Wave 9 introduces the read + lifecycle specs as siblings.

---

## Architectural Decisions

- **Bespoke vs generic per repo** (per Explore analysis):

  | Repo | Pattern | Why |
  |---|---|---|
  | AIRiskAdvice | **Bespoke** (write-once) | Only `AddAsync` + 1 read; aggregate immutable post-Create per entity docstring; extending `IRepository<T>` would force `UpdateAsync` + `DeleteAsync` that the contract forbids |
  | CoachingPrompt | **Bespoke** (write-once) | Same as AIRiskAdvice — only `AddAsync` + 2 reads; aggregate immutable post-Create |
  | ScannerFilter | **Bespoke** (CRUD without Delete) | Interface lacks `DeleteAsync` (deactivate via `IsActive = false`); the canonical `IRepository<T>` requires `DeleteAsync(T, ct)` — added as defensive stub throwing `NotSupportedException` + emitting `AuditAction.Failed` (Wave 7 7a.1 UserAuditDecorator precedent). `DecoratedRepository<ScannerFilter>` wraps the surface. |
  | AttachmentSweep | **Bespoke** (NEW batch soft-delete) | Wraps `SoftDeleteBatchAsync(IReadOnlyList<Guid>, ct)`; emits 1 audit row per id (NOT 1 row per batch); cross-tenant `IsOwner` per id; doesn't fit `IRepository<T>` |
  | TradeAttachmentUsage | **SKIP** | Read-only repo with no mutations to audit |

- **`IAttachmentSweepRepository.InsertAuditAsync` NOT audited**: writes to `trading.attachments_quota_audit` — a SEPARATE audit table owned by the sweep itself. That table IS the audit log for the sweep; emitting `audit.events` rows on top would be doubly-recorded noise. Mirrors Wave 8 8b.2 `IStripeWebhookEventRepository` SKIP rationale. The decorator forwards `InsertAuditAsync` without audit.

- **`AttachmentSweepAuditDecorator` NEW pattern (1-call-many-audit-rows)**: `SoftDeleteBatchAsync` emits 1 audit row per id in the batch. `EntityType = "TradeAttachment"` (the aggregate, not the sweep), `EntityId = id`, `ChangesJson = { "IsActive": { "before": true, "after": false } }`. Cross-tenant `IsOwner` check per id (load `attachment.UserId` via the same EF tracked instance the inner repo loaded for the `MarkSwept` call); on mismatch emit `AuditAction.Denied` + throw `UnauthorizedAccessException`. This is the first batch-soft-delete decorator in the codebase — warrants a design.md section to document the rationale (single-call N-audit-rows pattern).

- **Admin endpoint location: `JadeCapital.Admin.Api`** (NOT Identity.Api):
  1. `RequireAuthorization("AdminOnly")` + `RequireAdminPolicyHandler` deny-by-default BEFORE any DB lookup — no info leak about user existence / tenant scope / audit-event content (matches Wave 8 8b.1 AdminSubscriptionEndpoints template).
  2. Admin module csprojs (`Admin.Api`, `Admin.Application`, `Admin.Infrastructure`) already exist + are referenced from `JadeCapital.Host/Program.cs` (lines 131, 133, 151). The endpoint just lands in the empty `Admin.Api/Endpoints/` folder + a handler in `Admin.Application/Features/Audit/...` + a query store in `Admin.Infrastructure/Persistence/`.
  3. PII concerns: `user_id` + `tenant_id` are internal Guids (not PII like email/name); `changes` JSONB may contain user-owned entity data (e.g. trade price, journal text) — admin role sees all of it per the AdminOnly policy + handler.
  4. Identity.Api is for trader-facing endpoints (auth, risk-profile, tenant). Audit logs are operational / compliance tooling, not trader UX — they belong in Admin.
  5. **Alternative rejected**: Identity.Api (cross-cutting concerns like `IAuditLogger` live in Identity.Infrastructure, but the audit query surface is admin-only UX, not identity UX).

- **Read-side architecture**:
  - **New interface**: `IAuditEventQueryStore` in `JadeCapital.Admin.Application/Abstractions/` (admin-side abstraction; keeps Admin independent of Identity's internal `AuditDbContext`).
  - **EF impl**: `AuditEventQueryStore` in `JadeCapital.Admin.Infrastructure/Persistence/` — depends on `AuditDbContext` (already in Identity.Infrastructure + registered in DI). Cross-module reference OK: `Admin.Infrastructure` already transitively references `Identity.Infrastructure` via the csproj chain. Verify `<ProjectReference Include="..\..\Identity\JadeCapital.Identity.Infrastructure\JadeCapital.Identity.Infrastructure.csproj" />` exists in `Admin.Infrastructure.csproj`; add in 9b.1 Phase 5 if missing.
  - **Handler**: `ListAuditEventsQuery` + `ListAuditEventsHandler` in `Admin.Application/Features/Audit/ListAuditEvents/`.
  - **Endpoint**: `MapAdminAuditEndpoints()` extension in `Admin.Api/Endpoints/AdminAuditEndpoints.cs`.
  - **DI wiring**: `services.AddScoped<IAuditEventQueryStore, AuditEventQueryStore>()` + handler registration in `AdminModuleRegistration` (NEW csproj pattern — the file doesn't exist yet; csproj does; Wave 9 9b.1 creates it).

- **Cursor pagination**: `(occurred_at DESC, id DESC)` keyset. Cursor format: `base64("{occurred_at_ticks}:{id_guid}")`. Server filters `WHERE (occurred_at, id) < (cursor.occurred_at, cursor.id) ORDER BY occurred_at DESC, id DESC LIMIT $limit + 1`. The `+1` row determines `has_more`. If two events share `occurred_at` (rare batch inserts), the `id` tiebreaker guarantees deterministic ordering. Opaque base64 discourages client tampering.

- **Index coverage**: 3 existing indexes (`ix_audit_events_entity`, `ix_audit_events_tenant_time`, `ix_audit_events_user`) cover the 4 single-filter queries. Compound filters use the most selective single index; Postgres bitmap-AND. For 1M+ row tables, add a covering index in a follow-up migration if profiling shows it (out of Wave 9 scope).

- **Retention `BatchLimit` config**: `BatchLimit = 10000` default → ~10k rows deleted per day. For 3M accumulated rows (3-year backlog), drain takes 300 days. Initial backlog drain may need a one-shot script (NOT in Wave 9 scope) — flag for orchestrator.

- **`AuditRetentionOptions` validation**: `RetentionDays > 0`, `CleanupIntervalHours > 0`, `BatchLimit > 0`. Use `ValidateOnStart` (Wave 6 6c.2 `JwtOptions` precedent) to fail fast at startup if config is invalid.

- **Retention scheduling**:
  - **First run**: 2 minutes after startup (Identity hosts the audit infrastructure — gives the rest of the pipeline time to settle, matching the `BackfillTenantsHostedService` 15s precedent scaled for a heavier first run).
  - **Subsequent runs**: every `CleanupIntervalHours` (24h default) with `[0, +30min]` jitter to avoid thundering herd across replicas (matches `AttachmentLifecycleService` precedent).
  - **Per-run isolation**: `try { PurgeOldAsync + log } catch { LogError + continue }` — never crash the host (matches `RefreshTokenCleanupService` precedent).
  - **Logging**: `LogInformation` when rows deleted > 0; `LogDebug` when 0 rows deleted.

- **Cross-tenant `IsOwner` check on user-owned decorators** (same as Wave 6/7/8): AIRiskAdvice + CoachingPrompt + ScannerFilter + AttachmentSweep check `entity.UserId != tenant.CurrentUserId` BEFORE delegating to the inner. On mismatch: log `Denied` audit row + throw `UnauthorizedAccessException`. No exceptions (no Instrument-style catalog entity in Wave 9 — all 4 audited repos are user-owned).

- **`Shared.Infrastructure` ownership unchanged**: `DecoratedRepository<T>` lives at `src/3.Shared/JadeCapital.Shared.Infrastructure/Persistence/DecoratedRepository.cs` (Wave 7 7a.0). Wave 9 9a.1 (AIRiskAdvice + CoachingPrompt) + 9a.2 (ScannerFilter) reuse the helper from `Trading.Infrastructure/Audit/`. The new bespoke template `AttachmentSweepAuditDecorator` (9a.3) does NOT use the helper (batch shape doesn't fit `IRepository<T>`).

- **Test fixture per aggregate**: AIRiskAdvice + CoachingPrompt + ScannerFilter + AttachmentSweep use a focused helper `DbContext` (mirrors Wave 7's `TestTradingDbContext` pattern — `TradingDbContext` has Npgsql-specific array converters that fail on SQLite). **No Testcontainers Postgres** (sandbox carry-forward WARNING — Wave 5/6/7/8 precedent). Admin endpoint tests (9b.1) use `WebApplicationFactory` + Testcontainers Postgres (Wave 6 6f precedent) — same carry-forward WARNING; sandbox uses per-project test runs. BackgroundService tests use the Wave 4 4d `AttachmentLifecycleService.RunOnceAsync` exposed-for-tests pattern: `public async Task RunOnceAsync(CancellationToken ct)` so unit tests drive the loop deterministically. Retention service tests inject `FakeClock` (Wave 6 6d.2 precedent).

- **`size:exception` precedent**: Wave 5/6/7/8 all needed `size:exception` per slice. Wave 9 follows — expect `size:exception` for 9a.1 + 9a.2 + 9a.3 + 9b.1; 9b.2 is doc-only.

---

## Forecasting Rule Applied

Per Wave 7 verify-report SUGGESTION #2:

```
forecast = integration_scenarios + 2 * contract_scenarios_per_interface_surgery
```

**Interface surgery** = `IRepository<ScannerFilter>` extension adding `DeleteAsync` defensive stub (no real renames in Wave 9 — all 4 audited repos are bespoke shape).

| Slice | Decorators / Features | Integration | Contract | Surgery | Forecast |
|---|---|---:|---:|---:|---:|
| **9a.1** | AIRiskAdvice + CoachingPrompt (write-once) | 8 | 0 | 0 | **8** |
| **9a.2** | ScannerFilter (bespoke CRUD without Delete) | 4 | 1 (DeleteAsync defensive stub) | 1 | **6** |
| **9a.3** | AttachmentSweep (bespoke batch soft-delete) | 5 | 0 | 0 | **5** |
| **9b.1** | Audit query API + AuditRetention BackgroundService | 7 | 0 | 0 | **7** |
| **9b.2** | Reconciliation — SKIP TradeAttachmentUsage | 0 | 2 (rationale tests / source-inspection) | 0 | **2** |
| **Total** | 4 audited decorators + 2 features + 1 SKIP | **24** | **3** | **1** | **28 scenarios** |

ScannerFilter's `AddAsync` + `UpdateAsync` + 3 reads (forwarded) + cross-tenant `IsOwner` + 1 defensive stub = ~5 integration + 1 contract. No enum extension, no migration. The ~28 spec scenarios vs. the orchestrator's ~30 estimate — delta explained by bespoke write-once repos having smaller audit surfaces (AIRiskAdvice + CoachingPrompt each audit only one mutation method).

---

## Chained Delivery, Validation, and Rollback

| Slice | Boundary | LOC | Paths | Tests | size:exception | Validate | Rollback |
|---|---|---:|---:|---:|---|---|---|
| **9a.1** | Trading: AIRiskAdvice + CoachingPrompt (write-once) | ~600 | 10 | 8 | likely | `dotnet test --filter "FullyQualifiedName~AIRiskAdviceAudit\|CoachingPromptAudit\|AIRiskAdviceRepositoryIntegration\|CoachingPromptRepositoryIntegration"` + full cumulative suite | `git revert` the slice. DI registration removed. `audit.events` has no rows for AIRiskAdvice / CoachingPrompt. |
| **9a.2** | Trading: ScannerFilter (CRUD without Delete) | ~450 | 8 | 6 | likely | `dotnet test --filter "FullyQualifiedName~ScannerFilterAudit\|ScannerFilterRepositoryIntegration\|IScannerFilterRepositoryContractTests"` + full cumulative suite | `git revert` the slice. The `DeleteAsync` defensive stub on `IScannerFilterRepository` reverts (interface goes back to original shape). DI registration removed. `audit.events` has no rows for ScannerFilter. |
| **9a.3** | Trading: AttachmentSweep (NEW batch soft-delete) | ~350 | 6 | 5 | likely | `dotnet test --filter "FullyQualifiedName~AttachmentSweepAudit\|AttachmentSweepRepositoryIntegration"` + full cumulative suite | `git revert` the slice. DI registration removed. `audit.events` has no rows for TradeAttachment. The inner repo's `SoftDeleteBatchAsync` still works without audit. |
| **9b.1** | Identity + Admin: Query API + retention BackgroundService | ~1,000 | 12 | 7 | likely | `dotnet test --filter "FullyQualifiedName~AuditEventQueryStore\|ListAuditEvents\|AdminAuditEndpoints\|AuditRetention"` + full cumulative suite | `git revert` the slice. Endpoint unmapped; `MapAdminAuditEndpoints()` not called in `Program.cs`. BackgroundService not registered. `audit.events` continues to grow unbounded (operational risk documented in Wave 10 backlog drain). |
| **9b.2** | Reconciliation — SKIP TradeAttachmentUsage | ~50 | 2 | 0 | no | `git grep` 2 verification commands + spec REMOVED Requirements present | `git revert` the slice. Doc-only changes revert. Zero behavior change. |
| **Total** | 5 slices chained | **~2,450** | **38** | **~26 tests / ~28 spec scenarios** | 4 likely + 0 unlikely | — | — |

**Chain strategy**: `feature-branch-chain`. PR base = previous PR branch. Each PR merges into the previous PR's branch (or `feature/0a-identity-model` for the first slice). PR #1 targets `feature/0a-identity-model` (the Wave 8 archive commit `4f54013`).

| # | Branch | Base | Title |
|---|---|---|---|
| **#30** | `feature/wave9-trading-audit-write-once` (9a.1) | `feature/0a-identity-model` | Slice 9a.1 — `AIRiskAdviceAuditDecorator` + `CoachingPromptAuditDecorator` |
| **#31** | `feature/wave9-trading-audit-scanner-filter` (9a.2) | `feature/wave9-trading-audit-write-once` | Slice 9a.2 — `ScannerFilterAuditDecorator` (CRUD without Delete) |
| **#32** | `feature/wave9-trading-audit-attachment-sweep` (9a.3) | `feature/wave9-trading-audit-scanner-filter` | Slice 9a.3 — `AttachmentSweepAuditDecorator` (NEW batch soft-delete pattern) |
| **#33** | `feature/wave9-audit-query-retention` (9b.1) | `feature/wave9-trading-audit-attachment-sweep` | Slice 9b.1 — `AdminAuditEndpoints` + `IAuditEventQueryStore` + `AuditRetentionBackgroundService` |
| **#34** | `feature/wave9-skip-reconciliation` (9b.2) | `feature/wave9-audit-query-retention` | Slice 9b.2 — Reconciliation doc: SKIP `ITradeAttachmentUsageRepository` |

Chain integrity: 5 PRs total. Each PR targets the previous PR's branch. Order matches slice order. No PR targets `main` directly.

### Per-slice detail

#### Slice 9a.1 — Trading: AIRiskAdvice + CoachingPrompt (~600 LOC, ~10 paths, ~8 tests)

- **Phase 1** (RED — integration tests, 8 tests):
  - `AIRiskAdviceRepositoryIntegrationTests` (4 — `AddAsync` writes Created + `IsOwner`, cross-tenant `AddAsync` emits Denied + throws, `FindByUserAndTradeAsync` not audited, no Update/Delete methods exist — contract pin).
  - `CoachingPromptRepositoryIntegrationTests` (4 — same shape: `AddAsync` writes Created + `IsOwner`, cross-tenant Denied + throws, `FindByUserAndDateAsync` + `ListByUserAndWindowAsync` not audited, no Update/Delete — contract pin).
- **Phase 2** (GREEN):
  - `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AIRiskAdviceAuditDecorator.cs` (~120 LOC, bespoke write-once — mirrors `StripeCustomerAuditDecorator` shape; only `AddAsync` wraps; `IsOwner` on `advice.UserId`; reads forwarded without audit).
  - `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/CoachingPromptAuditDecorator.cs` (~120 LOC, bespoke write-once — same shape as AIRiskAdvice).
- **Phase 3** (DI wiring):
  - `services.Decorate<IAIRiskAdviceRepository, AIRiskAdviceAuditDecorator>()` in `TradingModuleRegistration.cs`.
  - `services.Decorate<ICoachingPromptRepository, CoachingPromptAuditDecorator>()` in `TradingModuleRegistration.cs`.
- **Phase 4** Validate: 8 new tests pass. Full cumulative suite (Wave 8 baseline + 8) zero regression.
- **Dependencies**: none (first slice in chain).
- **Rollback**: `git revert` the slice. DI registration removed. `audit.events` has no rows for AIRiskAdvice / CoachingPrompt.

#### Slice 9a.2 — Trading: ScannerFilter (~450 LOC, ~8 paths, ~6 tests)

- **Phase 1** (RED — contract + integration tests, 6 tests):
  - `IScannerFilterRepositoryContractTests.DeleteAsync_IsNotOnInterface_DefensiveStub_Throws` (1 — pins the `IRepository<ScannerFilter>` extension's defensive stub behavior; matches Wave 7 7a.1 UserAuditDecorator precedent for non-deletable aggregates).
  - `ScannerFilterRepositoryIntegrationTests` (5 — `AddAsync` writes Created + `IsOwner`, `UpdateAsync` writes Updated with before/after diff + `IsOwner`, cross-tenant `UpdateAsync` emits Denied + throws, `GetByIdAsync` + `GetByUserAndNameAsync` + `ListByUserAsync` not audited, `DeleteAsync` throws `NotSupportedException` + emits Failed).
- **Phase 2** (Surface extension): `IScannerFilterRepository` extended with `DeleteAsync(ScannerFilter, ct)` defensive stub (throws `NotSupportedException` + emits `AuditAction.Failed`). The canonical `IRepository<T>` shape requires it; the aggregate forbids hard-delete (deactivation via `IsActive = false` via `Deactivate(IClock)`). Verify NO handler calls `DeleteAsync` on `IScannerFilterRepository` before extending.
- **Phase 3** (GREEN): `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/ScannerFilterAuditDecorator.cs` (~180 LOC, bespoke — implements `IScannerFilterRepository` directly; wraps `AddAsync` + `UpdateAsync` via the extended `IRepository<ScannerFilter>` shape + `DecoratedRepository<ScannerFilter>` helper; `IsOwner` on `filter.UserId`; `DeleteAsync` defensive stub delegates to inner which throws).
- **Phase 4** (DI wiring): `services.Decorate<IScannerFilterRepository, ScannerFilterAuditDecorator>()` in `TradingModuleRegistration.cs`.
- **Phase 5** Validate: 6 new tests pass. Full cumulative suite (9a.1 baseline + 6) zero regression.
- **Dependencies**: 9a.1 must be merged (shares `TestTradingDbContext` fixture pattern).
- **Rollback**: `git revert` the slice. The `DeleteAsync` defensive stub on `IScannerFilterRepository` reverts (interface goes back to original shape). DI registration removed. `audit.events` has no rows for ScannerFilter.

#### Slice 9a.3 — Trading: AttachmentSweep (NEW batch soft-delete) (~350 LOC, ~6 paths, ~5 tests)

- **Phase 1** (RED — integration tests, 5 tests):
  - `AttachmentSweepRepositoryIntegrationTests` (5 — `SoftDeleteBatchAsync` with N ids emits N audit rows (one per id, not one batch), `SoftDeleteBatchAsync` with a cross-tenant id emits Denied for that id + throws `UnauthorizedAccessException` for the whole batch, `GetExpiredBatchAsync` + `GetUserAggregateAsync` + `GetActiveUserIdsAsync` not audited, `InsertAuditAsync` not audited, `ChangesJson` for soft-deleted attachments includes `IsActive: {before:true, after:false}`).
- **Phase 2** (GREEN): `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AttachmentSweepAuditDecorator.cs` (~150 LOC, bespoke — wraps `SoftDeleteBatchAsync` only; emits 1 audit row per id in the batch with `EntityType = "TradeAttachment"`; cross-tenant `IsOwner` per id via loaded `attachment.UserId`; `InsertAuditAsync` forwarded without audit; 3 reads forwarded without audit). NEW pattern — first "1-call-many-audit-rows" decorator in the codebase.
- **Phase 3** (DI wiring): `services.Decorate<IAttachmentSweepRepository, AttachmentSweepAuditDecorator>()` in `TradingModuleRegistration.cs`.
- **Phase 4** Validate: 5 new tests pass. Full cumulative suite (9a.2 baseline + 5) zero regression. The `AttachmentLifecycleService.RunOnceAsync` (the only caller of `SoftDeleteBatchAsync`) is unchanged — the decorator wraps transparently.
- **Dependencies**: 9a.2 must be merged (shares `TestTradingDbContext` fixture pattern).
- **Rollback**: `git revert` the slice. DI registration removed. `audit.events` has no rows for TradeAttachment. The inner repo's `SoftDeleteBatchAsync` still works without audit.

#### Slice 9b.1 — Identity + Admin: Query API + retention BackgroundService (~1,000 LOC, ~12 paths, ~7 tests)

- **Phase 1** (RED — query store tests, 3 tests):
  - `AuditEventQueryStoreTests` (3 — `ListAsync` with no filters returns newest-first, `ListAsync` with `entity_type` filter applies ix_audit_events_entity, `ListAsync` with `user_id` + `tenant_id` compound filter applies ix_audit_events_user + ix_audit_events_tenant_time).
- **Phase 2** (GREEN): `IAuditEventQueryStore` in `Admin.Application/Abstractions/` + `AuditEventQueryStore` in `Admin.Infrastructure/Persistence/` (~150 LOC, EF query against `AuditDbContext.AuditEvents`).
- **Phase 3** (RED — handler tests, 1 test):
  - `ListAuditEventsHandlerTests` (1 — DTO mapping + cursor encoding/decoding round-trip + limit clamping to [1, 200]).
- **Phase 4** (GREEN): `ListAuditEventsQuery` + `ListAuditEventsHandler` + `AuditEventDto` + `PagedAuditEventsDto` in `Admin.Application/Features/Audit/ListAuditEvents/` (~150 LOC).
- **Phase 5** (RED — endpoint tests, 1 test):
  - `AdminAuditEndpointsIntegrationTests` (1 — anonymous returns 401; trader role returns 403; admin role with filters returns 200 + paginated payload; uses `WebApplicationFactory` + Testcontainers Postgres).
- **Phase 6** (GREEN): `AdminAuditEndpoints.cs` in `Admin.Api/Endpoints/` (~120 LOC, `MapGroup("/api/admin/audit/events").RequireAuthorization("AdminOnly").MapGet("/", ListAsync).RequireRateLimiting("api-general")`).
- **Phase 7** (RED — retention tests, 2 tests):
  - `AuditRetentionServiceTests` (1 — `PurgeOldAsync(cutoff, batchLimit)` deletes only rows with `occurred_at < cutoff`, idempotent re-run returns 0 rows, `BatchLimit` caps the delete).
  - `AuditRetentionBackgroundServiceTests` (1 — first run after `InitialDelay`, exception in `RunOnceAsync` is logged + does not crash host).
- **Phase 8** (GREEN): `IAuditRetentionService` + `AuditRetentionService` + `IAuditRetentionBackgroundService` (alias for testability) + `AuditRetentionBackgroundService` + `AuditRetentionOptions` in `Identity.Infrastructure/Audit/` + `Configuration/AuditRetentionOptions.cs` (~200 LOC).
- **Phase 9** (DI + config wiring):
  - `services.Configure<AuditRetentionOptions>(Configuration.GetSection("AuditRetention"))` in `IdentityModuleRegistration.cs` (with `ValidateOnStart`).
  - `services.AddHostedService<AuditRetentionBackgroundService>()` in `IdentityModuleRegistration.cs`.
  - `services.AddScoped<IAuditEventQueryStore, AuditEventQueryStore>()` + `services.AddMediatR(...)` handler registration in `AdminModuleRegistration.cs` (NEW csproj pattern).
  - Verify `Admin.Infrastructure.csproj` has `<ProjectReference Include="..\..\Identity\JadeCapital.Identity.Infrastructure\JadeCapital.Identity.Infrastructure.csproj" />`; add if missing.
  - `app.MapAdminAuditEndpoints()` in `Program.cs` (after `app.UseAuthentication()` + `app.UseAuthorization()`).
- **Phase 10** Validate: 7 new tests pass (3 store + 1 handler + 1 endpoint + 2 retention). Full cumulative suite (9a.3 baseline + 7) zero regression.
- **Dependencies**: 9a.3 must be merged.
- **Rollback**: `git revert` the slice. Endpoint unmapped; `MapAdminAuditEndpoints()` not called in `Program.cs`. BackgroundService not registered. `audit.events` continues to grow unbounded (operational risk documented in Wave 10 backlog drain).

#### Slice 9b.2 — Reconciliation: SKIP TradeAttachmentUsage (~50 LOC, ~2 paths, 0 tests)

Doc-only slice. NO code changes; NO new tests; just the rationale baked into `tasks.md` + the spec REMOVED Requirements + this proposal's Out of Scope.

- **Phase 1** (2 verification commands):
  - [ ] 1.1 `git grep -E "Task (Add|Update|Delete)Async" src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/ITradeAttachmentUsageRepository.cs` → no matches (proves SKIP: no mutations to audit).
  - [ ] 1.2 Add the SKIP rationale to `openspec/changes/2026-08-19-wave9-audit-finalization/proposal.md` §"Out of Scope (Wave 9 SKIP — documented, not deferred)" + `specs/soft-delete-audit/spec.md` §"## REMOVED Requirements" (with `Reason:` block per OpenSpec convention) + `<remarks>` XML doc on `ITradeAttachmentUsageRepository.cs` (matches Wave 8 8b.2 SKIP pattern).
- **Validate**: 2 commands pass + spec REMOVED Requirements present + `<remarks>` on the interface. Zero behavior change.
- **Dependencies**: 9b.1 must be merged.
- **Rollback**: `git revert` the slice. Doc-only changes revert. Zero behavior change.

---

## Critical Questions for User

These decisions require explicit user acceptance before the proposal moves to specs/design. The orchestrator should surface them before launching sdd-spec.

| # | Decision | Recommendation | Why |
|---|---|---|---|
| 1 | `ITradeAttachmentUsageRepository` SKIP rationale | **SKIP**. No mutation methods on interface. The right seam for `TradeAttachment` audit IS `IAttachmentSweepRepository.SoftDeleteBatchAsync` (sub-scope A #4). Wrapping here would be a no-op on the audit-write path. | Explore §"5. ITradeAttachmentUsageRepository" + Wave 8 8b.2 SKIP precedent |
| 2 | Audit query API + retention as NEW capabilities vs MODIFY `soft-delete-audit`? | **NEW separate specs** for `audit-query-api` + `audit-retention-policy`; MODIFY only `soft-delete-audit` for the 4 decorators + 1 SKIP. | Query API + retention have different owners (Admin vs Identity), different compliance surfaces (read vs write vs lifecycle), different change cadences. Conflating them in `soft-delete-audit` makes the spec a god-object. Wave 6/7/8 treated `soft-delete-audit` as the write-path spec only. |
| 3 | Admin endpoint location: `Admin.Api` vs `Identity.Api`? | **`Admin.Api`** | Admin role gate already exists; csprojs exist; Identity.Api is for trader-facing endpoints (audit logs are operational / compliance tooling, not trader UX); PII exposure is admin-only by design |
| 4 | `AttachmentSweepAuditDecorator` `EntityType` value? | **`"TradeAttachment"`** (the aggregate, not `"AttachmentSweep"` or `"AttachmentLifecycleService"`) | The audited mutation IS a soft-delete on `TradeAttachment`. The sweep is the seam; the aggregate is the entity. This makes `audit.events` query semantics align with "what entity changed" not "which service made the change". |
| 5 | `AttachmentSweepAuditDecorator` batch shape — 1 audit row per id or 1 row per batch? | **1 audit row per id** (NOT 1 batch row with `EntityCount = N`) | Per-id rows preserve the existing audit-event shape (1 row per mutation); make per-entity query filters work; match Wave 6/7/8 precedent. The 1-row-per-batch alternative is post-Wave 10 (explicitly deferred in Wave 6/7/8 Out of Scope). |
| 6 | `AuditRetentionOptions` default values? | **`RetentionDays = 90`, `CleanupIntervalHours = 24`, `BatchLimit = 10000`, `InitialDelay = 2 minutes`** | 90-day retention matches common compliance defaults (GDPR-style data minimization); daily tick balances freshness vs overhead; 10k batch limit caps single-transaction lock time; 2-min startup settle matches `BackfillTenantsHostedService` precedent. All overridable via `appsettings.json`. |
| 7 | Retention backlog drain strategy? | **Daily trickle (10k rows/day). 3M-row backlog takes 300 days to drain. NO one-shot script in Wave 9.** | `ExecuteDeleteAsync` is safe but slow; one-shot script is operational tooling (NOT in Wave 9 scope); if first deploy has > 1M backlog rows, orchestrator should flag for Wave 10 backlog drain task OR adjust `BatchLimit` upward in `appsettings.json` |
| 8 | `AuditRetentionOptions.ValidateOnStart`? | **YES** | `RetentionDays > 0`, `CleanupIntervalHours > 0`, `BatchLimit > 0`. Fail fast at startup if config is invalid (Wave 6 6c.2 `JwtOptions` precedent). |
| 9 | Admin endpoint rate limiting? | **`RequireRateLimiting("api-general")`** (matches Wave 8 8b.1 AdminSubscriptionEndpoints pattern) | No special quota for audit queries — they're not the hot path. If profiling shows abuse, Wave 10+ can add a per-Admin-role quota. |
| 10 | `IAuditEventQueryStore` location: `Admin.Application` vs `Identity.Application`? | **`Admin.Application`** (with EF impl in `Admin.Infrastructure`) | Admin module is the consumer; Identity is the audit-write owner. The query store abstracts the read side for Admin, keeping Admin independent of Identity's internal `AuditDbContext`. Cross-module reference (`Admin.Infrastructure` → `Identity.Infrastructure`) is OK — the csproj chain exists. |

**No new ambiguities beyond these 10 decisions**. Each maps to a concrete Wave 9 slice + a specific code path. All decisions recorded here so future readers / Wave 10 sessions know they were deliberate.

---

## Migration Path

**No schema migration required.** The decorator pattern is additive — `audit.events` table already exists (Wave 6 migration 0027). Wave 7's migration 0029 (widening the `ck_audit_events_action` CHECK constraint to `IN (0,1,2,3,4,5)`) already covers all 4 new decorators' audit-action needs. No new columns. No new table. No new index.

**No data migration required.** The `audit.events` table starts receiving `AIRiskAdvice` / `CoachingPrompt` / `ScannerFilter` / `TradeAttachment` rows from the moment the deployment completes (slice 9a.1, 9a.2, 9a.3 each adds 1+ decorator; first audit rows land at slice 9a.1 deploy). Historical mutations (pre-Wave 9) are not backfilled — the audit log is forward-only.

**Retention is purely a DELETE operation.** No retention column, no partition strategy, no TTL. The first retention run happens 2 minutes after the first deploy of slice 9b.1. Rows older than `RetentionDays` (90 default) are deleted via EF Core 9 `ExecuteDeleteAsync` in batches of `BatchLimit` (10000 default). Idempotent + safe re-run.

**No interface-level deprecation period.** The 1 surface change (`IScannerFilterRepository` gains a defensive `DeleteAsync` stub that throws `NotSupportedException`) is additive in slice 9a.2. No consumers are affected (verified via `git grep` BEFORE the extension). The 4 new decorators register via `services.Decorate<IXxxRepository, XxxAuditDecorator>()` — no DB schema changes.

---

## Dependencies and Risks

### Dependencies (non-negotiable, from Wave 6/7/8 precedent)

- **Strict TDD** per `openspec/config.yaml` `apply.tdd: true`. RED tests FIRST for every new decorator + query handler + retention service. The 8 integration test files (4 decorators × ~5 scenarios + query store 3 + handler 1 + endpoint 1 + retention 2) follow the existing `AccountRepositoryIntegrationTests` + `StripeCustomerRepositoryIntegrationTests` pattern (SQLite-in-memory + raw SQL `CREATE TABLE IF NOT EXISTS events ...` workaround for EF Core 9 SQLite `EnsureCreated` all-or-nothing gotcha).
- **`size:exception`** likely needed for 9a.1 + 9a.2 + 9a.3 + 9b.1 (Wave 5/6/7/8 precedent — all accepted). 9b.2 is doc-only (no exception needed).
- **400-line PR budget**: 9a.1 = 10 paths, 9a.2 = 8, 9a.3 = 6, 9b.1 = 12, 9b.2 = 2. The 4 coverage/feature slices exceed the 400-line PR review budget per Wave 7 7a.1 (1412 LOC) / 7b.1 (1699 LOC) / Wave 8 8a.1 (~700 LOC) precedent. `size:exception` per slice is the expected resolution.
- **Scrutor 4.2.2** is already explicit in Trading + Billing csprojs (Wave 7 7a.0 + verified-no-op in Wave 8 8a.0). No csproj changes needed in Wave 9 sub-scope A.
- **Testcontainers Postgres** carry-forward WARNING (Wave 5/6/7/8 verify-reports): sandbox constraint; endpoint test in 9b.1 may not run locally without Docker. Per-project test runs workaround.

### Risks

| # | Risk | Likelihood | Mitigation |
|---|---|---|---|
| 1 | `ITradeAttachmentUsageRepository` SKIP rationale not convincing to future reviewer | Low | 4-line rationale grounded in `git grep` evidence (no mutations on interface) + Wave 8 8b.2 SKIP precedent (`IStripeWebhookEventRepository`) + alternative considered + rejected (audit at `AttachmentSweep` instead). Documented in proposal Out of Scope + spec REMOVED Requirements + `<remarks>` on the interface. |
| 2 | New capabilities `audit-query-api` + `audit-retention-policy` split vs MODIFY conflation | Low | Decision documented as Critical Question #2 with the recommendation + rationale. The orchestrator surfaces it for explicit user acceptance. The split keeps `soft-delete-audit` as the write-path spec only. |
| 3 | Admin endpoint PII exposure via `changes` JSONB | Low | `RequireAuthorization("AdminOnly")` + `RequireAdminPolicyHandler` enforce role check BEFORE any DB lookup. No trader / anonymous access ever. `user_id` + `tenant_id` are internal Guids (not PII like email/name); `changes` JSONB is admin-visible by design (compliance contract). Mirrors `AdminSubscriptionEndpoints` precedent. |
| 4 | Admin endpoint location dispute (Admin.Api vs Identity.Api) | Low | Decision documented as Critical Question #3 with the recommendation + 5-bullet rationale. The orchestrator surfaces it for explicit user acceptance. |
| 5 | `AttachmentSweepAuditDecorator` 1-call-many-audit-rows pattern surprises future reviewer | Med | NEW pattern — first batch-soft-delete decorator in the codebase. `design.md` section documents the rationale (single-call N-audit-rows, `EntityType = "TradeAttachment"`, per-id cross-tenant `IsOwner`). Per-decorator test fixture verifies the N-row emission + cross-tenant isolation. |
| 6 | `ScannerFilter.IsActive = false` transition NOT flagged as soft-delete | Low | The `Deactivate(IClock)` mutator happens INSIDE `UpdateAsync` and is captured as a normal `Updated` event. No `IsTerminated` reflection rule matches `ScannerFilter.IsActive` (the Wave 6 rule only matches `Status ∈ {Cancelled, Terminated, Expired}` enum values). The transition is queryable via the `AuditAction.Updated` event + the `changes` JSONB (`IsActive: {before:true, after:false}`). Documented in slice 9a.2 Phase 1 + Decision rationale. |
| 7 | `IScannerFilterRepository` `DeleteAsync` defensive stub breaks an existing handler | Low | `git grep -n "_scannerFilter.DeleteAsync\|IScannerFilterRepository.*Delete" src/` BEFORE extending. Expected: 0 matches (the aggregate docstring says "ScannerFilter has no DeleteAsync — deactivate via IsActive = false"). If a match exists, update the handler in slice 9a.2 atomically. |
| 8 | Retention backlog drain time (300 days for 3M rows) | Med | Initial deploy may have > 1M accumulated rows. The orchestrator should either (a) increase `BatchLimit` in `appsettings.json` to e.g. 100000 for the first deploy then revert, OR (b) flag for a Wave 10 backlog drain task. NO one-shot script in Wave 9 scope. |
| 9 | Retention `BatchLimit` config too low → table grows faster than retention deletes | Low | Default `BatchLimit = 10000` + `CleanupIntervalHours = 24` = 10k rows/day deletion rate. Current growth rate (Wave 8 baseline) is ~2500 rows/day. Headroom is 4x. If user count grows 4x+ (or compliance scope widens), increase `BatchLimit` via `appsettings.json`. |
| 10 | Cursor pagination tiebreaker for same-`occurred_at` events | Low | The `(occurred_at DESC, id DESC)` keyset guarantees deterministic ordering even when `occurred_at` ties (rare batch inserts). Cursor format is opaque + base64. Test fixture verifies the tiebreaker (insert 2 events with the same `occurred_at` + different `id` → both pages return deterministic order). |
| 11 | Cross-module DI edge (`Admin.Infrastructure` → `Identity.Infrastructure` for `AuditDbContext`) | Low | Verify `<ProjectReference Include="..\..\Identity\JadeCapital.Identity.Infrastructure\JadeCapital.Identity.Infrastructure.csproj" />` exists in `Admin.Infrastructure.csproj`; add in 9b.1 Phase 9 if missing. The csproj chain via `JadeCapital.Host.csproj` + the `Admin.Api.csproj` reference list already exists for handlers (verified in Wave 8 8b.1). |
| 12 | `Admin.Application` + `Admin.Infrastructure` empty folders | Low | Today only `JadeCapital.Host/Program.cs` references the 3 Admin csprojs. The csprojs exist but the source folders have no code yet. Wave 9 9b.1 creates the first files in these folders. Pattern precedent is `JadeCapital.Admin.Api/Endpoints/AdminSubscriptionEndpoints.cs` (already populated in Wave 0). |
| 13 | EF Core 9 SQLite `EnsureCreated` all-or-nothing gotcha | Med | Every new integration test must include the `CREATE TABLE IF NOT EXISTS events ...` raw SQL workaround (Wave 6 6d.2 fixture fix). The 4 new test files (AIRiskAdvice, CoachingPrompt, ScannerFilter, AttachmentSweep) copy the pattern verbatim. |
| 14 | Testcontainers NOT available in sandbox | Carry-forward | Decorator + retention tests use SQLite-in-memory per Wave 6/7/8 precedent. Admin endpoint test depends on Docker/Postgres — same carry-forward WARNING as Wave 8; sandbox uses per-project test runs. |
| 15 | `AuditAction.Denied` + `AuditAction.Failed` enum values | Low | Already present (Wave 7 7a.1 added `Denied = 4` + `Failed = 5`; migration 0029 widened the CHECK constraint). The 4 new decorators reuse these values for cross-tenant isolation + `ScannerFilter.DeleteAsync` defensive stub. **NO new enum extension, NO new migration.** |
| 16 | `AuditDbContext` schema collision risk | Low | All 4 new typed decorators share `audit.events` table. `EntityType` is the discriminator (`"AIRiskAdvice"`, `"CoachingPrompt"`, `"ScannerFilter"`, `"TradeAttachment"`). No collision with the 15 existing Wave 6/7/8 entities (all 19 are distinct). |
| 17 | `TradingDbContext` array mapping fails on SQLite | Med | AIRiskAdvice + CoachingPrompt + ScannerFilter + AttachmentSweep integration tests use focused helper `DbContext`s (mirrors Wave 7's `TestTradingDbContext` pattern). Npgsql-specific array columns (e.g., `ScannerFilter.Instruments`) are omitted from the test mapping. |
| 18 | `size:exception` precedent | Likely | Wave 5/6/7/8 all accepted. Wave 9 follows — expect `size:exception` for 9a.1 + 9a.2 + 9a.3 + 9b.1. 9b.2 is doc-only (no exception needed). |

---

## Affected Areas

| Area | Impact | Description |
|---|---|---|
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AIRiskAdviceAuditDecorator.cs` | New | Bespoke write-once decorator — mirrors `StripeCustomerAuditDecorator` shape; `IsOwner` on `advice.UserId` (9a.1). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/CoachingPromptAuditDecorator.cs` | New | Bespoke write-once decorator — mirrors `StripeCustomerAuditDecorator` shape; `IsOwner` on `prompt.UserId` (9a.1). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/ScannerFilterAuditDecorator.cs` | New | Bespoke CRUD-without-Delete decorator — `AddAsync` + `UpdateAsync` wrapped via `DecoratedRepository<ScannerFilter>`; `DeleteAsync` defensive stub (9a.2). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AttachmentSweepAuditDecorator.cs` | New | NEW bespoke batch soft-delete decorator — 1 audit row per id; per-id `IsOwner`; `InsertAuditAsync` forwarded without audit (9a.3). |
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/IScannerFilterRepository.cs` | Modified | Extended with `DeleteAsync(ScannerFilter, ct)` defensive stub (throws `NotSupportedException`) + `<remarks>` XML doc noting the deactivation-via-`IsActive` rationale (9a.2). |
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/ITradeAttachmentUsageRepository.cs` | Modified | Add `<remarks>` XML doc noting the read-only rationale + Wave 9 SKIP (9b.2). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs` | Modified | 4× `services.Decorate<IXxxRepository, XxxAuditDecorator>()` calls (9a.1, 9a.2, 9a.3). |
| `src/2.Modules/Admin/JadeCapital.Admin.Infrastructure/Persistence/AuditEventQueryStore.cs` | New | EF impl of `IAuditEventQueryStore` against `AuditDbContext.AuditEvents` (~150 LOC) (9b.1). |
| `src/2.Modules/Admin/JadeCapital.Admin.Application/Abstractions/IAuditEventQueryStore.cs` | New | Admin-side abstraction for the audit-events read surface (9b.1). |
| `src/2.Modules/Admin/JadeCapital.Admin.Application/Features/Audit/ListAuditEvents/ListAuditEventsQuery.cs` | New | MediatR query + handler + DTOs (9b.1). |
| `src/2.Modules/Admin/JadeCapital.Admin.Api/Endpoints/AdminAuditEndpoints.cs` | New | `MapGroup("/api/admin/audit/events").RequireAuthorization("AdminOnly").MapGet("/", ListAsync)` (~120 LOC) (9b.1). |
| `src/2.Modules/Admin/JadeCapital.Admin.Infrastructure/JadeCapital.Admin.Infrastructure.csproj` | Modified | Verify + add `<ProjectReference Include="..\..\Identity\JadeCapital.Identity.Infrastructure\JadeCapital.Identity.Infrastructure.csproj" />` if missing (9b.1). |
| `src/2.Modules/Admin/JadeCapital.Admin.Infrastructure/DependencyInjection/AdminModuleRegistration.cs` | New | DI wiring for `IAuditEventQueryStore` + MediatR handler registration (NEW csproj pattern; csproj already exists) (9b.1). |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/IAuditRetentionService.cs` | New | Cross-cutting concern abstraction for retention purge (9b.1). |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/AuditRetentionService.cs` | New | EF impl using `AuditDbContext.AuditEvents.Where(...).ExecuteDeleteAsync(ct)` (9b.1). |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/AuditRetentionBackgroundService.cs` | New | `BackgroundService` with 2-min `InitialDelay` + 24h `CleanupIntervalHours` + jitter + per-attempt isolation (9b.1). |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/Configuration/AuditRetentionOptions.cs` | New | Config POCO with `RetentionDays` / `CleanupIntervalHours` / `BatchLimit` / `InitialDelay` + `ValidateOnStart` (9b.1). |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/DependencyInjection/IdentityModuleRegistration.cs` | Modified | `services.Configure<AuditRetentionOptions>(Configuration.GetSection("AuditRetention"))` + `services.AddHostedService<AuditRetentionBackgroundService>()` (9b.1). |
| `src/2.Modules/Host/JadeCapital.Host/Program.cs` | Modified | `app.MapAdminAuditEndpoints()` call after `UseAuthentication` + `UseAuthorization` (9b.1). |
| `appsettings.json` | Modified | Add `"AuditRetention": { "RetentionDays": 90, "CleanupIntervalHours": 24, "BatchLimit": 10000 }` (9b.1). |
| `tests/UnitTests/JadeCapital.Trading.UnitTests/Persistence/AIRiskAdviceRepositoryIntegrationTests.cs` | New | 4 scenarios (9a.1). |
| `tests/UnitTests/JadeCapital.Trading.UnitTests/Persistence/CoachingPromptRepositoryIntegrationTests.cs` | New | 4 scenarios (9a.1). |
| `tests/UnitTests/JadeCapital.Trading.UnitTests/Persistence/ScannerFilterRepositoryIntegrationTests.cs` | New | 5 scenarios (9a.2). |
| `tests/UnitTests/JadeCapital.Trading.UnitTests/Persistence/IScannerFilterRepositoryContractTests.cs` | New | 1 contract scenario (9a.2). |
| `tests/UnitTests/JadeCapital.Trading.UnitTests/Persistence/AttachmentSweepRepositoryIntegrationTests.cs` | New | 5 scenarios (9a.3). |
| `tests/UnitTests/JadeCapital.Admin.UnitTests/Persistence/AuditEventQueryStoreTests.cs` | New | 3 scenarios (9b.1). |
| `tests/UnitTests/JadeCapital.Admin.UnitTests/Features/Audit/ListAuditEventsHandlerTests.cs` | New | 1 scenario (9b.1). |
| `tests/IntegrationTests/JadeCapital.Admin.IntegrationTests/Endpoints/AdminAuditEndpointsIntegrationTests.cs` | New | 1 scenario — anonymous 401 / trader 403 / admin 200 (9b.1). |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Audit/AuditRetentionServiceTests.cs` | New | 1 scenario (9b.1). |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Audit/AuditRetentionBackgroundServiceTests.cs` | New | 1 scenario (9b.1). |
| `openspec/specs/soft-delete-audit/spec.md` | Modified | Delta spec: extend "Decorator coverage" requirement to include the 4 new aggregates + add REMOVED Requirements section for `ITradeAttachmentUsageRepository` (sdd-spec phase). |
| `openspec/specs/audit-query-api/spec.md` | New | Full spec for the admin query API (sdd-spec phase). |
| `openspec/specs/audit-retention-policy/spec.md` | New | Full spec for the retention background service (sdd-spec phase). |

---

## Success Criteria

1. **AIRiskAdvice audit (9a.1)**: `AddAsync(advice)` → `Created` audit row + `IsOwner` check on `advice.UserId`. Cross-tenant `AddAsync` → `Denied` event + `UnauthorizedAccessException`. `FindByUserAndTradeAsync` not audited. No Update/Delete methods exist (contract pin). `AIRiskAdviceRepositoryIntegrationTests` (4 scenarios) all pass.
2. **CoachingPrompt audit (9a.1)**: `AddAsync(prompt)` → `Created` + `IsOwner`. Cross-tenant → `Denied` + throw. `FindByUserAndDateAsync` + `ListByUserAndWindowAsync` not audited. No Update/Delete methods exist. `CoachingPromptRepositoryIntegrationTests` (4 scenarios) all pass.
3. **ScannerFilter audit (9a.2)**: `AddAsync(filter)` → `Created` + `IsOwner`. `UpdateAsync(filter)` → `Updated` with diff + `IsOwner`. Cross-tenant update → `Denied` + throw. `DeleteAsync(filter)` → throws `NotSupportedException` + emits `Failed`. 3 reads not audited. `ScannerFilterRepositoryIntegrationTests` (5 scenarios) + `IScannerFilterRepositoryContractTests` (1 scenario) all pass.
4. **AttachmentSweep audit (9a.3)**: `SoftDeleteBatchAsync(ids)` with N ids → N audit rows (`EntityType = "TradeAttachment"`, `ChangesJson = { "IsActive": { "before": true, "after": false } }`). Cross-tenant id in the batch → `Denied` for that id + `UnauthorizedAccessException` for the whole batch. 3 reads not audited. `InsertAuditAsync` not audited. `AttachmentSweepRepositoryIntegrationTests` (5 scenarios) all pass.
5. **Admin query API (9b.1)**: Anonymous `GET /api/admin/audit/events` → 401. Trader role → 403. Admin role with filters → 200 + paginated payload (`items[]`, `next_cursor`, `has_more`). Cursor pagination produces keyset-correct next page. `Limit` clamped to [1, 200]. `AdminAuditEndpointsIntegrationTests` (1 scenario) + `AuditEventQueryStoreTests` (3 scenarios) + `ListAuditEventsHandlerTests` (1 scenario) all pass.
6. **Audit retention (9b.1)**: `PurgeOldAsync(cutoff, batchLimit)` deletes only rows with `occurred_at < cutoff`, idempotent re-run returns 0 rows, `BatchLimit` caps the delete. `AuditRetentionBackgroundService` first run after `InitialDelay = 2min`, exception in `RunOnceAsync` is logged + does not crash host. `AuditRetentionOptions` validation fails fast at startup on invalid config. `AuditRetentionServiceTests` (1 scenario) + `AuditRetentionBackgroundServiceTests` (1 scenario) all pass.
7. **SKIP reconciliation (9b.2)**: `ITradeAttachmentUsageRepository` has no mutation methods (verified via `git grep`). `<remarks>` XML doc on the interface documents the SKIP rationale. Spec REMOVED Requirements section present with `Reason:` block.
8. **Build green**: `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings (vs Wave 8 baseline of 3 pre-existing CA2263).
9. **Test cumulative**: Wave 8 baseline + ~26 (Wave 9 integration + contract tests) = N+26 tests pass. The ~28 spec scenarios vs. the ~26 implementation tests reflects that bespoke write-once repos have smaller audit surfaces (AIRiskAdvice + CoachingPrompt each audit only one mutation method). Zero regressions across the entire BE suite.
10. **Audit log queryable via admin API**: `GET /api/admin/audit/events?entity_type=TradeAttachment&action=Deleted&from=2026-08-01T00:00:00Z&to=2026-08-19T23:59:59Z&limit=50` returns the full history of TradeAttachment soft-deletes in the date range, newest first, with cursor pagination. Performance characteristics observed at first deploy (query latency, index usage). For 1M+ row tables, follow-up covering index may be needed (out of Wave 9 scope).
11. **Audit retention live**: After 9b.1 deploy + 2-min startup settle, the first retention run purges all `audit.events` rows with `occurred_at < now - 90 days`. Subsequent runs every 24h with jitter. Idempotent + safe re-run (re-purging the same cutoff = 0 rows deleted).
12. **All 5 slices under 38 paths** (mandatory). 9a.1=10, 9a.2=8, 9a.3=6, 9b.1=12, 9b.2=2. `size:exception` expected for 9a.1 + 9a.2 + 9a.3 + 9b.1 (Wave 5/6/7/8 precedent).
13. **PR chain intact**: 5 PRs (#30-#34), each targeting the previous PR's branch. Order matches slice order. No PR targets `main` directly.

---

## Non-Goals and Later Waves

- **Wave 10**: Admin write-back API for `audit.events` (e.g. POST to annotate, PATCH to tag for compliance). Read-only query surface in Wave 9; write-back is Wave 10+.
- **Wave 10**: `AuditAction.Restored` end-to-end support (soft-delete restore command + admin tooling). The enum value is reserved (Wave 6); restore is admin tooling in Wave 10+.
- **Wave 10**: Soft-delete cascade propagation for the 4 newly-decorated aggregates (similar to `ImportJob` Wave 6 precedent). `AttachmentSweep` already cascades via `SoftDeleteBatchAsync`; the other 3 (AIRiskAdvice, CoachingPrompt, ScannerFilter) may need explicit cascade rules.
- **Wave 10**: Audit retention configuration UI for admin (per-tenant override, dashboard, "purge now" button). Wave 9 is config-driven via `appsettings.json` only.
- **Wave 10**: Audit log export (CSV / JSON) for compliance officers. Query API is the first compliance surface; export builds on top.
- **Wave 10**: Initial backlog drain one-shot script (for tenants with > 1M accumulated rows at first deploy of 9b.1 retention).
- **Wave 10+**: User-facing read API (`GET /api/audit/me`) for "my mutation history" tab in mobile + admin.
- **Wave 10+**: Free-text search on `changes` JSONB. Wave 9 query API supports structured filters only.
- **Wave 10+**: `audit.events` partitioning strategy (Postgres native partitioning by `tenant_id` or month) if profiling shows the need.
- **Wave 10+**: Per-tenant retention override (`AuditRetentionOptions.PerTenantRetentionDays` dictionary). Wave 9 is global default only.
- **Wave 11+**: Migration to a different audit sink (Kafka, S3, external SIEM). Post-1.0.
- **Wave 11+**: Cross-tenant audit log access (today: tenant-isolated; future: admin tooling for cross-tenant forensics).
- **Wave 11+**: Bulk audit events for `AddRangeAsync` (currently 1 row per entity; bulk path emits 1 batch row with `EntityCount = N`).

---

## Out of Scope (explicit scope boundary)

- **NOT** adding `ISoftDelete` to any of the 4 newly-decorated aggregates. `ScannerFilter` uses `IsActive = false` via `Deactivate(IClock)` (captured as `AuditAction.Updated` with `IsActive: {before:true, after:false}` diff). The other 3 emit straight `AuditAction.Updated` or `Created` on mutations — no soft-delete semantics to audit differently.
- **NOT** changing the `audit.events` schema. Migration 0027 from Wave 6 + migration 0029 from Wave 7 already cover `EntityType`, `EntityId`, `Action`, `TenantId`, `UserId`, `Changes JSONB`, `OccurredAt`. No new columns. No new table. No new index.
- **NOT** changing the `AuditAction` enum. Wave 7's `Denied = 4` + `Failed = 5` already cover all 4 new decorators' audit-action needs (cross-tenant isolation + `ScannerFilter.DeleteAsync` defensive stub).
- **NOT** changing the `DecoratedRepository<T>` core implementation. The helper is unchanged from Wave 7 7a.0.
- **NOT** changing `AuditDbContext` / `AuditLogger` / `NoOpAuditLogger` ownership (stays in `Identity.Infrastructure`).
- **NOT** addressing the `ScannerFilter.Instruments` array mapping in production code (stays as Npgsql-specific). The Wave 9 integration tests use focused helper `DbContext`s that omit the array column.
- **NOT** adding `Restored` action path (deletion is one-way today; restore is admin tooling in Wave 10+).
- **NOT** unifying the per-aggregate decorator pattern. Each typed decorator is bespoke to its aggregate's mutation surface (AIRiskAdvice + CoachingPrompt are write-once; ScannerFilter is CRUD-without-Delete; AttachmentSweep is the new batch soft-delete pattern). Unification is post-Wave 9 if patterns stabilize.
- **NOT** auditing `ITradeAttachmentUsageRepository` (the documented SKIP). See "Out of Scope (Wave 9 SKIP — documented, not deferred)" above.
- **NOT** adding a retention one-shot drain tool for backlogs (operational tooling — Wave 10+).
- **NOT** adding free-text search on `changes` JSONB (structured filters only in Wave 9; free-text is Wave 10+).
- **NOT** adding per-tenant retention override (global default only in Wave 9; per-tenant is Wave 10+).