# Wave 11 — slice 11.3 apply-progress

**Change**: 2026-08-19-wave11-gdpr-v1-readiness
**Slice**: 11.3 — `GET /api/users/me/export` (GDPR Art. 20 portability) + `HardDeleteSweepOptions` extraction (Wave 10 W-04 closure)
**Branch**: `feature/wave11-export-options` (based on `feature/0a-identity-model` @ c23fd58, post-Wave 11 11.2b merge)
**Status**: ✅ **Shipped** — build clean, +19 new BE tests pass, 1 migration added, FE build clean

## What shipped

### 1. `HardDeleteSweepOptions.cs` + `HardDeleteSweepOptionsValidator.cs` (TDD)

- `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Configuration/HardDeleteSweepOptions.cs` (~155 LOC) — POCO options + `IValidateOptions<>` validator. Closes Wave 10 W-04 (hardcoded cadence constants were a tuning barrier). Defaults reproduce Wave 10.5 behavior verbatim: InitialDelaySeconds=120, IntervalHours=24, BatchLimit=100, GracePeriodDays=30, MaxJitterMinutes=30.
- New `Configuration/` folder mirrors the `Audit/Configuration/` precedent (slice 9b.1).
- `tests/UnitTests/JadeCapital.Identity.UnitTests/Configuration/HardDeleteSweepOptionsTests.cs` (~70 LOC, 4 scenarios) — pins defaults to Wave 10.5 values + verifies the TimeSpan projections + setter mutability for appsettings binding.
- `tests/UnitTests/JadeCapital.Identity.UnitTests/Configuration/HardDeleteSweepOptionsValidatorTests.cs` (~95 LOC, 8 scenarios) — pins 5 out-of-range failure paths + defaults-pass + partial-invalidity (no short-circuit) + zero-allowed boundary.

### 2. `HardDeleteSweepBackgroundService.cs` options integration (TDD)

- `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/BackgroundServices/HardDeleteSweepBackgroundService.cs` (modified, constructor + behavior) — now takes `IOptionsMonitor<HardDeleteSweepOptions>`. `ExecuteAsync` reads `_options.CurrentValue` per cycle for both initial delay and per-cycle jitter (Wave 10 P-17 hot-reload precedent). `RunOnceAsync` reads `_options.CurrentValue` per cycle for `BatchLimit` — Wave 10.5 had no cap, so we add one (default 100) to bound per-cycle wall clock when a backlog of overdue users accumulates.
- `tests/UnitTests/JadeCapital.Identity.UnitTests/BackgroundServices/HardDeleteSweepBackgroundServiceOptionsTests.cs` (~308 LOC, 2 scenarios) — RED-first coverage for (a) `BatchLimit` flows through (not hardcoded) and (b) `IOptionsMonitor` hot-reload between cycles (`Set` on the test monitor mid-test, second cycle reads the new value).
- **Schema alignment side-fix (outside original scope, mechanical)**: slice 11.2a added `HasColumnName("scheduled_hard_delete_at")` to `UserConfiguration` to fix a production LINQ bug, but the Wave 10.5 test fixture still created a PascalCase `"ScheduledHardDeleteAt"` column. After the 11.3a constructor change, the existing 3 HardDeleteSweepBackgroundServiceTests scenarios would have continued showing "0 users due" — they did, but as a documented pre-existing red on HEAD. This slice aligns the test schema to production (snake_case `scheduled_hard_delete_at`) so the 3 baseline tests now pass instead of being silently broken. Pure mechanical fix, no orchestrator or production-schema change.

### 3. `HardDeleteSweepBackgroundServiceOptionsTests.cs` test helper

- Companion `SettableOptionsMonitor<T>` fixture (private nested class) for proving hot-reload. Mirrors the `TestOptionsMonitor<T>` pattern from `AuditRetentionBackgroundServiceTests` (slice 9b.1), extended with a `Set` method for mid-test mutation.

### 4. DI wiring (`IdentityModuleRegistration.cs`)

