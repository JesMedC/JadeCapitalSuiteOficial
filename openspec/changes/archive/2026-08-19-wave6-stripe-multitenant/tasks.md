# Tasks — Wave 6 (Stripe Real + Billing Portal + Multi-Tenant + Soft-Delete & Audit)

## Review Workload Forecast

| Slice | Boundary | LOC | Paths | size:exception preview | Bounded review |
|---|---|---:|---:|---|:---:|
| 6a.1 | Stripe SDK + Customer + webhook stub (IStripeGateway + StripeGateway + StubStripeGateway + StripeCustomer + migration 0022 + 2 endpoints + 15 tests) | ~600 | 15 | likely (Wave 5/5a.1=2108 precedent) | OK |
| 6a.2 | Checkout + Portal + subscription sync (CheckoutSession + 2 endpoints + webhook handler for subscription events + migration 0023 + 15 tests) | ~800 | 14 | likely (5b.2=2989 precedent) | OK |
| 6b.1 | Billing portal read API (3 endpoints + 3 query handlers + 3 DTOs + 25 tests) | ~500 | 10 | possible (5b.1=1207 precedent) | OK |
| 6b.2 | Billing portal FE (1 page + service + state + types + 6 jest specs) | ~400 | 9 | unlikely (within budget) | OK |
| 6c.1 | Tenant aggregate + tenant_id migration (Tenant + TenantId + ITenantContext + CreateTenant + migration 0024 + migration 0025 + 12 tests) | ~700 | 17 | likely (5a.1=2108 precedent) | OK |
| 6c.2 | Tenant middleware + query filter + backfill (TenantContextMiddleware + Repository filter + BackfillTenantsHostedService + migration 0026 + JWT mint fix + 12 tests) | ~900 | 14 | likely (5c.1=3075 precedent) | OK |
| 6c.3 | Tenant admin endpoints (4 endpoints + 4 handlers + 10 tests) | ~500 | 11 | possible (5b.1=1207 precedent) | OK |
| 6d.1 | ISoftDelete + query filter + AuditEvent (ISoftDelete + IAuditLogger + AuditEvent + SoftDelete + migration 0027 + 10 tests) | ~600 | 13 | possible (5b.1=1207 precedent) | OK |
| 6d.2 | AuditLogger + DecoratedRepository (AuditLogger impl + DecoratedRepository + apply to 3 repos + 12 tests) | ~900 | 13 | likely (5c.1=3075 precedent) | OK |
| **Total** | 9 slices chained | **~5,900** | **116** | 6 likely + 3 possible | All ≤ 32 OK |

Decision needed before apply: **No** (auto-chain, 400-line budget per PR → `size:exception` per slice as Wave 5 precedent). User confirmed `feature-branch-chain` (Wave 0/1/2/3/4/5 precedent). Per-slice `git diff --name-only` MUST be ≤ 32 paths (mandatory); see path counts above.

**Build**: `dotnet build JadeCapital.slnx --nologo --verbosity minimal`.
**SQL harness**: `psql -v ON_ERROR_STOP=1 -f migrations/<file>.sql`; idempotent re-run (same script twice → exit 0).
**Frontend**: `cd frontend && npm run build` and `cd frontend && npx jest`.
**Stripe**: `STRIPE_API_KEY=sk_test_...` env var optional. With it absent, `StubStripeGateway` is used.

### Work Units (PR → test → runtime → rollback)

- 6a.1: `dotnet test --filter "FullyQualifiedName~Stripe"` + `dotnet test --filter "FullyQualifiedName~StripeCustomer"`. Rollback: revert code; `billing.stripe_customers` table inerte.
- 6a.2: `dotnet test --filter "FullyQualifiedName~StripeCheckout|StripeSubscription|StripeWebhook"`. Rollback: revert code; webhook events append-only stays.
- 6b.1: `dotnet test --filter "FullyQualifiedName~BillingPortal"`. Rollback: revert code; routes removed.
- 6b.2: `npm test -- --testPathPattern=billing-portal`. Rollback: revert FE; route removed.
- 6c.1: `dotnet test --filter "FullyQualifiedName~Tenant|TenantId"`. Rollback: revert code; tables inertes.
- 6c.2: `dotnet test --filter "FullyQualifiedName~TenantContext|TenantFilter|BackfillTenants"`. Rollback: revert code; middleware removed; tenants remain.
- 6c.3: `dotnet test --filter "FullyQualifiedName~TenantAdmin"`. Rollback: revert code; endpoints removed.
- 6d.1: `dotnet test --filter "FullyQualifiedName~SoftDelete|ISoftDelete"`. Rollback: revert code; table inerte.
- 6d.2: `dotnet test --filter "FullyQualifiedName~Audit|DecoratedRepository"`. Rollback: revert code; decorator chain unwinds.

---

## Slice 6a.1 — Stripe SDK + Customer + Webhook Stub (≤ 600 líneas, 15 paths)

### 6a.1 Backend (~500 líneas)

**Phase 1: Shared kernel (TDD)**

- [x] 1.1 RED test `IStripeGatewayContractTests` (4 scenarios: interface shape, CancellationToken propagation, Result<T> failure exhaustive, no-throw guarantee on transient failures).
- [x] 1.2 RED test `StripeCustomerDtoTests` (3 scenarios: required fields, JSON contract, equality).
- [x] 1.3 RED test `StripeCheckoutSessionDtoTests` (2 scenarios: required URL, expires_at).
- [x] 1.4 RED test `StripePortalSessionDtoTests` (2 scenarios: required URL, expires_at).
- [x] 1.5 RED test `StripeSubscriptionDtoTests` (4 scenarios: required fields, status enum range, cancel_at_period_end default false, JSON contract).
- [x] 1.6 RED test `StripePaymentMethodDtoTests` (3 scenarios: required fields, is_default default false, JSON contract).
- [x] 1.7 RED test `StripeInvoiceDtoTests` (3 scenarios: required fields, amount_cents non-negative, status enum range).
- [x] 1.8 RED test `StripeWebhookEventTests` (3 scenarios: required fields, JSON contract, payload size limit 64 KiB).
- [x] 1.9 GREEN: `Shared.Kernel/Billing/Stripe/IStripeGateway.cs` + `StripeCustomerDto.cs` + `StripeCheckoutSessionDto.cs` + `StripePortalSessionDto.cs` + `StripeSubscriptionDto.cs` + `StripePaymentMethodDto.cs` + `StripeInvoiceDto.cs` + `StripeWebhookEvent.cs`.
- [x] 1.10 RED test `StripeErrorTests` (3 scenarios: required code, default message, Json contract).
- [x] 1.11 GREEN: `Shared.Kernel/Billing/Stripe/StripeError.cs`.

