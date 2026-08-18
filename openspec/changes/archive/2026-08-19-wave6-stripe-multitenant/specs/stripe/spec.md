# Stripe Specification

## Purpose

Plugs real Stripe into the Billing module behind a testable interface. The system MUST integrate Stripe.net 47.0.0 for managing Customers, Checkout sessions, Customer Portal sessions, and webhook event handling. The integration is hidden behind `IStripeGateway` so the rest of the codebase never references the Stripe SDK directly. When `Stripe__ApiKey` env is absent, a `StubStripeGateway` returns synthetic responses so dev / CI without a Stripe key still works.

This spec covers the gateway contract, the Customer aggregate, the Checkout/Portal session flow, the webhook signature verification + idempotency + subscription sync, and the JSON-shape contracts. It does NOT cover subscription administration (covered by `subscription-administration` spec), Stripe Tax, Stripe Connect, or Stripe Terminal.

## Requirements

### Requirement: Stripe gateway contract

The system MUST define `IStripeGateway` in `Shared.Kernel.Billing.Stripe` with seven members: `CreateOrGetCustomerAsync`, `CreateCheckoutSessionAsync`, `CreatePortalSessionAsync`, `VerifyWebhookAsync`, `GetSubscriptionAsync`, `GetPaymentMethodsAsync`, `GetInvoicesAsync`. Every method MUST return `Result<T>` (never throw on transient failures) and MUST accept a `CancellationToken` as the last parameter. The implementation MUST map every Stripe.NET exception to a `StripeError` with a `code` of `stripe.api_error`, `stripe.authentication_error`, `stripe.rate_limit_error`, `stripe.invalid_request`, `stripe.internal_error`, or `stripe.unavailable`.

#### Scenario: Create customer for new user

- GIVEN a user with no existing Stripe Customer mapping
- WHEN `CreateOrGetCustomerAsync(userId, email, displayName)` is called
- THEN the gateway MUST call Stripe `CustomerService.CreateAsync` with `email`, `name`, and `metadata.user_id = userId`
- AND persist the returned `stripe_customer_id` in `billing.stripe_customers`
- AND return `Result.Success(StripeCustomerDto(stripeCustomerId, email, displayName, now))`

#### Scenario: Idempotent customer creation

- GIVEN a user with `billing.stripe_customers.user_id = U` already exists
- WHEN `CreateOrGetCustomerAsync(U, email, displayName)` is called again
- THEN the gateway MUST query `customers.list(email = email)` first
- AND if a Customer with the same email exists, return it without creating a new one
- AND the persisted `billing.stripe_customers` row count MUST remain 1

#### Scenario: Stripe API error

- GIVEN Stripe returns `401 Unauthorized` (invalid API key)
- WHEN `CreateOrGetCustomerAsync` is called
- THEN the gateway MUST return `Result.Failure(StripeError("stripe.authentication_error", "..."))`
- AND the HTTP endpoint MUST return 502 Bad Gateway
- AND no `billing.stripe_customers` row MUST be written

### Requirement: Customer aggregate

`StripeCustomer` MUST be a `Billing.Domain.Stripe` aggregate with `Id, UserId, StripeCustomerId, Email, DisplayName, CreatedAt`. The aggregate MUST enforce invariants: `UserId != Guid.Empty`, `StripeCustomerId` is non-empty and begins with `cus_`, `Email` matches a basic format validation. The aggregate is immutable after `Create` (no mutators).

#### Scenario: Create valid customer

- GIVEN a valid user id, Stripe customer id, email, displayName
- WHEN the `StripeCustomer.Create` factory runs
- THEN the result MUST be `Result.Success`
- AND the aggregate's `CreatedAt` MUST be set to the current UTC time

#### Scenario: Invalid Stripe customer id

- GIVEN a `stripeCustomerId` that does not begin with `cus_`
- WHEN the factory runs
- THEN the result MUST be `Result.Failure(StripeCustomerErrors.InvalidStripeCustomerId)`

### Requirement: Stripe checkout session

The system MUST provide `POST /api/billing/stripe/checkout` that creates a Stripe Checkout session for a subscription. The endpoint accepts `{ priceId: string, successUrl: string, cancelUrl: string }`, validates the URLs are HTTPS (or HTTP for localhost dev), and returns `{ sessionId, url, expiresAt }`. The caller (FE) redirects to the returned URL.

#### Scenario: Successful checkout session creation

