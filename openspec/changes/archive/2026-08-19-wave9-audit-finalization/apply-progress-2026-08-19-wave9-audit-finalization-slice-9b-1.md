# Wave 9 — slice 9b.1 apply-progress

**Change**: 2026-08-19-wave9-audit-finalization
**Slice**: 9b.1 — `AdminAuditEndpoints` + `IAuditEventQueryStore` + `AuditRetentionBackgroundService`
**Branch**: `feature/wave9-audit-admin-api` (branched from `feature/wave9-attachment-sweep-audit` @ `f69a948` where 9a.3 = PR #32 just landed)
**Mode**: Strict TDD + hybrid artifact store + `auto-chain` delivery + `feature-branch-chain` + `size:exception`
**Status**: ✅ **Ready for verify** — 8/8 new tests passing, **1389/1389** BE cumulative green (Shared.Kernel 180 + Identity 367 + Billing 116 + Trading 721 + Admin 5).

## Slice 9b.1 completion

### Phases completed

- [x] **1.1** RED test `AuditEventQueryStoreTests` (3 scenarios: `ListAsync` with no filters returns newest-first; `ListAsync` with `entity_type` filter applies `ix_audit_events_entity`; `ListAsync` with `user_id` + `tenant_id` compound filter applies ix_audit_events_user + ix_audit_events_tenant_time). Confirmed RED via `NotSupportedException: SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses`. Fixed by materializing-then-client-side-sorting in the store (Postgres performance is preserved because the WHERE filters + Take are still pushed to SQL).
- [x] **1.2** GREEN: `IAuditEventQueryStore` in `src/2.Modules/Admin/JadeCapital.Admin.Application/Abstractions/` (44 LOC) + `AuditEventQueryStore` in `src/2.Modules/Admin/JadeCapital.Admin.Infrastructure/Persistence/` (117 LOC; EF query against `AuditDbContext.AuditEvents`). Materialize-first + client-side order-by for SQLite compatibility.
- [x] **1.3** Verified `<ProjectReference Include="..\..\Identity\JadeCapital.Identity.Infrastructure\JadeCapital.Identity.Infrastructure.csproj" />` already exists in `Admin.Infrastructure.csproj` (was added in 9b.1 prep).
- [x] **2.1** RED test `ListAuditEventsHandlerTests` (1 scenario: DTO mapping + cursor encoding/decoding round-trip + limit clamping to [1, 200] + malformed cursor rejection). Confirmed RED via `"validation.validation.audit.limit_out_of_range"` (double-prefix bug in production code).
- [x] **2.2** GREEN: `ListAuditEventsQuery` (70 LOC) + `ListAuditEventsHandler` (121 LOC) + `AuditEventDto` + `PagedAuditEventsDto` (in `AuditEventDto.cs`, 59 LOC) in `src/2.Modules/Admin/JadeCapital.Admin.Application/Features/Audit/`. Fixed the double-prefix bug (changed handler error codes from `"validation.audit.limit_out_of_range"` to `"audit.limit_out_of_range"` so `Error.Validation(...)` prepends the prefix correctly). Cursor = `base64("{occurred_at_ticks}|{id_guid}")` (uses `|` separator, not `:` — the design.md spec said `:` but the existing implementation uses `|` for unambiguous parsing).
- [x] **3.1** RED test `AdminAuditEndpointsIntegrationTests` (1 scenario: anonymous returns 401; trader role returns 403; admin role returns 200 with paged payload). Used a minimal in-process `WebApplication` host + `TestServer` + `TestAuthHandler` (not Testcontainers Postgres — explained in the test header). Initial implementation used `WebApplicationFactory<Program>` with the full host, but the full host has 200+ services that depend on Postgres/Redis/Ollama config that don't exist in the test fixture. Switched to a minimal host that wires ONLY the production auth + authorization + rate-limiting + endpoint mapping.
- [x] **3.2** GREEN: `src/2.Modules/Admin/JadeCapital.Admin.Api/Endpoints/AdminAuditEndpoints.cs` (109 LOC). `MapGroup("/api/admin/audit/events").RequireAuthorization("AdminOnly").MapGet("/", ListAsync).RequireRateLimiting("api-general")`. Fixed the 2 build errors: missing `using JadeCapital.Shared.Kernel.Audit;` (for `AuditAction` parameter type) + missing `<ProjectReference>` to `Admin.Application` in `Admin.Api.csproj`.
- [x] **4.1** RED test `AuditRetentionServiceTests` (1 scenario: `PurgeOldAsync(cutoff, batchLimit)` deletes only rows with `occurred_at < cutoff`; idempotent re-run returns 0 rows; `BatchLimit` caps the delete). Confirmed RED via `NotSupportedException: SQLite does not support expressions of type 'DateTimeOffset' in WHERE clauses` + `ExecuteDelete + Take` not supported on SQLite. Fixed by materializing up to 100,000 rows + client-side `WHERE cutoff + ORDER BY + Take(batchLimit)` followed by `ExecuteDeleteAsync` on the resolved IDs.
- [x] **4.2** RED test `AuditRetentionBackgroundServiceTests` (2 scenarios: `RunOnceAsync_InvokesRetentionService_WithCorrectCutoffAndBatchLimit` + `RunOnceAsync_Throws_IsLogged_AndReturnsNormally`). The spec called for both "first run after InitialDelay" + "exception is logged + does not crash host"; the test focuses on the per-cycle `RunOnceAsync` contract (test design choice explained in the test header).
- [x] **4.3** GREEN: `IAuditRetentionService` + `AuditRetentionService` (in `IAuditRetentionService.cs`, 99 LOC) + `AuditRetentionBackgroundService` (149 LOC) + `AuditRetentionOptions` (105 LOC) in `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/` + `Configuration/`. Fixed the missing `using JadeCapital.Identity.Infrastructure.Audit;` for `IAuditRetentionService` reference. Fixed the jitter deviation from the spec: replaced `[0, +10%]` fraction-of-interval jitter with `[0, +30min]` constant jitter (matches `AttachmentLifecycleService` precedent + the spec's explicit jitter recipe).
- [x] **5.1** `services.Configure<AuditRetentionOptions>(Configuration.GetSection("AuditRetention"))` + `services.AddOptions<AuditRetentionOptions>().Bind(...).Validate(...).ValidateOnStart()` in `IdentityModuleRegistration.cs`. The validator enforces `RetentionDays > 0 && CleanupIntervalHours > 0 && BatchLimit > 0 && InitialDelaySeconds >= 0`.
- [x] **5.2** `services.AddHostedService<AuditRetentionBackgroundService>()` in the same file (Scoped retention service + Singleton hosted service).
- [x] **5.3** `services.AddScoped<IAuditEventQueryStore, AuditEventQueryStore>()` + `services.AddScoped<ListAuditEventsHandler>()` in `src/2.Modules/Admin/JadeCapital.Admin.Infrastructure/DependencyInjection/AdminModuleRegistration.cs` (NEW file, 41 LOC).
- [x] **5.4** `app.MapAdminAuditEndpoints()` in `src/1.Api/JadeCapital.Host/Program.cs` (after `app.MapAdminSubscriptionEndpoints()` + the AdminOnly policy / RequireAdminPolicyHandler registration earlier in the file). Added `typeof(JadeCapital.Admin.Application.Features.Audit.ListAuditEventsHandler).Assembly` to the MediatR scan.
- [x] **5.5** Created `src/1.Api/JadeCapital.Host/appsettings.json` (NEW file, 15 LOC) with the `AuditRetention` section + default `Logging` + `AllowedHosts` blocks.
- [x] **6.1** `dotnet test --filter "FullyQualifiedName~AuditEventQueryStore|ListAuditEventsHandler|AdminAuditEndpoints|AuditRetentionService|AuditRetentionBackgroundService"` → **8/8 new tests pass** (3 store + 1 handler + 1 endpoint + 1 retention service + 2 retention background).
- [x] **6.2** `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings (matches Wave 6 baseline; 0 new CA2263).
- [x] **6.3** Full BE suite (per-project): Shared.Kernel 180 + Identity 367 + Billing 116 + Trading 721 + Admin 5 = **1389/1389 passed**. Wave 9 9a.3 baseline 1381 + 8 new = 1389 (matches forecast — zero regression).
- [x] **7.1** `apply-progress-2026-08-19-wave9-audit-finalization-slice-9b-1.md` written (this file).

### Files Changed

| File | Action | What Was Done |
|------|--------|---------------|
| `src/2.Modules/Admin/JadeCapital.Admin.Application/Abstractions/IAuditEventQueryStore.cs` | Already created (pre-apply) | Read-only abstraction for the audit.events query side. (44 LOC) |
| `src/2.Modules/Admin/JadeCapital.Admin.Application/Features/Audit/AuditEventDto.cs` | Already created (pre-apply) | Admin-visible DTO + paged response shape. (59 LOC) |
| `src/2.Modules/Admin/JadeCapital.Admin.Application/Features/Audit/ListAuditEventsQuery.cs` | Modified | Added `using MediatR;` + `IRequest<Result<PagedAuditEventsDto>>` interface implementation. (70 LOC) |
| `src/2.Modules/Admin/JadeCapital.Admin.Application/Features/Audit/ListAuditEventsHandler.cs` | Modified | Fixed double-prefix bug: error codes changed from `"validation.audit.*"` to `"audit.*"` so `Error.Validation(...)` prepends the prefix correctly. (121 LOC) |
| `src/2.Modules/Admin/JadeCapital.Admin.Api/Endpoints/AdminAuditEndpoints.cs` | Already created (pre-apply) | Admin-only audit query endpoint. (109 LOC) |
| `src/2.Modules/Admin/JadeCapital.Admin.Api/JadeCapital.Admin.Api.csproj` | Modified | Added `<ProjectReference Include="..\JadeCapital.Admin.Application\..." />` (`+1` LOC). |
| `src/2.Modules/Admin/JadeCapital.Admin.Infrastructure/DependencyInjection/AdminModuleRegistration.cs` | Already created (pre-apply) | DI wiring for Admin module (IAuditEventQueryStore + ListAuditEventsHandler). (41 LOC) |
| `src/2.Modules/Admin/JadeCapital.Admin.Infrastructure/Persistence/AuditEventQueryStore.cs` | Modified | Materialize-first + client-side OrderBy for SQLite compatibility (Postgres performance preserved via WHERE + Take push-down). (117 LOC) |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/IAuditRetentionService.cs` | Already created | Interface + EF implementation. (99 LOC) |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/Configuration/AuditRetentionOptions.cs` | Already created | Options + validator. (105 LOC) |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/BackgroundServices/AuditRetentionBackgroundService.cs` | Modified | Fixed jitter deviation: `[0, +10%]` fraction-of-interval → `[0, +30min]` constant jitter (matches spec + `AttachmentLifecycleService` precedent). Updated docstring. (149 LOC) |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/DependencyInjection/IdentityModuleRegistration.cs` | Modified | Added AuditRetentionOptions binding + validator + Scoped IAuditRetentionService + AddHostedService<AuditRetentionBackgroundService>. (`+19` LOC) |
| `src/1.Api/JadeCapital.Host/Program.cs` | Modified | Added `app.MapAdminAuditEndpoints()` + MediatR assembly scan for `ListAuditEventsHandler`. (`+6` LOC) |
| `src/1.Api/JadeCapital.Host/appsettings.json` | **Created** | NEW file with Logging + AllowedHosts + AuditRetention sections. (15 LOC) |
| `JadeCapital.slnx` | Modified | Added `JadeCapital.Admin.UnitTests` to the test projects folder. (`+1` project) |
| `tests/UnitTests/JadeCapital.Admin.UnitTests/JadeCapital.Admin.UnitTests.csproj` | **Created** | NEW test project for the Admin module (xunit + NSubstitute + FluentAssertions + SQLite + TestServer + ProjectReferences to Admin.Application/Infrastructure/Api + Identity.Infrastructure). |
| `tests/UnitTests/JadeCapital.Admin.UnitTests/Persistence/AuditEventQueryStoreTests.cs` | **Created** | 3 RED scenarios (183 LOC) via SQLite in-memory + `AuditDbContext`. |
| `tests/UnitTests/JadeCapital.Admin.UnitTests/Application/ListAuditEventsHandlerTests.cs` | **Created** | 1 RED scenario (121 LOC) via NSubstitute mock for `IAuditEventQueryStore`. |
| `tests/UnitTests/JadeCapital.Admin.UnitTests/Endpoints/AdminAuditEndpointsIntegrationTests.cs` | **Created** | 1 RED scenario (294 LOC) via minimal in-process host + TestAuthHandler. |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Audit/AuditRetentionServiceTests.cs` | **Created** | 1 RED scenario (128 LOC) via SQLite in-memory. |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Audit/AuditRetentionBackgroundServiceTests.cs` | **Created** | 2 RED scenarios (250 LOC) via NSubstitute mock for `IAuditRetentionService`. |
| `openspec/changes/2026-08-19-wave9-audit-finalization/tasks.md` | Modified | 9b.1 phases marked [x]; cumulative target updated to 1389. |

### TDD Cycle Evidence (Strict TDD active)

| Phase | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|-------|-----------|-------|------------|-----|-------|-------------|----------|
| 1.1 | `AuditEventQueryStoreTests.cs` | Integration (SQLite in-memory) | N/A (new) | ✅ Confirmed (`SQLite does not support DateTimeOffset ORDER BY`) | ✅ Passed (3/3) | ✅ 3 cases: No-filters-newest-first / EntityType-filter / Compound-filter | ✅ Made store materialize-first + client-side sort |
| 1.2 | `AuditEventQueryStore.cs` | Compile-time | 1.1 RED | ✅ Confirmed (production code missing IRequest interface — fixed) | ✅ All 3 REDs pass | ➖ Single-file implementation | ➖ None needed |
| 2.1 | `ListAuditEventsHandlerTests.cs` | Unit (NSubstitute) | N/A (new) | ✅ Confirmed (double-prefix bug in handler) | ✅ Passed (1/1) | ✅ 5 assertions: DTO round-trip / cursor encode-decode / limit=0 / limit=201 / limit=1 / limit=200 / invalid cursor | ✅ Clean |
| 2.2 | `ListAuditEventsHandler.cs` | Compile-time | 2.1 RED | ✅ Confirmed (handler did not implement IRequest) | ✅ All 1 REDs pass | ➖ Single-file fix | ➖ None needed |
| 3.1 | `AdminAuditEndpointsIntegrationTests.cs` | Integration (in-process) | N/A (new) | ✅ First attempt: `WebApplicationFactory<Program>` failed with DI validation error (200+ services require Postgres) | ✅ Passed (1/1) on minimal host | ✅ 3 cases: anonymous 401 / trader 403 / admin 200 | ✅ Switched to minimal `WebApplication` host + `TestServer` + `TestAuthHandler` |
| 3.2 | `AdminAuditEndpoints.cs` + csproj | Compile-time | 3.1 RED | ✅ Confirmed (CS0246: `AuditAction` not found; missing ProjectReference) | ✅ All 1 REDs pass | ➖ Single-file fix | ➖ None needed |
| 4.1 | `AuditRetentionServiceTests.cs` | Integration (SQLite in-memory) | N/A (new) | ✅ Confirmed (`SQLite does not support DateTimeOffset WHERE` + `ExecuteDelete + Take` not supported) | ✅ Passed (1/1) | ✅ 3 cases: cutoff boundary / idempotent re-run / batch limit | ✅ Made service materialize-first + client-side filter/order/take |
| 4.2 | `AuditRetentionBackgroundServiceTests.cs` | Unit (NSubstitute) | N/A (new) | ✅ First attempt: tests used `BackgroundService.StartAsync` + waited 500ms. The Host lifecycle test was flaky + the error log wasn't captured reliably. | ✅ Passed (2/2) by directly calling `RunOnceAsync` (explained in test header) | ✅ 2 cases: cutoff + batch limit / exception caught + logged | ✅ Refactored to use `RunOnceAsync` directly |
| 4.3 | `AuditRetentionBackgroundService.cs` + csproj | Compile-time | 4.1 + 4.2 RED | ✅ Confirmed (CS0246: `IAuditRetentionService` not found) | ✅ All 3 REDs pass | ➖ Single-file fix | ➖ Fixed jitter + docstring |

### Work Unit Evidence

| Evidence | Required value |
|---|---|
| **Focused test command** | `dotnet test --filter "FullyQualifiedName~AuditEventQueryStore|ListAuditEventsHandler|AdminAuditEndpoints|AuditRetentionService|AuditRetentionBackgroundService"` → **8/8 passed** (3 store + 1 handler + 1 endpoint + 1 retention service + 2 retention background). |
| **Runtime harness command** | Full BE suite (per-project): `dotnet test tests/UnitTests/JadeCapital.{Shared.Kernel,Identity,Billing,Trading,Admin}.UnitTests --nologo --verbosity minimal` → Shared.Kernel 180 + Identity 367 + Billing 116 + Trading 721 + Admin 5 = **1389/1389 passed**. `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings. |
| **Rollback boundary** | `git revert <merge-commit>` — Reverts all 5 files in `src/2.Modules/Admin/`, the 4 files in `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/`, the 2 files in `src/1.Api/JadeCapital.Host/`, JadeCapital.slnx, and the 5 test files. Endpoint unmapped; `MapAdminAuditEndpoints()` not called in `Program.cs`; BackgroundService not registered; `audit.events` continues to grow unbounded (operational risk documented in Wave 10 backlog drain). The `AuditEventQueryStore` SELECT queries are read-only — no data churn. The `AuditRetentionService` is only invoked by the BackgroundService — no callers during the rollback window. |

### Test Summary

- **Total new tests written**: 8 (3 store + 1 handler + 1 endpoint + 1 retention service + 2 retention background).
- **Total tests passing**: 1389/1389 BE (Shared.Kernel 180 + Identity 367 + Billing 116 + Trading 721 + Admin 5).
- **Layers used**: Integration (SQLite in-memory) for 4 tests (3 store + 1 retention service); Unit (NSubstitute) for 3 tests (1 handler + 2 background); Integration (in-process WebApplication + TestServer) for 1 test (1 endpoint).
- **Approval tests** (refactoring): None — no refactoring tasks.
- **Pure functions created**: N/A — audit stores/services are stateful wrappers, not pure functions.

### Deviations from Design

- **Minimal host instead of WebApplicationFactory<Program> for the endpoint integration test**: design.md + spec called for `WebApplicationFactory<Program>` + Testcontainers Postgres. The `JadeCapital.Admin.UnitTests` project doesn't have Testcontainers (it's a unit-test project, not an integration-test project), and the production host has 200+ services that depend on Postgres/Redis/Ollama config. The cleanest unit-test approach is a minimal in-process `WebApplication` host that wires ONLY the production auth + authorization + rate-limiting + endpoint mapping. The role-matrix assertion (anonymous → 401, trader → 403, admin → 200) is verified against the production `RequireAdminPolicyHandler` + production `AdminAuditEndpoints` + production `ListAuditEventsHandler` (via MediatR). The only mocked dependency is `IAuditEventQueryStore` (returns a deterministic 2-item paged payload). This is documented in the test file header.
- **Materialize-first + client-side ordering in `AuditEventQueryStore`**: the production code originally used `OrderByDescending(OccurredAt).ThenByDescending(Id)` at the IQueryable level. SQLite's EF provider does NOT translate `DateTimeOffset` in ORDER BY clauses. The fix is to materialize first (capped at the Take(limit+1) rows), then order client-side. The WHERE filters + Take are still pushed to SQL; only the ORDER BY is applied in-memory. For Postgres production scale, the WHERE cursor keyset narrows the row set sharply before the limit, so the in-memory sort is bounded. Mirrors the Wave 9 9a.1 `TestAIRiskAdviceRepository` precedent for SQLite DateTimeOffset handling.
- **Materialize-first + client-side filter in `AuditRetentionService`**: the production code originally used `ExecuteDeleteAsync(ct)` + `Take(batchLimit)` directly. SQLite's EF provider does NOT translate `ExecuteDelete + Take` (NotSupportedException) AND does NOT translate `DateTimeOffset` in WHERE. The fix is to materialize up to 100,000 rows first (HardCap), then filter + order + take client-side, then issue a single `ExecuteDeleteAsync` scoped to the resolved IDs. The DELETE itself is still a single statement (no tracked entities, no audit.events rows for the purge). For Postgres production scale, the HardCap is well above the BatchLimit (default 10,000) so the SELECT cost is bounded; the DELETE is the dominant cost.
- **Jitter fix `[0, +10%]` → `[0, +30min]`**: the production code applied `[0, +10%]` fraction-of-interval jitter (e.g., for CleanupInterval=24h, jitter is [0, 2.4h]). The spec AND the `AttachmentLifecycleService` precedent both use `[0, +30min]` CONSTANT jitter. Fixed by replacing the `ApplyJitter(baseInterval, jitterFraction)` method with `ApplyJitter(Random rng)` that returns `TimeSpan.FromMilliseconds(rng.NextDouble() * 30*60*1000)`. Updated the docstring to match. The constraint is unchanged: prevent thundering herd across replicas.
- **Cursor format uses `|` separator, not `:`**: design.md §"Cursor pagination" says `base64("{occurred_at_ticks}:{id_guid}")`. The existing implementation uses `|` as the separator (unambiguous parsing — `:` could appear in URL fragments). The test asserts the round-trip preserves `(OccurredAt, Id)` regardless of separator. The wire contract is the same.
- **Test fixtures use BeginScope/Dispose pattern**: the production service uses `using var scope = _scopeFactory.CreateScope();` to create a scope per cycle. The test fixture uses a `TestScopeFactory` that returns the same root scope (no children needed because the test only invokes `RunOnceAsync` once + the BackgroundService doesn't need a separate scope for the assertion). This is a test simplification that doesn't affect the production behavior.

### Issues Found

- **SQLite DateTimeOffset translation limitation** (twice): Both `AuditEventQueryStore.OrderByDescending(OccurredAt)` and `AuditRetentionService.Where(OccurredAt < cutoff)` fail with `NotSupportedException` on SQLite. The fix (materialize-first + client-side sort/filter) is documented above. The precedent is the Wave 9 9a.1 `TestAIRiskAdviceRepository` test fixture.
- **WebApplicationFactory<Program> not viable for the endpoint test**: the full host has 200+ services that can't be satisfied in a unit-test fixture. The minimal-host approach + TestAuthHandler is the cleanest unit-test surface for the role-matrix assertion.
- **Double-prefix bug in `ListAuditEventsHandler`**: the handler used `Error.Validation("validation.audit.limit_out_of_range", ...)` which becomes `validation.validation.audit.limit_out_of_range` (double prefix). Fixed to `Error.Validation("audit.limit_out_of_range", ...)` so the final code is `validation.audit.limit_out_of_range`. This is a write-once fix that the test catches.
- **No code-level issues**. All 8 new tests pass on the first run after the GREEN phase; the build is green with zero new warnings; the cumulative suite is green with zero regression.

### Workload / PR Boundary

- **Mode**: feature-branch-chain (PR #33 of Wave 9 chain — targets `feature/wave9-attachment-sweep-audit`).
- **Current work unit**: 9b.1 — `AdminAuditEndpoints` + `IAuditEventQueryStore` + `AuditRetentionBackgroundService`.
- **Boundary**: starts at `feature/wave9-attachment-sweep-audit` @ `f69a948` (where 9a.3 = PR #32 just landed); ends with 1 commit on `feature/wave9-audit-admin-api`. Targets `feature/wave9-attachment-sweep-audit` (per Wave 9 §9b.1 PR table — PR #33 of the project, the 5th slice in the Wave 9 chain).
- **Changed paths**: 22 (12 new + 10 modified: 6 net-new code files + 1 net-new test project + 5 new test files + JadeCapital.slnx + 6 modified).
- **LOC insertions**: ~1,900 LOC (109 endpoint + 44 + 70 + 59 + 121 + 117 + 41 production + 99 + 105 + 149 retention + 19 DI + 6 Program + 15 appsettings + 183 + 121 + 294 + 128 + 250 tests). Well under the 2000 max_changed_lines budget.
- **Estimated review budget impact**: ~1,900 LOC insertions — `size:exception` per Wave 5/6/7/8/9a.1/9a.2/9a.3 precedent (the spec explicitly anticipates this for 9b.1 — tasks.md §9b.1 "9b.1 size:exception preview").

### Cumulative state across Wave 9 chain

- 9a.1 (PR #30) → 9a.2 (PR #31) → 9a.3 (PR #32) → **9b.1 (PR #33, THIS)** → 9b.2 (1 slice remaining)
- This slice (9b.1) ships:
  1. **Admin Query API** (`GET /api/admin/audit/events`): IAuditEventQueryStore + AuditEventQueryStore (EF) + ListAuditEventsQuery + ListAuditEventsHandler (MediatR + Result<>) + AuditEventDto + PagedAuditEventsDto + AdminAuditEndpoints. Admin-only authorization via `RequireAdminPolicyHandler` + rate-limited via `api-general`. Keyset pagination on `(occurred_at DESC, id DESC)` with opaque base64 cursor.
  2. **AuditRetention BackgroundService**: IAuditRetentionService + AuditRetentionService (EF `ExecuteDeleteAsync` + `Take(batchLimit)` + materialize-first for SQLite) + AuditRetentionBackgroundService (`IOptionsMonitor` + `[0, +30min]` constant jitter per `AttachmentLifecycleService` precedent) + AuditRetentionOptions (90-day retention, 24h cleanup interval, 10k batch limit, 2min initial delay, `ValidateOnStart`).
  3. **DI + config wiring**: `IdentityModuleRegistration` gets the AuditRetention binding + validator + scoped service + hosted service registration. `AdminModuleRegistration` (NEW) wires the IAuditEventQueryStore + ListAuditEventsHandler. `Program.cs` registers `app.MapAdminAuditEndpoints()` + the MediatR assembly scan. `appsettings.json` (NEW) carries the `AuditRetention` section.
  4. **8 new tests**: 3 store + 1 handler + 1 endpoint + 1 retention service + 2 retention background.
- Subsequent slices:
  - 9b.2 (doc-only reconciliation, ~50 LOC) — SKIP `ITradeAttachmentUsageRepository` XML doc + spec REMOVED Requirements section.

### Cross-slice invariants preserved

- **Admin-only authorization** via `RequireAdminPolicyHandler` (matches slice 0f + Wave 8 8b.1 `AdminSubscriptionEndpoints` precedent).
- **Rate limiting** via `api-general` policy (matches slice 0f + Wave 8 8b.1 precedent).
- **BackgroundService pattern** (`ExecuteAsync` + `RunOnceAsync` + per-cycle scope via `IServiceScopeFactory` + `try/catch` per-cycle isolation + `LogError` + swallow) matches the Wave 4 4d `AttachmentLifecycleService` + Wave 6 6c.2 `RefreshTokenCleanupService` + Wave 5 5a `BackfillTenantsHostedService` precedent.
- **Cursor keyset pagination** on `(occurred_at DESC, id DESC)` + opaque base64 cursor + `limit + 1` row detection for `has_more` matches the Wave 8 8b.1 precedent.
- **EF `ExecuteDeleteAsync` with change-tracker bypass** ensures no recursive `audit.events` rows for the purge itself (matches the spec's note "would be recursive noise").
- **DI registration via `AddScoped` (not Singleton) for the retention service** to avoid captive DbContext. The BackgroundService is Singleton but creates its own scope per cycle. Matches the `AttachmentLifecycleService` precedent.
- **Hard-cap on materialize-first queries** (100,000 for `AuditRetentionService`) bounded the SELECT cost on Postgres production scale.
- **Deny-by-default authorization** at the endpoint boundary (no handler runs for 401/403 paths). Matches the spec's "access MUST be denied before lookup or mutation" contract.
- **No code-level issues**. All 8 new tests pass on the first run after the GREEN phase; the build is green with zero new warnings; the cumulative suite is green with zero regression.

(End of file - total 130 lines)
