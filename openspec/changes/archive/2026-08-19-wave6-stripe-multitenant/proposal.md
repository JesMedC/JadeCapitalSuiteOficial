# Proposal: Wave 6 — Stripe Real + Billing Portal + Multi-Tenant + Soft-Delete & Audit

## Intent and Problem

Wave 0-5 delivered a working trader operating system: identity, billing admin, trading CRUD, risk, journal, alerts, scanner, realtime, importers, and AI coaching + advisory. What remains is the **commercial + governance layer** that turns the product into a multi-tenant SaaS:

1. **Stripe is mocked**. `Billing.Infrastructure` declares `Stripe.net 47.0.0` but has zero references. `billing.subscriptions` is populated by Admin (or seeded), not by Stripe. We cannot accept real payments, cannot react to `customer.subscription.deleted`, and cannot honor trials or dunning. Wave 6a plugs the real Stripe API in behind a testable interface (`IStripeGateway`).
2. **The user cannot self-manage**. The only way to cancel today is Admin. Users need a `Billing Portal` where they see their plan, payment methods, invoices, and click "Manage in Stripe" to update card / cancel. Wave 6b delivers this.
3. **No tenant isolation**. Single hard-coded namespace. Wave 6c introduces `tenant_id` on every user-owned table, a `Tenant` aggregate, repository-level query filters, and a JWT-derived `ITenantContext`.
4. **No audit trail + soft-delete story**. Every `DELETE` is a hard delete. Compliance footprint is zero. Wave 6d adds `ISoftDelete` (with EF global query filter), an `AuditEvent` write-only aggregate, and a `DecoratedRepository<T>` that logs every Create/Update/Delete.

Without Wave 6 the product is a trader demo, not a SaaS. With Wave 6, JadeCapital can onboard real customers, charge them through Stripe, scale to multiple law-firms / prop firms / signal services (tenants), and produce a defensible audit trail.

## Goals

- **6a Stripe real**: Real Stripe SDK integration with `IStripeGateway` testable seam. Customer creation (idempotent), Checkout session creation, Customer Portal redirect, webhook signature verification, subscription state sync from `customer.subscription.*` events. Mocked in tests; live in prod once `Stripe__ApiKey` env var is set.
- **6b Billing portal**: Self-service read API (`GET /api/billing/portal/subscription|payment-methods|invoices`) + Angular page. "Manage in Stripe" button delegates to Stripe Customer Portal.
- **6c Multi-tenant**: `Tenant` aggregate (`id`, `name`, `slug`, `owner_user_id`, `plan`, `created_at`). `tenant_id` FK on every user-owned table. `ITenantContext` resolved from JWT. `Repository<T>` query filter. Tenant admin endpoints (list/invite/remove users).
- **6d Soft-delete + audit**: `ISoftDelete` interface on all deletable aggregates. EF global query filter. `AuditEvent` (`entity_type`, `entity_id`, `action`, `user_id`, `tenant_id`, `timestamp`, `changes JSONB`). Decorator wraps `IRepository<T>` writes to log every mutation.

## Scope Boundaries

### In Scope

| Capability | Deliverable | Slices |
|---|---|---|
| `stripe` | Stripe SDK integration + Customer create + Checkout + Portal + Webhooks + Subscription sync | 6a.1, 6a.2 |
| `billing-portal` | Self-service billing API + Angular portal page | 6b.1, 6b.2 |
| `multi-tenant` | Tenant aggregate + tenant_id migration + query isolation + tenant admin endpoints | 6c.1, 6c.2, 6c.3 |
| `soft-delete-audit` | `ISoftDelete` + EF query filter + `AuditEvent` + `DecoratedRepository` | 6d.1, 6d.2 |

### Out of Scope (deferred to Wave 7+)

