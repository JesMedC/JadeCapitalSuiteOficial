# Delta for account-lifecycle — Wave 11 HardDeleteSweepOptions Extraction

**Change**: 2026-08-19-wave11-gdpr-v1-readiness
**Wave**: 11 (GDPR v1 readiness — closure of Wave 10.5 narrower scope)
**Slice**: 11.3 — `HardDeleteSweepOptions` extraction + `IOptionsMonitor<HardDeleteSweepOptions>` injection
**Status**: DELTA — extends `openspec/specs/account-lifecycle/spec.md` (Wave 10.5 canonical) with the 4 options-extraction scenarios that were deferred from Wave 10.5 (mechanical extraction per Wave 10 archive lesson #3)
**Strict TDD**: ACTIVE — every Requirement + Scenario here MUST be covered by tests in slice 11.3

## MODIFIED Requirements

### Requirement: HardDeleteSweepBackgroundService uses IOptionsMonitor<HardDeleteSweepOptions>

(Previously: the BackgroundService shipped from Wave 10.5 with hardcoded constants at lines 46-47: `public const int GracePeriodDays = 30;` + `private const double MaxJitterMs = 30d * 60d * 1000d;` + `await Task.Delay(TimeSpan.FromMinutes(2), ...)` (initial delay) + `await Task.Delay(TimeSpan.FromHours(24).Add(...))` (cycle interval). Wave 10 archive lesson #3: "HardDeleteSweepOptions missing — tasks.md specified a separate options class for grace/interval/jitter tuning. Wave 10.5 hardcoded these. Wave 11+ should extract." Wave 11.3 ships the options class + DI wiring + 4 coverage scenarios below.)

The system MUST define `HardDeleteSweepOptions` at `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Configuration/HardDeleteSweepOptions.cs` with the following properties (all nullable-tolerant — defaults applied at validation time):
- `InitialDelaySeconds` (int, default `120`) — delay before the first sweep after process start.
- `IntervalHours` (int, default `24`) — cycle interval (one sweep per `IntervalHours`).
- `MaxJitterMinutes` (int, default `30`) — per-cycle jitter window (`[0, +MaxJitterMinutes]`).
- `GracePeriodDays` (int, default `30`) — grace period between soft-delete + hard-delete.
- `BatchLimit` (int, default `100`) — max users per sweep cycle.

The options MUST be validated at startup via `IValidateOptions<HardDeleteSweepOptions>` (Wave 9 `AuditRetentionOptions` precedent) — `GracePeriodDays` MUST be ≥ 1 + ≤ 365, `IntervalHours` MUST be ≥ 1 + ≤ 168, `BatchLimit` MUST be ≥ 1 + ≤ 10,000. The BackgroundService MUST inject `IOptionsMonitor<HardDeleteSweepOptions>` and read `_optionsMonitor.CurrentValue` at the start of each cycle (NOT cached at construction time — supports hot reload via `IOptionsMonitor.OnChange`). The hardcoded constants MUST be removed from the BackgroundService (replaced with options reads). The DI registration in `IdentityModuleRegistration` MUST call `services.Configure<HardDeleteSweepOptions>(configuration.GetSection("HardDeleteSweep"))` + `services.AddOptions<HardDeleteSweepOptions>().ValidateOnStart().Validate(...)`.

#### Scenario: HardDeleteSweepOptions defaults match Wave 10.5 hardcoded behavior (behavior parity)

- GIVEN `HardDeleteSweepOptions` is constructed via `new HardDeleteSweepOptions()` (no configuration override)
- WHEN the property values are read
- THEN `InitialDelaySeconds` MUST equal `120` (matches the `Task.Delay(TimeSpan.FromMinutes(2))` at Wave 10.5 line 57)
- AND `IntervalHours` MUST equal `24` (matches the `Task.Delay(TimeSpan.FromHours(24).Add(...))` at Wave 10.5 line 69)
- AND `MaxJitterMinutes` MUST equal `30` (matches the `MaxJitterMs = 30d * 60d * 1000d` at Wave 10.5 line 47)
- AND `GracePeriodDays` MUST equal `30` (matches the `GracePeriodDays = 30` at Wave 10.5 line 46)
- AND `BatchLimit` MUST equal `100` (the Wave 10.5 implicit limit — verified by reading `RunOnceAsync` at line 81)

(Note: this is the "behavior parity" test. The options MUST NOT change runtime behavior when constructed with defaults — the BackgroundService behaves identically to Wave 10.5.)

#### Scenario: HardDeleteSweepOptions validator fails on out-of-range values

- GIVEN `HardDeleteSweepOptions { GracePeriodDays = 0 }` (below minimum)
- WHEN `IValidateOptions<HardDeleteSweepOptions>.Validate(name, options)` runs
- THEN the result MUST be `ValidateOptionsResult.Fail("HardDeleteSweep:GracePeriodDays must be >= 1")`

- GIVEN `HardDeleteSweepOptions { IntervalHours = 200 }` (above maximum)
- WHEN the validator runs
- THEN the result MUST be `ValidateOptionsResult.Fail("HardDeleteSweep:IntervalHours must be <= 168")`

- GIVEN `HardDeleteSweepOptions { BatchLimit = 50000 }` (above maximum)
- WHEN the validator runs
- THEN the result MUST be `ValidateOptionsResult.Fail("HardDeleteSweep:BatchLimit must be <= 10000")`

- GIVEN `HardDeleteSweepOptions { GracePeriodDays = 30, IntervalHours = 24, BatchLimit = 100, MaxJitterMinutes = 30, InitialDelaySeconds = 120 }` (all defaults)
- WHEN the validator runs
- THEN the result MUST be `ValidateOptionsResult.Success` (defaults pass)

#### Scenario: HardDeleteSweepBackgroundService hot reload via IOptionsMonitor.OnChange

- GIVEN the BackgroundService is running with `_optionsMonitor.CurrentValue.GracePeriodDays = 30`
- WHEN the configuration is updated at runtime (e.g., `appsettings.json` change + `IConfigurationRoot.Reload()`) to `GracePeriodDays = 60`
- AND `_optionsMonitor.OnChange` fires
- THEN the next cycle's grace period check MUST use `GracePeriodDays = 60` (verified via `_optionsMonitor.CurrentValue.GracePeriodDays == 60` at the start of the cycle)
- AND the BackgroundService MUST NOT restart (the change is hot — no process recycle)

(Note: `IOptionsMonitor.OnChange` is fired by the configuration system on `IConfigurationRoot.Reload()`. The BackgroundService subscribes in `ExecuteAsync` and updates its local copy of the options. Verified via xUnit `IOptionsMonitorFake<T>` from `Microsoft.Extensions.Options.Test` package.)

#### Scenario: HardDeleteSweepBackgroundService reads options at each cycle start (not cached)

- GIVEN the BackgroundService is constructed with `IOptionsMonitor<HardDeleteSweepOptions>` where `_optionsMonitor.CurrentValue` is fetched per cycle (NOT cached in a field)
- WHEN `RunOnceAsync` is called 3 times in succession
- AND between calls, `_optionsMonitor.CurrentValue` is updated to different values
- THEN each call MUST read the latest `_optionsMonitor.CurrentValue` (not the value at construction time)
- AND the BackgroundService's behavior MUST reflect the latest options (e.g., if `BatchLimit` changes from 100 → 50, the second call processes at most 50 users)

## Cross-references

- Companion spec: `openspec/specs/account-lifecycle/spec.md` (canonical Wave 10.5 — the BackgroundService + state machine)
- Companion spec: `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/specs/gdpr-endpoint-coverage/spec.md` (the integration tests that exercise `HardDeleteSweepBackgroundService.RunOnceAsync` with `IClock` injection)
- Pattern: `openspec/specs/observability-light/spec.md` (Wave 10.6 — `AuditRetentionOptions` precedent for `IOptions<T>` + `IValidateOptions<T>` + `ValidateOnStart`)
- Source: `openspec/changes/archive/2026-08-18-wave10-v1-readiness/archive-report.md` line 95 (lesson #3) + `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/explore.md` §"Deferred items validation" item 6

## Out of scope

- Per-tenant hard-delete grace period overrides (e.g., Pro tenants get 60-day grace) — Wave 12+ (requires `tenants.grace_period_days` column + per-tenant options resolution)
- Distributed hard-delete coordination across multiple API instances (only one instance should run the sweep at a time) — Wave 13+ (requires distributed lock via Redis)
- Configurable cascade behavior (e.g., Trading-only vs full cascade) — Wave 12+ (requires `cascade_mode` enum on `users`)
- Hard-delete audit event customization (e.g., include `cascade_summary` in `changes_json`) — Wave 12+ (requires `GdprAuditAnonymizer` extension)