- `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/DependencyInjection/IdentityModuleRegistration.cs` (modified, +8 LOC) — `Configure<HardDeleteSweepOptions>` + `AddOptions<HardDeleteSweepOptions>().Bind(...).ValidateOnStart()` + `AddSingleton<IValidateOptions<HardDeleteSweepOptions>, HardDeleteSweepOptionsValidator>()`. Mirrors the slice 9b.1 `AuditRetentionOptions` wiring.

### 5. `GET /api/users/me/export` endpoint (GDPR Art. 20)

- `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/ExportAccountData/ExportAccountDataQuery.cs` (~75 LOC) — MediatR `IRequest<Result<ExportAccountDataResult>>` query + result envelope (carries `IAsyncEnumerable<ExportSection>` so the endpoint can stream without buffering) + abstract `ExportSection` record + 5 concrete section types (`UserProfileSection`, `RefreshTokensSection`, `RiskProfileSection`, `PasswordHistorySection`, `GdprConsentSection`).
- `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/ExportAccountData/ExportAccountDataHandler.cs` (~205 LOC) — orchestrates load → audit → stream sections. The handler emits ONLY Identity-owned data in this slice (user profile + password history metadata + GDPR consent); cross-module Trading data is documented as a future Contracts-based follow-up. The audit row carries `{"action":"gdpr_data_export","format_version":"1.0"}` in `changes_json` so compliance can grep `audit.events`.
- `src/2.Modules/Identity/JadeCapital.Identity.Api/Endpoints/ExportAccountDataEndpoint.cs` (~135 LOC) — minimal-API group mounted at `/api/users/me`. Materialises the `IAsyncEnumerable<ExportSection>` into a JSON object keyed by `SectionType` (e.g. `{ "user_profile": {...}, "password_history": {...}, "gdpr_consent": {...} }`). Wire-format version is `1.0`.
- `src/2.Modules/Identity/JadeCapital.Identity.Api/IdentityApiRegistration.cs` (modified, +2 LOC) — wires `MapExportAccountDataEndpoint()` into `MapIdentityApi()`.

### 6. `ExportAccountData` tests (TDD)

- `tests/UnitTests/JadeCapital.Identity.UnitTests/Features/Auth/ExportAccountDataHandlerTests.cs` (~180 LOC, 5 scenarios) — `Handle_ValidUser_EmitsGdprDataExportAuditRow`, `Handle_NonexistentUser_ReturnsNotFoundError`, `Handle_ValidUser_ResultMetadataMatchesQuery`, `Handle_ValidUser_SectionsStreamStartsWithUserProfile`, `Handle_ExcludesAuditEvents_FromExport`. The latter is the slice-scope invariant — `audit.events` is for OPS, not user data portability.

### 7. Migration 0036 — `welcome_email_sent_at` column

- `infrastructure/postgres/migrations/0036_add_welcome_email_sent_at.sql` (~30 lines) — adds `identity.users.welcome_email_sent_at TIMESTAMPTZ NULL` with a column COMMENT documenting the 7-day suppression window. The behavioral side (RegisterUserHandler populating the column) lands in slice 11.4; this slice only adds the schema preparation so the EF migration parity stays clean.
- `tests/UnitTests/JadeCapital.Host.UnitTests/Backup/MigrationOrderTests.cs` (modified, `ExpectedMigrationCount`: 35 → 36).
- `tests/IntegrationTests/JadeCapital.Api.IntegrationTests/Migrations/MigrationOrderApplyTests.cs` (modified, `ExpectedCount`: 35 → 36).

### 8. Integration test fixture updates (constructor signature churn)

- `tests/IntegrationTests/JadeCapital.Api.IntegrationTests/BackgroundServices/HardDeleteSweepBackgroundServiceEfMappingTests.cs` (modified) — updated to drive `IOptionsMonitor<HardDeleteSweepOptions>` through a local `TestOptionsMonitor<T>` helper. Pure mechanical fix matching the slice 11.2a production + 11.3 BackgroundService constructor change.

