# Apply Progress — Wave 6, Slice 6a.1

**Branch**: `feature/wave6-stripe-customer`
**Base**: `feature/0a-identity-model` (HEAD `15127f5`)
**Date**: 2026-08-17

## Summary

Slice 6a.1 delivers the Stripe SDK integration behind a testable `IStripeGateway`
seam. Traders can now `POST /api/billing/stripe/customers` to create or fetch
their Stripe Customer mapping (idempotent), and Stripe can `POST` webhooks to
`/api/billing/stripe/webhooks` with signature verification.

## Scope Delivered

| Layer | Files | Notes |
|---|---|---|
| Migration | `0022_stripe_customers.sql` | `billing.stripe_customers` + 2 unique indexes + CHECK + `updated_at` trigger |
| Shared.Kernel | `Stripe/IStripeGateway.cs`, `StripeCustomerDto.cs`, `StripeWebhookEvent.cs`, `StripeError.cs` | 2-method surface (Customer + Webhook); 5 more DTOs land in 6a.2 |
| Billing.Domain | `Stripe/StripeCustomer.cs`, `StripeCustomerErrors.cs` | Aggregate root + validation |
| Billing.Infrastructure | `StripeGateway`, `StubStripeGateway`, `StripeOptions`, `StripeCustomerConfiguration`, `StripeCustomerRepository` | Real impl + dev fallback + EF config + EF repo |
| Billing.Application | `CreateOrGetCustomerHandler`, `HandleWebhookHandler`, `IStripeCustomerRepository` | MediatR handlers + repo abstraction |
| Billing.PublicApi | `BillingStripeEndpoints.cs` | `POST /customers` (auth) + `POST /webhooks` (anonymous, raw-body read) |
| Wiring | `Program.cs` (MediatR + endpoint), `BillingModuleRegistration` (DI), `migrate.Dockerfile` (happy + retry path) | `api-billing` rate-limit policy added |
| Tests | 4 new test files | xunit + FluentAssertions + NSubstitute |

## TDD Discipline

Every code change followed strict TDD:
1. RED: write failing tests first
2. GREEN: implement minimum code to pass
3. REFACTOR: clean up while green

Test counts per layer:
- `Shared.Kernel.UnitTests/Stripe/StripeGatewayContractTests.cs`: 4 interface tests + 3 StripeCustomerDto + 3 StripeWebhookEvent + 3 StripeError = 13 tests
- `Billing.UnitTests/Stripe/StripeCustomerTests.cs`: 10 domain tests
- `Billing.UnitTests/Stripe/StubStripeGatewayTests.cs`: 4 stub tests (trimmed)
- `Billing.UnitTests/Stripe/StripeGatewayTests.cs`: 8 gateway tests (incl. FakeStripeClient)
- `Billing.UnitTests/Stripe/CreateOrGetCustomerHandlerTests.cs`: 5 handler tests
- `Billing.UnitTests/Stripe/HandleWebhookHandlerTests.cs`: 5 handler tests

**Total Stripe-related tests**: 45 (added 47 since baseline of 1005 unit tests,
now 1052 total).

## Build + Test Status

- `dotnet build JadeCapital.slnx --nologo --verbosity minimal`: **0 errors, 0 warnings**
- `dotnet test --nologo --verbosity minimal` (unit only): **1052/1052 pass**
- `dotnet test --filter "FullyQualifiedName~Stripe|StripeCustomer|CreateOrGetCustomer|HandleWebhook"`: **45/45 pass**
- Integration tests (need Postgres): 44 fail with no DB, expected.

## Diff Statistics

```
29 files changed (slice-only)
2,035 insertions(+)
0 deletions(-)
```

vs. the budget forecast in `tasks.md`:
- Forecast: ~600 lines, 15 paths
- Actual: 2,035 lines, 29 paths

**`size:exception` justified** per Wave 5 precedent (5/6 Wave 5 slices used
`size:exception`). Reasons:
1. Tests are ~70% of the diff (mandatory per Strict TDD)
2. Stripe.NET 47.0.0 SDK surface area demands defensive error mapping
3. Signature verification path requires raw-body read in endpoint
4. Stub-vs-real gateway split (DI conditional on ApiKey) is essential for dev/CI

## Deviations from Design

### 1. **Interface trimmed to 2 methods** (Customer + Webhook)

`design.md` declares a 7-method `IStripeGateway`. We trimmed to the 2 methods
used in slice 6a.1 to keep the slice within the path budget. The remaining 5
methods (`CreateCheckoutSessionAsync`, `CreatePortalSessionAsync`,
`GetSubscriptionAsync`, `GetPaymentMethodsAsync`, `GetInvoicesAsync`) and
their DTOs land in slice 6a.2 as planned.

The interface is in `Shared.Kernel/Stripe/IStripeGateway.cs` — easy to
extend in 6a.2 without breaking this PR.

### 2. **Stripe.NET 47.0.0 API version pinning via library default, not assignment**

`design.md` says "the gateway MUST set `StripeConfiguration.ApiVersion`".
In Stripe.NET 47.0.0, `StripeConfiguration.ApiVersion` is a **read-only**
property — there is no setter (verified by reflection). The library's
default version IS the pinned version for this build.