- GIVEN an authenticated user with a Stripe Customer mapping
- WHEN the user calls `POST /api/billing/stripe/checkout` with `{ priceId: "price_123", successUrl: "https://app.example.com/billing/success", cancelUrl: "https://app.example.com/billing/cancel" }`
- THEN the gateway MUST call `Checkout.SessionService.CreateAsync` with `customer=stripeCustomerId, mode=subscription, line_items=[{price: priceId, quantity: 1}]`, `success_url`, `cancel_url`
- AND the endpoint MUST return 200 with `{ sessionId: "cs_test_...", url: "https://checkout.stripe.com/...", expiresAt: "..." }`

#### Scenario: User has no Stripe Customer

- GIVEN an authenticated user with no `billing.stripe_customers` row
- WHEN the user calls `POST /api/billing/stripe/checkout`
- THEN the handler MUST call `CreateOrGetCustomerAsync` first
- AND then proceed with the checkout session creation

### Requirement: Stripe Customer Portal session

The system MUST provide `POST /api/billing/stripe/portal` that creates a Stripe Customer Portal session for the authenticated user. The endpoint accepts `{ returnUrl: string }` and returns `{ sessionId, url, expiresAt }`. The caller (FE) redirects to the returned URL.

#### Scenario: Successful portal session creation

- GIVEN an authenticated user with a Stripe Customer mapping
- WHEN the user calls `POST /api/billing/stripe/portal` with `{ returnUrl: "https://app.example.com/billing" }`
- THEN the gateway MUST call `BillingPortal.SessionService.CreateAsync` with `customer=stripeCustomerId, return_url=returnUrl`
- AND the endpoint MUST return 200 with `{ sessionId: "ps_test_...", url: "https://billing.stripe.com/...", expiresAt: "..." }`

#### Scenario: User has no Stripe Customer

- GIVEN an authenticated user with no `billing.stripe_customers` row
- WHEN the user calls `POST /api/billing/stripe/portal`
- THEN the endpoint MUST return 404 with `error.code = "stripe.customer_not_found"`

### Requirement: Webhook signature verification

The system MUST provide `POST /api/billing/stripe/webhooks` (anonymous) that verifies the Stripe signature header against the request body using `Stripe.EventUtility.ConstructEvent(payload, signatureHeader, webhookSecret)`. On invalid signature, the endpoint MUST return 401 with `error.code = "stripe.signature_invalid"` and MUST NOT log the payload to Serilog (avoid PII leak). The endpoint MUST read the raw body via `Request.EnableBuffering()` + `Body.CopyToAsync` BEFORE model binding — JSON model binding would corrupt the payload.

#### Scenario: Valid webhook signature

- GIVEN a Stripe webhook payload with a valid `Stripe-Signature` header
- WHEN the endpoint receives the request
- THEN `VerifyWebhookAsync` MUST return the parsed `Stripe.Event`
- AND the handler MUST proceed to idempotency + dispatch

#### Scenario: Invalid webhook signature

- GIVEN a Stripe webhook payload with a malformed `Stripe-Signature` header
- WHEN the endpoint receives the request
- THEN the endpoint MUST return 401
- AND no `billing.stripe_webhook_events` row MUST be inserted
- AND the payload MUST NOT be logged to Serilog

#### Scenario: Missing webhook signature

- GIVEN a request without a `Stripe-Signature` header
- WHEN the endpoint receives the request
- THEN the endpoint MUST return 401 with `error.code = "stripe.signature_missing"`

### Requirement: Webhook idempotency

The webhook handler MUST check `billing.stripe_webhook_events.event_id` for the incoming event. If a row exists with `processed_at IS NOT NULL`, the handler MUST return 200 with `{ outcome: "duplicate" }` without re-mutating any state. If a row exists with `processed_at IS NULL`, the handler MUST retry the dispatch. If no row exists, the handler MUST insert a new row and proceed.

#### Scenario: Webhook re-delivery

- GIVEN a webhook event with `event_id = "evt_123"` was previously processed (row exists with `processed_at IS NOT NULL`)
- WHEN the same webhook is re-delivered
- THEN the handler MUST return 200 with `{ outcome: "duplicate", eventId: "evt_123" }`
- AND no further mutation MUST occur

#### Scenario: First webhook delivery

- GIVEN a webhook event with `event_id = "evt_456"` that has no prior row
- WHEN the handler processes it
- THEN a new `billing.stripe_webhook_events` row MUST be inserted with `event_id = "evt_456"`, `received_at = now`, `processed_at = NULL`
- AND the dispatch MUST proceed

### Requirement: Subscription sync from webhook

