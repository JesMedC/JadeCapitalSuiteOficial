# Tasks — Wave 9 (Audit Finalization: 4 Decorators + Query API + 90-Day Retention)

## Review Workload Forecast

| Slice | Boundary | LOC | Paths | Tests | size:exception preview | Bounded review |
|---|---|---:|---:|---:|---|:---:|
| **9a.1** | Trading: AIRiskAdvice + CoachingPrompt (write-once) | ~600 | 8 | 6 | likely (Wave 5/6/7/8 precedent) | OK |
| **9a.2** | Trading: ScannerFilter (CRUD without Delete) | ~450 | 5 | 5 | likely (Wave 5/6/7/8 precedent) | OK |
| **9a.3** | Trading: AttachmentSweep (NEW batch soft-delete) | ~350 | 5 | 5 | likely (Wave 5/6/7/8 precedent) | OK |
| **9b.1** | Identity + Admin: Query API + retention BackgroundService | ~1000 | 12 | 8 | likely (Wave 5/6/7/8 precedent) | OK |
| **9b.2** | Reconciliation — SKIP TradeAttachmentUsage (doc-only) | ~50 | 2 | 0 | no (doc-only) | OK |
| **Total** | 5 slices chained | **~2,450** | **32** | **~24 new tests** | 4 likely + 0 unlikely + 1 no | All ≤ 32 OK |

Decision needed before apply: **Yes** (`size:exception` per slice expected per Wave 5/6/7/8 precedent — explicit user acceptance per slice recommended). The 400-line PR review budget is exceeded by every Wave 7/8 slice; the `feature-branch-chain` strategy keeps each PR's reviewer-load bounded at the PR-level scope of work.

Chained PRs recommended: **Yes** (5 PRs via `feature-branch-chain`; each targets the immediate previous PR branch). PR #30 targets `feature/0a-identity-model` (the Wave 8 archive commit `4f54013`).

400-line budget risk: **High** — each slice will exceed the 400-line PR review budget per Wave 7 7a.1 (1412 LOC) / 7b.1 (1699 LOC) / Wave 8 8a.1 (~700 LOC) / 8b.1 (~700 LOC) precedent. `size:exception` per slice is the expected resolution.

**Build**: `dotnet build JadeCapital.slnx --nologo --verbosity minimal`.
**No SQL harness** — no migration in Wave 9 (migration 0029 from Wave 7 already widened `ck_audit_events_action` to `IN (0,1,2,3,4,5)`).
**No FE changes** — all BE.

Cumulative target: **1365 (Wave 8) + 24 (Wave 9) = 1389** BE tests pass zero regression.

### Work Units (PR → test → runtime → rollback)

- 9a.1: `dotnet test --filter "FullyQualifiedName~AIRiskAdviceAudit|CoachingPromptAudit|AIRiskAdviceRepositoryIntegration|CoachingPromptRepositoryIntegration"`. Rollback: revert code; DI registration removed; `audit.events` has no rows for AIRiskAdvice / CoachingPrompt.
- 9a.2: `dotnet test --filter "FullyQualifiedName~ScannerFilterAudit|ScannerFilterRepositoryIntegration|IScannerFilterRepositoryContractTests"`. Rollback: revert code; the `DeleteAsync` defensive stub on `IScannerFilterRepository` reverts; DI registration removed; `audit.events` has no rows for ScannerFilter.
- 9a.3: `dotnet test --filter "FullyQualifiedName~AttachmentSweepAudit|AttachmentSweepRepositoryIntegration"`. Rollback: revert code; DI registration removed; `audit.events` has no rows for TradeAttachment. The inner repo's `SoftDeleteBatchAsync` still works without audit.
- 9b.1: `dotnet test --filter "FullyQualifiedName~AuditEventQueryStore|ListAuditEventsHandler|AdminAuditEndpoints|AuditRetentionService|AuditRetentionBackgroundService"`. Rollback: revert code; endpoint unmapped; `MapAdminAuditEndpoints()` not called in `Program.cs`; BackgroundService not registered; `audit.events` continues to grow unbounded (operational risk documented in Wave 10 backlog drain).
- 9b.2: `git grep` 2 verification commands + spec REMOVED Requirements present + `<remarks>` on the interface. Rollback: revert code; doc-only changes revert; zero behavior change.