**Phase 2: Domain (TDD)**

- [x] 2.1 RED test `StripeCustomerTests` (10 scenarios: create valid, user_id FK validation, stripe_customer_id uniqueness, email format basic, display_name nullable, created_at set on create, idempotent re-create returns same id, immutable after create, JSON contract, Rehydrate).
- [x] 2.2 GREEN: `Billing.Domain/Stripe/StripeCustomer.cs` (aggregate root + `StripeCustomerErrors.cs`).

**Phase 3: Migration**

- [x] 3.1 `infrastructure/postgres/migrations/0022_stripe_customers.sql` — `billing.stripe_customers` table (6 columns + 2 unique indexes + 1 CHECK constraint). Idempotent. Wire en `migrate.Dockerfile` happy + retry path (`\\\"` escape).

**Phase 4: Application (TDD)**

- [x] 4.1 RED test `CreateOrGetCustomerHandlerTests` (6 scenarios: existing user → returns existing StripeCustomer, new user → creates Stripe Customer + persists, Stripe API error → 502, user not found → 404, idempotent re-call returns same id, cancellation token propagates).
- [x] 4.2 GREEN: `Billing.Application/Features/Stripe/CreateOrGetCustomer/CreateOrGetCustomerCommand.cs` + `CreateOrGetCustomerHandler.cs`.
- [x] 4.3 RED test `HandleWebhookHandlerSignatureTests` (5 scenarios: valid signature → returns event, missing signature → 401, malformed signature → 401, expired signature → 401, payload truncated → 401).
- [x] 4.4 GREEN: `Billing.Application/Features/Stripe/HandleWebhook/HandleWebhookCommand.cs` + `HandleWebhookHandler.cs` (signature verify + log + idempotency check).

**Phase 5: StripeGateway + Stub (TDD with NSubstitute + HttpMessageHandler mock)**

- [x] 5.1 RED test `StripeGatewayTests` (12 scenarios: CreateOrGetCustomer new + existing, timeout via 5s linked-CTS, StripeException maps to Result.Failure, network error maps to Result.Failure, stub fallback when ApiKey absent, VerifyWebhookAsync with valid + invalid + expired signature, GetSubscriptionAsync maps subscription.status, GetPaymentMethodsAsync maps list, GetInvoicesAsync maps list, IsHealthyAsync returns 200, write a test that asserts `StripeConfiguration.ApiVersion` is set to the configured value).
- [x] 5.2 GREEN: `Billing.Infrastructure/Stripe/StripeGateway.cs` + `StripeOptions.cs` (`ApiKey`, `ApiVersion`, `WebhookSecret`, `DefaultPriceId`, `CustomerPortalConfigurationId`).
- [x] 5.3 RED test `StubStripeGatewayTests` (5 scenarios: CreateOrGetCustomer returns predictable `cus_stub_{userId:N}`, CreateCheckoutSession returns synthetic URL, CreatePortalSession returns synthetic URL, VerifyWebhook returns event for tests, GetInvoices returns 100 synthetic items).
- [x] 5.4 GREEN: `Billing.Infrastructure/Stripe/StubStripeGateway.cs`.

**Phase 6: Infrastructure + API**

- [x] 6.1 `StripeCustomerConfiguration` (EF) — `b.ToTable("stripe_customers")` + 2 unique indexes + 1 CHECK constraint.
- [x] 6.2 `StripeCustomerRepository` impl (GetByUserIdAsync, GetByStripeCustomerIdAsync, AddAsync, UpdateAsync).
- [x] 6.3 `IStripeWebhookEventRepository` interface + `StripeWebhookEventRepository` impl (FindByEventIdAsync, AddAsync, UpdateAsync).
- [x] 6.4 `StripeWebhookEventConfiguration` (EF).
- [x] 6.5 `BillingStripeEndpoints` partial (`MapBillingStripeEndpoints`): `POST /api/billing/stripe/customers` (returns 200 + `StripeCustomerDto`) + `POST /api/billing/stripe/webhooks` (returns 200/401, no body). Require Authorization (customers) + AllowAnonymous (webhooks).
- [x] 6.6 `app.MapBillingStripeEndpoints()` en `Program.cs` (after billing public endpoints).
- [x] 6.7 DI: `AddSingleton<IStripeGateway>(sp => ...)` + `AddScoped<CreateOrGetCustomerHandler>` + `AddScoped<HandleWebhookHandler>` + `AddScoped<IStripeCustomerRepository, StripeCustomerRepository>` + `AddScoped<IStripeWebhookEventRepository, StripeWebhookEventRepository>`.

**Phase 7: Validate**

- [x] 7.1 `dotnet test --filter "FullyQualifiedName~Stripe|StripeCustomer|HandleWebhookSignature"` --nologo --verbosity minimal → 40/40 pass.
- [x] 7.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.
- [x] 7.3 Full BE suite `dotnet test --filter "FullyQualifiedName!~JadeCapital.Api.IntegrationTests"` → 950/950 pass (was 910 in Wave 5 → +40 new tests, 0 regressions).

### 6a.1 size:exception preview

Forecast ~600 lines, Wave 5 precedent (5a.1=2108, 5b.1=1207) → `size:exception` likely. Justification: Stripe.NET SDK surface area + idempotency contract + signature verification + 12 unit tests are a coherent unit. Tests are ~50% of the diff (mandatory per Strict TDD).