- Multi-tenant billing (each tenant has its own Stripe account) — Wave 7.
- Stripe Connect / marketplace (split payments) — Wave 7+.
- Tenant-level RBAC (roles per tenant, not just global) — Wave 7.
- Audit log retention policy + auto-purge (90-day retention job) — Wave 7.
- Audit log query UI for admins — Wave 7.
- Per-tenant rate limits (Redis token bucket per tenant) — Wave 7.
- Stripe Tax / Stripe Terminal — Wave 7+.
- Stripe Invoicing one-off (only subscription invoices in Wave 6) — Wave 8.
- Tenant impersonation (login-as) — Wave 8 (requires heavy audit).
- Backfill of historical `tenant_id` for users created BEFORE Wave 6 — done in 6c.2 with a one-shot idempotent script.

## Capabilities (new)

- **`stripe`** — `IStripeGateway` interface in `Shared.Kernel/Billing/Stripe` (testable seam). `StripeGateway` impl in `Billing.Infrastructure` using `Stripe.net 47.0.0`. `StripeCustomer` aggregate (`Billing.Domain/Stripe/StripeCustomer.cs`). `CheckoutSession` projection. Webhook signature verify via `StripeEventUtility`. Migrations 0022 (`billing.stripe_customers`) + 0023 (`billing.stripe_checkout_sessions`). `POST /api/billing/stripe/customers` + `POST /api/billing/stripe/checkout` + `POST /api/billing/stripe/portal` + `POST /api/billing/stripe/webhooks`.
- **`billing-portal`** — `GET /api/billing/portal/subscription` + `GET /api/billing/portal/payment-methods` + `GET /api/billing/portal/invoices`. All require authenticated user; return only the caller's own data. `billing-portal-page.ts` standalone Signals OnPush SCSS Angular 19 page.
- **`multi-tenant`** — `Tenant` aggregate (`Identity.Domain/Tenants/Tenant.cs`). `TenantId` value object. `ITenantContext` interface in `Shared.Kernel/MultiTenancy`. `TenantContextMiddleware` resolves tenant from JWT `tenant_id` claim. `Repository<T>` extension or global query filter. Migrations 0024 (`identity.tenants`) + 0025 (`ALTER TABLE identity.users ADD COLUMN tenant_id` + FK) + 0026 (backfill script). `CreateTenantCommand` + `CreateTenantHandler`. `GET /api/tenants/{id}/users` + `POST /api/tenants/{id}/users` + `DELETE /api/tenants/{id}/users/{user_id}` + `PATCH /api/tenants/{id}`.
- **`soft-delete-audit`** — `ISoftDelete` interface in `Shared.Kernel/SoftDelete` (`IsDeleted`, `DeletedAt`, `DeletedBy` properties). EF `HasQueryFilter` on all `ISoftDelete` entities. `AuditEvent` aggregate (`Identity.Domain/Audit/AuditEvent.cs`). `IAuditLogger` interface. `AuditLogger` impl (writes to `audit.events`). `DecoratedRepository<T>` decorator pattern wrapping `IRepository<T>` to log Create/Update/Delete. Migration 0027 (`audit.events`). New exception `not_found` when soft-deleted entity is queried.

## Capabilities (modified)

- **`subscription-administration`** (Wave 0) — `billing.subscriptions` rows now ALSO can be mutated by webhook events (`customer.subscription.created|updated|deleted`). The optimistic-concurrency guard stays (Webhook handles `version_conflict` with retry-3 with jitter). History append on every webhook-driven change.
- **`identity`** — `identity.users` gains `tenant_id UUID` nullable column (FK `identity.tenants.id`). Backfill script assigns existing users to a default `Personal` tenant. New login flow also stamps `tenant_id` into the JWT.

## Ownership and Approach

