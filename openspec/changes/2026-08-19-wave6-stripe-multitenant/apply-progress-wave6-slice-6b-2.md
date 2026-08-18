# Apply Progress — Wave 6, Slice 6b.2

**Branch**: `feature/wave6-billing-portal-fe`
**Base**: `feature/wave6-billing-portal-api` (HEAD `44ba141` — Slice 6b.1 PR #14 OPEN, not yet merged)
**Date**: 2026-08-19
**PR title**: `feat(wave6-billing-portal-fe): slice 6b.2 — Billing Portal Angular Page`

## Summary

Slice 6b.2 closes the FE self-service billing portal gap. Traders can now
open `/app/billing` to see their current subscription, list of payment
methods, and invoice history — all read from the 3 GET endpoints added in
slice 6b.1 (= PR #14). A "Manage in Stripe" button calls the 6a.2 portal
session endpoint and redirects the browser to Stripe's hosted Customer
Portal so the user can update cards, cancel the subscription, and view
details Stripe tracks internally.

This slice is the FE-only counterpart of slice 6b.1. The BE accumulated
all the cross-user-isolation logic; the FE renders the resulting DTOs and
sends the user to Stripe for anything that needs stronger guarantees.

## Scope Delivered

| Layer | Files | Notes |
|---|---|---|
| Types | `frontend/src/app/features/trader/billing/api/billing-portal.types.ts` | 4 DTOs (`BillingPortalSubscriptionDto`, `BillingPortalPaymentMethodDto`, `BillingPortalInvoiceDto`, `StripePortalSessionDto`) + RFC 7807 problem shape. camelCase TS fields matching the actual ASP.NET Core wire shape (no global `JsonNamingPolicy` is configured — see `BillingPortalDtos.cs` doc comments for the snake_case documentation drift). |
| Service | `frontend/src/app/features/trader/billing/api/billing-portal.service.ts` | 4 HTTP wrappers: `getSubscription`, `getPaymentMethods`, `getInvoices`, `createPortalSession`. Auth header injected by the existing `auth.interceptor` — no manual token wiring. |
| State | `frontend/src/app/features/trader/billing/state/billing-portal.state.ts` | Signal store: `subscription`, `paymentMethods`, `invoices`, `isLoading`, `error`. `hasSubscription` + `canNavigateToPortal` computed signals. Parallel `loadAll()` using `Promise.all`. 404 on `/subscription` is treated as "no subscription yet" (empty state), NOT as an error banner — this is the user-facing UX when a brand-new user has not completed a checkout. |
| Page | `frontend/src/app/features/trader/billing/billing-portal-page.ts` | Standalone component, Signals + OnPush + SCSS. Header + plan card (status, next billing date, cancel flag) + "Manage in Stripe" button + payment methods list + invoices list. `navigatingToPortal` local signal keeps the redirect-in-flight state out of the global store. |
| Routes | `frontend/src/app/features/trader/billing/billing.routes.ts` | Lazy-loaded `BILLING_ROUTES` mounted at `/app/billing`. Auth inherited from the trader shell guard (no extra guard needed). |
| Trader routes | `frontend/src/app/features/trader/trader.routes.ts` | New `billing` route added. 13 routes total. |
| Trader shell | `frontend/src/app/features/trader/trader-shell.ts` | New `'Billing'` nav entry (12 items total) + credit-card SVG icon case. New `'card'` icon type added to `MobileNavItem`. |
| Mobile nav | `frontend/src/app/shared/mobile-nav.ts` | New `'card'` icon variant rendered at the same 20×20 px scale as the existing icons. Horizontal scroll keeps the bottom-nav usable on narrow viewports. |
| Tests | `frontend/src/app/features/trader/billing/__tests__/billing-portal-page.spec.ts` | 6 page-level jest specs covering title, helper methods, canNavigateToPortal, empty / error / loading states. |
| Tests | `frontend/src/app/features/trader/__tests__/trader-shell.spec.ts` | Updated to expect 12 items (added `'Billing'` entry). |

## TDD Discipline

### TDD Cycle Evidence

| Task | RED test | GREEN impl | REFACTOR |
|---|---|---|---|
| 1.1 Service HTTP methods | (covered transitively via the 6 page-level specs — the spec fakes the 4 methods before the real service exists) | `billing-portal.service.ts` | None — straight `firstValueFrom` wrappers; no refactor needed. |
| 1.2 DTO types | (covered transitively via the spec — fixtures `sampleSubscription`/`sampleMethod`/`sampleInvoice` are typed against the DTOs) | `billing-portal.types.ts` | None — pure types. |
| 1.3 Signal state | (covered transitively via the 6 page-level specs — state is the canonical loading/error store) | `billing-portal.state.ts` | Extracted `formatError` helper (mirrors `strategies.state.ts`). |
| 2.1 Page | The 6 specs in `__tests__/billing-portal-page.spec.ts` were written first; the initial run RED'd on the missing page. | `billing-portal-page.ts` | Single-pass implementation; the page has 2 `computed` signals (`planDisplay`, `statusLabel`) + a local `_navigating` signal for the redirect-in-flight state. |
| 2.2 Sub-routes | (covered via the trader-shell spec — adding the route was a wiring change) | `billing.routes.ts` | Minimal — single page, no children. |
| 2.3 Trader route | Page renders when navigating to `/app/billing` (verified by build + full FE suite). | `trader.routes.ts` | Comment added documenting the slice origin. |
| 2.4 Nav entry | `trader-shell.spec.ts` updated from 11 → 12 items; updated test fails on the 11-item assertion before the entry is added. | `trader-shell.ts` — new `'Billing'` entry + `'card'` icon case. | None. |
| 2.5 6 page-level specs | The 6 specs are the RED test for the whole page. Initial run: 1 RED (the helper-methods assertion that incorrectly expected `false` before the constructor's `loadAll()` resolved). | Spec asserted after `settle(fixture)` so the subscription is already loaded — the assertion was updated to expect `true` (mirrors the actual UX). | Re-organized the helper-methods spec to exercise `formatAmount` (USD, EUR, zero) + `formatDate` (valid, empty) + bound `canNavigateToPortal` to the loaded state. |
| Mobile nav `'card'` icon | (covered transitively via the trader-shell spec — the icon enum widens, types compile, build passes) | `mobile-nav.ts` — new icon variant. | None. |
| Trader shell spec update | Pre-existing `trader-shell.spec.ts` 11-item assertion → 12-item. RED before the spec update, GREEN after. | `trader-shell.spec.ts` | The "slice 4e" header comment was updated to reflect the 12-item scope and the Wave 6 append. |

**Test counts per file** (verified green):

- `billing-portal-page.spec.ts`: 6 scenarios (target 6 per `tasks.md` line 219 — exact match).
- `trader-shell.spec.ts`: 5 existing scenarios (still 5 — updated the count assertion only).

**Total new tests**: 6 (target 6 per `tasks.md` line 223 — exact match).

## Build + Test Status

- `npx jest --testPathPattern=billing-portal`: **6/6 pass** (focused filter).
- `npx jest` (full FE suite, 42 test suites): **185/185 pass** (was 179 before this slice → +6 new tests, 0 regressions).
- `npm run build`: **0 errors** (only pre-existing warnings in untouched files: `LoginPage`, `RegisterPage`, `LandingPage`, `mfe-mae-mini-chart` — none in this slice).
- `dotnet test --filter "FullyQualifiedName~BillingPortal"` (Billing.UnitTests): **25/25 pass** (slice 6b.1 still green).
- `dotnet test --filter "FullyQualifiedName~Stripe"` (Billing.UnitTests): **94/94 pass** (6a.1 + 6a.2 + 6b.1 BE tests all green — no regressions introduced by the FE slice).

## Diff Statistics

```
10 files changed
868 insertions(+)
    8 deletions(-)
860 net lines
10 paths total
```

vs. the forecast in `tasks.md`:

- Forecast: ~400 lines, 9 paths
- Actual: 860 lines, 10 paths

### Path count over by 1

The forecast was 9 paths (6 new + 3 modified). The actual is 10 because the
`trader-shell.spec.ts` had to be updated to reflect the new 12-item nav order
(it was pinned to 11 items in Wave 5). The 6a.2/6b.1 slices did not need
to update the spec because Wave 5's nav slice was already shipped; this
slice's `Billing` nav addition was the first nav change since Wave 5.

### Line count over by ~2x

The page (338 lines) and spec (255 lines) are both template-heavy — they
mirror the 6a.2/6b.1 page-level conventions. The page-by-section breakdown:

| File | Lines | Reason |
|---|---|---|
| `billing-portal.types.ts` | 80 | Type comments + 4 DTOs + RFC 7807 problem. |
| `billing-portal.service.ts` | 60 | 4 thin HTTP wrappers + endpoint doc comments. |
| `billing-portal.state.ts` | 83 | 5 signals + 2 computed + `loadAll()` + `formatError`. |
| `billing-portal-page.ts` | 338 | Header + 4 conditional blocks + plans card + 2 lists + ~120 lines of SCSS. |
| `billing.routes.ts` | 22 | Lazy route + doc comment. |
| `billing-portal-page.spec.ts` | 255 | 6 specs + 4 fixture factories + 2 helper functions. |
| Modified files | 38 net | `trader.routes.ts` (+3), `trader-shell.ts` (+7), `mobile-nav.ts` (+7), `trader-shell.spec.ts` (+20 / -8). |

The 868 line total is **within the 1000-LOC budget** — no `size:exception`
needed. The 400-line forecast was tight; the page + spec convention is
template-heavy in this project (matches 6a.2/6b.1).

## Deviations from Design

### 1. camelCase wire shape on the FE (NOT snake_case as the DTO docs imply)

The backend DTOs (`BillingPortalDtos.cs`, `StripePortalSessionDto.cs`,
`StripeSubscriptionDto.cs`) document themselves as "snake_case JSON", but
`src/1.Api/JadeCapital.Host/Program.cs` does NOT configure a global
`JsonNamingPolicy` — the actual HTTP response uses ASP.NET Core's
default camelCase policy. The snake_case doc comments are documentation
drift/wishful thinking; the contract tests in `BillingPortalDtosTests`
use a custom `JsonSerializerOptions { PropertyNamingPolicy = SnakeCaseLower }`
that does NOT reflect the actual minimal API response.

The FE follows the project's actual convention (camelCase): `planCode`,
`currentPeriodEnd`, `cancelAtPeriodEnd`, `amountCents`, `pdfUrl`,
`sessionId`, `expiresAt`. This matches the rest of the FE codebase
(`PlanInfo`, `StrategyDto`, `StripeCustomerDto`, `AdminApiService`).

The slice 6b.1 DTO contract tests cannot serve as a FE source-of-truth
for the wire shape — they assert against a custom serializer that does
not match the actual response. If the BE ever adds a global
`PropertyNamingPolicy = SnakeCaseLower`, the FE will break and the
contract tests will still pass — a learning for Wave 7.

### 2. 404 on `/subscription` is empty state, not error state

The `BillingPortalState.loadAll()` method treats a 404 on
`GET /api/billing/portal/subscription` as "no subscription yet" (a
brand-new user has not completed a Stripe Checkout). This is the
user-facing UX the spec describes ("Aún no tienes una suscripción
activa" + link to `/pricing`). A 404 on the other endpoints would
still be treated as an error banner.

The behavior is documented in the state file's doc comment:
> 404 on `/subscription` must NOT collapse the whole page into an error
> banner — it is the "no subscription yet" UX (the user has not
> completed a checkout). We treat it as an empty state by leaving
> subscription() at null and setting a friendly error.

This is the right UX: a 404 on the user's own subscription is the
normal "you're not subscribed yet" path, not a server error.

### 3. Local `_navigating` signal instead of state-level flag

The page uses a local `signal(false)` for the "Manage in Stripe" button
in-flight state, NOT a flag on the `BillingPortalState`. Rationale:

- The redirect is a one-shot, ephemeral event — it should not leak into
  the global store (a stale `navigatingToPortal=true` after the user
  comes back from Stripe would re-disable the button).
- The `navigatingToPortal` helper on the state is the SEMANTIC check
  (do we have a subscription?); the local signal is the IN-FLIGHT
  check (is the redirect currently running?). Two different concerns.

### 4. `canNavigateToPortal` is a method, not just a computed signal

The state exposes `canNavigateToPortal` as a computed signal, AND the
page exposes a `canNavigateToPortal()` method that delegates to the
state. Why both?

- The state signal is the canonical source of truth.
- The method is the "test seam" the spec asserts against (per the spec
  name `canNavigateToPortal returns true when subscription exists`).
- The method also lets the button's `[disabled]` and `onManageInStripe()`
  share a single `if (!this.canNavigateToPortal()) return;` guard.

Both delegate to the same state, so there is no behavioral drift.

### 5. `formatDate` helper exposed as a test seam

The page exposes a `formatDate(iso: string): string` helper that is
called by the spec to verify the date-formatting contract. The
template uses Angular's `date` pipe (`{{ subscription()!.currentPeriodEnd | date:'longDate' }}`)
so the helper is technically unused by the template. The method remains
because the spec pins it as a "helper method" deliverable — the spec's
naming explicitly asks for `formatAmount` + `formatDate` + `canNavigateToPortal`
exposure.

### 6. `'card'` icon type extended on `MobileNavItem`

The `MobileNavItem['icon']` union in `mobile-nav.ts` was extended from
`'dashboard' | 'trades' | 'calendar' | 'settings' | 'list' | 'tag' | 'home' | 'menu' | 'journal'`
to also include `'card'`. The credit-card SVG icon is rendered at the same
20×20 px scale as the existing icons. The trader-shell's `NavItem` type
was already `MobileNavItem['icon'] | 'journal'` — adding `'card'` to the
union widens the type, no further changes needed.

The credit-card SVG matches the Stripe-hosted portal UX (cards + sub
+ invoices) and is semantically clearer than the generic `'menu'` icon
used by other navigation entries without a purpose-built SVG.

### 7. Single error banner override (no separate inline error on the button)

When the "Manage in Stripe" POST fails (e.g. transient Stripe 502), the
component DOES NOT overwrite the page-level error banner — the global
state's `formatError` is only called by `loadAll()`. Rationale:

- The page-level error banner is about the initial data load. The
  navigation button's failure is a distinct, transient event.
- The `error.interceptor` already logs the network failure to the
  console — the user sees the button re-enable (after we set
  `_navigating(false)`) and can retry.
- A future Wave 7 slice can add a toast / inline error if this UX
  needs polish.

### 8. `navigatingToPortal` is a method (not a computed signal) on the page

The page exposes `navigatingToPortal()` as a method (calling the
underlying `_navigating.asReadonly()`), but the template uses it as a
function call (`[disabled]="navigatingToPortal()"`). This is Angular
19-friendly but reads like a function call. Reason: the spec asserts
the method exists and is callable from tests. The signal is the
underlying primitive; the method is the test seam.

## Critical Lessons Applied (from Wave 5 + 6a.1 + 6a.2 + 6b.1)

| Lesson | Where applied |
|---|---|
| Standalone components + Signals + OnPush | Page is `standalone: true`, `changeDetection: ChangeDetectionStrategy.OnPush`, uses `signal()` / `computed()` throughout. |
| No naked `subscribe()` in components | `takeUntilDestroyed()` is NOT used — the page uses signals + `firstValueFrom` (Promise-based HTTP). No subscriptions at all. |
| Match existing FE conventions | camelCase wire fields, Spanish UI copy (matches `pricing-page.ts`, `strategies-page.ts`, `admin-list.page.ts`), English nav labels (matches `trader-shell.ts`), `data-testid` for every testable block. |
| State + Service + Page trio | Same shape as `strategies.{service,state,page}` and `journal`/`alerts`/`planner` features. |
| Test seam helpers | `canNavigateToPortal()`, `formatAmount()`, `formatDate()` are exposed as public methods so the spec asserts against stable public surface, not private signals. |
| RFC 7807 problem shape | `BillingPortalProblem` mirrors the BE error envelope; `formatError` extracts `detail` / `title` / `status`. |
| No `any` | TypeScript strict mode respected — all signals typed, all DTOs typed, all methods return typed values. |
| `data-testid` everywhere | `billing-portal-page`, `billing-portal-error`, `billing-portal-loading`, `billing-portal-empty`, `billing-portal-plan-card`, `billing-portal-manage`, `billing-portal-methods`, `billing-portal-invoices`. |
| Defensive load state | `404` from `/subscription` is "no subscription yet" (empty state), not "error banner". See deviation #2. |
| Auth inherited from shell | No `authGuard` on `billing.routes.ts` — the trader shell is guarded by `authGuard` at `/app` (see `app.routes.ts`), so the billing portal inherits auth automatically. |
| `feature-branch-chain` chain strategy | PR targets `feature/wave6-billing-portal-api` (the previous PR's branch, PR #14 still OPEN). Once PR #14 merges, future 6c.x slices can target `feature/0a-identity-model` again. |

## What's NOT in Slice 6b.2

Per `tasks.md` 6b.2 scope, these arrive in subsequent slices:

- **6c.1-6c.3**: Multi-tenant (different scope). The portal DTOs will get
  a `tenantId` field in 6c.1 once `IStripeCustomer` carries tenant context.
  The FE state already has room to add it (just one more DTO field + one
  more signal).
- **6d.1-6d.2**: Soft-delete + audit (different scope). Portal reads are
  read-only; audit is not applicable. The page does not need to change.
- **Per-tenant isolation**: not a 6b.2 concern. The 6b.1 BE already enforces
  cross-user isolation (JWT → `StripeCustomer` → Stripe id). Once 6c.1
  adds tenant context, the FE will need a new nav entry pointing to the
  per-tenant view.

## Reviewer Notes

- **6 new tests** (target 6 per `tasks.md` line 223 — exact match).
- **Path count**: 10 (was forecast 9, +1 for the `trader-shell.spec.ts`
  11→12 item update). All paths in scope of the slice (no opportunistic
  refactors).
- **Lines**: 868 net insertions / 8 deletions (was forecast ~400, actual
  860 net). Within the 1000-LOC budget — no `size:exception` needed.
- **No `any`**: TypeScript strict mode honored. All DTOs typed, all
  signals typed, all methods return typed values.
- **Empty state is the primary 404 path**: brand-new users without a
  Stripe subscription see "Aún no tienes una suscripción activa" + a
  link to `/pricing`, NOT an error banner. The 404 semantics live in
  `BillingPortalState.loadAll()` with a comment-blocked rationale.
- **Local navigation signal**: the "Manage in Stripe" in-flight state is
  a local `signal(false)` on the page, not a flag on the global state.
  Keeping the redirect-in-flight ephemeral prevents a stale state
  ("navigating=true") after the user returns from Stripe.
- **404 on `/subscription` is intentionally treated as an empty state**:
  documented in the state file's doc comment + deviation #2. This is
  the right UX — the slice 6b.1 BE's 404 is "no Stripe customer mapping",
  which is the normal "you haven't paid yet" path, not a server error.
- **`'card'` icon on the MobileNavItem type**: extends the icon union
  with a credit-card SVG. The bottom-nav stays usable on narrow viewports
  via horizontal scroll (visible in the existing `mobile-nav.spec.ts`).
- **No backend changes**: this slice is FE-only. The PR does not touch
  any `.cs` file, no `.csproj`, no migration. The BE's 3 GET endpoints
  (slice 6b.1) + 1 POST endpoint (slice 6a.2) are consumed as-is.
- **Wire shape decision**: the FE uses camelCase (matches the actual
  ASP.NET Core minimal API response). The BE DTOs' snake_case doc
  comments are documentation drift — the actual response is camelCase
  because no global `JsonNamingPolicy` is configured. See deviation #1.

## Rollback

Revert code; the `/app/billing` route disappears. The 4 HTTP endpoints
delivered by 6b.1 + 6a.2 still exist but have no FE consumer. No
migration; no schema change. The `trader-shell.ts` nav entry removal
returns the sidebar to 11 items.

## Next Slice

**6c.1** — Tenant Aggregate + Migration (≤ 700 lines, 17 paths).
Multi-tenant context surfaces in the portal DTOs once `IStripeCustomer`
carries tenant context. The portal FE page will need a `tenantId` field
on the subscription DTO + a per-tenant view (likely a new page, not
extending this one).

Once **PR #14** (Slice 6b.1) merges, future 6c.x slices can target
`feature/0a-identity-model` again. Until then, 6c.1 should also target
`feature/wave6-billing-portal-api` (the merge base after 6b.1 lands).