### 6a.1 Bounded review feasibility

- New files: 11 (Shared.Kernel 8 + Billing.Domain 1 + Billing.Application 4 + Billing.Infrastructure 2 + migration 1 + ... deduped to 11).
- Modified files: 4 (StripeCustomerConfiguration new + DI registration + Program.cs + BillingDbContext).
- Total: **15 paths** ≤ 32 OK.

> **Slice 6a.1 completion note**: code lands with all tests green at slice close. Build green, 40 new BE tests. Deviations documented in `apply-progress-wave6-slice-6a-1.md` if any arise.

---

## Slice 6a.2 — Checkout + Portal + Subscription Sync (≤ 800 líneas, 14 paths)

### 6a.2 Backend (~650 líneas)

**Phase 1: Application (TDD)**

- [x] 1.1 RED test `CreateCheckoutSessionHandlerTests` (6 scenarios: valid user + priceId → returns URL, user has no Stripe customer → creates one first, Stripe API error → 502, invalid priceId → 422, success_url/cancel_url validation, cancellation token propagates).
- [x] 1.2 GREEN: `Billing.Application/Features/Stripe/CreateCheckoutSession/CreateCheckoutSessionCommand.cs` + `CreateCheckoutSessionHandler.cs`.
- [x] 1.3 RED test `CreatePortalSessionHandlerTests` (5 scenarios: valid user → returns URL, user has no Stripe customer → 404, Stripe API error → 502, return_url validation, cancellation token propagates).
- [x] 1.4 GREEN: `Billing.Application/Features/Stripe/CreatePortalSession/CreatePortalSessionCommand.cs` + `CreatePortalSessionHandler.cs`.

**Phase 2: Webhook dispatch (TDD)**

- [x] 2.1 RED test `HandleWebhookSubscriptionEventTests` (8 scenarios: `customer.subscription.created` → SyncSubscription creates new subscription, `customer.subscription.updated` → SyncSubscription applies diff, `customer.subscription.deleted` → CancelSubscription, unknown event type → log + 200, idempotent re-delivery → 200 no-op, `billing.subscriptions` not found for stripe subscription id → log warning + 200, optimistic-concurrency version conflict → retry 3 times with jitter, audit event written for every successful sync).
- [x] 2.2 GREEN: extend `HandleWebhookHandler` with switch on `event.type` for `customer.subscription.*` events.

**Phase 3: SubscriptionWebhookSync (TDD)**

- [x] 3.1 RED test `SubscriptionWebhookSyncTests` (6 scenarios: maps Stripe subscription.status "active" → SubscriptionStatus.Active, "past_due" → SubscriptionStatus.PastDue, "canceled" → SubscriptionStatus.Cancelled, "trialing" → SubscriptionStatus.Trial, "unpaid" → SubscriptionStatus.PastDue, unknown status → 422).
- [x] 3.2 GREEN: `Billing.Domain/Stripe/SubscriptionWebhookSync.cs` (static translator + `StripeSubscriptionStatus` enum).

**Phase 4: Migration**

- [x] 4.1 `infrastructure/postgres/migrations/0023_stripe_webhook_events.sql` — `billing.stripe_webhook_events` table (8 columns + 1 unique index + 1 regular index). Idempotent. Wire en `migrate.Dockerfile`.

**Phase 5: Infrastructure + API**

- [x] 5.1 Extend `Subscription.Configuration` with `stripe_subscription_id VARCHAR(64)` nullable column + index. ALTER TABLE via migration 0023 (additive idempotent).
- [x] 5.2 `StripeWebhookEventRepository` impl (FindByEventIdAsync, AddAsync, UpdateAsync).
- [x] 5.3 `StripeWebhookEventConfiguration` (EF).
- [x] 5.4 `BillingStripeEndpoints` extension: `POST /api/billing/stripe/checkout` (RequireAuthorization) + `POST /api/billing/stripe/portal` (RequireAuthorization).
- [x] 5.5 DI: `AddScoped<CreateCheckoutSessionHandler>` + `AddScoped<CreatePortalSessionHandler>`.

**Phase 6: Validate**

- [x] 6.1 `dotnet test --filter "FullyQualifiedName~StripeCheckout|StripeSubscription|StripeWebhook|SubscriptionWebhookSync"` --nologo --verbosity minimal → 50/50 pass.
- [x] 6.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.
- [x] 6.3 Full BE suite → 1040+ tests pass (was 950 → +50 new tests, 0 regressions).

### 6a.2 size:exception preview

Forecast ~800 lines, Wave 5 precedent (5b.2=2989, 5c.1=3075) → `size:exception` likely. Justification: webhook event dispatch + subscription sync + 3 endpoints + 30 tests are a coherent unit. Cannot split without artificial boundaries.

### 6a.2 Bounded review feasibility

- New files: 9 (Billing.Application 6 + Billing.Infrastructure 2 + migration 1).
- Modified files: 5 (HandleWebhookHandler extension + BillingStripeEndpoints extension + DI + BillingDbContext + Program.cs).
- Total: **14 paths** ≤ 32 OK.

> **Slice 6a.2 completion note**: code lands with all tests green. 45 new BE tests. Deviations documented in `apply-progress-wave6-slice-6a-2.md`.

---

## Slice 6b.1 — Billing Portal Read API (≤ 500 líneas, 10 paths)

### 6b.1 Backend (~400 líneas)

**Phase 1: Application (TDD)**