The webhook handler MUST dispatch on `event.type` for `customer.subscription.created`, `customer.subscription.updated`, and `customer.subscription.deleted`. For `created`/`updated`, the handler MUST call `Subscription.SyncFromStripe(stripeSubscription)` which preserves the optimistic-concurrency guard. For `deleted`, the handler MUST call `Subscription.Cancel`. On success, the handler MUST mark the webhook event as processed (`processed_at = now`) and write an `audit.events` row with `action = "Updated"` or `"Deleted"`, `actor = "stripe-webhook"`, `changes = diff JSON`.

#### Scenario: Subscription created

- GIVEN a webhook event `customer.subscription.created` with `stripe_subscription_id = "sub_123"` and `status = "active"`
- WHEN the handler processes it
- THEN the handler MUST find the local `Subscription` by `stripe_subscription_id`
- AND call `Subscription.SyncFromStripe(stripeSub, clock)` → updates `PlanCode`, `Status`, `CurrentPeriod`
- AND mark the webhook event as processed
- AND write an `audit.events` row with `tenant_id` from the subscription's user, `changes = { "Status": { "before": "Pending", "after": "Active" } }`

#### Scenario: Subscription updated

- GIVEN a webhook event `customer.subscription.updated` with `status = "past_due"`
- WHEN the handler processes it
- THEN the subscription MUST be updated to `Status = PastDue`
- AND the version MUST be incremented
- AND the audit event MUST be written

#### Scenario: Subscription deleted

- GIVEN a webhook event `customer.subscription.deleted`
- WHEN the handler processes it
- THEN the subscription MUST be cancelled (`Status = Cancelled`)
- AND the version MUST be incremented
- AND the audit event MUST be written with `action = "Deleted"`

#### Scenario: Optimistic concurrency conflict

- GIVEN a webhook event arrives but the subscription's `version` was changed by an admin between read and write
- WHEN `SyncFromStripe` is called
- THEN the handler MUST retry up to 3 times with 200ms jitter
- AND if all retries fail, mark the webhook event as failed with `processing_error = "version_conflict"`

### Requirement: Stub fallback for dev / CI

When `StripeOptions.ApiKey` is null or empty, the gateway MUST be replaced with `StubStripeGateway` that returns synthetic responses with predictable shapes. The stub MUST return `cus_stub_{userId:N}` for Customer IDs, `https://stub.example.com/checkout/{sessionId}` for Checkout URLs, `https://stub.example.com/portal/{sessionId}` for Portal URLs, and a synthetic parsed event for `VerifyWebhookAsync`.

#### Scenario: Dev environment without Stripe key

- GIVEN `Stripe__ApiKey` env is not set
- WHEN the application starts
- THEN DI MUST register `StubStripeGateway` as `IStripeGateway`
- AND `CreateOrGetCustomerAsync` MUST return `cus_stub_{userId:N}` without calling Stripe

#### Scenario: CI environment with Stripe key

- GIVEN `Stripe__ApiKey = "sk_test_..."` env is set
- WHEN the application starts
- THEN DI MUST register `StripeGateway` as `IStripeGateway`
- AND `CreateOrGetCustomerAsync` MUST call the real Stripe API

### Requirement: API version pinning

The `StripeGateway` MUST set `StripeConfiguration.ApiVersion` to the value of `StripeOptions.ApiVersion` at construction time. The default value MUST be `Stripe.net`'s library default (resolved at build time). The setting MUST be asserted in the constructor (fail-fast on invalid version).

#### Scenario: Default API version

- GIVEN `StripeOptions.ApiVersion` is not set
- WHEN `StripeGateway` is constructed
- THEN `StripeConfiguration.ApiVersion` MUST be set to the library default

#### Scenario: Custom API version

- GIVEN `StripeOptions.ApiVersion = "2025-08-13"` is configured
- WHEN `StripeGateway` is constructed
- THEN `StripeConfiguration.ApiVersion` MUST be `"2025-08-13"`

## Data Model

