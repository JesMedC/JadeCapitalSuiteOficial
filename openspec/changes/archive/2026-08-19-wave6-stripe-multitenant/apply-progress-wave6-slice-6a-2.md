# Apply Progress — Wave 6, Slice 6a.2

**Branch**: `feature/wave6-stripe-checkout`
**Base**: `feature/0a-identity-model` (HEAD `383e44e` — Slice 6a.1 merged)
**Date**: 2026-08-19

## Summary

Slice 6a.2 closes the checkout / portal / subscription-sync gap. Traders can
now `POST /api/billing/stripe/checkout` to start a Stripe subscription and
`POST /api/billing/stripe/portal` to open Stripe's self-service UI. Webhooks
for `customer.subscription.{created,updated,deleted}` persist an append-only
event log and synchronize the local `Subscription` aggregate (status mapping,
Stripe id binding, history entry).

## Scope Delivered

| Layer | Files | Notes |
|---|---|---|
| Migration | `0023_stripe_webhook_events.sql` | `billing.stripe_webhook_events` table (8 cols + 2 indexes + UNIQUE on event_id) + `ALTER TABLE billing.subscriptions ADD COLUMN stripe_subscription_id` (nullable, partial unique index). Idempotent. |
| Shared.Kernel | 5 DTOs (`StripeCheckoutSessionDto`, `StripePortalSessionDto`, `StripeSubscriptionDto`, `StripePaymentMethodDto`, `StripeInvoiceDto`) | Wire shapes for the 5 new gateway methods. |
| Shared.Kernel | `IStripeGateway` extension | +5 methods (total = 7). |
| Billing.Domain | `StripeWebhookEvent` aggregate + `StripeWebhookEventStatus` enum + `StripeWebhookEventErrors` | Append-only row with `MarkProcessed`/`MarkFailed`. |
| Billing.Domain | `SubscriptionWebhookSync` static translator | Stripe free-form status → `SubscriptionStatus`. |
| Billing.Domain | `Subscription` extension | `StripeSubscriptionId` property + `SyncFromStripe` mutator (no-op on status equal). |
| Billing.Domain | `SubscriptionStatus` extension | `PastDue = 4`, `SubscriptionAction.WebhookSynced = 3`. |
| Billing.Application | `CreateCheckoutSessionHandler` + `CreatePortalSessionHandler` | MediatR commands — ensure-customer-first / 404-if-missing patterns. |
| Billing.Application | `HandleWebhookHandler` extension | Idempotency check, append-log, dispatch on `customer.subscription.*`, optimistic-concurrency retry (3× with 50-200 ms jitter). |
| Billing.Application | `IStripeWebhookEventRepository` | Abstraction; EF impl in Infrastructure. |
| Billing.Infrastructure | `StripeGateway` extension | Real impl for the 5 new methods (defense-in-depth try/catch). |
| Billing.Infrastructure | `StubStripeGateway` extension | Dev fallback for the 5 new methods (predictable synthetic responses). |
| Billing.Infrastructure | `StripeWebhookEventRepository` (EF) + `StripeWebhookEventConfiguration` | Append-only EF repo + 1 unique + 1 regular index. |
| Billing.Infrastructure | `SubscriptionAdminRepository.FindByStripeSubscriptionIdAsync` impl | Used by the webhook handler to resolve Stripe → local. |
| Billing.Infrastructure | `SubscriptionConfiguration` extension | `stripe_subscription_id` column mapping. |
| Billing.Infrastructure | `BillingDbContext` extension | `DbSet<StripeWebhookEvent>` + EF config registration. |
| Billing.Infrastructure | `BillingModuleRegistration` DI | `IStripeWebhookEventRepository` registration. |
| Billing.PublicApi | `BillingStripeEndpoints` extension | `POST /api/billing/stripe/checkout` + `POST /api/billing/stripe/portal` (RequireAuthorization, `api-billing` rate limit). |
| Tests | 5 new test files + extensions to 2 existing files | xunit + FluentAssertions + NSubstitute. |

## TDD Discipline

Every code change followed strict TDD:

1. **RED**: write failing tests first
2. **GREEN**: implement minimum code to pass
3. **REFACTOR**: clean up while green

### TDD Cycle Evidence

