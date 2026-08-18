# Billing Portal Specification

## Purpose

Provide a self-service billing portal where the authenticated user can view their current subscription, payment methods, and invoices, and navigate to Stripe Customer Portal for plan changes / cancellation / payment method updates. The portal complements the Admin-only subscription administration (covered by `subscription-administration` spec) by giving the end user the same visibility without admin involvement.

The portal reads data exclusively through `IStripeGateway` (covered by `stripe` spec) and returns only the caller's own data. It does NOT expose any data from the Admin subscription administration surface.

## Requirements

### Requirement: Subscription status visibility

The system MUST provide `GET /api/billing/portal/subscription` that returns the authenticated user's current subscription state. The endpoint MUST return 404 if the user has no Stripe Customer mapping OR no active subscription. The response MUST include `planCode`, `status`, `currentPeriodEnd`, `cancelAtPeriodEnd`, and `stripeSubscriptionId`.

#### Scenario: User has active subscription

- GIVEN an authenticated user with `billing.stripe_customers` row AND `billing.subscriptions` row with `status = "Active"`
- WHEN the user calls `GET /api/billing/portal/subscription`
- THEN the endpoint MUST return 200 with `{ planCode: "Pro", status: "Active", currentPeriodEnd: "2026-09-19T00:00:00Z", cancelAtPeriodEnd: false, stripeSubscriptionId: "sub_123" }`

#### Scenario: User has no Stripe Customer

- GIVEN an authenticated user with no `billing.stripe_customers` row
- WHEN the user calls `GET /api/billing/portal/subscription`
- THEN the endpoint MUST return 404 with `error.code = "stripe.customer_not_found"`
- AND the response MUST NOT reveal any other user's data

#### Scenario: User has Customer but no subscription

- GIVEN an authenticated user with `billing.stripe_customers` row but no `billing.subscriptions` row
- WHEN the user calls `GET /api/billing/portal/subscription`
- THEN the endpoint MUST return 404 with `error.code = "subscription.not_found"`
- AND the response MUST suggest the user call `POST /api/billing/stripe/checkout` to subscribe

#### Scenario: Cross-user isolation

- GIVEN user A has an active subscription
- WHEN user B calls `GET /api/billing/portal/subscription`
- THEN the endpoint MUST return user B's subscription (or 404 if B has none)
- AND MUST NOT return user A's data

### Requirement: Payment methods visibility

The system MUST provide `GET /api/billing/portal/payment-methods` that returns the authenticated user's saved payment methods. The endpoint MUST return an empty array if the user has no payment methods. The response MUST include `id`, `brand`, `last4`, `expiresAt` (nullable), and `isDefault`.

#### Scenario: User has multiple payment methods

- GIVEN an authenticated user with 2 saved payment methods (Visa ending 4242, Mastercard ending 5555)
- WHEN the user calls `GET /api/billing/portal/payment-methods`
- THEN the endpoint MUST return 200 with an array of 2 DTOs
- AND the DTOs must include `brand`, `last4`, `isDefault = true` for one of them

#### Scenario: User has no payment methods

- GIVEN an authenticated user with no saved payment methods
- WHEN the user calls `GET /api/billing/portal/payment-methods`
- THEN the endpoint MUST return 200 with an empty array `[]`
- AND MUST NOT return 404

#### Scenario: User has no Stripe Customer

- GIVEN an authenticated user with no `billing.stripe_customers` row
- WHEN the user calls `GET /api/billing/portal/payment-methods`
- THEN the endpoint MUST return 404 with `error.code = "stripe.customer_not_found"`

### Requirement: Invoice history visibility

The system MUST provide `GET /api/billing/portal/invoices` that returns the authenticated user's invoice history (newest first, capped at 100). The response MUST include `id`, `number`, `amount`, `currency`, `issuedAt`, `paidAt` (nullable), `status`, and `pdfUrl`.

#### Scenario: User has invoice history

- GIVEN an authenticated user with 3 paid invoices
- WHEN the user calls `GET /api/billing/portal/invoices`
- THEN the endpoint MUST return 200 with an array of 3 DTOs sorted by `issuedAt DESC`
- AND the DTOs must include `status = "paid"`, `paidAt` set, `pdfUrl` non-null

#### Scenario: User has unpaid invoice

- GIVEN an authenticated user with 1 paid invoice and 1 unpaid invoice
- WHEN the user calls `GET /api/billing/portal/invoices`
- THEN the endpoint MUST return 200 with 2 DTOs
- AND the unpaid one MUST have `status = "open"`, `paidAt = null`

#### Scenario: User has no invoices

- GIVEN an authenticated user with no invoices
- WHEN the user calls `GET /api/billing/portal/invoices`
- THEN the endpoint MUST return 200 with an empty array `[]`

#### Scenario: Invoice pagination cap

- GIVEN an authenticated user with 500 invoices
- WHEN the user calls `GET /api/billing/portal/invoices`
- THEN the endpoint MUST return at most 100 DTOs (the most recent)
- AND the response MUST include a `hasMore: true` flag

### Requirement: Stripe Customer Portal redirect

The system MUST provide `POST /api/billing/stripe/portal` (covered by `stripe` spec) that returns a Stripe Customer Portal URL. The billing portal frontend MUST call this endpoint when the user clicks the "Manage in Stripe" button and MUST redirect the browser to the returned URL.