---

## Slice 9a.1 — `AIRiskAdviceAuditDecorator` + `CoachingPromptAuditDecorator` (write-once) (~600 LOC, ~8 paths, ~6 tests)

### 9a.1 Backend (~600 LOC)

**Phase 1: `AIRiskAdviceAuditDecorator` (TDD)**

- [x] 1.1 RED test `AIRiskAdviceRepositoryIntegrationTests` (3 scenarios: `AddAsync` writes Created + `IsOwner` on `advice.UserId`; cross-tenant `AddAsync` emits Denied + throws `UnauthorizedAccessException`; `FindByUserAndTradeAsync` is not audited).
- [x] 1.2 GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AIRiskAdviceAuditDecorator.cs` (~120 LOC; bespoke write-once — mirrors `StripeCustomerAuditDecorator` shape; only `AddAsync` wraps; `IsOwner` on `advice.UserId`; `FindByUserAndTradeAsync` forwarded without audit).

**Phase 2: `CoachingPromptAuditDecorator` (TDD)**

- [x] 2.1 RED test `CoachingPromptRepositoryIntegrationTests` (3 scenarios: `AddAsync` writes Created + `IsOwner` on `prompt.UserId`; cross-tenant `AddAsync` emits Denied + throws `UnauthorizedAccessException`; `FindByUserAndDateAsync` + `ListByUserAndWindowAsync` are not audited).
- [x] 2.2 GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/CoachingPromptAuditDecorator.cs` (~120 LOC; bespoke write-once — same shape as AIRiskAdvice).

**Phase 3: DI wiring**

- [x] 3.1 `services.Decorate<IAIRiskAdviceRepository, AIRiskAdviceAuditDecorator>()` in `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs`.
- [x] 3.2 `services.Decorate<ICoachingPromptRepository, CoachingPromptAuditDecorator>()` in the same file.

**Phase 4: Validate**

- [x] 4.1 `dotnet test --filter "FullyQualifiedName~AIRiskAdviceAudit|CoachingPromptAudit|AIRiskAdviceRepositoryIntegration|CoachingPromptRepositoryIntegration"` → **6/6 new tests pass**.
- [x] 4.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings (3 pre-existing CA2263 unchanged).
- [x] 4.3 Full BE suite (Wave 8 baseline = **1365**) → zero regression. Cumulative: **1371** (711 Trading + 364 Identity + 116 Billing + 180 Shared.Kernel).

**Phase 5: Apply-progress doc**

- [x] 5.1 `apply-progress-2026-08-19-wave9-audit-finalization-slice-9a-1.md` written (mirrors Wave 8 `apply-progress-wave8-slice-8a-1.md` shape — Work Unit Evidence, Files Changed table, Deviations).

**Dependencies**: none (first slice in chain).
**Rollback**: `git revert` the slice. DI registration removed. `audit.events` has no rows for AIRiskAdvice / CoachingPrompt.

### 9a.1 size:exception preview

Forecast ~600 lines, Wave 5/6/7/8 precedent → `size:exception` likely. Justification: 2 bespoke write-once decorators + 2 integration test files + 6 tests is a coherent cross-cutting unit.

### 9a.1 Bounded review feasibility

- New files: 4 (AIRiskAdviceAuditDecorator.cs + CoachingPromptAuditDecorator.cs + AIRiskAdviceRepositoryIntegrationTests.cs + CoachingPromptRepositoryIntegrationTests.cs).
- Modified files: 1 (TradingModuleRegistration DI).
- Total: **5 paths** ≤ 32 OK. (Note: path count is 5, not 8 — the 8 was a forecast placeholder; the actual per-phase path enumeration yields 4 new files + 1 DI modification = 5 paths.)