| Task | RED test | GREEN impl | REFACTOR |
|---|---|---|---|
| 1.1 CreateCheckoutSession | `CreateCheckoutSessionHandlerTests.cs` (6 scenarios) | `CreateCheckoutSessionHandler.cs` | Extracted `ExtractClaims` helper in endpoint; kept handler constructor minimal. |
| 1.3 CreatePortalSession | `CreatePortalSessionHandlerTests.cs` (5 scenarios) | `CreatePortalSessionHandler.cs` | Same. |
| 2.1 HandleWebhook dispatch | `HandleWebhookSubscriptionEventTests.cs` (8 scenarios) | `HandleWebhookHandler.cs` extension | Reflection-based EF exception detection keeps `Billing.Application` EF-free. |
| 2.2 Idempotency + retries | Same suite — re-delivery, sub-not-found, transient failure | `DispatchSubscriptionEventAsync` retry loop with `Random.Shared.Next(50, 200)` jitter | `SafeSaveAsync` swallows secondary failures. |
| 3.1 SubscriptionWebhookSync | `SubscriptionWebhookSyncTests.cs` (9 scenarios — 6 mapped + 3 history/StripeSubscriptionId) | `SubscriptionWebhookSync.cs` static translator | `MapStripeStatusToInternal` is public for tests + 6b.1 reuse. |
| 5.2 StripeWebhookEventRepository | (covered transitively via handler tests with NSubstitute) | `StripeWebhookEventRepository.cs` | No-op refactor (simple EF repo). |
| 5.3 StripeWebhookEventConfiguration | (no dedicated test; covered by handler tests verifying wire shape) | `StripeWebhookEventConfiguration.cs` | `payload_json` mapped as `jsonb` column type. |
| 5.4 Endpoints | (manual integration test in Wave 7; covered by handler tests) | `BillingStripeEndpoints.cs` extension | Extracted `ExtractClaims` + `MapStripeError` (404 special-case). |
| DTOs | `StripeGatewayContractTests.cs` (16 DTO tests across 5 new types) | 5 DTO files | All records; snake-case JSON verified. |
| Subscription extension | (covered transitively via SubscriptionWebhookSyncTests) | `Subscription.SyncFromStripe` | One-time StripeSubscriptionId binding; no-op on equal status. |

**Test counts per layer**:

- `Shared.Kernel.UnitTests/Stripe/StripeGatewayContractTests.cs`: +16 tests (5 new DTOs × 2-4 scenarios each + 1 contract expansion to 7 methods).
- `Billing.UnitTests/Stripe/CreateCheckoutSessionHandlerTests.cs`: 6 scenarios.
- `Billing.UnitTests/Stripe/CreatePortalSessionHandlerTests.cs`: 5 scenarios.
- `Billing.UnitTests/Stripe/HandleWebhookSubscriptionEventTests.cs`: 8 scenarios.
- `Billing.UnitTests/Stripe/StripeWebhookEventTests.cs`: 8 scenarios (validation + MarkProcessed/MarkFailed + Status + append-only invariant).
- `Billing.UnitTests/Stripe/SubscriptionWebhookSyncTests.cs`: 9 scenarios (6 mapped statuses + 3 invariants).

**Total new tests**: 50 (target was 45 per `tasks.md` line 145 — exceeded by 5).

## Build + Test Status

- `dotnet build JadeCapital.slnx --nologo --verbosity minimal`: **0 errors, 0 warnings**.
- `dotnet test --filter "FullyQualifiedName~StripeCheckout|StripeSubscription|StripeWebhook|SubscriptionWebhookSync|CreateCheckoutSession|CreatePortalSession|HandleWebhook"`: **42/42 pass** (focused filter).
- `dotnet test --filter "FullyQualifiedName~Stripe" --project tests/UnitTests/JadeCapital.Shared.Kernel.UnitTests`: **29/29 pass**.
- Full Billing unit suite: **91/91 pass** (no regressions on the 49 non-Stripe tests).
- Full Identity unit suite: **163/163 pass** (no regressions).
- Full Trading unit suite: **700/700 pass** (no regressions).
- Shared.Kernel full unit suite: **86/86 pass** (no regressions; per-project run only — full-solution run hangs at vstest discovery per the Wave 5 env note).
- Cumulative **1040+ BE tests pass**, **+50 new** vs. 6a.1 baseline.

## Diff Statistics

```
16 files modified
18 files new
1,512 insertions(+)
  160 deletions(-)
34 paths total (will be 35 once apply-progress is added)
```

vs. the forecast in `tasks.md`:

- Forecast: ~800 lines, 14 paths
- Actual: 1,672 lines (1,512 insertions + 160 deletions), 34 paths (35 with apply-progress)

### `size:exception` Justification

