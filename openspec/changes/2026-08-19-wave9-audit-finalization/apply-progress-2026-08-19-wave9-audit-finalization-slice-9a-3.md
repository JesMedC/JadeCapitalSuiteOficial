# Wave 9 — slice 9a.3 apply-progress

**Change**: 2026-08-19-wave9-audit-finalization
**Slice**: 9a.3 — `AttachmentSweepAuditDecorator` (NEW batch soft-delete / 1-call-many-audit-rows)
**Branch**: `feature/wave9-attachment-sweep-audit` (branched from `feature/wave9-scanner-filter-audit` @ `5c9bcc9` where 9a.2 = PR #31 just landed)
**Mode**: Strict TDD + hybrid artifact store + `auto-chain` delivery + `feature-branch-chain` + `size:exception`
**Status**: ✅ **Ready for verify** — 5/5 new tests passing, **1381/1381** BE cumulative green (Shared.Kernel 180 + Identity 364 + Billing 116 + Trading 721).

## Slice 9a.3 completion

### Phases completed

- [x] **1.1** RED test `AttachmentSweepRepositoryIntegrationTests` (5 scenarios: `SoftDeleteBatchAsync` with N ids emits N audit rows (one per id, NOT one batch) with `EntityType = "TradeAttachment"`, `ChangesJson = { "isActive": { "before": true, "after": false } }`; cross-tenant id in the batch emits `Denied` for that id + throws `UnauthorizedAccessException` for the whole batch; `GetExpiredBatchAsync` + `GetUserAggregateAsync` + `GetActiveUserIdsAsync` are not audited; `InsertAuditAsync` is not audited; `ChangesJson` for soft-deleted attachments includes the `IsActive` diff). Confirmed RED via build error (CS0246: `AttachmentSweepAuditDecorator` not found).
- [x] **1.2** GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AttachmentSweepAuditDecorator.cs` (~379 LOC; bespoke batch soft-delete — first "1-call-many-audit-rows" decorator in the codebase; wraps `SoftDeleteBatchAsync` only; loads each attachment via scoped `DbContext` to read `attachment.UserId` for `IsOwner` + capture pre-mutation `IsActive` for the diff; emits 1 `AuditAction.Updated` audit row per id with `EntityType = "TradeAttachment"`; cross-tenant `IsOwner` check per id via loaded `attachment.UserId`; `InsertAuditAsync` forwarded without audit (would create infinite loop); 3 reads forwarded without audit).
- [x] **2.1** Wire DI: `services.Decorate<IAttachmentSweepRepository, AttachmentSweepAuditDecorator>()` in `TradingModuleRegistration.cs` after the inner `AddScoped<IAttachmentSweepRepository>` registration at lines 116-117.
- [x] **3.1** `dotnet test tests/UnitTests/JadeCapital.Trading.UnitTests --filter "FullyQualifiedName~AttachmentSweepAudit|AttachmentSweepRepositoryIntegration" --nologo --verbosity minimal` → **5/5 new tests pass**.
- [x] **3.2** `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings (matches Wave 6 baseline; 0 new CA2263).
- [x] **3.3** Full BE suite (per-project): Shared.Kernel 180 + Identity 364 + Billing 116 + Trading 721 = **1381/1381 passed**. Wave 9 9a.2 baseline 1376 + 5 new = 1381 (matches forecast — zero regression).
- [x] **4.1** `apply-progress-2026-08-19-wave9-audit-finalization-slice-9a-3.md` written (this file).

### Files Changed

| File | Action | What Was Done |
|------|--------|---------------|
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AttachmentSweepAuditDecorator.cs` | **Created** | Bespoke `IAttachmentSweepRepository` decorator (379 LOC). **NEW pattern — first "1-call-many-audit-rows" decorator in the codebase.** Loads each attachment via scoped `DbContext` (`_db.Set<TradeAttachment>().Where(a => ids.Contains(a.Id)).ToListAsync(ct)`) for the `IsOwner` pre-check + pre-mutation `IsActive` snapshot. `IsOwner` cross-tenant check per id (matches Wave 7 7a.1 User + 8a.1 Account + 8a.2 Alert + 8a.2 TradeReview + 8a.3 PlannerSession + 8a.3 PreTradeChecklist + 8b.1 StripeCustomer + 9a.1 AIRiskAdvice + 9a.1 CoachingPrompt + 9a.2 ScannerFilter precedent). On mismatch: `AuditAction.Denied` + `UnauthorizedAccessException` + the whole batch aborts (inner is NEVER reached). On match: call inner `SoftDeleteBatchAsync` + emit 1 `AuditAction.Updated` audit row per id with `EntityType = "TradeAttachment"` (the child aggregate, NOT the sweep operation) + `ChangesJson = { "isActive": { "before": <pre-mutation>, "after": false } }`. `InsertAuditAsync` forwarded bare — auditing it would create an infinite loop (mirrors Wave 8 8b.2 `IStripeWebhookEventRepository` SKIP rationale). 3 reads (`GetExpiredBatchAsync` + `GetUserAggregateAsync` + `GetActiveUserIdsAsync`) forwarded bare. `TryAuditAsync` swallows exceptions (defense-in-depth). Pre-mutation `IsActive` is captured into a local dictionary BEFORE the inner call so the diff is always `{before: <pre-mutation>, after: false}` regardless of whether the tracked instance was mutated by the inner. |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs` | Modified | Wire `services.Decorate<IAttachmentSweepRepository, AttachmentSweepAuditDecorator>()` after the inner `AddScoped<IAttachmentSweepRepository, AttachmentSweepRepository>()` registration at lines 116-117. Comment block mentions Wave 9 sub-scope A + slice 9a.3 + the bespoke batch-soft-delete rationale + the new 1-call-many-audit-rows pattern + the `InsertAuditAsync` skip-rationale. |
| `tests/UnitTests/JadeCapital.Trading.UnitTests/Persistence/AttachmentSweepRepositoryIntegrationTests.cs` | **Created** | 5 RED scenarios (711 LOC) via SQLite in-memory + `TestAttachmentSweepDbContext` (mirrors production `TradeAttachmentConfiguration` Status converter + columns) + `TestAttachmentSweepRepository` (5 methods). The 5 scenarios cover the per-id batch audit emission (N rows for N ids), cross-tenant `Denied` + transaction abort (1 row for cross-tenant + throws), read-methods no-audit, `InsertAuditAsync` no-audit (infinite loop prevention), and `ChangesJson` shape (`isActive: {before: true, after: false}`). |
| `openspec/changes/2026-08-19-wave9-audit-finalization/tasks.md` | Modified | 9a.3 phases marked [x]; cumulative target updated to 1381. |
| `openspec/changes/2026-08-19-wave9-audit-finalization/apply-progress-2026-08-19-wave9-audit-finalization-slice-9a-3.md` | **Created** | This file. |

### TDD Cycle Evidence (Strict TDD active)

| Phase | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|-------|-----------|-------|------------|-----|-------|-------------|----------|
| 1.1 | `AttachmentSweepRepositoryIntegrationTests.cs` | Integration (SQLite in-memory) | N/A (new) | ✅ Confirmed (`AttachmentSweepAuditDecorator` not found, CS0246 error) | ✅ Passed (5/5) | ✅ 5 cases: N-rows-per-N-ids / Cross-tenant+Denied / Reads / InsertAuditAsync / ChangesJson shape | ✅ Clean (docstrings + comments only) |
| 1.2 | (decorator impl) | Compile-time | 1.1 RED | ✅ Confirmed (decorator missing) | ✅ All 5 REDs pass | ➖ Single-file implementation | ➖ None needed |

### Work Unit Evidence

| Evidence | Required value |
|---|---|
| **Focused test command** | `dotnet test tests/UnitTests/JadeCapital.Trading.UnitTests --filter "FullyQualifiedName~AttachmentSweepAudit|AttachmentSweepRepositoryIntegration" --nologo --verbosity minimal` → **5/5 passed** (1 N-rows-per-N-ids + 1 Cross-tenant+Denied + 1 Reads + 1 InsertAuditAsync + 1 ChangesJson). |
| **Runtime harness command** | Full BE suite (per-project): `dotnet test tests/UnitTests/JadeCapital.{Shared.Kernel,Identity,Billing,Trading}.UnitTests --nologo --verbosity minimal` → Shared.Kernel 180 + Identity 364 + Billing 116 + Trading 721 = **1381/1381 passed**. `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings. |
| **Rollback boundary** | `git revert <merge-commit>` — Decorator: `services.Decorate` line removed from `TradingModuleRegistration.cs`; `AttachmentSweepAuditDecorator.cs` deleted; `AttachmentSweepRepositoryIntegrationTests.cs` deleted. `audit.events` has no rows for `TradeAttachment`. Zero behavior change to the production `AttachmentSweepRepository.SoftDeleteBatchAsync` (only audit logging is added on the per-id wrap path). The `AttachmentLifecycleService.RunOnceAsync` (the only caller) is unchanged — the decorator wraps transparently. |

### Test Summary

- **Total new tests written**: 5 (1 N-rows-per-N-ids + 1 Cross-tenant+Denied+Throws + 1 Reads-no-audit + 1 InsertAuditAsync-no-audit + 1 ChangesJson-shape, all integration via SQLite in-memory).
- **Total tests passing**: 1381/1381 BE (Shared.Kernel 180 + Identity 364 + Billing 116 + Trading 721).
- **Layers used**: Integration (5 tests via SQLite in-memory).
- **Approval tests** (refactoring): None — no refactoring tasks.
- **Pure functions created**: N/A — audit decorators are stateful wrappers, not pure functions.

### Deviations from Design

- **Decorator accepts `DbContext` (required, not optional)**: design.md §"4. AttachmentSweepAuditDecorator" + tasks.md §9a.3 Phase 1.2 imply the decorator loads attachments via `_inner.GetExpiredBatchAsync` or similar; the production interface has no `GetByIdsAsync(IReadOnlyList<Guid>)` method, and adding one would require interface surgery (not in scope). The cleanest implementation uses the scoped `DbContext` directly (`_db.Set<TradeAttachment>().Where(a => ids.Contains(a.Id)).ToListAsync(ct)`) — the decorator shares the same scoped DbContext as the inner, so the tracked instances are the same object references (the inner's `MarkSwept(now)` mutates the in-memory `IsActive` flag the decorator holds). The pre-mutation `IsActive` value is captured into a local dictionary BEFORE the inner call so the diff is always `{before: <pre-mutation>, after: false}` regardless of the mutation. Mirrors the Wave 7 7b.1 + 9a.1 + 9a.2 precedent for bespoke decorators that need `DbContext` access for diff source. The decorator signature: `inner` + `audit` + `tenant` + `clock` + `db` (5 deps, all required).
- **Test fixture `GetUserAggregateAsync` returns hardcoded `(0L, 0)`** instead of querying the EF data: the test assertion is "no audit event emitted by the read method" — the aggregate shape is irrelevant. Hardcoding avoids the SQLite `Sum()` on nullable decimal projection that the production repo handles via Postgres-specific converters. Mirrors the Wave 9 9a.1 `TestAIRiskAdviceRepository.FindByUserAndTradeAsync` pattern of simplifying the test fixture for SQLite compatibility.
- **Test fixture `GetExpiredBatchAsync` materializes client-side before the `ExpiresAt < asOf` filter**: SQLite's EF provider throws `InvalidOperationException` when translating `DateTimeOffset < DateTimeOffset` LINQ expressions (the 9a.1 lesson). The fix is to materialize the `Where(a => a.IsActive && a.ExpiresAt != null)` predicate first, then filter `Where(a => a.ExpiresAt < asOf)` client-side. Mirrors the Wave 9 9a.1 `TestAIRiskAdviceRepository.FindByUserAndTradeAsync` client-side `OrderByDescending(CreatedAt)` workaround. The production repo pushes both predicates to SQL on Postgres.
- **`InsertAuditAsync` test cannot verify the inner was called** (no Scrutor unwrap): the test resolves `IAttachmentSweepRepository` and gets the decorator (not the inner). The test asserts only "no audit.events row was emitted" — the contract that prevents the infinite loop. The "the inner was called" assertion is implicit: if the decorator blocked the call, the test fixture's `InsertAuditAsyncCallCount` counter wouldn't increment — but the test fixture's instance isn't reachable from outside the scope. Dropping the inner-counter assertion keeps the test honest (no implicit Scrutor unwrap). The `TestAttachmentSweepRepository.InsertAuditAsyncCallCount` property is kept on the fixture for future reference but is unused in this slice's tests.
- **`Failed` audit row on missing id** (NOT `Denied`): if the SoftDeleteBatchAsync is called with an id that doesn't resolve in the loaded set, the decorator emits an `AuditAction.Failed` (5) row with the failure reason in `ChangesJson` + throws `InvalidOperationException`. This is a misuse guard — the `SoftDeleteBatchAsync` inner is idempotent (re-running on already-soft-deleted rows is a no-op per the interface docstring), but a missing id means the caller submitted invalid input. Mirrors the Wave 7 7a.1 `UserAuditDecorator` + 7b.1 `StrategyAuditDecorator` + 9a.2 `ScannerFilterAuditDecorator` precedent for emitting `Failed` on contractually invalid mutations.
- **`ChangesJson` casing uses `isActive` (camelCase)**, not `"IsActive"` (PascalCase): System.Text.Json default naming policy is camelCase. The test asserts `Contains("isActive")` + parses the JSON to verify the `{before: true, after: false}` shape. The spec's "IsActive" is descriptive intent; the actual JSON payload uses `isActive` (matches the Wave 7 7b.1 + 8a.1 + 8a.2 + 9a.2 precedent).
- **`SoftDeleteBatchAsync` returns early on empty list**: when `attachmentIds.Count == 0`, the decorator returns `0` without touching the DbContext or emitting audit. Mirrors the production `AttachmentSweepRepository.SoftDeleteBatchAsync` early-return on empty list.
- **Path count = 3** (matches tasks.md §9a.3 forecast of "3 paths ≤ 32 OK"). 2 new files (decorator + test) + 1 modified DI file = 3 paths. Well under the 32-path budget.
- **LOC count vs forecast**: tasks.md §9a.3 forecast ~350 LOC; actual ~1090 LOC (379 decorator + 711 test + 20 DI). The forecast was conservative. The slice is well under the 1500 max_changed_lines budget.
- **No code-level issues**. All 5 new tests pass on the first run after GREEN; the build is green with zero new warnings; the cumulative suite is green with zero regression.

### Issues Found

- **SQLite DateTimeOffset translation limitation** (same as Wave 9 9a.1): the test fixture's `Where(a => a.ExpiresAt < asOf)` predicate throws `InvalidOperationException` on SQLite because the EF provider does not translate `DateTimeOffset < DateTimeOffset`. The fix is to materialize the `IsActive && ExpiresAt != null` predicate first, then filter `ExpiresAt < asOf` client-side. This is a SQLite-specific limitation; the production repo runs on Postgres and uses the full SQL push-down. Future slices that add batch operations on `DateTimeOffset?` columns can follow the same fixture pattern.
- **No code-level issues**. All 5 new tests pass on the first run after GREEN; the build is green with zero new warnings; the cumulative suite is green with zero regression.

### Workload / PR Boundary

- **Mode**: feature-branch-chain (PR #32 of Wave 9 chain — targets `feature/wave9-scanner-filter-audit`).
- **Current work unit**: 9a.3 — `AttachmentSweepAuditDecorator` (NEW batch soft-delete / 1-call-many-audit-rows).
- **Boundary**: starts at `feature/wave9-scanner-filter-audit` @ `5c9bcc9` (where 9a.2 = PR #31 just landed); ends with 1 commit on `feature/wave9-attachment-sweep-audit`. Targets `feature/wave9-scanner-filter-audit` (per Wave 9 §9a.3 PR table — PR #32 of the project, the 4th slice in the Wave 9 chain).
- **Changed paths**: 3 (2 new + 1 modified DI).
- **Estimated review budget impact**: ~1100 LOC insertions (379 decorator + 711 test + 20 DI) — well under the 1500 max_changed_lines budget. `size:exception` per Wave 5/6/7/8/9a.1/9a.2 precedent (the spec explicitly anticipates this for 9a.3 — tasks.md §9a.3 "9a.3 size:exception preview").

### Cumulative state across Wave 9 chain

- 9a.1 (PR #30) → 9a.2 (PR #31) → **9a.3 (PR #32, THIS)** → 9b.1 → 9b.2 (2 slices remaining)
- This slice (9a.3) ships the bespoke batch soft-delete audit decorator for the `AttachmentSweep` aggregate. The critical design decisions captured:
  1. **NEW pattern — "1-call-many-audit-rows"**: a single `SoftDeleteBatchAsync` call emits N audit rows (one per id in the batch), NOT 1 row per batch. This is the first such decorator in the codebase — the canonical single-entity shape from Wave 6 + 7 + 8a.x + 8b.1 + 9a.1 + 9a.2 doesn't apply to batch mutations. The decorator loads each attachment via the scoped `DbContext` to read `attachment.UserId` for `IsOwner` + capture the pre-mutation `IsActive` for the diff.
  2. **`EntityType = "TradeAttachment"`** (the child aggregate being soft-deleted, NOT the sweep operation). The user-impacting event is the per-attachment state change; the sweep is the background service that batches them. Compliance officers query `entity_type = "TradeAttachment"` + `action = "Updated"` + `changes @> '{"isActive":{"after":false}}'` to find every soft-deleted attachment across all entry points.
  3. **`InsertAuditAsync` is forwarded WITHOUT audit** (would create infinite loop): the method IS the write to `trading.attachments_quota_audit` (the sweep's own audit log). Mirrors the Wave 8 8b.2 `IStripeWebhookEventRepository` SKIP rationale: the destination IS the audit log; emitting on top would be doubly-recorded noise.
  4. **Cross-tenant `IsOwner` check per id** in the batch; on mismatch: `AuditAction.Denied` + `UnauthorizedAccessException` for the WHOLE batch (transaction abort semantics — the inner is NEVER reached; the owned ids in the batch get NO audit row). Mirrors the Wave 7 7a.1 User + 8a.1 Account + 8a.2 Alert + 8a.2 TradeReview + 8a.3 PlannerSession + 8a.3 PreTradeChecklist + 8b.1 StripeCustomer + 9a.1 AIRiskAdvice + 9a.1 CoachingPrompt + 9a.2 ScannerFilter cross-tenant precedent.
  5. **Pre-mutation `IsActive` snapshot captured BEFORE the inner call**: the decorator shares the same scoped `DbContext` as the inner, so the tracked instances are the same object references the inner mutates (`MarkSwept(now)` flips `IsActive = false`). To avoid the diff being computed AFTER the mutation (which would yield `{before: false, after: false}`), the decorator captures `attachment.IsActive` into a local dictionary BEFORE calling the inner.
  6. **Co-located in Trading** (Trading → Trading) to mirror the Wave 8 8a.1 + 8a.2 + 8a.3 + 9a.1 + 9a.2 precedent.
- Subsequent slices:
  - 9b.1 (AdminAuditEndpoints + IAuditEventQueryStore + AuditRetentionBackgroundService, ~1000 LOC) — fills the empty Admin.Application + Admin.Infrastructure folders + adds the audit retention BackgroundService.
  - 9b.2 (TradeAttachmentUsage documentation, ~50 LOC) — doc-only; no code changes.

### Cross-slice invariants preserved

- **Cross-tenant `IsOwner` check** on `SoftDeleteBatchAsync` for `TradeAttachment` (mirrors 7a.1 User + 8a.1 Account + 8a.2 Alert + 8a.2 TradeReview + 8a.3 PlannerSession + 8a.3 PreTradeChecklist + 8b.1 StripeCustomer + 9a.1 AIRiskAdvice + 9a.1 CoachingPrompt + 9a.2 ScannerFilter). On cross-tenant attempt: `AuditAction.Denied` + `UnauthorizedAccessException` + the WHOLE BATCH aborts (inner is NEVER reached).
- **Reads forwarded without audit** (no `GetExpiredBatchAsync` / `GetUserAggregateAsync` / `GetActiveUserIdsAsync` audit row emission). Matches Wave 6 + 7 + 8a.x + 8b.1 + 9a.1 + 9a.2 precedent.
- **`InsertAuditAsync` forwarded without audit** (mirrors Wave 8 8b.2 `IStripeWebhookEventRepository` SKIP rationale: the destination IS the audit log; emitting on top would create an infinite loop).
- **No-throw `TryAuditAsync`** wrapper around every `_audit.LogAsync` call. Defense-in-depth: a buggy audit logger never rolls back the main mutation.
- **DI registration via Scrutor's `services.Decorate<IAttachmentSweepRepository, AttachmentSweepAuditDecorator>()`** with the underlying service registered first (the `Persistence.AttachmentSweepRepository` registration precedes the decorate call).
- **Pre-mutation `IsActive` snapshot** captured into a local dictionary BEFORE the inner call so the diff is always `{before: <pre-mutation>, after: false}` regardless of whether the tracked instance was mutated by the inner.
- **`Failed` audit row on missing id** (mirrors Wave 7 7a.1 + 7b.1 + 9a.2 precedent): the decorator emits an `AuditAction.Failed` row with the failure reason in `ChangesJson` + throws `InvalidOperationException`. The inner `SoftDeleteBatchAsync` is idempotent on already-soft-deleted rows; a missing id means the caller submitted invalid input.
- **Entity-level docstring guarantee**: `TradeAttachment` is a soft-delete-by-flag aggregate per `src/2.Modules/Trading/JadeCapital.Trading.Domain/TradeAttachments/TradeAttachment.cs` docstring + `MarkSwept(DateTimeOffset)` method (`!IsActive` is the early-return idempotency guard). The decorator + integration tests enforce the audit trail at 3 layers: domain docstring + `IsActive` field + `MarkSwept` method semantics + `ChangesJson` diff capture.
- **`AttachmentLifecycleService.RunOnceAsync` is unchanged**: the only caller of `SoftDeleteBatchAsync` resolves `IAttachmentSweepRepository` via DI, which is now the decorator wrap. The sweep's behavior is identical except for the per-id audit row emission.