- **Shared.Kernel** owns:
  - `Billing/Stripe/IStripeGateway.cs` (testable seam): `CreateOrGetCustomerAsync`, `CreateCheckoutSessionAsync`, `CreatePortalSessionAsync`, `VerifyWebhookAsync`, `GetSubscriptionAsync`, `GetPaymentMethodsAsync`, `GetInvoicesAsync`.
  - `MultiTenancy/ITenantContext.cs` (`TenantId Current { get; }`, `UserId CurrentUser { get; }`).
  - `MultiTenancy/TenantId.cs` (strongly-typed Guid wrapper).
  - `SoftDelete/ISoftDelete.cs` interface.
  - `SoftDelete/IAuditLogger.cs` interface.
  - `Exceptions/NotFoundException.cs` (added to the existing `Exceptions/` namespace).
- **Identity.Domain** owns:
  - `Tenants/Tenant.cs` (aggregate root).
  - `Tenants/TenantErrors.cs` (error catalog).
  - `Tenants/TenantStatus.cs` (Active=0, Suspended=1, Archived=2).
  - `Tenants/Events/TenantCreatedDomainEvent.cs`.
  - `Audit/AuditEvent.cs` (aggregate root — append-only, no mutators).
  - `Audit/AuditAction.cs` (Created=0, Updated=1, Deleted=2, Restored=3).
- **Identity.Application** owns:
  - `Tenants/CreateTenantCommand` + `CreateTenantHandler`.
  - `Tenants/GetTenantQuery` + `GetTenantHandler`.
  - `Tenants/UpdateTenantCommand` + `UpdateTenantHandler`.
  - `Tenants/ListTenantUsersQuery` + `ListTenantUsersHandler`.
  - `Tenants/InviteTenantUserCommand` + `InviteTenantUserHandler`.
  - `Tenants/RemoveTenantUserCommand` + `RemoveTenantUserHandler`.
  - `Tenants/ITenantRepository.cs` interface.
  - `Abstractions/IDecoratedRepository.cs` (silent write hook into audit log).
- **Identity.Infrastructure** owns:
  - `Tenants/TenantRepository.cs` (EF).
  - `Persistence/Configurations/TenantConfiguration.cs`.
  - `Persistence/Configurations/UserConfiguration.cs` (additive `tenant_id` mapping).
  - `Audit/AuditLogger.cs` (EF impl — writes to `audit.events`).
  - `Persistence/DecoratedRepository.cs` (generic decorator that wraps any `IRepository<T>`).
  - `SoftDelete/SoftDeleteCommand.cs` + `SoftDeleteHandler.cs` (sets `IsDeleted`, `DeletedAt`, `DeletedBy` — does NOT delete).
  - `MultiTenancy/TenantContext.cs` (HttpContext-bound impl).
  - `MultiTenancy/TenantContextMiddleware.cs` (resolves from JWT claim).
  - `MultiTenancy/BackfillTenantsHostedService.cs` (one-shot idle-after-startup backfill — idempotent).
- **Billing.Domain** owns:
  - `Stripe/StripeCustomer.cs` (aggregate root).
  - `Stripe/StripeCustomerErrors.cs`.
  - `Stripe/StripeCheckoutSession.cs` (projection; not aggregate — read-only).
  - `Stripe/WebhookEvent.cs` (append-only log of received webhook payloads).
- **Billing.Application** owns:
  - `Stripe/CreateOrGetCustomerCommand` + `CreateOrGetCustomerHandler`.
  - `Stripe/CreateCheckoutSessionCommand` + `CreateCheckoutSessionHandler`.
  - `Stripe/CreatePortalSessionCommand` + `CreatePortalSessionHandler`.
  - `Stripe/HandleWebhookCommand` + `HandleWebhookHandler`.
  - `Stripe/GetSubscriptionQuery` + `GetSubscriptionHandler`.
  - `Stripe/GetPaymentMethodsQuery` + `GetPaymentMethodsHandler`.
  - `Stripe/GetInvoicesQuery` + `GetInvoicesHandler`.
  - `Abstractions/IStripeGateway.cs` (re-export of Shared.Kernel interface for module isolation).
  - `Abstractions/IStripeCustomerRepository.cs` + `IStripeWebhookEventRepository.cs`.