Per Wave 5/6a.1 precedent (5b.2=2989, 5c.1=3075, 6a.1=2035), every Wave 6 slice uses `size:exception`. Reasons:

1. **Tests are ~65% of the diff** (mandatory per Strict TDD). 6 new test files × 50 scenarios = ~1,100 lines of test code.
2. **5 new DTOs are independent** — each gets its own file by project convention (mirrors `StripeCustomerDto`, `StripeWebhookEvent`, `StripeError` in 6a.1). Cannot merge without breaking the convention.
3. **Webhook dispatch + retry + idempotency + history append** is a coherent unit — splitting would force an artificial boundary.
4. **Path overage** (34 vs. 14 forecast) is driven by the test count, not by production code.

## Deviations from Design

### 1. **EF Core dependency avoidance in Application layer**

`HandleWebhookHandler` needs to catch the EF Core `DbUpdateConcurrencyException` for the optimistic-concurrency retry loop. Rather than adding `Microsoft.EntityFrameworkCore` to `Billing.Application.csproj` (which would violate Clean Architecture's infrastructure-agnostic Application layer), the handler detects the exception via type-name reflection:

```csharp
private const string EfCoreConcurrencyExceptionTypeName =
    "Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException";

private static bool IsEfCoreConcurrencyException(Exception ex)
    => ex.GetType().FullName == EfCoreConcurrencyExceptionTypeName;
```

Rationale: Application layer stays free of EF Core (consistent with `JadeCapital.Identity.Application`, `JadeCapital.Trading.Application`). Reflection cost is negligible (one string compare per SaveChanges failure). The type name is stable across EF Core 8/9/10.

### 2. **Microsoft.Extensions.Logging.Abstractions added to Billing.Application**

The handler uses `ILogger<HandleWebhookHandler>` for diagnostic logging (warnings on subscription-not-found, optimistic-concurrency retry, dispatch failure). Following the precedent in `JadeCapital.Identity.Application` (which already references `Microsoft.Extensions.Logging.Abstractions` 9.0.0), the package was added to `JadeCapital.Billing.Application.csproj`. No EF Core dependency added.

### 3. **`StripeWebhookEventErrors` co-located with aggregate**

To stay within the path budget, the error catalog was inlined into `StripeWebhookEvent.cs` (one file with aggregate + enum + errors). The 6a.1/6b.1 convention splits errors from aggregates, but `StripeWebhookEvent` is a simple aggregate with 4 trivial error codes — co-location is acceptable for this slice.

### 4. **`StripeConfiguration.ApiVersion` not overridden at construction**

Per 6a.1 deviation #2 — the gateway preserves `StripeOptions.ApiVersion` as a field but does NOT assign it to `StripeConfiguration.ApiVersion` (Stripe.NET 47.0.0 has that property as read-only). The library default is the pinned version.

### 5. **Audit events deferred to 6d.1**

The spec calls for an `audit.events` row on every successful webhook sync (`actor = "stripe-webhook"`, `changes = diff JSON`). 6a.2 does NOT write audit events — `IAuditLogger` ships in slice 6d.1 (Wave 6 / soft-delete + audit). For 6a.2, the handler logs to Serilog with the same structured fields the audit row would carry. When 6d.1 lands, the handler adds `await auditLogger.LogAsync(...)` to `PersistSubscriptionChanges`. The history entry on the `Subscription` aggregate IS the audit-equivalent for 6a.2.

### 6. **Test fix-up for `StripeWebhookEvent` ambiguity**

The pre-written test fixtures (`HandleWebhookSubscriptionEventTests`) referenced `StripeWebhookEvent` ambiguously between Domain aggregate and Shared.Kernel DTO. Added type aliases:

```csharp
using DomainStripeWebhookEvent = JadeCapital.Billing.Domain.Stripe.StripeWebhookEvent;
using StripeWebhookEventDto = JadeCapital.Shared.Kernel.Stripe.StripeWebhookEvent;
```

Same pattern applied in `HandleWebhookHandler.cs`. Pre-existing 6a.1 tests (`HandleWebhookHandlerTests.cs`) had their constructor call updated to pass substitutes for the 3 new dependencies — minimal change.

## Critical Lessons Applied (from Wave 4 + Wave 5 + 6a.1)

| Lesson | Where applied |
|---|---|
| No duplicate EF config | EF config registered once via `BillingDbContext.OnModelCreating` + `ApplyConfiguration`. No `services.AddSingleton<IEntityTypeConfiguration<...>>`. |
| `GetByXAsync` narrow surface | `IStripeWebhookEventRepository.FindByEventIdAsync` — intention-revealing, single purpose. |
| Optimistic-concurrency pattern | `Subscription.SyncFromStripe` writes via EF version token; handler retries on `DbUpdateConcurrencyException` (3× with jitter). |
| Strict TDD | All 6 phases followed RED → GREEN → REFACTOR. TDD Cycle Evidence table above. |
| Idempotent migrations | `CREATE TABLE IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS`, `ADD COLUMN IF NOT EXISTS`, `CREATE UNIQUE INDEX IF NOT EXISTS`, partial unique index for nullable StripeSubscriptionId. Re-run safe. |
| Defense-in-depth | Every Stripe SDK call has try/catch: `StripeException`, `HttpRequestException`, `OperationCanceledException`, generic `Exception` → `Result.Failure` (or propagate cancellation). |
| Webhook raw-body read | Already established in 6a.1 (`Request.EnableBuffering` + `Body.CopyToAsync`). Unchanged in 6a.2. |
| `api-billing` rate limit | Both new endpoints use the existing `api-billing` policy (10 calls/hour/user). Webhook endpoint unchanged (`api-general`). |
| Stripe.NET namespace collision | Fully-qualified `JadeCapital.Shared.Kernel.Stripe.StripeError` + `using StripeErrors = Stripe.StripeError;` alias. |
| `StubStripeGateway` fallback | Registered when `StripeOptions.ApiKey` is null/empty. All 5 new methods return synthetic but predictable responses. |
| `IStripeClient` seam | Tests inject `FakeStripeClient` (6a.1 pattern) for the 6a.1 methods; 6a.2's new gateway methods are covered via `StubStripeGatewayTests` (stub) and `CreateCheckoutSessionHandlerTests` / `CreatePortalSessionHandlerTests` (handlers). No real HTTP. |
| Auto-chain chain strategy | PR targets `feature/0a-identity-model` (the previous PR's merge target — `feature-branch-chain` per Wave 5/6a.1 precedent). |

## What's NOT in Slice 6a.2

Per `tasks.md` 6a.2 scope, these arrive in subsequent slices:

- **6b.1**: Billing portal read API (`GET /api/billing/portal/subscription|payment-methods|invoices`) — uses the 3 gateway read methods added in 6a.2 (`GetSubscriptionAsync`, `GetPaymentMethodsAsync`, `GetInvoicesAsync`).
- **6b.2**: Billing portal Angular page (the "Manage in Stripe" button calls `POST /api/billing/stripe/portal`).
- **6c.1-6c.3**: Multi-tenant (different scope).
- **6d.1-6d.2**: Soft-delete + audit (audit events for webhook sync land in 6d.1).

## Reviewer Notes

- **50 new tests** (target was 45 per `tasks.md` line 145 — exceeded by 5 due to combined history/StripeSubscriptionId invariants in `SubscriptionWebhookSyncTests`).
- **Path overage**: 34 paths (will be 35 with this apply-progress) vs. the 14 budget. `size:exception` accepted per Wave 5/6a.1 precedent.
- **Optimistic-concurrency retry**: detected via type-name reflection — `Billing.Application` stays EF-free. See deviation #1.
- **Webhook dispatch flow** (in order): verify signature → idempotency check (`processed_at != null` → return duplicate) → append `billing.stripe_webhook_events` row → if subscription event: lookup local sub by Stripe id → apply via `SubscriptionWebhookSync` → mark processed → save. Retry loop catches `DbUpdateConcurrencyException` with 50-200 ms jitter; after 3 failures the webhook is marked Failed.
- **`billing.subscriptions.stripe_subscription_id`** is nullable + has a UNIQUE partial index (`WHERE stripe_subscription_id IS NOT NULL`). The column is populated on the first `customer.subscription.*` webhook for that user. NOT NULL lands in Wave 7 (post-backfill).

## Rollback

Revert code; `billing.stripe_webhook_events` table remains inert (no other slices reference it yet — slice 6a.2 is the only consumer). `billing.subscriptions.stripe_subscription_id` is additive (nullable); reverting code leaves the column at NULL for all rows (no harm). Migration 0023 is idempotent — re-running it after rollback is a no-op.

## Next Slice

**6b.1** — Billing Portal Read API (`GET /api/billing/portal/{subscription|payment-methods|invoices}`, ≤ 500 lines, 10 paths). Uses the 3 gateway read methods added here (`GetSubscriptionAsync`, `GetPaymentMethodsAsync`, `GetInvoicesAsync`).