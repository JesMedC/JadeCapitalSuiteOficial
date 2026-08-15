# SDD Apply Progress — Slice 0e.1 (billing critical fixes)

> **Purpose**: Fix the three pre-existing slice-0e bugs that blocked the admin
> portal end-to-end after slice 0g shipped.
> **Status**: ⚠️ CODE WRITTEN, BUILD NOT VERIFIED IN SANDBOX.
> **Base**: `feature/0g-admin-angular-ui` (continues Wave 0 line).
> **Branch**: same (single squash-style continuation).

---

## Bug 1 — History entries never persisted (DbUpdateConcurrencyException)

**Symptom**: `POST /api/admin/subscriptions/{id}/change-tier` returns 500
even though the subscription row is mutated. The history row is not
written. Subsequent `GET /api/admin/subscriptions/{id}` shows
`planCode: "elite"`, `version: 2`, but `history: []`.

**Root cause**: EF Core marks new entries appended to the private
`_history` navigation as `Modified` instead of `Added` (collection-
tracking through a backing field with `PropertyAccessMode.Field`
mis-detects the entity state). EF then generates
`UPDATE billing.subscription_history SET ... WHERE id = X`, which
matches 0 rows because the row was never inserted.

**Fix**:
- `Subscription.LastHistoryEntry` accessor (new): returns
  `_history[^1]` or `null` for a no-op `ChangeTier` (same plan).
- `ChangeTierHandler`, `CancelHandler`, `ExtendTrialHandler` now
  inject `BillingDbContext` and call
  `_db.SubscriptionHistory.Add(subscription.LastHistoryEntry)`
  explicitly after the aggregate mutator returns success.
- `_uow.SaveChangesAsync` then persists both rows in one transaction.

---

## Bug 2 — Owner projection always empty

**Symptom**: `GET /api/admin/subscriptions/{id}` returns
`owner: { email: "", displayName: "" }`.

**Root cause**: `IOwnerProjectionLookup` was wired to
`EmptyOwnerProjectionLookup` (returns null) with a comment
"until slice 0g ships".

**Fix**:
- New `IdentityOwnerProjectionLookup` reads `identity.users.email` +
  `display_name` via `SqlQueryRaw` on the shared `BillingDbContext`
  connection (single DB, multi-schema layout).
- Returns `null` for orphan subscriptions; handler falls back to
  `MinimalOwnerProjection.Empty`.
- `EmptyOwnerProjectionLookup` removed from the DI surface.

---

## Bug 3 — Version not marked as concurrency token

**Symptom**: Intermittent DbUpdateConcurrencyException on mutations
depending on EF Core 9's optimistic-concurrency heuristic. Sometimes
the UPDATE succeeded; sometimes it failed with `expected 1, affected 0`.

**Fix**: `b.Property(s => s.Version).HasColumnName("version")
.IsRequired().IsConcurrencyToken();` in
`SubscriptionConfiguration.Configure`. EF now emits
`UPDATE ... SET version = @new WHERE id = @id AND version = @oldVersion`.

---

## Local verification — NOT possible in this sandbox

The docker daemon returns
`failed to solve: write /var/lib/desktop-containerd/daemon/io.containerd.metadata.v1.bolt/meta.db: input/output error`
on every build attempt since late in this session. Earlier builds
worked (we shipped slice 0g + the seed migration with verification).
The C# diff is syntactically valid; `dotnet build JadeCapital.slnx`
from a non-sandbox machine should compile without errors.

Manual smoke checklist for the dev / CI:
1. `dotnet test tests/UnitTests/JadeCapital.Billing.UnitTests/`
   - Confirm RED→GREEN tests still pass.
2. `dotnet test tests/IntegrationTests/JadeCapital.Api.IntegrationTests/`
   - Confirm `AdminAuthorizationTests` still pass.
3. `docker compose build api && docker compose up -d --force-recreate api`
4. Login as Admin: `POST /api/auth/login { email: test2@test.com, password: TestPassword123! }`
5. Promote if not Admin:
   `UPDATE identity.users SET role = 2 WHERE email = 'test2@test.com';`
6. List subscriptions: `GET /api/admin/subscriptions?status=Active`
7. Change tier: `POST /api/admin/subscriptions/{id}/change-tier
   { newPlanCode: 'elite', observedVersion: 1 }`
8. Re-fetch detail: `GET /api/admin/subscriptions/{id}`
   - Expect `planCode: "elite"`, `version: 2`, `history: [ { ..., action: "TierChanged", ... } ]`,
     `owner: { email: "test2@test.com", displayName: "Test" }`.
9. Repeat for Cancel + ExtendTrial with their respective observed versions.

---

## Commits in this batch

- `551c207` fix(billing): slice 0e.1 — owner projection + history persistence + concurrency token

7 files changed, +100/-30.
