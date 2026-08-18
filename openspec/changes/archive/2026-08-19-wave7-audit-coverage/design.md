# Design — Wave 7 (Audit Coverage Extension + Shared.Infrastructure Helper Refactor)

## Architecture Overview

Wave 7 widens the Wave 6 audit decorator pattern from **3 of 8** user-owned aggregates to **8 of 8**, and eliminates a cross-module layering tax by moving the generic `DecoratedRepository<T>` helper from `Identity.Infrastructure` to `Shared.Infrastructure`. The wave ships in 4 chained slices (7a.0, 7a.1, 7b.1, 7b.2) totaling ~1,800 LOC, 33 file paths, and 24 new BE tests.

The wave is driven by two orthogonal concerns:

1. **Compliance gap** (sub-scopes A + C) — the 5 user-owned aggregates `User`, `RiskProfile`, `Strategy`, `Trade`, `JournalEntry` have no `audit.events` rows on mutation. Wave 6 (slice 6d.2) shipped the decorator pattern (generic `DecoratedRepository<T>` + 3 typed decorators `TenantAuditDecorator` / `ImportJobAuditDecorator` / `SubscriptionAuditDecorator`); Wave 7 widens the pattern to the remaining 5 aggregates. The pattern is identical to Wave 6 — `services.Decorate<IXxxRepository, XxxAuditDecorator>()` registers the typed decorator, which forwards mutations to a `DecoratedRepository<T>` instance.

2. **Maintenance friction** (sub-scope B) — `Trading.Infrastructure.csproj` and `Billing.Infrastructure.csproj` both reference `Identity.Infrastructure.csproj` **solely** to import the generic `DecoratedRepository<T>` helper. After the move, both csprojs drop the redundant `<ProjectReference>` and add an explicit `<PackageReference Include="Scrutor" Version="4.2.2" />` (no longer transitive via the Identity reference). The cross-module edge is eliminated; `Trading` + `Billing` no longer depend on `Identity` for the helper.

The two new audit actions — `Denied` (cross-tenant access attempt) and `Failed` (`DeleteAsync` `NotSupportedException` path) — require widening the `audit.events.action SMALLINT` CHECK constraint from `IN (0,1,2,3)` to `IN (0,1,2,3,4,5)`. The widening lands in migration `0029_audit_events_action_denied_failed.sql` in slice 7a.1, atomically with the `AuditAction` enum extension. Without the migration, the new typed decorators cannot insert `Denied` / `Failed` rows (CHECK constraint rejection).

The wave ships one breaking change: `ITradeRepository.RemoveAsync(Trade, ct)` → `DeleteAsync(Trade, ct)` (matching `IRepository<T>.DeleteAsync(T, ct)`). All 5 known handler call sites in `Trading.Application/Features/Trades/{Create,Update,Close,Cancel,Remove}*Handler.cs` are updated atomically in slice 7b.1 — no `[Obsolete]` attribute, no overload, no deprecation period. The rename is the only behavioral break in the wave; everything else is additive.

## New Abstractions (Shared.Kernel)

### `AuditAction` enum extension (Shared.Kernel/Audit/AuditAction.cs)

```csharp
public enum AuditAction : byte
{
    Created = 0,    // existing (Wave 6, slice 6d.1)
    Updated = 1,    // existing
    Deleted = 2,    // existing
    Restored = 3,   // existing (reserved, not used in Wave 6)
    Denied = 4,     // NEW (Wave 7, slice 7a.1) — cross-tenant access attempt
    Failed = 5,     // NEW (Wave 7, slice 7a.1) — DeleteAsync NotSupportedException path
}
```

**Why no renumbering**: the byte values 0-3 are persisted in the `audit.events.action` column. Renumbering would break historical rows. The CHECK constraint `ck_audit_events_action` MUST be widened in lock-step with the enum extension (migration 0029).

**Why two new values, not one**: `Denied` and `Failed` are semantically distinct. `Denied` means "the actor was not authorized to perform this mutation" (cross-tenant isolation breach). `Failed` means "the mutation was contractually invalid" (calling `DeleteAsync` on a non-deletable aggregate). Compliance officers reading `audit.events` can filter by action to distinguish "someone tried to break in" (Denied) from "someone called a deprecated method" (Failed).

**Why `Failed` exists at all**: the typed decorator for `User` and `Strategy` must implement the full `IRepository<T>` interface (so the Scrutor pattern is consistent across all 5 typed decorators). `User` and `Strategy` have no `Delete` path (cancellation is a domain op, deactivation is `UpdateAsync`). The decorator surfaces a `Failed` audit row BEFORE re-throwing `NotSupportedException` so the attempt is recorded. The `Failed` value gives admins a filterable signal for "this handler called a method it shouldn't have".

## Modified Abstractions

### `IRepository<T>` (Shared.Kernel/Repository/IRepository.cs) — surface surgery

