# Delta for Stripe

## MODIFIED Requirements

### Requirement: Stub fallback for dev / CI

Billing MUST bind `StripeOptions.SecretKey` only from `Stripe:SecretKey` (`Stripe__SecretKey`). A key MUST select `StripeGateway`; otherwise DI MUST select `StubStripeGateway`. `ApiKey` MUST NOT remain an alias. Existing stub shapes MUST remain unchanged.
(Previously: gateway selection depended on `StripeOptions.ApiKey` and `Stripe__ApiKey`.)

#### Scenario: Empty SecretKey selects stub
- GIVEN an empty `Stripe__SecretKey`
- WHEN Billing composes
- THEN DI MUST resolve `StubStripeGateway` without Stripe authentication

#### Scenario: SecretKey reaches gateway
- GIVEN deployment supplies `Stripe__SecretKey = "sk_test_..."`
- WHEN Billing composes
- THEN DI MUST resolve `StripeGateway` with that key, regardless of any `ApiKey` value