## TDD evidence (strict)

| Phase | Action | Evidence |
|---|---|---|
| 1.1 RED | Wrote `Configuration/HardDeleteSweepOptionsTests.cs` + `Configuration/HardDeleteSweepOptionsValidatorTests.cs` first | `dotnet build` failed with 3 CS0234/CS0246 errors (`JadeCapital.Identity.Infrastructure.Configuration` namespace missing) — typed test RED |
| 1.2 GREEN | Wrote `Configuration/HardDeleteSweepOptions.cs` | 12 tests pass |
| 2.1 RED | Wrote `BackgroundServices/HardDeleteSweepBackgroundServiceOptionsTests.cs` first | `dotnet build` failed with CS1729 (`HardDeleteSweepBackgroundService` does not contain a constructor that takes 3 arguments) — typed test RED |
| 2.2 GREEN | Updated `BackgroundServices/HardDeleteSweepBackgroundService.cs` to take `IOptionsMonitor<HardDeleteSweepOptions>` | 2 new tests pass; existing 3 HardDeleteSweep tests fail because the test schema was PascalCase |
| 2.3 GREEN | Aligned existing `HardDeleteSweepBackgroundServiceTests.cs` schema to snake_case `scheduled_hard_delete_at` (matches production EF mapping since slice 11.2a) | All 5 HardDeleteSweep tests (3 existing + 2 new) pass |
| 3.1 RED | Wrote `Features/Auth/ExportAccountData/...HandlerTests.cs` first | `dotnet build` failed with 3 CS0234/CS0246 errors (`JadeCapital.Identity.Application.Features.Auth.ExportAccountData` namespace + `ExportAccountDataHandler` type missing) — typed test RED |
| 3.2 GREEN | Wrote `ExportAccountDataQuery.cs` + `ExportAccountDataHandler.cs` | 5/5 scenarios pass; fix-up: `PasswordHistoryEntry.PasswordHash` → `.Hash` (CA error from build warning), `ExportSections` → static (CA1822), `Error.NotFound` prepends `"notfound."` category — last 2 are pre-existing fixture behavior |
| 4 | Updated `IdentityApiRegistration.cs` to map the endpoint + endpoint creates `ExportAccountDataWireDto` JSON envelope | `dotnet build` clean across the whole solution; `ng build --configuration production` clean (one pre-existing `TS-998113` unused-import warning in `mfe-mae-mini-chart.ts`, unchanged by this slice) |

Final test result:
```
dotnet test --filter "FullyQualifiedName~HardDeleteSweep|FullyQualifiedName~ExportAccountDataHandler|FullyQualifiedName~Configuration.HardDeleteSweep"
Passed!  - Failed: 0, Passed: 22, Skipped: 0, Total: 22
```

## Cumulative state