- **Billing.Infrastructure** owns:
  - `Stripe/StripeGateway.cs` (real impl using `Stripe.net 47.0.0`).
  - `Stripe/StubStripeGateway.cs` (test/dev fallback when `Stripe__ApiKey` env is absent — returns synthetic responses).
  - `Stripe/StripeOptions.cs` (`ApiKey`, `WebhookSecret`, `DefaultPriceId`, `CustomerPortalConfigurationId`).
  - `Persistence/StripeCustomerRepository.cs` (EF).
  - `Persistence/WebhookEventRepository.cs` (EF append-only).
  - `Persistence/Configurations/StripeCustomerConfiguration.cs` + `WebhookEventConfiguration.cs`.
- **Identity.Api** owns:
  - `Endpoints/TenantEndpoints.cs` (4 endpoints).
  - DI: `AddIdentityInfrastructure` + `MapTenantEndpoints`.
- **Billing.PublicApi** owns:
  - `Extensions/BillingStripeEndpoints.cs` (4 endpoints).
  - `Extensions/BillingPortalEndpoints.cs` (3 endpoints).
  - DI: `AddBillingStripe` + `MapBillingStripeEndpoints` + `MapBillingPortalEndpoints`.
- **Frontend** owns:
  - `features/trader/billing/billing-portal-page.ts` (current plan + payment methods + invoices + "Manage in Stripe" button).
  - `features/trader/billing/api/billing-portal.service.ts` (3 HTTP wrappers).
  - `features/trader/billing/api/billing-portal.types.ts` (DTOs).
  - `features/trader/billing/state/billing-portal.state.ts` (Signals).
  - `features/trader/billing/billing.routes.ts` (sub-route).
  - `features/trader/trader.routes.ts` (add `billing` lazy route).
  - `features/trader/trader-shell.ts` (add `Billing` nav entry — 12 items total).

## Architectural Decisions

- **`IStripeGateway` in Shared.Kernel, impl in Billing.Infrastructure**: mirrors `IQuoteProvider` (Wave 4b) and `IAIProvider` (Wave 5b) precedent. The wire shapes are cross-module stable. Tests mock `IStripeGateway`; the real `StripeGateway` is only registered when `Stripe__ApiKey` env is present, otherwise a `StubStripeGateway` returns synthetic responses (so dev / CI without a Stripe key still works).
- **Webhook handler is signature-verified AND idempotent**: every webhook event is logged in `billing.stripe_webhook_events` (append-only). Re-delivery of the same `event_id` is a no-op (returns 200 without re-mutating). No retries on persistent failures (Stripe will retry on non-2xx).
- **Webhook path is `AllowAnonymous` but POST-only**: CSRF is moot because there is no user session. The signature header (`Stripe-Signature`) is the auth. Reject any request without a valid signature with 401.
- **Multi-tenant via query filter, not via separate schemas**: Wave 6c takes the simpler path of `tenant_id` column + EF query filter. Schema-per-tenant is faster to deploy but harder to migrate. Schema-per-tenant is Wave 8+ if needed.
- **`Tenant` belongs to Identity, not a new `Tenants` module**: Identity already owns `user` aggregate and JWT issuance. Adding `Tenant` here keeps the bounded context tight. The cost is a tighter coupling between Identity and tenant lifecycle — acceptable for Wave 6.
- **Tenant from JWT claim, not from request header**: The JWT carries `tenant_id`. The `TenantContextMiddleware` reads the claim and sets `ITenantContext.Current`. No client-side header spoofing possible. Refresh tokens must include the `tenant_id` claim (mint-time fix; existing tokens expire naturally).
- **`ISoftDelete` is opt-in per aggregate**: not all aggregates are soft-deleteable (e.g., `AuditEvent`, `StripeWebhookEvent` are append-only and never deleted). The interface is applied only where business rules allow deletion.
- **Audit log is `INSERT`-only**: no `UPDATE` or `DELETE` on `audit.events`. EF is configured to prevent it. This is the compliance guarantee.
- **Audit decorator wraps `IRepository<T>`**: but ONLY the Application-layer `AddAsync` / `UpdateAsync` / `DeleteAsync` — not raw EF queries. This guarantees coverage where the aggregates are mutated, while staying non-intrusive on legacy code paths that call `DbContext` directly.
- **Soft-delete exceptions DON'T trigger 404**: a soft-deleted entity appears as `not_found` to the caller (returned by handler), but the audit log records the attempt with `FoundDeleted = true`. This is the audit-vs-404 tradeoff.
- **Default tenant `Personal` is auto-created on startup**: `BackfillTenantsHostedService` runs idle-after-startup, creates `Personal` tenant if absent, assigns all users with `tenant_id IS NULL` to it. Idempotent — safe across restarts.
- **`size:exception` forecast per slice**: Wave 5 precedent (5a.1=2108, 5a.2=1134, 5b.1=1207, 5b.2=2989, 5c.1=3075, 5c.2=971) means `size:exception` is the default. Wave 6 slices aim for ≤ 1000 lines but expect `size:exception` per slice.