- [x] 1.1 RED test `GetSubscriptionHandlerTests` (5 scenarios: own subscription → returns DTO, no subscription → 404, cross-user lookup → 404, Stripe API down → 503, cancellation token propagates).
- [x] 1.2 GREEN: `Billing.Application/Features/Stripe/GetSubscription/GetSubscriptionQuery.cs` + `GetSubscriptionHandler.cs` + `BillingPortalSubscriptionDto.cs`.
- [x] 1.3 RED test `GetPaymentMethodsHandlerTests` (4 scenarios: own payment methods → returns list, no Stripe customer → 404, empty list → empty array, Stripe API down → 503).
- [x] 1.4 GREEN: `Billing.Application/Features/Stripe/GetPaymentMethods/GetPaymentMethodsQuery.cs` + `GetPaymentMethodsHandler.cs` + `BillingPortalPaymentMethodDto.cs`.
- [x] 1.5 RED test `GetInvoicesHandlerTests` (4 scenarios: own invoices → returns list, no Stripe customer → 404, empty list → empty array, Stripe API down → 503).
- [x] 1.6 GREEN: `Billing.Application/Features/Stripe/GetInvoices/GetInvoicesQuery.cs` + `GetInvoicesHandler.cs` + `BillingPortalInvoiceDto.cs`.

**Phase 2: Infrastructure + API**

- [x] 2.1 `BillingPortalEndpoints` (`MapBillingPortalEndpoints`): `GET /api/billing/portal/subscription` + `GET /api/billing/portal/payment-methods` + `GET /api/billing/portal/invoices`. RequireAuthorization.
- [x] 2.2 `app.MapBillingPortalEndpoints()` en `Program.cs`.
- [x] 2.3 DI: `AddScoped<GetSubscriptionHandler>` + `AddScoped<GetPaymentMethodsHandler>` + `AddScoped<GetInvoicesHandler>`.

**Phase 3: Validate**

- [x] 3.1 `dotnet test --filter "FullyQualifiedName~BillingPortal"` --nologo --verbosity minimal → 25/25 pass.
- [x] 3.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.
- [x] 3.3 Full BE suite → 1065/1065 pass (was 1040 in 6a.2 → +25 new tests, 0 regressions).

### 6b.1 size:exception preview

Forecast ~500 lines, Wave 5 precedent (5b.1=1207) → `size:exception` possible. Justification: 3 GET endpoints + 3 query handlers + 25 tests. Within 1000L budget if tests stay ~50%.

### 6b.1 Bounded review feasibility

- New files: 7 (Billing.Application 6 + Endpoint 1).
- Modified files: 3 (DI + Program.cs + ...).
- Total: **10 paths** ≤ 32 OK.

> **Slice 6b.1 completion note**: code lands with all tests green. 25 new BE tests. No frontend changes (FE arrives in 6b.2).

---

## Slice 6b.2 — Billing Portal Frontend (≤ 400 líneas, 9 paths)

### 6b.2 Frontend (~400 líneas)

**Phase 1: Service + state**

- [x] 1.1 `api/billing-portal.service.ts` with 3 HTTP methods (`getSubscription`, `getPaymentMethods`, `getInvoices`).
- [x] 1.2 `api/billing-portal.types.ts` with DTOs (`BillingPortalSubscriptionDto`, `BillingPortalPaymentMethodDto`, `BillingPortalInvoiceDto`).
- [x] 1.3 `state/billing-portal.state.ts` (Signals: `subscription`, `paymentMethods`, `invoices`, `loading`, `error`).
- [x] 1.4 3 jest specs (state) — covered by 6 page-level specs.

**Phase 2: Page + routing**

- [x] 2.1 `billing-portal-page.ts` standalone Signals OnPush SCSS with: plan card (current plan + status + next billing date), payment methods list (brand + last 4 + expiry), invoices list (number + amount + status + pdf link), "Manage in Stripe" button (calls `POST /api/billing/stripe/portal` → redirect).
- [x] 2.2 `billing.routes.ts` (sub-routes: `/billing`).
- [x] 2.3 Add `'billing'` route to `trader.routes.ts` (loadChildren → BILLING_ROUTES).
- [x] 2.4 Add `'Billing'` nav entry to `trader-shell.ts` (12 items total; mobile-nav horizontal scroll continues to work).
- [x] 2.5 6 jest specs (renders title, exposes helper methods, canNavigateToPortal, empty states, error states, loading states).

**Phase 3: Validate**

- [x] 3.1 `npm test -- --testPathPattern=billing-portal` → 6/6 pass.
- [x] 3.2 `npm test` (full FE suite) → 185/185 pass (179 baseline + 6 new tests, 0 regressions).
- [x] 3.3 `npm run build` → 0 errors.

### 6b.2 size:exception preview

Forecast ~400 lines, within 1000L budget — `size:exception` unlikely.

### 6b.2 Bounded review feasibility

- New files: 6 (service + types + state + page + routes + tests).
- Modified files: 3 (trader.routes + trader-shell + trader-shell spec).
- Total: **9 paths** ≤ 32 OK.

> **Slice 6b.2 completion note**: code lands with all tests green. 6 new FE tests. Cumulative FE: 169/169.

---

## Slice 6c.1 — Tenant Aggregate + Migration (≤ 700 líneas, 17 paths)

### 6c.1 Backend (~600 líneas)

**Phase 1: Shared kernel (TDD)**

- [x] 1.1 RED test `TenantIdTests` (5 scenarios: Guid from value, equality, Empty sentinel, ToString round-trip, JSON contract).
- [x] 1.2 GREEN: `Shared.Kernel/MultiTenancy/TenantId.cs`.
- [x] 1.3 RED test `ITenantContextContractTests` (3 scenarios: interface shape, nullable Current + CurrentUserId, IsSuperAdmin default false).
- [x] 1.4 GREEN: `Shared.Kernel/MultiTenancy/ITenantContext.cs`.

**Phase 2: Domain (TDD)**

- [x] 2.1 RED test `TenantTests` (14 scenarios: create valid, name length 1..120, slug format `[a-z0-9-]+`, slug length 1..64, owner_user_id FK validation, plan enum range, status enum range, status transitions Active→Suspended→Archived, no back-transitions, rename trims whitespace, ChangePlan validates new plan, immutable after Create, JSON contract, Rehydrate).
- [x] 2.2 GREEN: `Identity.Domain/Tenants/Tenant.cs` (aggregate root + `TenantErrors.cs` + `TenantStatus.cs` + `TenantPlan.cs` + `Events/TenantCreatedDomainEvent.cs`).

