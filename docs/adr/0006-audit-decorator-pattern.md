# 6. Audit decorator pattern

## Status
Accepted (2026-08-19, Wave 10)

## Context
Waves 6-9 shipped 19 entity types with audit decorators. The pattern must be documented.

## Decision
- Each user-owned repository has an `IXxxRepositoryAuditDecorator : IXxxRepository`
- `services.Decorate<IXxxRepository, XxxAuditDecorator>()` wires it (Scrutor)
- Audit decorator emits to `audit.events` via `IAuditLogger`:
  - `Created` on Add
  - `Updated` on Update (with diff JSON via ChangeTracker.OriginalValues)
  - `Deleted` on Delete / soft-delete via IsActive=false (per spec)
  - `Denied` on cross-tenant (with `UnauthorizedAccessException`)
  - `Failed` on contractually-invalid mutations (e.g., defensive DeleteAsync stub)
- Audit log excluded from any DELETE (append-only, defense-in-depth via AuditDbContext)
- Cross-tenant `IsOwner` check on `entity.UserId`
- Pre-mutation snapshot captured for batch soft-delete (Wave 9 9a.3 pattern)

## Consequences
- Pros: consistent audit shape across all 19 entities; the `audit.events` query surface is predictable
- Cons: bespoke decorators (vs generic `DecoratedRepository<T>`) per aggregate; some aggregates need DbContext access for cross-aggregate reads
- Test coverage: every decorator has integration tests via SQLite in-memory