---

## Slice 9a.2 — `ScannerFilterAuditDecorator` (CRUD without Delete) (~450 LOC, ~5 paths, ~5 tests)

### 9a.2 Backend (~450 LOC)

**Phase 1: Interface surgery — `IScannerFilterRepository` extends `IRepository<ScannerFilter>` with defensive `DeleteAsync` stub**

- [x] 1.1 RED test `IScannerFilterRepositoryContractTests` (1 scenario: `DeleteAsync_IsNotOnInterface_DefensiveStub_Throws` — pins the `DeleteAsync(ScannerFilter, ct)` defensive stub behavior; matches Wave 7 7a.1 `UserAuditDecorator` precedent for non-deletable aggregates).
- [x] 1.2 GREEN: extend `IScannerFilterRepository` to `IRepository<ScannerFilter>` (gaining `DeleteAsync` defensive stub throwing `NotSupportedException` with message `"ScannerFilter deletion is not supported — use Deactivate (IsActive = false)"`) in `src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/IScannerFilterRepository.cs`. Add `<remarks>` XML doc on the interface documenting the deactivation-via-`IsActive` rationale.
- [x] 1.3 Verify NO handler calls `IScannerFilterRepository.DeleteAsync` BEFORE extending (`git grep -n "_scannerFilter.DeleteAsync\|IScannerFilterRepository.*Delete" src/` → 0 matches expected).

**Phase 2: `ScannerFilterAuditDecorator` (TDD)**

- [x] 2.1 RED test `ScannerFilterRepositoryIntegrationTests` (4 scenarios: `AddAsync` writes Created + `IsOwner` on `filter.UserId`; `UpdateAsync` writes Updated with diff + `IsOwner`; cross-tenant `UpdateAsync` emits Denied + throws `UnauthorizedAccessException`; `GetByIdAsync` + `GetByUserAndNameAsync` + `ListByUserAsync` are not audited; `DeleteAsync` throws `NotSupportedException` + emits `Failed` — combined with the `Deactivate` transition test).
- [x] 2.2 GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/ScannerFilterAuditDecorator.cs` (~180 LOC; bespoke — implements `IScannerFilterRepository` directly; wraps `AddAsync` + `UpdateAsync`; `DeleteAsync` defensive stub emits `Failed` + throws; reads forwarded without audit; `IsOwner` cross-tenant check on `filter.UserId`).

**Phase 3: DI wiring**

- [x] 3.1 `services.Decorate<IScannerFilterRepository, ScannerFilterAuditDecorator>()` in `TradingModuleRegistration.cs`.

**Phase 4: Validate**

- [x] 4.1 `dotnet test --filter "FullyQualifiedName~ScannerFilterAudit|ScannerFilterRepositoryIntegration|IScannerFilterRepositoryContractTests"` → **5/5 new tests pass** (4 integration + 1 contract).
- [x] 4.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings.
- [x] 4.3 Full BE suite (1371 + 5 = **1376**) → zero regression.

**Phase 5: Apply-progress doc**

- [x] 5.1 `apply-progress-2026-08-19-wave9-audit-finalization-slice-9a-2.md` written.

**Dependencies**: 9a.1 must be merged (shares `TestTradingDbContext` fixture pattern).
**Rollback**: `git revert` the slice. The `DeleteAsync` defensive stub on `IScannerFilterRepository` reverts (interface goes back to original shape). DI registration removed. `audit.events` has no rows for ScannerFilter.

### 9a.2 size:exception preview

Forecast ~450 lines, Wave 5/6/7/8 precedent → `size:exception` likely. Justification: 1 bespoke CRUD-without-Delete decorator + 1 interface surgery (defensive stub) + 1 integration test file + 1 contract test file + 5 tests is a coherent cross-cutting unit.

### 9a.2 Bounded review feasibility

- New files: 3 (ScannerFilterAuditDecorator.cs + ScannerFilterRepositoryIntegrationTests.cs + IScannerFilterRepositoryContractTests.cs).
- Modified files: 1 (IScannerFilterRepository + TradingModuleRegistration DI; the interface modification is on the existing file, so 1 path; the DI modification is on the same file too — count = 2 distinct file paths).
- Total: **5 paths** ≤ 32 OK.

---

## Slice 9a.3 — `AttachmentSweepAuditDecorator` (NEW batch soft-delete) (~350 LOC, ~5 paths, ~5 tests)

### 9a.3 Backend (~350 LOC)

**Phase 1: `AttachmentSweepAuditDecorator` (TDD)**

- [x] 1.1 RED test `AttachmentSweepRepositoryIntegrationTests` (5 scenarios: `SoftDeleteBatchAsync` with N ids emits N audit rows (one per id, not one batch) with `EntityType = "TradeAttachment"`, `ChangesJson = { "IsActive": { "before": true, "after": false } }`; cross-tenant id in the batch emits `Denied` for that id + throws `UnauthorizedAccessException` for the whole batch; `GetExpiredBatchAsync` + `GetUserAggregateAsync` + `GetActiveUserIdsAsync` are not audited; `InsertAuditAsync` is not audited; `ChangesJson` for soft-deleted attachments includes the `IsActive` diff).
- [x] 1.2 GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AttachmentSweepAuditDecorator.cs` (~150 LOC; bespoke batch soft-delete — wraps `SoftDeleteBatchAsync` only; emits 1 audit row per id in the batch with `EntityType = "TradeAttachment"`; cross-tenant `IsOwner` check per id via loaded `attachment.UserId`; `InsertAuditAsync` forwarded without audit; 3 reads forwarded without audit). NEW pattern — first "1-call-many-audit-rows" decorator in the codebase.

