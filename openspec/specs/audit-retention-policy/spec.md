# Audit Retention Policy Specification

## Purpose

Bounded retention + auto-purge for the `audit.events` table. The `audit.events` table grows unbounded (every mutation on a user-owned aggregate emits a row); without retention, the table accumulates ~2500 rows/day (Wave 8 baseline: 100 users × ~5 audited mutations/user/day × 5 aggregates), reaching ~3M rows over 3 years. This spec covers the `AuditRetentionOptions` config contract, `IAuditRetentionService` + `AuditRetentionService`, the `AuditRetentionBackgroundService` scheduling contract, idempotency + per-attempt isolation, and `ValidateOnStart` config validation.

This spec covers the retention lifecycle. It does NOT cover the write path (see `soft-delete-audit`) or the read path (see `audit-query-api`).

## Requirements

### Requirement: AuditRetentionOptions config contract

The system MUST define `AuditRetentionOptions` at `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/Configuration/AuditRetentionOptions.cs` with the following properties and defaults: `RetentionDays` (int, default 90), `CleanupIntervalHours` (int, default 24), `BatchLimit` (int, default 10000), `InitialDelay` (TimeSpan?, default 2 minutes). The options MUST be bound via `services.Configure<AuditRetentionOptions>(Configuration.GetSection("AuditRetention"))` in `IdentityModuleRegistration`. The `appsettings.json` MUST contain an `"AuditRetention": { "RetentionDays": 90, "CleanupIntervalHours": 24, "BatchLimit": 10000 }` block. `ValidateOnStart` MUST enforce `RetentionDays > 0`, `CleanupIntervalHours > 0`, `BatchLimit > 0` — invalid config MUST fail the host at startup (matches Wave 6 6c.2 `JwtOptions` precedent).

#### Scenario: Default config loads at startup

- GIVEN `appsettings.json` contains the default `AuditRetention` block
- WHEN the host starts
- THEN `IOptions<AuditRetentionOptions>` MUST resolve with `RetentionDays = 90`, `CleanupIntervalHours = 24`, `BatchLimit = 10000`, `InitialDelay = TimeSpan.FromMinutes(2)`

#### Scenario: ValidateOnStart rejects zero RetentionDays

- GIVEN `appsettings.json` contains `"AuditRetention": { "RetentionDays": 0, ... }`
- WHEN the host starts with `ValidateOnStart` enabled
- THEN the host MUST fail to start with an `OptionsValidationException` naming `RetentionDays`
- AND the error MUST be logged via Serilog at `Error` level

#### Scenario: ValidateOnStart rejects non-positive BatchLimit

- GIVEN `appsettings.json` contains `"AuditRetention": { "BatchLimit": -1, ... }`
- WHEN the host starts
- THEN the host MUST fail to start with `OptionsValidationException` naming `BatchLimit`

### Requirement: IAuditRetentionService.PurgeOldAsync contract

The interface `IAuditRetentionService` MUST be declared at `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/IAuditRetentionService.cs`. The method `Task<int> PurgeOldAsync(DateTimeOffset cutoff, int batchLimit, CancellationToken ct)` MUST delete all `audit.events` rows where `occurred_at < cutoff`, capped at `batchLimit` rows per call, using EF Core 9 `_db.AuditEvents.Where(e => e.OccurredAt < cutoff).Take(batchLimit).ExecuteDeleteAsync(ct)`. The method MUST return the count of deleted rows.

#### Scenario: PurgeOldAsync deletes rows older than cutoff

- GIVEN 100 audit events: 60 with `occurred_at` before `cutoff` and 40 after
- WHEN `PurgeOldAsync(cutoff, batchLimit: 100, ct)` is called
- THEN 60 rows MUST be deleted
- AND the method MUST return `60`

#### Scenario: PurgeOldAsync caps the delete at BatchLimit