The `IRepository<T>` interface already exposes `AddAsync(T, ct)`, `UpdateAsync(T, ct)`, `DeleteAsync(T, ct)`, `GetByIdAsync(Guid, ct)`. Wave 7 confirms the signature. The 5 typed decorators instantiate `DecoratedRepository<T>` with `_inner` cast as `IRepository<T>`, so the interface must be the source of truth.

If `DeleteAsync(T, ct)` is missing from the current `IRepository<T>`, slice 7a.1 adds it (1-line addition; the existing `DecoratedRepository<T>` already expects it).

### `IUserRepository` (Identity.Infrastructure/Abstractions/Repositories/IUserRepository.cs) — surface surgery

```csharp
public interface IUserRepository : IRepository<User>
{
    // Existing methods (Wave 0): AddAsync, UpdateAsync, GetByIdAsync, FindByEmailAsync, ListByTenantAsync

    /// <summary>
    /// NOT SUPPORTED. User deletion is not a valid operation — cancel via the
    /// User.Cancel(reason, clock) domain op (which flips Status to Cancelled),
    /// or reassign to a different Tenant via User.AssignToTenant(tenantId, clock).
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// Always thrown. The decorator emits AuditAction.Failed before re-throwing.
    /// </exception>
    Task DeleteAsync(User user, CancellationToken ct = default);
}
```

The concrete `UserRepository.DeleteAsync(User, ct)` STUB throws `NotSupportedException` with the message `"User deletion happens via Tenant reassignment or Deactivation, not direct delete"`. The `UserAuditDecorator.DeleteAsync(User, ct)` short-circuits to `Failed` audit + re-throw (the underlying `IRepository<User>.DeleteAsync` is NEVER invoked from the decorator).

### `IRiskProfileRepository` (Identity.Infrastructure/Abstractions/Repositories/IRiskProfileRepository.cs) — surface surgery

```csharp
public interface IRiskProfileRepository : IRepository<RiskProfile>
{
    // Existing methods (Wave 4b): AddAsync, MarkSupersededAsync(Guid, IClock, ct), GetActiveAsync(userId, ct), ListByUserAsync(userId, ct)

    /// <summary>
    /// Decorator-friendly overload. Internally calls MarkSupersededAsync(profile.SupersededBy, clock, ct).
    /// Handlers should prefer MarkSupersededAsync directly for clarity.
    /// </summary>
    Task DeleteAsync(RiskProfile profile, CancellationToken ct = default);
}
```

The concrete `RiskProfileRepository.DeleteAsync(RiskProfile, ct)` is a real implementation that calls `MarkSupersededAsync(supersededBy: profile.SupersededBy ?? Guid.NewGuid(), clock, ct)`. The `RiskProfileAuditDecorator.DeleteAsync(RiskProfile, ct)` short-circuits to `Failed` audit + re-throw — the underlying impl is NOT the public path. The decorator surfaces a clean error so handlers learn to use `MarkSupersededAsync` directly.

### `IStrategyRepository` (Trading.Infrastructure/Abstractions/Repositories/IStrategyRepository.cs) — surface surgery

```csharp
public interface IStrategyRepository : IRepository<Strategy>
{
    // Existing methods (Wave 3): AddAsync, UpdateAsync, GetByIdAsync, ListByUserAsync, FindBySlugAsync

    /// <summary>
    /// NOT SUPPORTED. Strategy deletion is not a valid operation — use
    /// Strategy.Deactivate(clock) followed by UpdateAsync to flip IsActive to false.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// Always thrown. The decorator emits AuditAction.Failed before re-throwing.
    /// </exception>
    Task DeleteAsync(Strategy strategy, CancellationToken ct = default);
}
```

### `ITradeRepository` (Trading.Infrastructure/Abstractions/Repositories/ITradeRepository.cs) — BREAKING rename

```csharp
public interface ITradeRepository : IRepository<Trade>
{
    // Wave 7: RemoveAsync(Trade) renamed to DeleteAsync(Trade, ct) — BREAKING
    // No [Obsolete] attribute, no overload, no deprecation period.
    // The decorator (TradeAuditDecorator) emits AuditAction.Deleted on the new signature.

    Task AddAsync(Trade trade, CancellationToken ct = default);
    Task UpdateAsync(Trade trade, CancellationToken ct = default);

    /// <summary>Hard-deletes a trade. Valid only when Status ∈ {Open, Cancelled}.</summary>
    Task DeleteAsync(Trade trade, CancellationToken ct = default);

    // ... read-only methods unchanged ...
}
```

The 5 known handler call sites in `Trading.Application/Features/Trades/{Create,Update,Close,Cancel,Remove}*Handler.cs` are updated atomically in slice 7b.1. The `RemoveTradeHandler` (or its renamed equivalent) is the only handler that calls `DeleteAsync`; the other 4 handlers call `AddAsync` / `UpdateAsync` (unchanged signatures).