## Chained Delivery, Validation, and Rollback

| Slice | Deliverable | Bounded review | Validate | Rollback |
|---|---|---|---|---|
| **6a.1** | `IStripeGateway` interface + `StripeGateway` impl + `StubStripeGateway` + `StripeCustomer` aggregate + migration 0022 + `POST /api/billing/stripe/customers` + `POST /api/billing/stripe/webhooks` (signature verify + log) + `StripeOptions` + ~15 tests | ≤ 12 paths | `dotnet test --filter "FullyQualifiedName~Stripe"` | Revert code; `billing.stripe_customers` table inerte |
| **6a.2** | `CheckoutSession` projection + `POST /api/billing/stripe/checkout` + `POST /api/billing/stripe/portal` + webhook handler for `customer.subscription.*` events + subscription sync + migration 0023 + ~15 tests | ≤ 14 paths | `dotnet test --filter "FullyQualifiedName~StripeCheckout\|StripeSubscription"` | Revert code; webhook events append-only stays |
| **6b.1** | `GET /api/billing/portal/subscription` + `GET /api/billing/portal/payment-methods` + `GET /api/billing/portal/invoices` + 3 query handlers + ~10 tests | ≤ 8 paths | `dotnet test --filter "FullyQualifiedName~BillingPortal"` | Revert code; route removida |
| **6b.2** | `billing-portal-page.ts` + service + state + types + nav entry + ~6 jest specs | ≤ 6 paths | `npm test -- --testPathPattern=billing-portal` | Revert FE; route removida |
| **6c.1** | `Tenant` aggregate + `TenantId` VO + `ITenantContext` interface + `CreateTenantCommand/Handler` + migration 0024 + migration 0025 (`identity.users.tenant_id`) + ~12 tests | ≤ 10 paths | `dotnet test --filter "FullyQualifiedName~Tenant"` | Revert code; tablas inertes |
| **6c.2** | `TenantContextMiddleware` + `Repository<T>` query filter + `BackfillTenantsHostedService` + migration 0026 (backfill script) + `tenant_id` stamping in JWT + ~12 tests | ≤ 12 paths | `dotnet test --filter "FullyQualifiedName~TenantContext\|TenantFilter"` | Revert code; middleware removida; tenants quedan |
| **6c.3** | `GET /api/tenants/{id}/users` + `POST /api/tenants/{id}/users` + `DELETE /api/tenants/{id}/users/{user_id}` + `PATCH /api/tenants/{id}` + 4 query/command handlers + ~10 tests | ≤ 8 paths | `dotnet test --filter "FullyQualifiedName~TenantAdmin"` | Revert code; endpoints removidas |
| **6d.1** | `ISoftDelete` interface + `IAuditLogger` interface + `AuditEvent` aggregate + EF global query filter on `ISoftDelete` + `SoftDeleteCommand/Handler` + migration 0027 + ~10 tests | ≤ 8 paths | `dotnet test --filter "FullyQualifiedName~SoftDelete\|ISoftDelete"` | Revert code; tabla inerte |
| **6d.2** | `AuditLogger` impl + `DecoratedRepository<T>` (wraps Create/Update/Delete) + apply decorator to `ImportJobRepository` + `TenantRepository` + `SubscriptionRepository` + ~12 tests | ≤ 12 paths | `dotnet test --filter "FullyQualifiedName~Audit\|DecoratedRepository"` | Revert code; decorator chain unwinds |