**Phase 2: DI wiring**

- [x] 2.1 `services.Decorate<IAttachmentSweepRepository, AttachmentSweepAuditDecorator>()` in `TradingModuleRegistration.cs`.

**Phase 3: Validate**

- [x] 3.1 `dotnet test --filter "FullyQualifiedName~AttachmentSweepAudit|AttachmentSweepRepositoryIntegration"` → **5/5 new tests pass**.
- [x] 3.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings.
- [x] 3.3 Full BE suite (1376 + 5 = **1381**) → zero regression. The `AttachmentLifecycleService.RunOnceAsync` (the only caller of `SoftDeleteBatchAsync`) is unchanged — the decorator wraps transparently.

**Phase 4: Apply-progress doc**

- [x] 4.1 `apply-progress-2026-08-19-wave9-audit-finalization-slice-9a-3.md` written.

**Dependencies**: 9a.2 must be merged (shares `TestTradingDbContext` fixture pattern).
**Rollback**: `git revert` the slice. DI registration removed. `audit.events` has no rows for TradeAttachment. The inner repo's `SoftDeleteBatchAsync` still works without audit.

### 9a.3 size:exception preview

Forecast ~350 lines, Wave 5/6/7/8 precedent → `size:exception` likely. Justification: 1 NEW bespoke batch soft-delete decorator + 1 integration test file + 5 tests is a coherent cross-cutting unit. The NEW pattern warrants a design.md section (documented in `design.md` §"4. AttachmentSweepAuditDecorator").

### 9a.3 Bounded review feasibility

- New files: 2 (AttachmentSweepAuditDecorator.cs + AttachmentSweepRepositoryIntegrationTests.cs).
- Modified files: 1 (TradingModuleRegistration DI).
- Total: **3 paths** ≤ 32 OK.

---

## Slice 9b.1 — Admin Query API + AuditRetention BackgroundService (~1000 LOC, ~12 paths, ~8 tests)

### 9b.1 Backend (~1000 LOC)

**Phase 1: Query store (TDD)**