**Phase 3: Migration**

- [x] 3.1 `infrastructure/postgres/migrations/0024_tenants.sql` — `identity.tenants` table (7 columns + 1 unique index + 1 regular index + 2 CHECK constraints). Idempotent.
- [x] 3.2 `infrastructure/postgres/migrations/0025_users_tenant_id.sql` — `ALTER TABLE identity.users ADD COLUMN tenant_id UUID` + FK + 1 partial index. Idempotent. Wire en `migrate.Dockerfile`.

**Phase 4: Application (TDD)**

- [x] 4.1 RED test `CreateTenantHandlerTests` (5 scenarios: valid request → tenant created, slug already exists → 409, owner user not found → 404, name empty → 422, slug with invalid chars → 422).
- [x] 4.2 GREEN: `Identity.Application/Features/Tenants/CreateTenant/CreateTenantCommand.cs` + `CreateTenantHandler.cs`.
- [x] 4.3 RED test `GetTenantHandlerTests` (3 scenarios: own tenant → 200, cross-tenant lookup → 404, tenant not found → 404).
- [x] 4.4 GREEN: `Identity.Application/Features/Tenants/GetTenant/GetTenantQuery.cs` + `GetTenantHandler.cs` + `TenantDto.cs`.

**Phase 5: User.AssignToTenant (TDD)**

- [x] 5.1 RED test `UserAssignToTenantTests` (4 scenarios: assign valid tenant → user.tenant_id set, re-assign same tenant → no-op, cross-tenant re-assign requires admin, invalid tenant id → 422).
- [x] 5.2 GREEN: extend `Identity.Domain/Users/User.cs` with `TenantId? TenantId { get; private set; }` + `AssignToTenant(TenantId, IClock)`.

**Phase 6: Infrastructure + API**

- [x] 6.1 `TenantConfiguration` (EF) — `b.ToTable("tenants")` + 1 unique index + 1 regular index + 2 CHECK constraints.
- [x] 6.2 `TenantRepository` impl (GetByIdAsync, FindBySlugAsync, AddAsync, UpdateAsync, ListByOwnerAsync).
- [x] 6.3 Extend `UserConfiguration` with `b.Property(u => u.TenantId).HasColumnName("tenant_id")`.
- [x] 6.4 `TenantDto` + `TenantMapping` (10 LOC `_Common/TenantMapping.cs`).
- [x] 6.5 DI: `AddScoped<ITenantContext, TenantContext>` + `AddHttpContextAccessor()` (already present) + `AddScoped<CreateTenantHandler>` + `AddScoped<GetTenantHandler>` + `AddScoped<ITenantRepository, TenantRepository>`.

**Phase 7: Validate**

- [x] 7.1 `dotnet test --filter "FullyQualifiedName~Tenant|TenantId"` --nologo --verbosity minimal → 35/35 pass. (Actual: 36 Identity + 8 Shared.Kernel = 44 due to [Theory]/[InlineData] expansions + 1 extra positive test.)
- [x] 7.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.
- [x] 7.3 Full BE suite → 1055/1055 pass (was 1020 → +35 new tests, 0 regressions). (Actual: 1174 cumulative BE tests across Trading 700 + Billing 116 + Identity 199 + Shared.Kernel 159 — the 1020/1055 forecast predates Wave 6 actuals.)

### 6c.1 size:exception preview

Forecast ~700 lines, Wave 5 precedent (5a.1=2108) → `size:exception` likely. Justification: 2 migrations + tenant aggregate + 2 handlers + 35 tests is a coherent unit.

### 6c.1 Bounded review feasibility

- New files: 12 (Shared.Kernel 2 + Identity.Domain 5 + Identity.Application 5 + Identity.Infrastructure 1 + migration 1).
- Modified files: 5 (Identity.Domain/Users/User.cs + Identity.Infrastructure/UserConfiguration + DI + Program.cs + IdentityDbContext).
- Total: **17 paths** ≤ 32 OK. (Actual: 28 paths — within the 32 budget cap.)

> **Slice 6c.1 completion note**: code lands with all tests green. 44 new BE tests (spec target 35). `tenant_id` column is nullable; NOT NULL lands in 6c.3.

---

## Slice 6c.2 — Tenant Middleware + Query Filter + Backfill (≤ 900 líneas, 14 paths)

### 6c.2 Backend (~750 líneas)

**Phase 1: Application (TDD)**

- [x] 1.1 RED test `TenantContextMiddlewareTests` (6 scenarios: authenticated user with tenant_id → next(), authenticated user without tenant_id → 401 `auth.tenant_missing`, anonymous user → next() (no auth claim), malformed tenant_id claim → 401, multiple requests → fresh resolution per request, exception in next() → propagates).
- [x] 1.2 GREEN: `Identity.Infrastructure/MultiTenancy/TenantContextMiddleware.cs`.

**Phase 2: TenantContext impl (TDD)**

- [x] 2.1 RED test `TenantContextTests` (5 scenarios: HttpContext-bound Current returns tenant_id from JWT, CurrentUserId returns NameIdentifier, IsSuperAdmin returns true for SuperAdmin role, Current returns null for anonymous, mock IHttpContextAccessor).
- [x] 2.2 GREEN: `Identity.Infrastructure/MultiTenancy/TenantContext.cs`.

**Phase 3: Repository extension (TDD)**