#### Scenario: User clicks "Manage in Stripe"

- GIVEN an authenticated user on the billing portal page
- WHEN the user clicks the "Manage in Stripe" button
- THEN the frontend MUST call `POST /api/billing/stripe/portal` with `returnUrl: "https://app.example.com/billing"`
- AND receive `{ url: "https://billing.stripe.com/..." }`
- AND redirect the browser to that URL

#### Scenario: User navigates back from Stripe Portal

- GIVEN the user was redirected to Stripe Portal and updates their payment method
- WHEN Stripe redirects back to `returnUrl`
- THEN the frontend MUST show the updated payment methods (after a fresh `GET /api/billing/portal/payment-methods`)

### Requirement: Defensive reads

Every billing portal endpoint MUST handle Stripe API failures gracefully. A 5xx from Stripe MUST return 503 with `error.code = "stripe.unavailable"` and the body MUST NOT include any Stripe internal error details. A 401 from Stripe MUST return 503 with `error.code = "stripe.authentication_error"` (the API key is misconfigured).

#### Scenario: Stripe API timeout

- GIVEN Stripe takes longer than 5s to respond
- WHEN the handler calls `GetSubscriptionAsync`
- THEN the handler MUST return 503 with `error.code = "stripe.unavailable"`
- AND the response MUST NOT include the Stripe error message

#### Scenario: Stripe API rate limit

- GIVEN Stripe returns 429 Too Many Requests
- WHEN the handler calls `GetInvoicesAsync`
- THEN the handler MUST return 503 with `error.code = "stripe.rate_limit_error"`
- AND the response MUST include a `Retry-After` header

### Requirement: Rate limiting

Every billing portal endpoint MUST be rate-limited at `api-billing` (10 calls/hour/user). A user exceeding the limit MUST receive 429 with `error.code = "rate_limit.exceeded"` and a `Retry-After` header.

#### Scenario: User exceeds rate limit

- GIVEN an authenticated user has made 10 calls to `GET /api/billing/portal/subscription` in the last hour
- WHEN the user makes an 11th call
- THEN the endpoint MUST return 429 with `Retry-After: <seconds-until-window-reset>`

## Data Model

No new tables. The portal reads from `billing.stripe_customers` (mapping user → Stripe Customer id) and the Stripe API directly (no local copy of payment methods or invoices).

## Endpoints

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/api/billing/portal/subscription` | Required | Returns the user's current subscription state. |
| GET | `/api/billing/portal/payment-methods` | Required | Returns the user's saved payment methods. |
| GET | `/api/billing/portal/invoices` | Required | Returns the user's invoice history (newest first, max 100). |
| POST | `/api/billing/stripe/portal` | Required | Returns a Stripe Customer Portal URL. |

All endpoints require `RequireAuthorization()`. Rate limit: `api-billing` (10 calls/hour/user).

## Output Shapes

```json
// GET /api/billing/portal/subscription
{
  "planCode": "Pro",
  "status": "Active",
  "currentPeriodEnd": "2026-09-19T00:00:00Z",
  "cancelAtPeriodEnd": false,
  "stripeSubscriptionId": "sub_123"
}

// GET /api/billing/portal/payment-methods
{
  "paymentMethods": [
    {
      "id": "pm_123",
      "brand": "Visa",
      "last4": "4242",
      "expiresAt": "2028-12-31T00:00:00Z",
      "isDefault": true
    },
    {
      "id": "pm_456",
      "brand": "Mastercard",
      "last4": "5555",
      "expiresAt": "2027-06-30T00:00:00Z",
      "isDefault": false
    }
  ]
}

// GET /api/billing/portal/invoices
{
  "invoices": [
    {
      "id": "in_123",
      "number": "INV-2026-001",
      "amount": 2900,
      "currency": "USD",
      "issuedAt": "2026-08-19T00:00:00Z",
      "paidAt": "2026-08-19T00:05:00Z",
      "status": "paid",
      "pdfUrl": "https://invoice.stripe.com/i/..."
    }
  ],
  "hasMore": false
}
```

## Architecture

- **Billing.Application/Features/Stripe** — `GetSubscriptionQuery`, `GetPaymentMethodsQuery`, `GetInvoicesQuery` + handlers + DTOs.
- **Billing.Application/Abstractions** — `IStripeGateway` (re-export from Shared.Kernel).
- **Billing.PublicApi/Endpoints** — `BillingPortalEndpoints` (3 endpoints).
- **Frontend** — `billing-portal-page.ts` (plan card + payment methods list + invoices list + "Manage in Stripe" button).

## Out of Scope

- Admin subscription administration (covered by `subscription-administration` spec).
- Plan changes / cancellation by the user (delegated to Stripe Portal).
- Refund flow — Wave 7.
- Coupon / discount codes — Wave 7.
- Email receipt customization — Wave 7.
- Multi-tenant Stripe (each tenant has its own Stripe account) — Wave 7.
- Read-only access to subscription history (the user only sees the most recent state; full history is Admin-only).
- Subscription timeline view (changes, cancellations, upgrades) — Wave 7.
- Forecasted next invoice (next billing date + amount) — Wave 7.
- Trial eligibility check — Wave 7.
- Per-user billing notification preferences — Wave 7.
