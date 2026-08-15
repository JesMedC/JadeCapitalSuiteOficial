# SDD Apply Progress — Wave 1 partial (seeding + admin DTO fix)

> **Scope**: Make the admin portal functional end-to-end on top of slice 0g.
> **Status**: ⚠️ Partial — admin READ works, mutate actions have a slice-0e
> pre-existing bug blocking history persistence.
> **Branch**: `feature/0g-admin-angular-ui` (continued from slice 0g).

---

## Accomplished

### 1.1 — Seed billing.plans (migration 0008)
- `infrastructure/postgres/migrations/20260814_0008_SeedPlans.sql`
  - INSERT starter/pro/elite at 9.99 / 29.99 / 99.99 USD
  - `ON CONFLICT (code) DO NOTHING` (idempotent — Admin edits survive)
- `migrate.Dockerfile` updated to apply 0008 in both happy path and retry
- 3 plans now in `billing.plans` table after `docker compose up -d`

### 1.2 — Fix admin DTOs to match backend contracts
- `frontend/src/app/core/api/admin-api.service.ts` — replaced the
  fabricated `PlanInfo`/`SubscriptionSummary` types with the real
  contracts from `SubscriptionDtos.cs`:
  - `SubscriptionListItem` (subscriptionId, userId, planCode, status, updatedAt, version)
  - `SubscriptionDetail` (subscriptionId, userId, planCode, planName, status, trialEndsAt, createdAt, updatedAt, version, owner, history)
  - `SubscriptionHistoryItem` (id, action, priorPlanCode, resultingPlanCode, priorStatus, resultingStatus, actor, occurredAt, version, reason, priorTrialEndsAt, newTrialEndsAt)
- `admin-list.page.ts` updated to render the new shape.
- `admin-detail.page.ts` updated to render owner/plan/history correctly.
- `admin-api.service.spec.ts` updated to match the new DTOs.

---

## Verification

```
curl .../api/admin/subscriptions?status=Active     → 200 OK, 1 item
curl .../api/admin/subscriptions/{id}              → 200 OK, full detail
Traders correctly get 403 from RequireAdminPolicyHandler
```

---

## Risks / Deviations

- **CRITICAL (pre-existing, slice 0e)**: `Subscription._history` collection
  tracking is broken — EF generates `UPDATE billing.subscription_history`
  instead of `INSERT` for new entries. The result:
  - `ChangeTier` succeeds at the subscription UPDATE (plan ver, version
    bump, updated_at) — verified: subscription changed from pro to elite,
    version went 1 → 2.
  - But the history INSERT attempts as UPDATE → 0 rows affected →
    `DbUpdateConcurrencyException` → HTTP 500.
  - The 500 leaves the DB in a half-committed state: subscription field
    changed, but history stays empty (`SELECT count(*) FROM
    billing.subscription_history` = 0).
- **WARNING (pre-existing, slice 0e)**: `IOwnerProjectionLookup` returns
  null on every call. `GetSubscriptionDetailHandler` falls back to
  `MinimalOwnerProjection.Empty` (email='', displayName=''). Need to
  wire the actual lookup — likely a service registration miss in
  `BillingModuleRegistration.cs` or an unimplemented `Identity` join.
- **WARNING (pre-existing, slice 0e)**: `Subscription.Version` is not
  marked as `IsConcurrencyToken` in EF; relies on EF Core 9 default
  optimistic concurrency detection. Sometimes works, sometimes the
  missing token causes the dual-mapping bug to manifest differently.
- **DEFERRED**: Stripe integration (`Stripe.net 47.0.0` declared but
  unused). Not in scope of this slice.
- **DEFERRED**: `/api/billing/plans` public endpoint to replace the
  hardcoded `PLANS` in `frontend/.../pricing/pricing-page.ts` and
  `landing-page.ts`. Plan: do it after the slice 0e mutation bugs
  are fixed (so the price source is the same code path).

---

## Recommended next steps

1. **Fix the slice 0e history persistence bug** (CRITICAL): the cleanest
   fix is to give EF a separate `ISubscriptionHistoryEntryRepository.Add(entry)`
   that calls `_db.SubscriptionHistory.Add(entry)` explicitly, bypassing
   the navigation-tracked collection. Then `ChangeTier` returns the
   entry, the handler persists it directly, and the navigation is
   read-only.
2. **Fix the owner projection lookup** (WARNING): investigate
   `BillingModuleRegistration.cs` and ensure `IOwnerProjectionLookup`
   is registered with the implementation that actually joins
   `identity.users` by `userId`.
3. **When both bugs are fixed**, re-test the admin list/detail/change-tier
   loop end-to-end via the UI.
4. **Then**: Wave 1.3 — public `/api/billing/plans` endpoint to feed
   the frontend pricing pages.

---

## Commits in this batch

- `243a044` fix(frontend): align admin DTOs with backend contracts
- `f996f10` chore(infra): seed billing.plans with starter/pro/elite