- GIVEN 50000 audit events older than `cutoff`
- WHEN `PurgeOldAsync(cutoff, batchLimit: 10000, ct)` is called
- THEN exactly 10000 rows MUST be deleted (capped)
- AND the method MUST return `10000`
- AND 40000 rows MUST remain (the BackgroundService loops until 0 rows deleted)

#### Scenario: PurgeOldAsync is idempotent

- GIVEN `PurgeOldAsync(cutoff, batchLimit, ct)` has already been called and deleted rows older than `cutoff`
- WHEN `PurgeOldAsync(cutoff, batchLimit, ct)` is called again with the same cutoff
- THEN the method MUST return `0`
- AND NO rows MUST be deleted (idempotent)

### Requirement: EF Core 9 ExecuteDeleteAsync is the single-statement purge

The `AuditRetentionService.PurgeOldAsync` implementation MUST use EF Core 9 `ExecuteDeleteAsync(ct)` (introduced in EF Core 7, mature in EF Core 9) which translates to a single `DELETE FROM audit.events WHERE ...` statement. The implementation MUST NOT iterate tracked entities + `SaveChangesAsync` (which would emit `audit.events` audit rows for its own deletes — a recursive noise). The `audit.events` table has no FK relationships (`entity_id` is intentionally denormalized for append-only semantics), so the DELETE is safe.

#### Scenario: Single DELETE statement is issued

- GIVEN the EF query is `_db.AuditEvents.Where(e => e.OccurredAt < cutoff).Take(batchLimit)`
- WHEN `PurgeOldAsync` runs
- THEN the EF interceptor MUST emit exactly ONE `DELETE` statement (not N+1)
- AND NO `INSERT INTO audit.events` MUST occur during the purge (the purge does NOT generate audit rows for itself)

### Requirement: AuditRetentionBackgroundService scheduling

The `AuditRetentionBackgroundService : BackgroundService` MUST live at `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/AuditRetentionBackgroundService.cs`. The first run MUST be delayed by `InitialDelay` (default 2 minutes) to let the rest of the pipeline settle (matches `BackfillTenantsHostedService` 15s precedent scaled for a heavier first run). Subsequent runs MUST execute every `CleanupIntervalHours` (default 24h) with a `[0, +30min]` jitter applied per cycle to avoid thundering herd across replicas (matches `AttachmentLifecycleService` precedent). Each run MUST be wrapped in `try { ... } catch (Exception ex) { LogError + continue }` so the host NEVER crashes from a retention error (matches `RefreshTokenCleanupService` precedent).

#### Scenario: First run fires after InitialDelay

- GIVEN the host starts at `T0`
- WHEN `T0 + InitialDelay` (2 minutes default) elapses
- THEN the first `RunOnceAsync` call MUST fire
- AND the Serilog log MUST record `RetentionRun` with `BatchLimit` + `RetentionDays`

#### Scenario: Subsequent runs follow CleanupIntervalHours with jitter

- GIVEN the first run completed at `T1 = T0 + 2min`
- WHEN the next cycle is scheduled
- THEN the next run MUST fire at `T1 + CleanupIntervalHours` + `[0, +30min]` jitter
- AND the jitter MUST be re-rolled each cycle (not a constant offset)

#### Scenario: Exception in RunOnceAsync does not crash host

- GIVEN `RunOnceAsync` throws `InvalidOperationException` (e.g., DB unavailable)
- WHEN the BackgroundService loop catches the exception
- THEN the host MUST remain running
- AND a Serilog `Error` log MUST be emitted with the exception detail
- AND the next cycle MUST still fire on schedule (the catch does NOT cancel the timer)

### Requirement: RunOnceAsync exposed for testability

The `AuditRetentionBackgroundService` MUST expose a `public async Task RunOnceAsync(CancellationToken ct)` method that performs a single retention cycle: compute `cutoff = _clock.UtcNow - RetentionDays`, call `_retention.PurgeOldAsync(cutoff, BatchLimit, ct)`, log the count. This mirrors the Wave 4 4d `AttachmentLifecycleService.RunOnceAsync` precedent so unit tests drive the loop deterministically without waiting for the timer.