```
billing.stripe_customers
  id                    UUID PK
  user_id               UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE
  stripe_customer_id    VARCHAR(64) NOT NULL
  email                 VARCHAR(320) NOT NULL
  display_name          VARCHAR(120)
  created_at            TIMESTAMPTZ NOT NULL DEFAULT now()

Constraints:
  ck_stripe_customers_email    CHECK (email LIKE '%_@_%.__%')
  ux_stripe_customers_user     UNIQUE (user_id)
  ux_stripe_customers_stripe_id UNIQUE (stripe_customer_id)

billing.stripe_webhook_events
  id                  UUID PK
  event_id            VARCHAR(64) NOT NULL
  event_type          VARCHAR(64) NOT NULL
  payload_json        JSONB NOT NULL
  signature_header    VARCHAR(256)
  received_at         TIMESTAMPTZ NOT NULL DEFAULT now()
  processed_at        TIMESTAMPTZ
  processing_error    VARCHAR(2000)

Constraints:
  ux_stripe_webhook_events_event_id  UNIQUE (event_id)

Indexes:
  ix_stripe_webhook_events_type  (event_type)
```

## Endpoints

| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/api/billing/stripe/customers` | Required | Create or fetch Stripe Customer for the authenticated user. Idempotent. |
| POST | `/api/billing/stripe/checkout` | Required | Create Stripe Checkout session for a subscription. Returns URL. |
| POST | `/api/billing/stripe/portal` | Required | Create Stripe Customer Portal session. Returns URL. |
| POST | `/api/billing/stripe/webhooks` | Anonymous (signature) | Receive Stripe webhook events. Signature-verified. |

`/api/billing/stripe/customers` and `POST /api/billing/stripe/checkout` and `POST /api/billing/stripe/portal` rate limit: `api-billing` (10 calls/hour/user). `/api/billing/stripe/webhooks` rate limit: `api-general` (no per-user limit).

## Output Shapes

```json
// POST /api/billing/stripe/customers
{
  "stripeCustomerId": "cus_123",
  "email": "user@example.com",
  "displayName": "John Doe",
  "createdAt": "2026-08-19T14:32:00Z"
}

// POST /api/billing/stripe/checkout
{
  "sessionId": "cs_test_abc123",
  "url": "https://checkout.stripe.com/c/pay/cs_test_abc123",
  "expiresAt": "2026-08-19T15:32:00Z"
}

// POST /api/billing/stripe/portal
{
  "sessionId": "ps_test_abc123",
  "url": "https://billing.stripe.com/p/session/ps_test_abc123",
  "expiresAt": "2026-08-19T15:32:00Z"
}

// POST /api/billing/stripe/webhooks (success)
{
  "outcome": "processed",
  "eventId": "evt_123"
}

// POST /api/billing/stripe/webhooks (duplicate)
{
  "outcome": "duplicate",
  "eventId": "evt_123"
}
```

## Architecture

- **Shared.Kernel/Billing/Stripe** — `IStripeGateway` interface + DTOs (`StripeCustomerDto`, `StripeCheckoutSessionDto`, `StripePortalSessionDto`, `StripeSubscriptionDto`, `StripePaymentMethodDto`, `StripeInvoiceDto`, `StripeWebhookEvent`) + `StripeError`.
- **Billing.Domain/Stripe** — `StripeCustomer` aggregate + `StripeWebhookEvent` aggregate + `SubscriptionWebhookSync` static translator + `StripeSubscriptionStatus` enum.
- **Billing.Application/Features/Stripe** — `CreateOrGetCustomerCommand`, `CreateCheckoutSessionCommand`, `CreatePortalSessionCommand`, `HandleWebhookCommand`, `GetSubscriptionQuery`, `GetPaymentMethodsQuery`, `GetInvoicesQuery` + handlers.
- **Billing.Infrastructure/Stripe** — `StripeGateway` (real impl) + `StubStripeGateway` (dev fallback) + `StripeOptions`.
- **Billing.Infrastructure/Persistence** — `StripeCustomerRepository`, `StripeWebhookEventRepository`, `StripeCustomerConfiguration`, `StripeWebhookEventConfiguration`.
- **Billing.PublicApi/Endpoints** — `BillingStripeEndpoints` (4 endpoints).
- **Frontend** — `billing-portal-page.ts` "Manage in Stripe" button (deferred to 6b.2).

## Out of Scope

- Stripe Tax / Stripe Terminal — Wave 7+.
- Stripe Connect / marketplace — Wave 7+.
- Stripe Invoicing one-off (only subscription invoices in Wave 6) — Wave 8.
- Multi-tenant Stripe (each tenant has its own Stripe account) — Wave 7.
- Per-tenant rate limits (10 calls/hour shared for now) — Wave 7.
- Customer detail page (the FE just gets a "Manage in Stripe" button) — Wave 7.
- Refund flow — Wave 7.
- Coupon / discount codes — Wave 7.
- Dunning emails (Stripe handles these; we just expose the URL) — Wave 7.
- Email receipt customization — Wave 7.