### `IJournalEntryRepository` (Trading.Infrastructure/Abstractions/Repositories/IJournalEntryRepository.cs) — additive overload

```csharp
public interface IJournalEntryRepository : IRepository<JournalEntry>
{
    // Existing methods (Wave 4a): AddAsync, UpdateAsync, DeleteAsync(Guid, ct) — production path

    /// <summary>
    /// Decorator-friendly overload. Internally calls DeleteAsync(entry.Id, ct).
    /// Production handlers can use either signature.
    /// </summary>
    Task DeleteAsync(JournalEntry entry, CancellationToken ct = default);

    // ... read-only methods unchanged ...
}
```

The `DeleteAsync(JournalEntry, ct)` overload is the path the `JournalEntryAuditDecorator` wraps. It costs 1 extra SELECT before the DELETE (the decorator loads the entity from the entity arg, but the underlying impl only needs the Guid). Production handlers that already have the `JournalEntry` in scope can call `DeleteAsync(entry, ct)` directly; production handlers that only have the Guid can call `DeleteAsync(guid, ct)` (the original signature, unchanged). Both paths emit `AuditAction.Deleted` via the decorator.

## Module Dependency Diagram (after 7a.0)

```
BEFORE Wave 7 (Wave 6 state):
  Trading.Infrastructure  ──→  Identity.Infrastructure  (for DecoratedRepository<T>)
  Billing.Infrastructure  ──→  Identity.Infrastructure  (for DecoratedRepository<T>)
  Identity.Infrastructure  ──→  Shared.Infrastructure
  Shared.Infrastructure    ──→  Shared.Kernel

AFTER Wave 7 (slice 7a.0):
  Trading.Infrastructure  ──→  Shared.Infrastructure  (for DecoratedRepository<T>)
  Billing.Infrastructure  ──→  Shared.Infrastructure  (for DecoratedRepository<T>)
  Identity.Infrastructure  ──→  Shared.Infrastructure
  Shared.Infrastructure    ──→  Shared.Kernel
```

The two edges `Trading → Identity` + `Billing → Identity` (which existed solely for the generic helper) are eliminated. `Identity` is no longer a transitive dependency of `Trading` or `Billing` for the decorator pattern.

**Why this is the right home for `DecoratedRepository<T>`**: the helper operates on `T` via reflection (`typeof(T).GetProperty("Id")`) and on `IRepository<T>` (from `Shared.Kernel`). It has no dependency on Identity types. The 2 unused `using JadeCapital.Identity.{Application,Domain}…` imports are removed on the move (the helper is genuinely cross-module). `Shared.Infrastructure` already references `Shared.Kernel` + EF Core + Npgsql + MediatR + FluentValidation + Serilog — the helper's full dependency set resolves there.

**Why Scrutor becomes explicit, not transitive**: `Identity.Infrastructure.csproj` references `Scrutor 4.2.2`. The `services.Decorate<IXxxRepository, XxxAuditDecorator>()` extension method comes from Scrutor. After 7a.0 drops the Identity ref from Trading + Billing csprojs, both modules must add Scrutor as an explicit `<PackageReference>` to keep the `Decorate` extension resolvable. Without this, the new typed decorators fail to compile in Trading (slice 7b.1 + 7b.2) and Billing (already had it; the 6d.2 SubscriptionAuditDecorator uses it).

## The `DecoratedRepository<T>` Class Structure (verbatim from Identity.Infrastructure, just moved)

The generic helper lives at `src/3.Shared/JadeCapital.Shared.Infrastructure/Persistence/DecoratedRepository.cs` with namespace `JadeCapital.Shared.Infrastructure.Persistence`. The file is byte-identical to the Wave 6 version except for:
- Namespace declaration (`JadeCapital.Shared.Infrastructure.Persistence`).
- 2 unused `using JadeCapital.Identity.{Application,Domain}…` imports removed.

The class shape is unchanged:

```csharp
public sealed class DecoratedRepository<T> : IRepository<T> where T : class
{
    private readonly IRepository<T> _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IDiff? _diff;
    private readonly DbContext? _db;     // for EF ChangeTracker.OriginalValues

    public DecoratedRepository(IRepository<T> inner, IAuditLogger audit,
        ITenantContext tenant, IClock clock, IDiff? diff = null, DbContext? db = null)
    { _inner = inner; _audit = audit; _tenant = tenant; _clock = clock; _diff = diff; _db = db; }

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await _inner.GetByIdAsync(id, ct);  // NO audit

    public async Task AddAsync(T entity, CancellationToken ct)
    {
        var result = await _inner.AddAsync(entity, ct);
        await _audit.LogAsync(new AuditEventEntry(
            EntityType: typeof(T).Name, EntityId: GetId(entity),
            Action: AuditAction.Created, TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId, ChangesJson: null,
            OccurredAt: _clock.UtcNow), ct);
        return result;
    }

    public async Task UpdateAsync(T entity, CancellationToken ct)
    {
        var action = IsTerminated(entity) ? AuditAction.Deleted : AuditAction.Updated;
        var before = await _inner.GetByIdAsync(GetId(entity), ct);  // snapshot
        var result = await _inner.UpdateAsync(entity, ct);
        var diff = SafeDiff(before, entity);
        await _audit.LogAsync(new AuditEventEntry(
            EntityType: typeof(T).Name, EntityId: GetId(entity),
            Action: action, TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId, ChangesJson: diff,
            OccurredAt: _clock.UtcNow), ct);
        return result;
    }

    public async Task DeleteAsync(T entity, CancellationToken ct)
    {
        var result = await _inner.DeleteAsync(entity, ct);
        await _audit.LogAsync(new AuditEventEntry(
            EntityType: typeof(T).Name, EntityId: GetId(entity),
            Action: AuditAction.Deleted, TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId, ChangesJson: null,
            OccurredAt: _clock.UtcNow), ct);
        return result;
    }

    private bool IsTerminated(T entity) { /* reflection: IsDeleted==true OR Status∈{Cancelled,Terminated,Expired} */ }
    private Guid GetId(T entity) { /* reflection on `Id` property */ }
    private string? SafeDiff(T? before, T after) { /* best-effort JSON diff with snapshot fallback */ }
}
```

The 5 typed decorators in Wave 7 follow the same pattern: instantiate `DecoratedRepository<T>` with `_inner` cast as `IRepository<T>`, forward mutations to `_decorated`, forward read-only methods to `_inner`. The cross-tenant `IsOwner` check + the `DeleteAsync` `Failed` audit are per-decorator concerns added on top of the generic shape.

## The 5 Typed Decorator Shapes (mirror Wave 6's `TenantAuditDecorator` / `ImportJobAuditDecorator` with deviations)

### `UserAuditDecorator` (Identity.Infrastructure/Audit/UserAuditDecorator.cs)