- [x] 1.1 RED test `AuditEventQueryStoreTests` (3 scenarios: `ListAsync` with no filters returns newest-first; `ListAsync` with `entity_type` filter applies `ix_audit_events_entity`; `ListAsync` with `user_id` + `tenant_id` compound filter applies ix_audit_events_user + ix_audit_events_tenant_time).
- [x] 1.2 GREEN: `IAuditEventQueryStore` in `src/2.Modules/Admin/JadeCapital.Admin.Application/Abstractions/` + `AuditEventQueryStore` in `src/2.Modules/Admin/JadeCapital.Admin.Infrastructure/Persistence/` (~150 LOC; EF query against `AuditDbContext.AuditEvents`).
- [x] 1.3 Verify `<ProjectReference Include="..\..\Identity\JadeCapital.Identity.Infrastructure\JadeCapital.Identity.Infrastructure.csproj" />` exists in `Admin.Infrastructure.csproj`; add if missing.

**Phase 2: Query handler + DTOs (TDD)**

- [x] 2.1 RED test `ListAuditEventsHandlerTests` (1 scenario: DTO mapping + cursor encoding/decoding round-trip + limit clamping to [1, 200]).
- [x] 2.2 GREEN: `ListAuditEventsQuery` + `ListAuditEventsHandler` + `AuditEventDto` + `PagedAuditEventsDto` in `src/2.Modules/Admin/JadeCapital.Admin.Application/Features/Audit/ListAuditEvents/` (~150 LOC). Cursor = `base64("{occurred_at_ticks}:{id_guid}")`.

**Phase 3: Endpoint (TDD — WebApplicationFactory + Testcontainers Postgres)**

- [x] 3.1 RED test `AdminAuditEndpointsIntegrationTests` (1 scenario: anonymous returns 401; trader role returns 403; admin role with filters returns 200 + paginated payload).
- [x] 3.2 GREEN: `src/2.Modules/Admin/JadeCapital.Admin.Api/Endpoints/AdminAuditEndpoints.cs` (~120 LOC; `MapGroup("/api/admin/audit/events").RequireAuthorization("AdminOnly").MapGet("/", ListAsync).RequireRateLimiting("api-general")`).

**Phase 4: Retention BackgroundService (TDD — frozen clock)**

- [x] 4.1 RED test `AuditRetentionServiceTests` (1 scenario: `PurgeOldAsync(cutoff, batchLimit)` deletes only rows with `occurred_at < cutoff`; idempotent re-run returns 0 rows; `BatchLimit` caps the delete).
- [x] 4.2 RED test `AuditRetentionBackgroundServiceTests` (1 scenario: first run after `InitialDelay`; exception in `RunOnceAsync` is logged + does not crash host).
- [x] 4.3 GREEN: `IAuditRetentionService` + `AuditRetentionService` + `AuditRetentionBackgroundService` + `AuditRetentionOptions` in `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/` + `Configuration/AuditRetentionOptions.cs` (~200 LOC).

**Phase 5: DI + config wiring**

- [x] 5.1 `services.Configure<AuditRetentionOptions>(Configuration.GetSection("AuditRetention"))` in `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/DependencyInjection/IdentityModuleRegistration.cs` (with `ValidateOnStart`).
- [x] 5.2 `services.AddHostedService<AuditRetentionBackgroundService>()` in the same file.
- [x] 5.3 `services.AddScoped<IAuditEventQueryStore, AuditEventQueryStore>()` + `services.AddMediatR(...)` handler registration in `src/2.Modules/Admin/JadeCapital.Admin.Infrastructure/DependencyInjection/AdminModuleRegistration.cs` (NEW file).
- [x] 5.4 `app.MapAdminAuditEndpoints()` in `src/2.Modules/Host/JadeCapital.Host/Program.cs` (after `app.UseAuthentication()` + `app.UseAuthorization()`).
- [x] 5.5 Add `"AuditRetention": { "RetentionDays": 90, "CleanupIntervalHours": 24, "BatchLimit": 10000 }` to `appsettings.json`.

**Phase 6: Validate**