#### Scenario: RunOnceAsync computes cutoff from injected IClock

- GIVEN `IClock.UtcNow` returns `2026-08-19T12:00:00Z` and `RetentionDays = 90`
- WHEN `RunOnceAsync(ct)` is called
- THEN the purge MUST use `cutoff = 2026-08-19T12:00:00Z - 90 days = 2026-05-21T12:00:00Z`
- AND rows with `occurred_at < 2026-05-21T12:00:00Z` MUST be deleted

#### Scenario: RunOnceAsync logs row count

- GIVEN `PurgeOldAsync` returns `7500`
- WHEN `RunOnceAsync(ct)` completes
- THEN the Serilog log MUST record `RetentionRun` with `DeletedCount = 7500` at `Information` level

#### Scenario: RunOnceAsync logs zero count at Debug

- GIVEN `PurgeOldAsync` returns `0`
- WHEN `RunOnceAsync(ct)` completes
- THEN the Serilog log MUST record `RetentionRun` with `DeletedCount = 0` at `Debug` level (not `Information` — the daily "no-op" run is operational noise)

### Requirement: DI wiring for retention

The `IdentityModuleRegistration` MUST register `IAuditRetentionService → AuditRetentionService` (Scoped or Singleton — Singleton matches the existing `IAuditLogger` lifetime; Scoped is acceptable too). The BackgroundService MUST be registered via `services.AddHostedService<AuditRetentionBackgroundService>()`. The `IOptions<AuditRetentionOptions>` MUST be resolved via `IOptionsMonitor<AuditRetentionOptions>` in the BackgroundService so config changes (e.g., ops adjusts `RetentionDays` via `appsettings.json` + rolling restart) propagate without host restart.

#### Scenario: BackgroundService is registered

- GIVEN `IdentityModuleRegistration` runs
- WHEN the DI container is built
- THEN `AuditRetentionBackgroundService` MUST be in the list of `IHostedService` instances
- AND `IAuditRetentionService` MUST be resolvable

#### Scenario: IOptionsMonitor picks up config changes

- GIVEN the BackgroundService reads `RetentionDays` via `IOptionsMonitor<AuditRetentionOptions>.CurrentValue`
- WHEN `appsettings.json` is updated and the host reads the change (e.g., on file-watch in dev)
- THEN the next `RunOnceAsync` MUST use the new `RetentionDays` value
- AND the running BackgroundService MUST NOT need a restart

### Requirement: Retention backlog drain is daily trickle

The default `BatchLimit = 10000` × `CleanupIntervalHours = 24` deletes ~10000 rows/day. For a 3M-row backlog (3 years at Wave 8 baseline growth rate), drain takes ~300 days. Wave 9 does NOT include a one-shot backlog drain script — operators with > 1M backlog rows at first deploy should either (a) increase `BatchLimit` in `appsettings.json` for the first deploy then revert, OR (b) flag for a Wave 10 backlog drain task.

#### Scenario: Daily trickle rate matches BatchLimit × 24h

- GIVEN `BatchLimit = 10000`, `CleanupIntervalHours = 24`
- WHEN the BackgroundService runs each day
- THEN the daily delete rate MUST be capped at ~10000 rows/day (the BackgroundService loops within a single cycle to drain all rows older than `cutoff` up to the cap, then exits the cycle)

#### Scenario: No one-shot script is required

- GIVEN the host runs the BackgroundService continuously
- WHEN the backlog exceeds `BatchLimit`
- THEN the system MUST drain at the daily trickle rate (no operator intervention required for tables under ~1M rows)
- AND the spec MUST NOT mandate a one-shot script — flagged for Wave 10 if needed

### Requirement: Retention across partitions

Retention MUST purge expired monthly/DEFAULT events while preserving newer events and parent writability.

#### Scenario: Partition-aware retention
- GIVEN expired/retained events across historical, current, and DEFAULT partitions
- WHEN one retention cycle completes
- THEN expired events MUST be removed and retained events MUST remain readable