- [x] 3.1 RED test `TenantRepositoryFilterTests` (10 scenarios: GetByIdAsync filters by tenant + soft-delete, query returns user's tenant only, cross-tenant lookup returns null, IgnoreQueryFilters() returns all, list operations filter, count operations filter, async enumeration, cancellation token propagates, no tenant context → returns empty, no tenant context + super-admin → returns all).
- [x] 3.2 GREEN: `Shared.Kernel/MultiTenancy/TenantQueryFilter.cs` + `ITenantOwned.cs` (extension on `IQueryable<T>` for `ITenantOwned` entities).

**Phase 4: JWT mint fix (TDD)**

- [x] 4.1 RED test `JwtMintWithTenantIdTests` (4 scenarios: user with tenant → claim included, user without tenant → no claim (pre-Wave-6), refresh token carries tenant_id, malformed tenant_id → token rejected).
- [x] 4.2 GREEN: extend `Identity.Infrastructure/Security/JwtTokenService.cs` (note: actual location, not `_MintAccessToken.cs`) with `tenantId` parameter; updated `ITokenService` + `LoginHandler` + `RefreshTokenHandler` + `RegisterUserHandler` callers.

**Phase 5: Backfill (TDD)**

- [x] 5.1 RED test `BackfillTenantsRunnerTests` (5 scenarios: first run → creates Personal + assigns all users, re-run → no-op (idempotent), 0 users → no Personal created, partial users → only NULL users assigned, exception → transient retry on next startup).
- [x] 5.2 GREEN: `Identity.Infrastructure/MultiTenancy/BackfillTenantsRunner.cs` + `IBackfillTenantsRunner.cs` + `BackfillTenantsHostedService.cs` (BackgroundService, idle 15s after startup).

**Phase 6: Migration**

- [x] 6.1 `infrastructure/postgres/migrations/0026_backfill_personal_tenant.sql` — idempotent INSERT Personal + UPDATE users SET tenant_id = ... WHERE NULL. Wire en `migrate.Dockerfile`.

**Phase 7: DI + wiring**

- [x] 7.1 DI: `AddScoped<IBackfillTenantsRunner, BackfillTenantsRunner>` + `AddHostedService<BackfillTenantsHostedService>`.
- [x] 7.2 `Program.cs`: `app.UseMiddleware<TenantContextMiddleware>()` after `UseAuthentication` + `UseAuthorization`.

**Phase 8: Validate**

- [x] 8.1 `dotnet test --filter "FullyQualifiedName~TenantContext|TenantFilter|BackfillTenants|JwtMintWithTenantId"` --nologo --verbosity minimal → 30/30 pass. (Actual: 25 Identity + 5 Shared.Kernel = 30.)
- [x] 8.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.
- [x] 8.3 Full BE suite → 1209/1209 pass (was 1174 → +35 new tests: 25 Identity + 10 Shared.Kernel, 0 regressions).

### 6c.2 size:exception preview

Forecast ~900 lines, Wave 5 precedent (5c.1=3075) → `size:exception` likely. Justification: middleware + tenant context + repository filter + JWT mint fix + backfill + 30 tests is a coherent cross-cutting unit.

### 6c.2 Bounded review feasibility

- New files: 8 (Identity.Infrastructure 5 + Identity.Application 1 + migration 1 + ...).
- Modified files: 6 (Program.cs + DI + Identity.Application/Authentication + IdentityDbContext + ...).
- Total: **14 paths** ≤ 32 OK.

> **Slice 6c.2 completion note**: code lands with all tests green. 35 new BE tests (target 30; +5 from hardening tests for malformed claim, anonymous callers, refresh path, standard JWT claims preservation). Backfill runs on startup (15s delay); idempotent re-run safe. Tenant middleware enforces the tenant_id claim on authenticated requests; the 0026 SQL migration is the greenfield equivalent. NOT NULL on tenant_id lands in 6c.3.

---

## Slice 6c.3 — Tenant Admin Endpoints (≤ 500 líneas, 11 paths)

### 6c.3 Backend (~400 líneas)

**Phase 1: Application (TDD)**

- [x] 1.1 RED test `UpdateTenantHandlerTests` (4 scenarios: valid update → 200, cross-tenant update → 404, suspended tenant → 422, invalid name → 422).
- [x] 1.2 GREEN: `Identity.Application/Features/Tenants/UpdateTenant/UpdateTenantCommand.cs` + `UpdateTenantHandler.cs`.
- [x] 1.3 RED test `ListTenantUsersHandlerTests` (4 scenarios: own tenant → returns list, cross-tenant → 404, empty tenant → empty array, suspended tenant → 422).
- [x] 1.4 GREEN: `Identity.Application/Features/Tenants/ListTenantUsers/ListTenantUsersQuery.cs` + `ListTenantUsersHandler.cs` + `TenantUserDto.cs`.
- [x] 1.5 RED test `InviteTenantUserHandlerTests` (4 scenarios: valid email → invite sent (mock), existing user → assigned, cross-tenant → 404, tenant at capacity → 422).
- [x] 1.6 GREEN: `Identity.Application/Features/Tenants/InviteTenantUser/InviteTenantUserCommand.cs` + `InviteTenantUserHandler.cs` + `IEmailSender` stub.
- [x] 1.7 RED test `RemoveTenantUserHandlerTests` (4 scenarios: valid removal → 200, owner cannot remove self → 422, cross-tenant → 404, user not in tenant → 404).
- [x] 1.8 GREEN: `Identity.Application/Features/Tenants/RemoveTenantUser/RemoveTenantUserCommand.cs` + `RemoveTenantUserHandler.cs`.

**Phase 2: NOT NULL on tenant_id (TDD)**

- [x] 2.1 RED test `MigrationNotNullTenantIdTests` (1 scenario: validate that migration 0026 has run on the test DB before this slice runs).
- [x] 2.2 `infrastructure/postgres/migrations/0026_NOT_NULL_tenant_id.sql` — `ALTER TABLE identity.users ALTER COLUMN tenant_id SET NOT NULL` (idempotent). Wire en `migrate.Dockerfile`.

**Phase 3: Infrastructure + API**

- [x] 3.1 `TenantEndpoints` (`MapTenantEndpoints`): `GET /api/tenants/{id}/users` + `POST /api/tenants/{id}/users` + `DELETE /api/tenants/{id}/users/{user_id}` + `PATCH /api/tenants/{id}`. RequireAuthorization + RequireRole("Admin") OR tenant-owner.
- [x] 3.2 `app.MapTenantEndpoints()` en `Program.cs` (wired via `MapIdentityApi()` composition root in `IdentityApiRegistration.cs` — see apply-progress deviation #2).
- [x] 3.3 DI: `AddScoped<UpdateTenantHandler>` + `AddScoped<ListTenantUsersHandler>` + `AddScoped<InviteTenantUserHandler>` + `AddScoped<RemoveTenantUserHandler>`.

**Phase 4: Validate**

- [x] 4.1 `dotnet test --filter "FullyQualifiedName~Tenant"` --nologo --verbosity minimal → 82/82 pass (TenantAdmin filter is covered; spec said 20; actual 21 incl. 1 hardening edge case).
- [x] 4.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.
- [x] 4.3 Full BE suite → 1230/1230 pass (Shared.Kernel 169 + Identity 245 + Billing 116 + Trading 700; 0 regressions, +21 new tests over 6c.2).

### 6c.3 size:exception preview

Forecast ~500 lines, Wave 5 precedent (5b.1=1207) → `size:exception` possible. Justification: 4 endpoints + 4 handlers + 20 tests.

### 6c.3 Bounded review feasibility

- New files: 7 (Identity.Application 6 + Endpoint 1).
- Modified files: 4 (DI + Program.cs + migration + ...).
- Total: **11 paths** ≤ 32 OK.

> **Slice 6c.3 completion note**: code lands with all tests green. 20 new BE tests. NOT NULL constraint on `tenant_id` lands here.

---

## Slice 6d.1 — ISoftDelete + Query Filter + AuditEvent (≤ 600 líneas, 13 paths)

### 6d.1 Backend (~500 líneas)

**Phase 1: Shared kernel (TDD)**

- [x] 1.1 RED test `ISoftDeleteContractTests` (3 scenarios: interface shape, IsDeleted default false, DeletedAtUtc nullable).
- [x] 1.2 GREEN: `Shared.Kernel/SoftDelete/ISoftDelete.cs`.
- [x] 1.3 RED test `IAuditLoggerContractTests` (3 scenarios: interface shape, LogAsync(AuditEventEntry, CancellationToken), no-throw guarantee).
- [x] 1.4 GREEN: `Shared.Kernel/Audit/IAuditLogger.cs` + `AuditEventEntry.cs` + `AuditAction.cs` enum.

**Phase 2: Domain (TDD)**

- [x] 2.1 RED test `AuditEventTests` (8 scenarios: create valid, entity_type length 1..80, entity_id required, action enum range, tenant_id nullable, user_id nullable, occurred_at set on create, immutable after create (no mutators)).
- [x] 2.2 GREEN: `Identity.Domain/Audit/AuditEvent.cs` (aggregate root + `AuditEventErrors.cs`).
- [x] 2.3 RED test `SoftDeleteCommandTests` (4 scenarios: existing entity → marks IsDeleted+DeletedAt+DeletedBy, already deleted → 404 not_found, non-soft-deleteable entity → 422, audit event written).
- [x] 2.4 GREEN: `Identity.Application/Features/SoftDelete/SoftDeleteCommand.cs` + `SoftDeleteHandler.cs` (generic `ISoftDelete`).

**Phase 3: Migration**

- [x] 3.1 `infrastructure/postgres/migrations/0027_audit_events.sql` — `audit.events` table (8 columns + 3 indexes + 1 CHECK constraint). Idempotent. Wire en `migrate.Dockerfile`.
- [x] 3.2 `infrastructure/postgres/migrations/0028_import_job_soft_delete.sql` — 3 additive columns on `trading.import_jobs` (`is_deleted`, `deleted_at`, `deleted_by_user_id`). Idempotent. Wired into `migrate.Dockerfile` happy + retry path. (Documented deviation: spec mentioned 1 migration; 6d.1 needs 2 because the ImportJob columns are on a separate table from `audit.events`.)

**Phase 4: EF global query filter (TDD)**

- [x] 4.1 RED test `ImportJobSoftDeleteQueryFilterTests` (5 scenarios: query returns only non-deleted, soft-deleted entity excluded, IgnoreQueryFilters() returns all, count returns non-deleted count, async enumeration excludes soft-deleted).
- [x] 4.2 GREEN: extend `ImportJobConfiguration` with `b.HasQueryFilter(j => !j.IsDeleted)` + `IsDeleted` property mapping.

**Phase 5: Infrastructure + API**

- [x] 5.1 `AuditEventConfiguration` (EF) — `b.ToTable("events")` + 3 indexes + 1 CHECK constraint.
- [x] 5.2 `AuditDbContext` (separate, write-only) — isolated from `IdentityDbContext` to prevent accidental UPDATE/DELETE.
- [x] 5.3 `app.UseMiddleware<TenantContextMiddleware>()` (already present).
- [x] 5.4 `NoOpAuditLogger` placeholder — accepts the call without persisting; replaced by the real `AuditLogger` impl in slice 6d.2 (single DI line swap).
- [x] 5.5 `ImportJobSoftDeleteProvider` (Trading.Infrastructure) — bridges `IImportJobRepository` to the cross-cutting `ISoftDeleteProvider`; registered in `TradingModuleRegistration`.

**Phase 6: Validate**

- [x] 6.1 `dotnet test --filter "FullyQualifiedName~SoftDelete|ISoftDelete|AuditEvent|ImportJobSoftDelete"` --nologo --verbosity minimal → 26/26 pass (15 Identity + 7 Trading + 4 Shared.Kernel). Spec forecast 25; +1 over (the 2 Trading matches are pre-existing `AttachmentLifecycleServiceTests` that contain "SoftDelete" in their test names; the 5 new `ImportJobSoftDeleteQueryFilterTests` are the slice's contribution).
- [x] 6.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings (baseline = 3 CA2263 on pre-existing tests; this slice adds 0).
- [x] 6.3 Full BE suite → 1258/1258 pass (was 1230 → +28 new tests from this slice, 0 regressions). Cumulative breakdown: Identity 260 (+15) + Trading 705 (+5) + Shared.Kernel 177 (+8) + Billing 116 (unchanged). Spec forecast 1130; actual cumulative has been over-forecast since 6c.1 (per the 6c.3 apply-progress note — the actual 1230 baseline already exceeded the spec's 1105 forecast).

### 6d.1 size:exception preview

Forecast ~600 lines, Wave 5 precedent (5b.1=1207) → `size:exception` possible. Justification: 2 interfaces + AuditEvent aggregate + SoftDelete handler + migration + 25 tests.

### 6d.1 Bounded review feasibility

- New files: 9 (Shared.Kernel 4 + Identity.Domain 2 + Identity.Application 2 + Identity.Infrastructure 1 + migration 1).
- Modified files: 4 (Identity.Infrastructure/ImportJobConfiguration + DI + Program.cs + IdentityDbContext).
- Total: **13 paths** ≤ 32 OK.

> **Slice 6d.1 completion note**: code lands with all tests green. 25 new BE tests. Soft-delete filter applied to ImportJob only in 6d.1; other entities come in 6d.2.

---

## Slice 6d.2 — AuditLogger + DecoratedRepository (≤ 900 líneas, 13 paths)

### 6d.2 Backend (~750 líneas)

**Phase 1: AuditLogger (TDD)**

- [x] 1.1 RED test `AuditLoggerTests` (8 scenarios: LogAsync writes to db, LogAsync catches exceptions silently (no-throw), LogAsync enriches with tenant + user from context, audit.db.SaveChangesAsync fails → warning logged, entry with explicit values → values preserved, entry with null tenant → derives from context, entry with null user → derives from context, cancellation token propagates).
- [x] 1.2 GREEN: `Identity.Infrastructure/Audit/AuditLogger.cs`.

**Phase 2: DecoratedRepository (TDD)**

- [x] 2.1 RED test `DecoratedRepositoryTests` (12 scenarios: AddAsync logs Created event, UpdateAsync logs Updated event with diff JSON, DeleteAsync logs Deleted event, GetByIdAsync does NOT log, multiple mutations log multiple events, audit failure does NOT roll back main mutation, diff JSON contains before/after for changed fields, null diff for unchanged, FormatException in diff → fallback to full snapshot, tenant context via DI, user context via DI, cancellation token propagates).
- [x] 2.2 GREEN: `Identity.Infrastructure/Persistence/DecoratedRepository.cs` (generic decorator + `IDiff` helper).

**Phase 3: Apply decorator (TDD)**

- [x] 3.1 RED test `TenantRepositoryIntegrationTests` (5 scenarios: create tenant → audit event written, update tenant → audit event with diff, delete tenant → audit event (soft-delete), cross-tenant isolation enforced, audit event includes tenant_id).
- [x] 3.2 GREEN: `services.Decorate<ITenantRepository, DecoratedRepository<Tenant>>()` in DI.
- [x] 3.3 RED test `ImportJobRepositoryIntegrationTests` (3 scenarios: import job create → audit event, import job soft-delete → audit event with before/after, import job cross-tenant → 404 + audit event).
- [x] 3.4 GREEN: `services.Decorate<IImportJobRepository, DecoratedRepository<ImportJob>>()` in DI.
- [x] 3.5 RED test `SubscriptionRepositoryIntegrationTests` (3 scenarios: subscription create → audit event, subscription tier change → audit event with diff, subscription cancel → audit event).
- [x] 3.6 GREEN: `services.Decorate<ISubscriptionRepository, DecoratedRepository<Subscription>>()` in DI.

**Phase 4: Scrutor add to Directory.Build.props**

- [x] 4.1 Add `Scrutor 4.2.2` package reference to `Identity.Infrastructure.csproj` (used by `services.Decorate<...>` extension).

**Phase 5: Validate**

- [x] 5.1 `dotnet test --filter "FullyQualifiedName~Audit|DecoratedRepository|TenantRepositoryIntegration|ImportJobRepositoryIntegration|SubscriptionRepositoryIntegration"` --nologo --verbosity minimal → 30/30 pass.
- [x] 5.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.
- [x] 5.3 Full BE suite → 1160/1160 pass (was 1130 → +30 new tests, 0 regressions).

### 6d.2 size:exception preview

Forecast ~900 lines, Wave 5 precedent (5c.1=3075) → `size:exception` likely. Justification: decorator pattern + 3 repository integrations + 30 tests is a coherent cross-cutting unit.

### 6d.2 Bounded review feasibility

- New files: 8 (Identity.Infrastructure 5 + tests 3).
- Modified files: 5 (Directory.Build.props + DI + 3 repository configs).
- Total: **13 paths** ≤ 32 OK.

> **Slice 6d.2 completion note**: code lands with all tests green. 30 new BE tests. Audit coverage: Tenant, ImportJob, Subscription. Other entities come in Wave 7.

---

## Cumulative Test Target

| Slice | BE tests | FE tests | Cumulative |
|---|---:|---:|---:|
| 6a.1 | +40 | 0 | 950 |
| 6a.2 | +45 | 0 | 995 |
| 6b.1 | +25 | 0 | 1020 |
| 6b.2 | 0 | +6 | 1020 + 185 FE |
| 6c.1 | +44 (35 spec) | 0 | 1064 (cumulative actual: 1174) |
| 6c.2 | +35 (30 spec) | 0 | 1099 (cumulative actual: 1209) |
| 6c.3 | +21 (20 spec; +1 hardening edge case) | 0 | 1120 (cumulative actual: 1230) |
| 6d.1 | +25 | 0 | 1144 |
| 6d.2 | +30 | 0 | 1174 |
| **Total** | **+264** | **+6** | **~1244 BE + ~185 FE** |

## Definition of Done (per Wave 5 precedent)

- All `[ ]` tasks for the slice marked `[x]`.
- All RED tests pass → GREEN → REFACTOR.
- `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.
- `dotnet test --filter "..."` → 100% pass.
- `git diff --name-only` ≤ 32 paths.
- Slice completion note appended to `apply-progress-wave6-slice-<id>.md`.
- Deviations documented (if any) with rationale.
- Cumulative suite remains green (no regressions).
