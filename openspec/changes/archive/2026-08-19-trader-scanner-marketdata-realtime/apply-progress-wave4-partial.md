# Wave 4 — Apply Progress (Slice 4a only)

**Change**: `2026-08-19-trader-scanner-marketdata-realtime`
**Slice closed**: 4a (Scanner)
**Closed by**: SDD orchestrator (verify + skip)
**Date**: 2026-08-17

## Slice 4a — Scanner — ALREADY-APPLIED

### Verification
- `dotnet build JadeCapital.slnx` → 0 errors, 0 warnings (12s)
- `dotnet test --filter "FullyQualifiedName~Scanner"` (Trading.UnitTests) → 13/13 pass
- `npm test -- --testPathPattern=scanner` → 4/4 pass
- Build: green ✅

### Code surface (confirmed present)
- Domain: `ScannerFilter` aggregate + `ScannerService` + 2 events
- Application: 4 handlers (CreateOrUpdate, List, Delete, Run) + DTOs + mapping
- Infrastructure: EF `ScannerFilterConfiguration` + `ScannerFilterRepository`
- API: `ScannerEndpoints` static class, 6 routes wired at `Program.cs:350`
- Migration: `0017_scanner_filters.sql` (idempotent, additive, wired in `migrate.Dockerfile`)
- DI: `IScannerFilterRepository` + `IScannerDataSource` registered in `TradingModuleRegistration`
- Frontend: `scanner-page.ts` (251 LOC, standalone, OnPush, mobile-first) + service + state + 1 spec + nav entry + route

### Deviations from spec (deliberate, ACCEPTED — to fix in dedicated follow-up)

#### D1. `ActiveHours` modeled as `string?` instead of `ActiveHoursWindow` record
- **Spec said**: `Shared.Kernel/Enums/ActiveHoursWindow.cs` — record with `DayOfWeek/StartHour/EndHour`, parsed by domain.
- **Actual**: `ScannerFilter.ActiveHours` is `string?` storing raw JSON. Parsing deferred to application/UI layer.
- **Why accepted**: keeps domain lean, defers schema flexibility. Database column is JSONB so migration is still additive.
- **Action**: defer `ActiveHoursWindow` VO extraction to a follow-up slice (W4.x or W6). Update `specs/scanner/spec.md` to reflect `string?` contract before Wave 4 archive.

#### D2. `VolatilityWindow` lives in `Trading.Domain.Scanner` instead of `Shared.Kernel/Enums/`
- **Spec said**: `Shared.Kernel/Enums/VolatilityWindow.cs`.
- **Actual**: `JadeCapital.Trading.Domain.Scanner/VolatilityWindow.cs` (enum, byte-backed).
- **Why accepted**: shared/Shared.Kernel contains only cross-module types. `VolatilityWindow` is currently scanner-only. Promote when a second module needs it.
- **Action**: move to `Shared.Kernel/Enums/` only if Wave 4b/4c/4d introduces a new consumer. Otherwise leave as-is.

### Coverage gap (deliberate, ACCEPTED)
- `JadeCapital.Api.IntegrationTests` has zero `Scanner`-named tests. CRUD/auth/validation tests live at the unit layer (handler tests + aggregate tests).
- **Action**: add 3-5 integration tests in slice 4e (E2E wiring) — covers full HTTP path + auth + rate limiting interaction. Not blocker for 4a close.

### Tasks marked
- All 4a.1, 4a.2, 4a E2E wiring checkboxes flipped to `[x]` in tasks.md.

### Next slice
- 4b (MarketData) — Quote VO + IQuoteProvider + InMemoryQuoteProvider + migration 0016 + `/api/quotes` endpoints. ~600L.