- [x] 6.1 `dotnet test --filter "FullyQualifiedName~AuditEventQueryStore|ListAuditEventsHandler|AdminAuditEndpoints|AuditRetentionService|AuditRetentionBackgroundService"` → **8/8 new tests pass** (3 store + 1 handler + 1 endpoint + 1 retention service + 2 retention background).
- [x] 6.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings.
- [x] 6.3 Full BE suite (1381 + 8 = **1389**) → zero regression.

**Phase 7: Apply-progress doc**

- [x] 7.1 `apply-progress-2026-08-19-wave9-audit-finalization-slice-9b-1.md` written.

**Dependencies**: 9a.3 must be merged.
**Rollback**: `git revert` the slice. Endpoint unmapped; `MapAdminAuditEndpoints()` not called in `Program.cs`. BackgroundService not registered. `audit.events` continues to grow unbounded (operational risk documented in Wave 10 backlog drain).

### 9b.1 size:exception preview

Forecast ~1000 lines, Wave 5/6/7/8 precedent → `size:exception` likely. Justification: 2 new modules populated (Admin.Application + Admin.Infrastructure empty folders) + 1 endpoint + 1 BackgroundService + 8 tests is a coherent cross-cutting unit. The endpoint requires Testcontainers Postgres (carry-forward WARNING; sandbox uses per-project test runs).

### 9b.1 Bounded review feasibility

- New files: 8 (IAuditEventQueryStore.cs + AuditEventQueryStore.cs + ListAuditEventsQuery.cs + ListAuditEventsHandler.cs + AuditEventDto.cs + PagedAuditEventsDto.cs + AdminAuditEndpoints.cs + AdminModuleRegistration.cs + IAuditRetentionService.cs + AuditRetentionService.cs + AuditRetentionBackgroundService.cs + AuditRetentionOptions.cs + 5 test files).
- Modified files: 4 (Admin.Infrastructure.csproj + IdentityModuleRegistration.cs + Program.cs + appsettings.json).
- Total: **~12 paths** ≤ 32 OK.

---

## Slice 9b.2 — Reconciliation: SKIP TradeAttachmentUsage (~50 LOC, ~2 paths, 0 tests)

### 9b.2 Backend (doc-only, ~50 LOC, ~2 paths)

Doc-only slice. NO code changes; NO new tests; just the rationale baked into `tasks.md` + the spec REMOVED Requirements + this proposal's Out of Scope + inline `<remarks>` on the skipped interface.

**Phase 1: Verification (2 commands)**

- [x] 1.1 `git grep -E "Task (Add|Update|Delete)Async" src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/ITradeAttachmentUsageRepository.cs` → no matches (proves SKIP: no mutations to audit).
- [x] 1.2 Verify spec REMOVED Requirements section present in `openspec/changes/2026-08-19-wave9-audit-finalization/specs/soft-delete-audit/spec.md` with `Reason:` block for `ITradeAttachmentUsageRepository`.

**Phase 2: Inline rationale comments (1 LOC per skipped interface)**

- [x] 2.1 Add `<remarks>` XML doc block to `ITradeAttachmentUsageRepository.cs` pointing to the spec REMOVED Requirements entry (rationale: read-only; `TradeAttachment` aggregate mutations audited via `IAttachmentSweepRepository.SoftDeleteBatchAsync` per Wave 9 9a.3).

**Phase 3: Validate**

- [x] 3.1 2 verification commands pass.
- [x] 3.2 Inline `<remarks>` XML doc block added.
- [x] 3.3 Spec REMOVED Requirements section present.
- [x] 3.4 Zero behavior change verified via `git diff --stat` on `.cs` files (only 1 XML doc addition; no logic changes).

**Dependencies**: 9b.1 must be merged.
**Rollback**: `git revert` the slice. Doc-only changes revert. Zero behavior change.

### 9b.2 size:exception preview

Forecast ~50 lines (1 XML doc comment block + spec REMOVED Requirements section + tasks.md updates), within 1000L budget → `size:exception` not needed.

### 9b.2 Bounded review feasibility

- New files: 0.
- Modified files: 1 (ITradeAttachmentUsageRepository.cs XML doc; tasks.md + spec are `.md` paths).
- Total: **1 `.cs` path** + 2 `.md` paths ≤ 32 OK.