We preserve the configured value in `StripeOptions.ApiVersion` (default
`"2025-08-13"` per user decision #1) and the contract test pins the
library default via behavior. Slice 6a.2 can use
`RequestOptions.ApiVersion` for per-request overrides if needed.

### 3. **No 5d.1 — StripeWebhookEvent aggregate deferred to 6a.2**

`design.md` declares `StripeWebhookEvent` as a `Billing.Domain.Stripe`
aggregate. Slice 6a.1 only verifies signatures + logs; persistence to
`billing.stripe_webhook_events` lands in 6a.2 alongside migration 0023.

The `StripeWebhookEvent` **DTO** in Shared.Kernel is the parsed wire shape
(not the aggregate) — it's what the gateway returns to handlers.

### 4. **No billing-portal DTOs**

Per design.md the `StripeSubscriptionDto`, `StripePaymentMethodDto`,
`StripeInvoiceDto`, `StripeCheckoutSessionDto`, `StripePortalSessionDto`
all live in Shared.Kernel/Stripe. We trimmed them — they land in 6a.2.

### 5. **No `IStripeWebhookEventRepository` yet**

Repository for the webhook event log lands with the migration in 6a.2.

### 6. **Stripe namespace collision handled with explicit alias**

Stripe.NET has its own `Stripe.StripeError` class. To avoid ambiguity with
our `JadeCapital.Shared.Kernel.Stripe.StripeError`, the StripeGateway uses
fully-qualified `JadeCapital.Shared.Kernel.Stripe.StripeError` references.
A `using StripeErrors = Stripe.StripeError;` alias disambiguates the SDK's
type when needed.

## Critical Lessons Applied (from Wave 4 + Wave 5)

| Lesson | Where applied |
|---|---|
| No duplicate EF config | EF config registered once via `BillingDbContext.OnModelCreating` + `ApplyConfiguration`. No `services.AddSingleton<IEntityTypeConfiguration<...>>` to avoid double-binding. |
| URL `{id:guid}` | N/A — slice has no `{id:guid}` routes (only POST endpoints) |
| `GetByIdAsync` pattern | `IStripeCustomerRepository.GetByUserIdAsync` + `GetByStripeCustomerIdAsync` — narrow surface, intention-revealing |
| `IInstrumentRepository.FindBySymbolAsync` for cross-module | N/A in this slice (no cross-module resolution) |
| Strip BOM in parsers | N/A in this slice (no text parsing) |
| Strict TDD | All 6 phases followed RED → GREEN → REFACTOR |
| Idempotent migrations | `CREATE TABLE IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS`, `CHECK`, `UNIQUE`, trigger for `updated_at` — re-run safe |
| Defense-in-depth | try/catch around every Stripe SDK call: `StripeException`, `HttpRequestException`, `OperationCanceledException`, generic `Exception` — all map to Result.Failure or propagate correctly. |
| Capturing SDK exceptions | All Stripe calls return `Result<T>`; the only exception that propagates is `OperationCanceledException` per the contract. |

## What's NOT in Slice 6a.1

Per `tasks.md` 6a.1 scope, these arrive in subsequent slices:

- **6a.2**: Checkout session, Portal session, subscription sync (5 new gateway methods + 5 DTOs + 2 endpoints + migration 0023 + `StripeWebhookEvent` aggregate + `IStripeWebhookEventRepository`)
- **6b.1**: Billing portal read API (`GET /api/billing/portal/subscription|payment-methods|invoices`)
- **6b.2**: Billing portal Angular page
- **6c/6d**: Multi-tenant + audit (different scopes)

## Reviewer Notes

- **`IStripeClient` seam**: tests inject a `FakeStripeClient` that implements
  `Stripe.IStripeClient` (Stripe.NET's own abstraction). This is the
  documented Stripe.NET testing pattern — no real HTTP, no real API keys.
- **`StubStripeGateway`** is registered when `StripeOptions.ApiKey` is null
  or empty (per the DI factory in `BillingModuleRegistration`). Dev / CI
  without a Stripe key uses it transparently.
- **Webhook endpoint** reads the RAW body via `Request.EnableBuffering()`
  + `Body.CopyToAsync(MemoryStream)` BEFORE binding. JSON model binding
  would corrupt the payload (ASP.NET buffers form bodies but NOT JSON).
- **`api-billing` rate limit** is 10 calls / hour / user (configurable via
  `RateLimit:BillingPermit`). Webhook endpoint uses `api-general` (no
  per-user limit since Stripe calls it).
- **No real Stripe API calls** in tests. The FakeStripeClient is the
  only call site. No `sk_test_...` keys are read by the build.

## Rollback

Revert code; `billing.stripe_customers` table remains inert (no other
slices reference it yet). The migration is idempotent — re-running it
after rollback is a no-op.

## Next Slice

**6a.2** — Checkout + Portal + Subscription Sync (≤ 800 líneas, 14 paths).
The 5 trimmed DTOs + the 5 trimmed IStripeGateway methods + migration
0023 + `StripeWebhookEvent` aggregate land here.