**Chain strategy**: `feature-branch-chain`. 9 slices total. Each PR base = previous PR branch (per Wave 5 precedent). PR #1 targets `feature/0a-identity-model` (since Wave 5 PR #5 merged there); PR #2-9 target the previous PR branch.

Size forecast (mirror Wave 5 precedent; tests = ~50% of diff):

| Slice | Forecast | Paths | Bounded review | Risk |
|---|---:|---:|---|---|
| 6a.1 | ~600 | 12 | OK | low — interface + SDK + 1 endpoint |
| 6a.2 | ~800 | 14 | OK | medium — webhook event handling |
| 6b.1 | ~500 | 8 | OK | low — read-only API |
| 6b.2 | ~400 | 6 | OK | low — standalone FE page |
| 6c.1 | ~700 | 10 | OK | medium — migration on existing users table |
| 6c.2 | ~900 | 12 | OK | high — query filter is cross-cutting |
| 6c.3 | ~500 | 8 | OK | low — admin endpoints |
| 6d.1 | ~600 | 8 | OK | medium — global query filter |
| 6d.2 | ~900 | 12 | OK | high — decorator pattern is cross-cutting |
| **Total** | **~5,900** | — | All ≤ 32 paths | — |

**Per-PR `size:exception` precedent (Wave 5)**: ALL 5 of 6 Wave 5 slices used `size:exception` (5a.1=2108, 5a.2=1134, 5b.1=1207, 5b.2=2989, 5c.1=3075, 5c.2=971). Wave 6 follows the same model — expect `size:exception` per slice.

## Critical Questions for User (must be resolved before 6a.1 / 6c.1 / 6d.1 apply)

1. **Stripe API version**: Use Stripe's default pinned version `2025-XX-acacia` (Stripe.net 47.0.0 default), or pin to a specific date like `2025-08-13`? **Default**: `Stripe.net` library default; **Safer**: explicit pin via `StripeConfiguration.ApiVersion = "2025-08-13"`.
2. **Tenant migration strategy**: Push `tenant_id` to all user-owned tables in ONE migration (atomic), or incrementally (one module per slice)? **Recommended**: One migration that adds `tenant_id` to all user-owned tables + nullable + backfill in 6c.2 + NOT NULL in 6c.3 (after backfill confirmed). **Alternative**: One module per slice (Identity → Trading → Billing → Admin).
3. **Audit log visibility**: Should the audit log be readable by the user (e.g., "show my action history" at `GET /api/audit/me`)? Or admin-only (Wave 7+)? **Recommended**: Admin-only in Wave 6; user-internal "my actions" tab is Wave 7. The audit log is for compliance, not user-facing history.

## Migration Wire-Up

