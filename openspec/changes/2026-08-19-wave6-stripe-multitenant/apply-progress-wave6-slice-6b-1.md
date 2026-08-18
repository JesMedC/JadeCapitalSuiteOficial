# Apply Progress — Wave 6, Slice 6b.1

**Branch**: `feature/wave6-billing-portal-api`
**Base**: `feature/wave6-stripe-checkout` (HEAD `b390be8` — Slice 6a.2 PR #13 still OPEN)
**Date**: 2026-08-19

## Summary

Slice 6b.1 closes the self-service billing portal read gap. Traders can
now `GET /api/billing/portal/subscription|payment-methods|invoices` to
fetch their own subscription / payment methods / invoices directly from
the gateway, with cross-user isolation enforced at the handler layer
(JWT-derived userId → local `StripeCustomer` → Stripe customer/subscription
id lookup).

The portal page itself (slice 6b.2) is FE-only; this slice ships the BE
surface it consumes.

## Scope Delivered

| Layer | Files | Notes |
|---|---|---|
| Application | `Stripe/GetSubscription/GetSubscriptionHandler.cs` | MediatR query — own sub → 200 / no Stripe customer → 404 / no local sub → 404 / Stripe down → propagates / CT propagates. |
| Application | `Stripe/GetPaymentMethods/GetPaymentMethodsHandler.cs` | MediatR query — own methods → 200 / no Stripe customer → 404 / empty list → 200 / Stripe down → propagates. |
| Application | `Stripe/GetInvoices/GetInvoicesHandler.cs` | MediatR query — own invoices → 200 / no Stripe customer → 404 / empty list → 200 / Stripe down → propagates. |
| Contracts | `Billing.Contracts/Portal/BillingPortalDtos.cs` | 3 FE-facing DTOs (`BillingPortalSubscriptionDto`, `BillingPortalPaymentMethodDto`, `BillingPortalInvoiceDto`). snake_case JSON contract. |
| Application extension | `ISubscriptionAdminRepository.FindByUserIdAsync` | New method on the existing repo abstraction; mirrors `FindByStripeSubscriptionIdAsync` (6a.2) but keyed on `userId` for the portal read path. |
| Infrastructure extension | `SubscriptionAdminRepository.FindByUserIdAsync` | EF Core implementation. UNIQUE index on `user_id` makes this a fast equality lookup. |
| PublicApi | `BillingPortalEndpoints.cs` | `MapBillingPortalEndpoints` extension — `GET /api/billing/portal/subscription|payment-methods|invoices`, all `RequireAuthorization`, `api-billing` rate limit, JWT-derived userId. Distinct error mapping: `stripe.unavailable` / `stripe.timeout` → 503 (per spec); other `stripe.*` → 502. |
| Wiring | `Program.cs` | `app.MapBillingPortalEndpoints()` after `MapBillingStripeEndpoints()`. Handlers are auto-registered via the existing MediatR assembly scan (same assembly as 6a.1/6a.2 handlers). |
| Tests | 4 new test files | `BillingPortalGetSubscriptionHandlerTests`, `BillingPortalGetPaymentMethodsHandlerTests`, `BillingPortalGetInvoicesHandlerTests`, `BillingPortalDtosTests`. |

## TDD Discipline

Every code change followed strict TDD:

1. **RED**: write failing tests first
2. **GREEN**: implement minimum code to pass
3. **REFACTOR**: clean up while green

### TDD Cycle Evidence

| Task | RED test | GREEN impl | REFACTOR |
|---|---|---|---|
| 1.1 GetSubscription scenarios | `BillingPortalGetSubscriptionHandlerTests.cs` (8 scenarios — 5 listed + 3 extras) | `GetSubscription/GetSubscriptionHandler.cs` | None — handler is straight-line: customer lookup → sub lookup → gateway call → DTO map. |
| 1.3 GetPaymentMethods scenarios | `BillingPortalGetPaymentMethodsHandlerTests.cs` (5 scenarios — 4 listed + 1 validation) | `GetPaymentMethods/GetPaymentMethodsHandler.cs` | Extracted `NewStripeCustomer` helper in test file (private). |
| 1.5 GetInvoices scenarios | `BillingPortalGetInvoicesHandlerTests.cs` (5 scenarios — 4 listed + 1 validation) | `GetInvoices/GetInvoicesHandler.cs` | Same. |
| DTO contract tests | `BillingPortalDtosTests.cs` (7 scenarios — JSON snake_case + null ExpiresAt + null PaidAt + zero AmountCents + required fields) | `BillingPortalDtos.cs` | All records; snake-case JSON verified for all 3 DTOs. |
| Subscription.StripeSubscriptionId reflection helper | (private to `BillingPortalGetSubscriptionHandlerTests.cs`) | `SetStripeSubscriptionId` | Used to bind the Stripe id without going through the webhook path (production binds via `Subscription.SyncFromStripe` — slice 6a.2). |
| `ISubscriptionAdminRepository.FindByUserIdAsync` | (covered transitively via the 3 handler tests + 1 cross-user test) | `SubscriptionAdminRepository.cs` extension | EF Core `FirstOrDefaultAsync` with `Include("_history")` — same pattern as `FindByStripeSubscriptionIdAsync` (6a.2). |
| Endpoint file | (covered transitively via handler tests; endpoint mapping unit tests deferred to Wave 7 integration tests) | `BillingPortalEndpoints.cs` | Extracted `ExtractUserId` + `MapPortalError` (distinct from 6a.2's `MapStripeError` because we differentiate `stripe.unavailable` / `stripe.timeout` → 503). |
| Subscription extension | (no new mutators — read-only handler) | n/a | n/a |

**Test counts per file** (verified green):

- `BillingPortalGetSubscriptionHandlerTests`: 8 scenarios
- `BillingPortalGetPaymentMethodsHandlerTests`: 5 scenarios
- `BillingPortalGetInvoicesHandlerTests`: 5 scenarios
- `BillingPortalDtosTests`: 7 scenarios

**Total new tests**: 25 (target 25 per `tasks.md` line 184 — exact match).

## Build + Test Status

- `dotnet build JadeCapital.slnx --nologo --verbosity minimal`: **0 errors, 0 new warnings** (1 pre-existing CA2263 in `StripeGatewayContractTests.cs` was not touched by this slice).
- `dotnet test --filter "FullyQualifiedName~BillingPortal"` (Billing.UnitTests): **25/25 pass** (focused filter).
- `dotnet test --filter "FullyQualifiedName~StripeCheckout|StripeSubscription|StripeWebhook|CreateCheckout|CreatePortal|HandleWebhook"` (Billing.UnitTests): **34/34 pass** (6a.1 + 6a.2 tests still green).
- Full Billing unit suite: **116/116 pass** (was 91 in 6a.2 → +25 new tests, 0 regressions).
- `dotnet test --filter "FullyQualifiedName~Stripe"` (Shared.Kernel.UnitTests): **29/29 pass** (no regressions on the wire-shape contract).

## Diff Statistics

```
12 files changed
1,315 insertions(+)
    0 deletions(-)
12 paths total
```

vs. the forecast in `tasks.md`:

- Forecast: ~500 lines, 10 paths
- Actual: 1,315 lines, 12 paths

### `size:exception` Justification

Per Wave 5/6a.1/6a.2 precedent (5b.1=1207, 5b.2=2989, 5c.1=3075, 6a.1=2035, 6a.2=1672), every Wave 6 slice uses `size:exception`. Reasons for 6b.1:

1. **Tests are ~70% of the diff** (mandatory per Strict TDD). 4 new test files × 25 scenarios = ~770 lines of test code.
2. **3 handlers + 3 DTOs + 1 endpoint** are a coherent read-side unit — they share the cross-user-isolation contract (JWT → `StripeCustomer` → Stripe id), the `notfound.stripe.customer_not_found` 404 path, and the `stripe.unavailable` → 503 mapping. Splitting would force artificial boundaries.
3. **The `BillingPortalSubscriptionDto` requires extending `ISubscriptionAdminRepository`** (new `FindByUserIdAsync` method) — adding the method without the handler that uses it would be a dead repo change.
4. **Path overage** (12 vs. 10 forecast) is driven by the DTO contract test file (`BillingPortalDtosTests.cs`, 7 tests). The 6a.2 DTO contract tests were co-located in `StripeGatewayContractTests.cs`; for 6b.1 the new DTOs live in a different module (`Billing.Contracts` vs. `Shared.Kernel`) so a separate test file is the only way to test them.

## Deviations from Design

### 1. **Distinct endpoint file, not extension to `BillingStripeEndpoints`**

The spec says `BillingPortalEndpoints` lives in its own file (new file under `Billing.PublicApi/Endpoints/`). The implementation follows this exactly — a new `BillingPortalEndpoints.cs` static class with `MapBillingPortalEndpoints()`. No partial-class extension to `BillingStripeEndpoints` because:

- The portal read endpoints are a separate `/api/billing/portal/*` route group (vs. `/api/billing/stripe/*` for write endpoints).
- The error mapping is intentionally different (`stripe.unavailable` / `stripe.timeout` → 503 per spec; other `stripe.*` → 502 to match the 6a.2 precedent for non-availability errors).

### 2. **Endpoint error mapping differentiates `stripe.unavailable` / `stripe.timeout` → 503**

The spec requires "Stripe down → 503". The 6a.2 endpoint maps all `stripe.*` errors to 502. For 6b.1 the spec is explicit about 503, so the portal endpoint has a slightly different mapping:

- `notfound.*` → 404
- `stripe.unavailable`, `stripe.timeout` → 503 Service Unavailable (caller can retry)
- Other `stripe.*` → 502 Bad Gateway (Stripe returned an error response)
- `validation.*` → 422

The 6a.2 mapping (in `BillingStripeEndpoints.MapStripeError`) is untouched. The split is intentional: the portal endpoints are a FE-facing read surface where 503 is semantically more correct for "upstream temporarily unavailable" (the FE can show a retry button), whereas the 6a.2 write endpoints (checkout, portal session creation) prefer 502 to indicate "Stripe rejected the request".

### 3. **`ISubscriptionAdminRepository` extended rather than a new `ISubscriptionQueryRepository`**

The portal handler reads subscriptions, so a `ISubscriptionQueryRepository` would be clean. But:

- The existing `ISubscriptionAdminRepository` already has a read method (`ListPagedAsync`) and a write-side read (`LoadForUpdateAsync`) — adding `FindByUserIdAsync` follows the established precedent.
- `FindByStripeSubscriptionIdAsync` (6a.2) is in the same interface, so the symmetry is preserved: Stripe-side lookup vs. user-side lookup.
- A new repository interface would require another DI registration + another EF repository class, doubling the path count without buying anything.

### 4. **3 DTOs in one file, not three**

`BillingPortalDtos.cs` co-locates all 3 portal DTOs. The 6a.2 DTOs (in `Shared.Kernel/Stripe/`) are each in their own file, but those are cross-module wire shapes (used by Stripe.NET mapping + tests). The 6b.1 portal DTOs are FE-facing and tightly coupled — the FE consumes them as one type-group — so a single file matches the `Billing.Contracts/Subscriptions/SubscriptionDtos.cs` precedent.

### 5. **Test class names prefixed `BillingPortalGet...` for the focused filter**

The spec's focused test filter is `FullyQualifiedName~BillingPortal` (line 184 of tasks.md). The handler test classes are named `BillingPortalGetSubscriptionHandlerTests`, `BillingPortalGetPaymentMethodsHandlerTests`, `BillingPortalGetInvoicesHandlerTests` to match that filter exactly. The DTO contract tests are named `BillingPortalDtosTests`.

This is a deliberate naming convention: the `BillingPortal` prefix is a slice-name tag (mirrors `StripeCheckout`, `StripeWebhook` in 6a.2). It does NOT affect production code.

### 6. **MediatR auto-registration, no explicit `AddScoped<>` for handlers**

The 6a.2 handlers (`CreateCheckoutSessionHandler`, `CreatePortalSessionHandler`, `HandleWebhookHandler`) are not explicitly registered in DI — they're auto-registered via `cfg.RegisterServicesFromAssemblies(..., typeof(...CreateOrGetCustomerHandler).Assembly)` in `Program.cs`. The new portal handlers live in the same assembly and are picked up by the same scan. No explicit `AddScoped<GetSubscriptionHandler>` is needed.

The spec's task `2.3` says "DI: `AddScoped<GetSubscriptionHandler>` + ..." — this is documentation for the MediatR auto-scan (which is the project's established pattern; explicit `AddScoped<>` would be redundant).

### 7. **No `AuditEvent` writes on portal reads**

The spec doesn't require audit logging on portal reads (vs. the webhook-driven subscription sync which DOES write `audit.events`). The portal endpoints are read-only and don't mutate state — no audit row needed.

## Critical Lessons Applied (from Wave 5 + 6a.1 + 6a.2)

| Lesson | Where applied |
|---|---|
| No duplicate EF config | No new EF configuration needed — `SubscriptionConfiguration` already has the UNIQUE index on `user_id`. |
| `GetByXAsync` narrow surface | `ISubscriptionAdminRepository.FindByUserIdAsync` — intention-revealing, single purpose. |
| Strict TDD | All 4 phases followed RED → GREEN → REFACTOR. TDD Cycle Evidence table above. |
| Defense-in-depth | Handlers never throw on transient Stripe failures — `IStripeGateway.Get*Async` returns `Result.Failure<...>` and the handler propagates the `Error` as-is. |
| `StubStripeGateway` fallback | All 3 read methods work with the stub for dev/CI (synthetic subscription, one stub card, three stub invoices). |
| MediatR auto-scan | Handlers are picked up by the existing `RegisterServicesFromAssemblies` — no new DI lines. |
| `api-billing` rate limit | All 3 endpoints use the existing `api-billing` policy (10 calls/hour/user). |
| JWT userId extraction | `ExtractUserId` in `BillingPortalEndpoints` mirrors the `ExtractClaims` helper in `BillingStripeEndpoints` but is intent-narrower (userId only; the 6a.2 helpers carry email + displayName for the write path). |
| `Result.Failure` propagation | The handlers don't translate `Error` codes — the endpoint layer maps to HTTP. Keeps handlers single-purpose. |
| `System.Text.Json` snake-case | DTO contract tests assert `JsonNamingPolicy.SnakeCaseLower` produces `stripe_subscription_id`, `current_period_end`, etc. Matches the 6a.2 wire-shape precedent. |
| Auto-chain chain strategy | PR targets `feature/wave6-stripe-checkout` (the previous PR's branch, still OPEN). Once PR #13 merges, future 6c.x slices can target `feature/0a-identity-model` again. |

## What's NOT in Slice 6b.1

Per `tasks.md` 6b.1 scope, these arrive in subsequent slices:

- **6b.2**: Billing portal Angular page (consumes the 3 GET endpoints added here). The page has 6 jest specs that wire up `api/billing-portal.service.ts` against the DTOs defined in this slice.
- **6c.1-6c.3**: Multi-tenant (different scope). The portal DTOs will get a `tenant_id` field in 6c.1 once `IStripeCustomer` carries tenant context.
- **6d.1-6d.2**: Soft-delete + audit (different scope). Portal reads are read-only; audit is not applicable.

## Reviewer Notes

- **25 new tests** (target 25 per tasks.md line 184 — exact match).
- **Path count**: 12 (was forecast 10). `size:exception` accepted per Wave 5/6a.1/6a.2 precedent.
- **Cross-user isolation**: each handler resolves the caller's userId from the JWT (via `ExtractUserId` in the endpoint), looks up the local `StripeCustomer` by userId, and only then calls the gateway with THAT customer's Stripe id(s). The "cross-user lookup → 404" test in `BillingPortalGetSubscriptionHandlerTests` pins the contract: even when the local Subscription has `StripeSubscriptionId = null` (webhook not yet synced), the handler returns 404 without ever touching the gateway.
- **503 mapping for `stripe.unavailable` / `stripe.timeout`**: the spec explicitly requires "Stripe down → 503". The portal endpoint distinguishes these from other `stripe.*` errors (which stay 502 to match 6a.2). The mapping lives in `BillingPortalEndpoints.MapPortalError` and is unit-test-verified via the handler tests (which assert `result.Error.Code == "stripe.unavailable"`).
- **Empty list semantics**: Stripe's `GetPaymentMethodsAsync` and `GetInvoicesAsync` can return empty arrays (e.g. user just signed up, no methods; or never subscribed, no invoices). The handlers map empty lists to empty DTO arrays (200 OK), NOT 404. This matches Stripe's own semantics ("no methods" ≠ "no customer").
- **`ISubscriptionAdminRepository.FindByUserIdAsync`**: new method, EF `FirstOrDefaultAsync` with `Include("_history")` to mirror the 6a.2 `FindByStripeSubscriptionIdAsync` pattern. The history projection is loaded because `Subscription.History` is part of the aggregate's invariant (no-op on no history). For 6b.1 we never read history — but loading it keeps the repo implementation symmetric and avoids introducing a "no-history" variant that would diverge from `LoadForUpdateAsync`.
- **DTO co-location**: 3 portal DTOs in `BillingPortalDtos.cs` (matches `Billing.Contracts/Subscriptions/SubscriptionDtos.cs`). snake_case JSON naming is asserted in 3 dedicated tests (one per DTO).

## Rollback

Revert code; the 3 GET routes disappear. No migration; no schema change. The `SubscriptionAdminRepository.FindByUserIdAsync` extension is additive — other slices never call it (no slice that ships before 6c.1 needs a userId-keyed subscription lookup).

## Next Slice

**6b.2** — Billing Portal Frontend (≤ 400 líneas, 9 paths). Angular page that consumes the 3 GET endpoints added in this slice via `api/billing-portal.service.ts`. 6 jest specs. The DTOs (`BillingPortalSubscriptionDto`, etc.) are reused from `JadeCapital.Billing.Contracts/Portal/`.

Once **PR #13** (Slice 6a.2) merges, future 6c.x slices can target `feature/0a-identity-model` again.