Mirrors `TenantAuditDecorator` (the canonical Wave 6 shape). Forwards `AddAsync` + `UpdateAsync` to `_decorated` (which emits `Created` / `Updated` with diff). The `DeleteAsync(User, ct)` is bespoke: it emits `AuditAction.Failed` with `changes = { "reason": { "before": null, "after": "User deletion is not supported — use Tenant reassignment or Deactivation" } }` and re-throws `NotSupportedException`. The underlying `IRepository<User>.DeleteAsync` is NEVER invoked. Cross-tenant `IsOwner` check: `user.Id == _tenant.CurrentUserId` (User doesn't have a `UserId` FK — the id IS the user; compare directly).

```csharp
public sealed class UserAuditDecorator : IUserRepository
{
    private readonly IUserRepository _inner;
    private readonly DecoratedRepository<User> _decorated;

    public UserAuditDecorator(IUserRepository inner, IAuditLogger audit, ITenantContext tenant, IClock clock, DbContext db)
    {
        _inner = inner;
        _decorated = new DecoratedRepository<User>(inner, audit, tenant, clock, db: db);
    }

    public async Task<User> AddAsync(User user, CancellationToken ct) =>
        await _decorated.AddAsync(user, ct);

    public async Task<User> UpdateAsync(User user, CancellationToken ct)
    {
        if (user.Id != _tenant.CurrentUserId && !_tenant.IsSuperAdmin)
        {
            await _audit.LogAsync(new AuditEventEntry(
                EntityType: nameof(User), EntityId: user.Id,
                Action: AuditAction.Denied, TenantId: _tenant.Current?.Value,
                UserId: _tenant.CurrentUserId,
                ChangesJson: "{\"reason\":{\"before\":null,\"after\":\"cross-tenant update attempt\"}}",
                OccurredAt: _clock.UtcNow), ct);
            throw new UnauthorizedAccessException(...);
        }
        return await _decorated.UpdateAsync(user, ct);
    }

    public async Task DeleteAsync(User user, CancellationToken ct)
    {
        await _audit.LogAsync(new AuditEventEntry(
            EntityType: nameof(User), EntityId: user.Id,
            Action: AuditAction.Failed, TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: "{\"reason\":{\"before\":null,\"after\":\"User deletion is not supported — use Tenant reassignment or Deactivation\"}}",
            OccurredAt: _clock.UtcNow), ct);
        throw new NotSupportedException("User deletion happens via Tenant reassignment or Deactivation, not direct delete");
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) => _inner.GetByIdAsync(id, ct);
    public Task<User?> FindByEmailAsync(string email, CancellationToken ct) => _inner.FindByEmailAsync(email, ct);
    public Task<IReadOnlyList<User>> ListByTenantAsync(TenantId tenantId, CancellationToken ct) => _inner.ListByTenantAsync(tenantId, ct);
}
```

### `RiskProfileAuditDecorator` (Identity.Infrastructure/Audit/RiskProfileAuditDecorator.cs)

**Bespoke**: wraps `MarkSupersededAsync` directly (NOT `UpdateAsync`, because RiskProfile has no `UpdateAsync` in its canonical mutation surface — the supersede IS the termination). The `MarkSupersededAsync` decorator wraps the inner call + emits `AuditAction.Deleted` with the supersession diff `{ "SupersededBy": { "before": null, "after": "<guid>" }, "SupersededAtUtc": { "before": null, "after": "<utcNow>" } }`. The `DeleteAsync(RiskProfile, ct)` short-circuits to `Failed` + re-throw (the underlying impl is NOT the public path). Cross-tenant `IsOwner` check on `profile.UserId`.

```csharp
public sealed class RiskProfileAuditDecorator : IRiskProfileRepository
{
    private readonly IRiskProfileRepository _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public async Task<RiskProfile> AddAsync(RiskProfile profile, CancellationToken ct)
    {
        var result = await _inner.AddAsync(profile, ct);
        await _audit.LogAsync(new AuditEventEntry(
            EntityType: nameof(RiskProfile), EntityId: profile.Id,
            Action: AuditAction.Created, TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId, ChangesJson: null,
            OccurredAt: _clock.UtcNow), ct);
        return result;
    }

    public async Task<RiskProfile> MarkSupersededAsync(Guid supersededBy, IClock clock, CancellationToken ct)
    {
        var profile = await _inner.GetActiveAsync(/* need to resolve */, ct);
        if (profile is null) throw new InvalidOperationException("no active profile to supersede");
        if (profile.UserId != _tenant.CurrentUserId && !_tenant.IsSuperAdmin)
        {
            await _audit.LogAsync(new AuditEventEntry(
                EntityType: nameof(RiskProfile), EntityId: profile.Id,
                Action: AuditAction.Denied, TenantId: _tenant.Current?.Value,
                UserId: _tenant.CurrentUserId,
                ChangesJson: "{\"reason\":{\"before\":null,\"after\":\"cross-tenant supersede attempt\"}}",
                OccurredAt: _clock.UtcNow), ct);
            throw new UnauthorizedAccessException(...);
        }

        var result = await _inner.MarkSupersededAsync(supersededBy, clock, ct);
        var changes = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["SupersededBy"] = new { before = (object?)null, after = supersededBy },
            ["SupersededAtUtc"] = new { before = (object?)null, after = clock.UtcNow }
        });
        await _audit.LogAsync(new AuditEventEntry(
            EntityType: nameof(RiskProfile), EntityId: profile.Id,
            Action: AuditAction.Deleted, TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId, ChangesJson: changes,
            OccurredAt: _clock.UtcNow), ct);
        return result;
    }

    public async Task DeleteAsync(RiskProfile profile, CancellationToken ct)
    {
        await _audit.LogAsync(new AuditEventEntry(
            EntityType: nameof(RiskProfile), EntityId: profile.Id,
            Action: AuditAction.Failed, TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: "{\"reason\":{\"before\":null,\"after\":\"RiskProfile supersede is canonical — use MarkSupersededAsync\"}}",
            OccurredAt: _clock.UtcNow), ct);
        throw new NotSupportedException("RiskProfile supersede is canonical — use MarkSupersededAsync");
    }

    public Task<RiskProfile?> GetActiveAsync(Guid userId, CancellationToken ct) => _inner.GetActiveAsync(userId, ct);
    public Task<IReadOnlyList<RiskProfile>> ListByUserAsync(Guid userId, CancellationToken ct) => _inner.ListByUserAsync(userId, ct);
}
```

### `StrategyAuditDecorator` (Trading.Infrastructure/Audit/StrategyAuditDecorator.cs)

Mirrors `ImportJobAuditDecorator` (cross-tenant `IsOwner` check on `strategy.UserId`). The `UpdateAsync` path is the canonical "soft-delete-by-flag" path: when `Deactivate()` flips `IsActive` from `true` to `false`, the existing `DecoratedRepository<T>.IsTerminated` reflection check does NOT upgrade `Updated → Deleted` (the check only fires on `IsDeleted == true` or `Status ∈ {Cancelled, Terminated, Expired}`). The result is an `Updated` event with `changes = { "IsActive": { "before": true, "after": false } }` — semantically correct audit trail for "soft-delete-via-flag". The `DeleteAsync(Strategy, ct)` short-circuits to `Failed` + re-throw.

### `TradeAuditDecorator` (Trading.Infrastructure/Audit/TradeAuditDecorator.cs)

Mirrors `ImportJobAuditDecorator` (cross-tenant `IsOwner` check on `trade.UserId`). The `UpdateAsync` path: when `Status` changes to `Cancelled` (or `Terminated` / `Expired`), the existing `IsTerminated` reflection check upgrades `Updated → Deleted`. The result is a `Deleted` event with the status diff. The `DeleteAsync(Trade, ct)` (renamed from `RemoveAsync`) emits `AuditAction.Deleted` with the before/after diff.

### `JournalEntryAuditDecorator` (Trading.Infrastructure/Audit/JournalEntryAuditDecorator.cs)

**Bespoke**: wraps the new `DeleteAsync(JournalEntry, ct)` overload (the decorator-friendly one). The overload internally calls `DeleteAsync(Guid, ct)` (the original production path). The decorator emits `AuditAction.Deleted` with `entity_id = entry.Id`. The cross-tenant `IsOwner` check is on `entry.UserId`. Read-only methods (`FindByIdAsync`, `ListByUserAsync`, `ListByDateRangeAsync`) forward to `_inner` without auditing.

## Test Architecture (SQLite-in-memory per-aggregate fixtures)

The 5 new integration test files mirror the Wave 6 `TenantRepositoryIntegrationTests` pattern (SQLite in-memory + `IdentityDbContext` + `AuditDbContext` + `IAuditLogger` + `ITenantContext` + `IClock`). The key gotcha — EF Core 9 SQLite `EnsureCreated` is all-or-nothing — is solved by the raw `CREATE TABLE IF NOT EXISTS events ...` SQL workaround (documented in 6d.2 fixture fix lines 102-128 of `TenantRepositoryIntegrationTests.cs`). The 5 new test files copy the pattern verbatim.

| Test file | Module | Helper DbContext | Notes |
|---|---|---|---|
| `UserRepositoryIntegrationTests` | Identity | `IdentityDbContext` (production) | Direct use; the production context handles `User` + `tenant_id` mappings cleanly. |
| `RiskProfileRepositoryIntegrationTests` | Identity | `IdentityDbContext` (production) | Direct use; same as User. |
| `StrategyRepositoryIntegrationTests` | Trading | `TestTradingDbContext` (focused) | Mirrors `ImportJobRepositoryIntegrationTests.TestTradingDbContext` — a subset of `TradingDbContext` with only `Strategy` + `JournalEntry` (omits Npgsql-specific Money complex types). |
| `TradeRepositoryIntegrationTests` | Trading | `TestTradingDbContext` (focused, extended) | Same as Strategy; adds `Trade` to the subset. |
| `JournalEntryRepositoryIntegrationTests` | Trading | `TestTradingDbContext` (focused) | Omits the Npgsql-specific `JournalEntry.Tags` array column (mirrors 6d.2 precedent for `ImportJob.Tags`). |

The test pattern per scenario is the same as Wave 6:
1. Create the aggregate via the typed repo (decorator is the production chain).
2. Query `audit.events` for the expected `entity_type` + `entity_id`.
3. Assert the audit row exists with the expected `Action` + `ChangesJson` + `TenantId` + `UserId` + `OccurredAt`.
4. For cross-tenant scenarios: use a different `ITenantContext` to simulate tenant T2; assert the row has `Action = Denied` and the decorator threw `UnauthorizedAccessException`.
5. For `DeleteAsync` STUB scenarios: assert the row has `Action = Failed` and the decorator re-threw `NotSupportedException`.

## AuditAction Enum Expansion + Migration

The `AuditAction` enum extension is in `src/3.Shared/JadeCapital.Shared.Kernel/Audit/AuditAction.cs`. The migration 0029 widens the CHECK constraint. Both land in slice 7a.1, atomically — without the migration, the new typed decorators cannot insert `Denied` / `Failed` rows (CHECK constraint rejection at the DB level).

```sql
-- 0029_audit_events_action_denied_failed.sql (slice 7a.1, idempotent)
BEGIN;
ALTER TABLE audit.events DROP CONSTRAINT IF EXISTS ck_audit_events_action;
ALTER TABLE audit.events
    ADD CONSTRAINT ck_audit_events_action CHECK (action IN (0, 1, 2, 3, 4, 5));
COMMENT ON CONSTRAINT ck_audit_events_action ON audit.events IS
    'AuditAction enum range (Wave 6: 0=Created, 1=Updated, 2=Deleted, 3=Restored; Wave 7: +4=Denied, +5=Failed). Idempotent re-run safe.';
COMMIT;
```

**Why the CHECK constraint is a defense-in-depth check**: the `AuditAction` enum is enforced at the application level (EF's `HasConversion<byte>()`); the CHECK constraint is a safety net against future contributors adding a 7th value without extending the constraint. The widening lands in the same slice as the enum extension — atomic, no risk of a partial state where the enum has `Failed=5` but the DB rejects it.

**Why no `DISTINCT` on `EntityType`**: the discriminator is `entity_type VARCHAR(80) NOT NULL`. Each of the 5 new aggregates has a distinct type name (`User`, `RiskProfile`, `Strategy`, `Trade`, `JournalEntry`). No collision with the 3 existing ones (`Tenant`, `ImportJob`, `Subscription`). The discriminator is not indexed separately — `ix_audit_events_entity (entity_type, entity_id)` already supports the "all events for this entity" query pattern.

## DI Wiring

### `IdentityModuleRegistration` (7a.1 additions)

```csharp
// Wave 7 slice 7a.1
services.Decorate<IUserRepository, UserAuditDecorator>();
services.Decorate<IRiskProfileRepository, RiskProfileAuditDecorator>();
```

### `TradingModuleRegistration` (7a.0 + 7b.1 + 7b.2 additions)

```csharp
// Wave 7 slice 7a.0
// Drop: <ProjectReference Include="..\..\Identity\JadeCapital.Identity.Infrastructure\..." />
// Add:  <PackageReference Include="Scrutor" Version="4.2.2" />

// Wave 7 slice 7b.1
services.Decorate<IStrategyRepository, StrategyAuditDecorator>();
services.Decorate<ITradeRepository, TradeAuditDecorator>();

// Wave 7 slice 7b.2
services.Decorate<IJournalEntryRepository, JournalEntryAuditDecorator>();
```

### `BillingModuleRegistration` (7a.0 csproj change, no new decorator)

```csharp
// Wave 7 slice 7a.0
// Drop: <ProjectReference Include="..\..\Identity\JadeCapital.Identity.Infrastructure\..." />
// Add:  <PackageReference Include="Scrutor" Version="4.2.2" />

// No new typed decorator — Billing already has SubscriptionAuditDecorator from Wave 6 6d.2.
```

## Sequence Diagrams

### UserAuditDecorator — Create + Update + Cross-tenant + Failed scenarios

```
Handler                IUserRepository                  UserAuditDecorator                  IAuditLogger (AuditLogger)              AuditDbContext
   |                        |                                    |                                       |                                |
   |--AddAsync(newUser)--->|                                    |                                       |                                |
   |                        |--AddAsync(newUser)---------------->|                                       |                                |
   |                        |                                    |--inner.AddAsync(newUser)---------->|                                |
   |                        |                                    |<--ok-------------------------------|                                |
   |                        |                                    |--audit.LogAsync(Created entry)---->|                                |
   |                        |                                    |                                       |--db.AuditEvents.AddAsync----->|                                |
   |                        |                                    |                                       |--db.SaveChangesAsync--------->|                                |
   |                        |<--result--|                      |                                       |                                |
   |<--result--|            |                                    |                                       |                                |
   |                        |                                    |                                       |                                |
   |--UpdateAsync(u1)------>|                                    |                                       |                                |
   |                        |--UpdateAsync(u1)----------------->|                                       |                                |
   |                        |                                    |--IsOwner check: u1.Id == T2User?  |                                |
   |                        |                                    |  NO (cross-tenant)                |                                |
   |                        |                                    |--audit.LogAsync(Denied entry)---->|                                |
   |                        |                                    |<--ok---|                          |                                |
   |                        |<--throws UnauthorizedAccessException---|       |                          |                                |
   |                        |                                    |                                       |                                |
   |--DeleteAsync(user)---->|                                    |                                       |                                |
   |                        |--DeleteAsync(user)---------------->|                                       |                                |
   |                        |                                    |--audit.LogAsync(Failed entry)---->|                                |
   |                        |                                    |<--ok---|                          |                                |
   |                        |<--throws NotSupportedException------|       |                          |                                |
```

### TradeAuditDecorator — Create + Close + DeleteAsync (renamed) scenarios

```
Handler                ITradeRepository                  TradeAuditDecorator                   IAuditLogger (AuditLogger)              AuditDbContext
   |                        |                                    |                                       |                                |
   |--AddAsync(newTrade)-->|                                    |                                       |                                |
   |                        |--AddAsync(newTrade)--------------->|                                       |                                |
   |                        |                                    |--inner.AddAsync(newTrade)---------->|                                |
   |                        |                                    |--audit.LogAsync(Created entry)---->|                                |
   |                        |<--result--|                      |                                       |                                |
   |                        |                                    |                                       |                                |
   |--UpdateAsync(t1, ct)->|                                    |                                       |                                |
   |  t1.Status = Closed    |                                    |                                       |                                |
   |                        |--UpdateAsync(t1)------------------>|                                       |                                |
   |                        |                                    |--IsOwner check: t1.UserId == T1User? OK|                                |
   |                        |                                    |--inner.UpdateAsync(t1)------------>|                                |
   |                        |                                    |--IsTerminated(t1)? NO (Status=Closed, not in {Cancelled,Terminated,Expired})|                                |
   |                        |                                    |--audit.LogAsync(Updated entry with status diff)---->|                                |
   |                        |<--result--|                      |                                       |                                |
   |                        |                                    |                                       |                                |
   |--DeleteAsync(t1, ct)-->|                                    |                                       |                                |
   |  t1.Status = Open       |                                    |                                       |                                |
   |                        |--DeleteAsync(t1)------------------>|                                       |                                |
   |                        |                                    |--IsOwner check: t1.UserId == T1User? OK|                                |
   |                        |                                    |--inner.DeleteAsync(t1)------------>|                                |
   |                        |                                    |--audit.LogAsync(Deleted entry with before/after diff)---->|                                |
   |                        |<--result--|                      |                                       |                                |
```

### RiskProfileAuditDecorator — MarkSuperseded (bespoke) scenario

```
Handler              IRiskProfileRepository        RiskProfileAuditDecorator            IAuditLogger (AuditLogger)             AuditDbContext
   |                        |                                |                                       |                                |
   |--MarkSupersededAsync(->|                                |                                       |                                |
   |  newSupersededByGuid,  |                                |                                       |                                |
   |  clock, ct)            |                                |                                       |                                |
   |                        |--MarkSupersededAsync(...)----->|                                       |                                |
   |                        |                                |--load profile (GetActiveAsync)------->|                                |
   |                        |                                |--IsOwner check: profile.UserId == T1User? OK|                                |
   |                        |                                |--inner.MarkSupersededAsync(...)---->|                                |
   |                        |                                |--audit.LogAsync(Deleted entry with supersession diff)---->|                                |
   |                        |                                |                                       |--changes = {SupersededBy: {before: null, after: <guid>}, SupersededAtUtc: {before: null, after: <utcNow>}}--->|
   |                        |<--result--|                  |                                       |                                |
```

## Cross-Module Concerns

### Migration sequencing (7a.1)

The `AuditAction` enum extension (Denied=4 + Failed=5) MUST land in the same PR as migration 0029 (CHECK constraint widening). Without the migration, the new typed decorators fail to insert `Denied` / `Failed` rows (CHECK constraint rejection). The migration is idempotent — re-running it is a no-op (DROP CONSTRAINT IF EXISTS + ADD CONSTRAINT).

### `ITradeRepository.RemoveAsync` → `DeleteAsync` (7b.1)

The rename is breaking. `git grep -n "RemoveAsync" src/2.Modules/Trading/` BEFORE the rename enumerates the 5 known handler call sites. All 5 are updated atomically in slice 7b.1. The compile is the proof of atomicity — any missed call site surfaces as a `CS1061: 'ITradeRepository' does not contain a definition for 'RemoveAsync'` error.

### Scrutor transitive ref drop (7a.0)

After slice 7a.0 drops the `Identity.Infrastructure` `<ProjectReference>` from `Trading.Infrastructure.csproj` and `Billing.Infrastructure.csproj`, both modules must add an explicit `<PackageReference Include="Scrutor" Version="4.2.2" />`. Without this, the `services.Decorate<IXxxRepository, XxxAuditDecorator>()` calls fail to compile (`The type or namespace name 'Decorate' could not be found`).

The `Directory.Build.props` could be updated to centralize the Scrutor version, but the proposal keeps it as a per-csproj reference (matches the Wave 5/6 precedent of explicit refs in each csproj). The `Identity.Infrastructure.csproj` already references Scrutor 4.2.2 (added in slice 6d.2).

### Testcontainers not available in sandbox

All 5 new integration tests use SQLite-in-memory per Wave 6 precedent. Zero Docker dependency. The focused `TestTradingDbContext` helper mirrors the 6d.2 precedent for sidestepping Npgsql-specific `Money` complex types + `Tags` array columns.

## Per-Slice Path Budget

| Slice | Files created | Files modified | Total paths | Bounded review ≤ 32 |
|---|---:|---:|---:|:---:|
| 7a.0 | 1 | 5 | 6 | OK |
| 7a.1 | 6 | 4 | 10 | OK |
| 7b.1 | 4 | 6 | 10 | OK |
| 7b.2 | 2 | 2 | 7 | OK |
| **Total** | **13** | **17** | **33** (with delete counted as 1 new path) | All ≤ 32 OK |

Each slice is well under 32 paths. The largest is 7a.1 + 7b.1 at 10 paths. Wave 5/6 precedent shows 32 paths is comfortable even for cross-cutting slices.

## Total Wave 7

~1,800 net LOC, 33 file paths, 4 PRs chained. Each ≤ 32 paths. `audit.events` coverage goes from 3 of 8 user-owned aggregates to 8 of 8. The cross-module edge `Trading → Identity` + `Billing → Identity` (for the generic helper) is eliminated.