Migrations 0022-0027 are idempotent and additive:
- `0022_stripe_customers.sql` — `billing.stripe_customers` (1 new table + 1 index).
- `0023_stripe_webhook_events.sql` — `billing.stripe_webhook_events` (1 new table + 1 index on `event_id`).
- `0024_tenants.sql` — `identity.tenants` (1 new table + 2 indexes).
- `0025_users_tenant_id.sql` — `ALTER TABLE identity.users ADD COLUMN tenant_id UUID` + FK.
- `0026_backfill_personal_tenant.sql` — Idempotent backfill: INSERT `Personal` tenant if absent + UPDATE `users SET tenant_id = ...` WHERE NULL.
- `0027_audit_events.sql` — `audit.events` table (1 new + 1 index on `entity_type, entity_id`).
- All wire en `infrastructure/postgres/migrate.Dockerfile` happy + retry path with `\\\"` escape.

## Dependencies and Risks

- **Stripe.net 47.0.0 already declared** (first usage in Wave 6). The package is fresh — no prior accident waiting. Risk: API version drift. Mitigation: `StripeConfiguration.ApiVersion` set explicitly + CI test that the version string is `"2025-08-13"` (or whatever the user picks).
- **Webhook signature requires raw body**: ASP.NET Core buffers request bodies by default for form-urlencoded but NOT for JSON. The webhook endpoint MUST read the raw body via `Request.EnableBuffering()` BEFORE model binding. Test must assert this.
- **Multi-tenant migration on existing users table** (6c.1): `ALTER TABLE identity.users ADD COLUMN tenant_id UUID` is fast (no rewrite in PG 11+). Risk: lock contention during deploy. Mitigation: `ALTER TABLE ... ADD COLUMN ... DEFAULT NULL` (no NOT NULL constraint initially) → zero locking. NOT NULL constraint lands in 6c.3 after backfill confirmed.
- **Tenant from JWT claim requires re-login**: existing JWTs do NOT carry `tenant_id`. Users must re-login after deploy to get the new claim. Acceptable for a SaaS whose users log in daily. Mitigation: include `tenant_id` in the mint-time payload; existing tokens expire naturally (15 min) and refresh tokens carry the new claim.
- **Global query filter (6c.2 + 6d.1)**: Both introduce `HasQueryFilter` on multiple entities. EF Core requires the filter to be a property of the entity type. Wave 6c.2 uses `tenant_id == ITenantContext.Current` + `IsDeleted == false` (composed). Risk: filters compose; one buggy filter silently hides data. Mitigation: tests use an explicit `IgnoreQueryFilters()` override in test fixtures that need to see soft-deleted rows.
- **DecoratedRepository coverage** (6d.2): the decorator only wraps the `IRepository<T>` interface. Legacy code that calls `DbContext` directly to mutate rows is NOT audited. Mitigation: write a test that scans for `DbContext.Set<T>.Add|Update|Remove` calls and asserts each one originates in a Repository (a static analyzer rule + a code review checklist).
- **Ollama precedent for cross-cutting failure**: 5b.2/5c.1 lessons — defense-in-depth at every external boundary. Wave 6a adds `try/catch` + safe defaults around every Stripe SDK call. Wave 6c adds `try/catch` around the JWT claim parse (user with malformed tenant_id → 403, not 500).
- **Audit-event row explosion**: every mutation writes one row. A trader importing 1000 rows creates 1000 `audit.events` rows. Mitigation: bulk operations (`AddRange`) emit ONE audit event with `entity_count = 1000` and `entity_ids = [...]`. This is the only deviation from "1 row per mutation" rule.
- **Soft-delete exceptions on PATCH/DELETE**: if a user PATCHes a soft-deleted entity, the handler returns `not_found` (404). The audit log captures the attempt with `FoundDeleted = true`. The 404 is correct from the user's POV (the entity is gone); the audit log captures the attempt for compliance.

## Success Criteria