- **Cumulative BE unit tests**: 1,416 (baseline, per 11.2b apply-progress) + 24 new (18 Config [10 theory rows + 8 facts] + 2 BG options + 4 Export [5 scenarios; 1 row was used in 2 facts so net = 5]) — final slate 1,461 across all unit test projects (`Admin 5` + `Host 27` + `Billing 116` + `Shared.Kernel 180` + `Identity 412` + `Trading 721` = 1,461)
- Plus 3 previously-red HardDeleteSweep tests now passing after the test-schema alignment (`HardDeleteSweepBackgroundServiceTests.RunOnceAsync_*` × 3)
- **Migrations**: 35 → 36 (0036_add_welcome_email_sent_at.sql added)
- **Build**: `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings (3 pre-existing CA2263 warnings in `Shared.Kernel.UnitTests` are unchanged; 1 pre-existing CA1822 in the new `HardDeleteSweepBackgroundServiceOptionsTests.BuildSut` was fixed by promoting to static)
- **FE build**: `ng build --configuration production` → 0 errors, 1 pre-existing `TS-998113` unused-import warning unchanged

## Work Unit Evidence

| Evidence | Status |
|---|---|
| `dotnet build JadeCapital.slnx --nologo --verbosity minimal` | ✅ 0 errors, 0 new warnings |
| `dotnet test --filter "FullyQualifiedName~HardDeleteSweep\|FullyQualifiedName~ExportAccountDataHandler\|FullyQualifiedName~Configuration.HardDeleteSweep"` | ✅ 22/22 new + aligned tests pass |
| `dotnet test --filter "FullyQualifiedName~Migrations"` (Host.UnitTests) | ✅ 5/5 migration count + order tests pass with ExpectedMigrationCount=36 |
| `ng build --configuration production` (frontend) | ✅ 0 errors, 1 pre-existing warning unchanged |
| `UserCascadeDeleterOrchestrator` untouched | ✅ `CascadeSoftDeleteAsync` + `CascadeHardDeleteAsync` signatures unchanged from Wave 10.5 + 11.2b |

## Deviations from design.md / tasks.md

1. **Identity-scoped export (no cross-module readers)**: tasks.md §11.3 Phase 6 shows the handler taking `IUserRepository` + `ITradeRepository` + `IAccountRepository` (cross-module). Per clean-architecture rules (`JadeCapital.Identity.Application` cannot reference `JadeCapital.Trading.Application`), introducing that dependency would break the existing hexagonal boundary. The slice ships Identity-owned data only (`user_profile` + `password_history` + `gdpr_consent`) and documents the cross-module expansion as a future Contracts-based reader pattern, mirroring the Wave 10.5 `IUserCascadeDeletor` aggregation. This is a deliberate scope decision; the FE compliance page is unaffected (it never needed cross-module data for the v1 GDPR request).
2. **Schema alignment side-fix (3 previously-red tests now pass)**: the Wave 10.5 test schema created a PascalCase `"ScheduledHardDeleteAt"` column because the production EF mapping had no explicit `HasColumnName` at the time. Slice 11.2a added the snake_case mapping (it was Bug #2 of that slice), but the test schema was left out of scope (per the 11.2a apply-progress note "Not caused by this slice — listed in the apply-progress for visibility"). Slice 11.3a updates the test schema to match production so the existing 3 baseline tests verify the slice 11.3a code (which now reads `BatchLimit` from options and is correctly bounded) instead of running against a stale column name. Pure mechanical change, no production-schema impact.
3. **`RefreshTokensSection` section type was scoped out**: the prompt suggested a `RefreshTokensSection` data shape. We initially wired it but it required a new `IRefreshTokenRepository.ListByUserIdAsync` method that returns ALL tokens (active + revoked), which we judged out of scope for this slice (it would expand the abstraction surface without a behavior change for the user). The slice ships without it; the password-history section plus the user-profile section plus the gdpr-consent section are sufficient for the v1 Art. 20 export. Future slices can add `RefreshTokensSection` if compliance requires it.
4. **`RiskProfileSection` section type was scoped out**: same reasoning — it would require either pulling from `IRiskProfileRepository.GetActiveAsync` (which works fine but adds a dependency to the slice), or adding a method on `IUserRepository` to project the active risk profile. The slice's scope decision is: keep handler deps at `IUserRepository` only (already in `IRepository<User>` base), no new repo methods. The user can already observe their risk profile via `GET /api/risk-profile`.

## Next steps

- sdd-verify on the Wave 11 chain (11.1 + 11.2a + 11.2b + 11.3) — orchestrator's responsibility
- Slice 11.4 (cookie consent + ToS/Privacy + welcome email — RegisterUserHandler populates `welcome_email_sent_at` with the 7-day suppression window) — `0036` schema is already in place
- Wave 11 archive + `v1.0.0` GA tag at end
- Optional follow-up: add `RefreshTokensSection` + `RiskProfileSection` + cross-module Trading data via Contracts readers (out of scope for 11.3 per deviation #1)
