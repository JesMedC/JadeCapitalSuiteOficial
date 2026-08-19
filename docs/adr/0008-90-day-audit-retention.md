# 8. 90-day audit retention

## Status
Accepted (2026-08-19, Wave 10)

## Decision
- `AuditRetentionBackgroundService` (Wave 9 9b.1) runs daily with `[0, +30min]` constant jitter
- Deletes rows where `occurred_at < UtcNow.AddDays(-RetentionDays)` (default 90)
- `BatchLimit` caps per-cycle deletes (default 10000)
- `IOptionsMonitor` for hot config reload
- Per-cycle scope via `IServiceScopeFactory` (avoids captive DbContext)

## Consequences
- Pros: predictable compliance horizon; hot-reload via IOptionsMonitor
- Cons: SQLite EF provider doesn't translate `ExecuteDelete` + `Take` — materialize-then-delete workaround documented in source
- Migration: `audit.events` partitioning by tenant_id or month deferred to Wave 11+