1. Trader calls `POST /api/billing/stripe/customers` → `StripeGateway.CreateOrGetCustomerAsync` succeeds (stub or real), returns Stripe Customer id, persists in `billing.stripe_customers`. Same call twice with same user returns same Stripe Customer id (idempotent).
2. Trader calls `POST /api/billing/stripe/checkout` with `priceId` → returns a Stripe Checkout URL. Trader navigates → completes payment → Stripe fires `customer.subscription.created` webhook → `HandleWebhookHandler` syncs the subscription to `billing.subscriptions` (preserves optimistic-concurrency via `version`).
3. Trader calls `POST /api/billing/stripe/webhooks` with a raw JSON body + `Stripe-Signature` header → `VerifyWebhookAsync` returns the event → handler logs in `billing.stripe_webhook_events` → handler dispatches on `event.type`. Bad signature → 401. Re-delivery of same `event_id` → 200 no-op.
4. Trader visits `/app/billing` → sees `{plan, status, nextBillingDate, paymentMethods: [...], invoices: [...]}`. "Manage in Stripe" button calls `POST /api/billing/stripe/portal` → returns Stripe Portal URL → redirect.
5. New user signs up → `CreateTenantHandler` creates `Personal` tenant + assigns user as owner. `identity.users.tenant_id` is set. JWT carries `tenant_id` claim. Subsequent API calls succeed; `tenant_id` resolves consistently.
6. Existing users (pre-Wave 6 backfill) → `BackfillTenantsHostedService` runs on startup → creates `Personal` tenant if absent → assigns all `tenant_id IS NULL` users to `Personal`. Idempotent re-run → no-op.
7. Admin calls `GET /api/tenants/{id}/users` → returns users in tenant. Admin calls `POST /api/tenants/{id}/users` with `email` → user is invited (mock invite in Wave 6, real email Wave 7). Admin calls `DELETE /api/tenants/{id}/users/{user_id}` → user is removed from tenant.
8. Trader calls `DELETE /api/imports/{id}` (any entity owning `ISoftDelete`) → entity is marked `IsDeleted = true`, `DeletedAt = now()`, `DeletedBy = caller`. Subsequent `GET` returns 404. `audit.events` row written with `action = "Deleted"`, `changes = { "IsDeleted": { "before": false, "after": true } }`.
9. Audit log query: `SELECT * FROM audit.events WHERE entity_type = 'trading.import_jobs' AND entity_id = 'J1'` returns the full history of mutations on J1, newest first.
10. Build green, 0 warnings nuevos, 0 regressions en los ~910 BE tests + ~163 FE tests existentes. Cumulative: ~1,070-1,100 BE tests + ~190-200 FE tests.
11. All 9 slices under 32 paths (mandatory). Expect `size:exception` per slice (Wave 4/5 precedent).

## Non-Goals and Later Waves

- NO multi-tenant Stripe (each tenant has its own Stripe account) — Wave 7.
- NO Stripe Connect / marketplace — Wave 7+.
- NO per-tenant RBAC — Wave 7.
- NO audit log retention policy + auto-purge — Wave 7.
- NO audit log query UI for admins — Wave 7.
- NO Stripe Tax / Stripe Terminal — Wave 7+.
- NO tenant impersonation — Wave 8 (requires heavy audit).
- NO AI signal generation from scanner results (deferred from Wave 5) — Wave 7.
- NO live broker integration (IBKR, MT5 native) — Wave 7.

## Critical Questions for User (must be resolved before 6a.1 / 6c.1 / 6d.1 apply)

1. **Stripe API version**: Use Stripe.net 47.0.0 default `2025-XX-acacia` or pin to a specific date `2025-08-13`? **Default**: `Stripe.net` library default; **Safer**: explicit pin.
2. **Tenant migration strategy**: All user-owned tables in ONE migration (atomic) OR incremental module-by-module (Identity → Trading → Billing)? **Recommended**: One migration with nullable + backfill + NOT NULL in 6c.3.
3. **Audit log visibility**: User-readable (`GET /api/audit/me`) OR admin-only (Wave 7+)? **Recommended**: Admin-only in Wave 6; user-facing history tab is Wave 7.
