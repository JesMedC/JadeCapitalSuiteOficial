# 7. Stripe integration

## Status
Accepted (2026-08-19, Wave 10)

## Decision
- `IStripeGateway` abstraction (Wave 6 6a.1) with `StripeHttpGateway` + `StubStripeGateway` impls
- Selection via env var `Stripe__ApiKey` presence (real) vs empty (stub)
- Webhook signature verification via `Stripe__WebhookSecret` (mandatory)
- `IStripeWebhookEventRepository` is append-only (Wave 6 6b.2) for idempotency
- Subscription lifecycle: Trial → Active → PastDue → Cancelled
- `StripeCustomerAuditDecorator` (Wave 8 8b.1) audits StripeCustomer mutations

## Consequences
- Pros: testable without live Stripe keys; webhook idempotency prevents double-charges
- Cons: dev .env with placeholder Stripe key will fall back to stub; ops must verify live keys in `Wave 10.6 stripe-test-smoke.sh`
- v1.0.0-rc1 ships test-mode only — live keys deferred to ops deploy