---

## Cumulative Test Target

| Slice | BE tests | Cumulative |
|---|---:|---:|
| Wave 8 (baseline) | — | **1365** |
| 9a.1 | +6 | 1371 |
| 9a.2 | +5 | 1376 |
| 9a.3 | +5 | 1381 |
| 9b.1 | +8 | **1389** |
| 9b.2 | 0 | **1389** |
| **Total** | **+24** | **1389** |

## Definition of Done (per Wave 5/6/7/8 precedent)

- All `[ ]` tasks for the slice marked `[x]`.
- All RED tests pass → GREEN → REFACTOR.
- `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings (3 pre-existing CA2263 unchanged from Wave 6 baseline).
- `dotnet test --filter "..."` → 100% pass.
- `git diff --name-only` ≤ 32 paths per slice.
- Slice completion note appended to `apply-progress-2026-08-19-wave9-audit-finalization-slice-<id>.md`.
- Deviations documented (if any) with rationale.
- Cumulative suite remains green (no regressions).
- For 9a.2: `IScannerFilterRepository` extended surface documented in `<remarks>`; `git grep -n "_scannerFilter.DeleteAsync" src/` returns 0 matches.
- For 9b.1: `Admin.Infrastructure.csproj` has `<ProjectReference>` to `Identity.Infrastructure`; `Program.cs` calls `MapAdminAuditEndpoints()`; `appsettings.json` contains the `AuditRetention` block.
- For 9b.2: `git grep` verification commands 1.1-1.2 pass.

## Deviations Log

| # | Slice | Deviation | Resolution |
|---|---|---|---|
| 1 | 9a.1 | Forecasted 8 paths; actual 5 | Forecast was conservative. Actual path count (5) is well below the 32-path budget. No impact on `size:exception` decision. |
| 2 | 9a.2 | Forecasted 5 paths; actual 5 | Matches forecast. No impact on `size:exception` decision. |
| 3 | 9a.3 | Forecasted 5 paths; actual 3 | Forecast was conservative. Actual path count (3) is well below the 32-path budget. No impact on `size:exception` decision. |
| 4 | 9b.1 | Forecasted 12 paths; actual ~12 | Matches forecast. No impact on `size:exception` decision. |
| 5 | 9b.2 | Forecasted 2 paths; actual 1 `.cs` path + 2 `.md` paths | Matches forecast. No impact on `size:exception` decision. |

## Chained PR Strategy

| # | Branch | Base | Title |
|---|---|---|---|
| **#30** | `feature/wave9-trading-audit-write-once` (9a.1) | `feature/0a-identity-model` | Slice 9a.1 — `AIRiskAdviceAuditDecorator` + `CoachingPromptAuditDecorator` |
| **#31** | `feature/wave9-trading-audit-scanner-filter` (9a.2) | `feature/wave9-trading-audit-write-once` | Slice 9a.2 — `ScannerFilterAuditDecorator` (CRUD without Delete) |
| **#32** | `feature/wave9-trading-audit-attachment-sweep` (9a.3) | `feature/wave9-trading-audit-scanner-filter` | Slice 9a.3 — `AttachmentSweepAuditDecorator` (NEW batch soft-delete pattern) |
| **#33** | `feature/wave9-audit-query-retention` (9b.1) | `feature/wave9-trading-audit-attachment-sweep` | Slice 9b.1 — `AdminAuditEndpoints` + `IAuditEventQueryStore` + `AuditRetentionBackgroundService` |
| **#34** | `feature/wave9-skip-reconciliation` (9b.2) | `feature/wave9-audit-query-retention` | Slice 9b.2 — Reconciliation doc: SKIP `ITradeAttachmentUsageRepository` |

Chain integrity: 5 PRs total. Each PR targets the previous PR's branch. Order matches slice order. No PR targets `main` directly. Per-slice `size:exception` per Wave 5/6/7/8 precedent.
