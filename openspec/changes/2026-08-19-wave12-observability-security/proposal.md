# Proposal: Complete Wave 12 Observability and Security

## Intent

Close only the production gaps remaining after merged PRs #62 and #63. Sentry hooks, CSP/a11y work, AI options DI, Stripe host validation, client-IP capture, and welcome-email options exist, but deployment wiring and trust boundaries remain incomplete; OpenTelemetry is absent.

## Scope

### In Scope
- Normalize backend Sentry configuration to `Sentry:Dsn` (`Sentry__Dsn` in environment variables), activate frontend Sentry through the production build/deployment path, and preserve PII-safe defaults.
- Add backend OpenTelemetry tracing with configurable OTLP export and safe disabled-by-default behavior.
- Trust forwarded client addresses only from configured proxies/networks; return the processed remote address instead of parsing arbitrary `X-Forwarded-For` input.
- Replace nginx's literal `{request_nonce}` CSP token with a real per-request nonce and matching HTML injection.
- Align Billing's runtime `StripeOptions` with canonical `Stripe:SecretKey`; remove the remaining `ApiKey` binding split.

### Out of Scope
- Reimplement shipped WCAG/axe CI, `AIProviderOptions` DI, `WelcomeEmailPolicyOptions`, or GA tagging (`v1.0.0` already exists at `9a473bf`).
- Metrics dashboards, log aggregation, strict style nonces, or broader Playwright E2E coverage.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
- `observability-light`: production-activatable Sentry and OpenTelemetry/OTLP tracing.
- `security-headers`: cryptographically random, request-matched CSP nonces.
- `gdpr-compliance`: trustworthy consent client-IP derivation behind known proxies.
- `stripe`: one `SecretKey` configuration contract from deployment through Billing runtime.

## Approach

Harden Host composition and existing infrastructure adapters without changing module boundaries: Host owns telemetry and proxy configuration; Identity consumes ASP.NET Core's resolved remote IP; Billing owns its canonical Stripe options; nginx generates and injects matching nonces. Add focused configuration, security, and deployment-contract tests before implementation.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `src/1.Api/JadeCapital.Host/` | Modified | Sentry, OTel, trusted proxies |
| `src/2.Modules/{Identity,Billing}/` | Modified | Client IP and Stripe binding |
| `frontend/`, `infrastructure/` | Modified | Sentry activation and CSP nonce |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Telemetry exports sensitive data | Medium | Explicit enrichment allowlist; PII disabled |
| Proxy/CSP misconfiguration blocks traffic or scripts | Medium | Fail-safe configuration and deployment tests |

## Rollback Plan

Disable Sentry/OTLP via empty configuration, revert proxy and nonce infrastructure independently, and temporarily restore the prior Stripe alias only with an explicit compatibility test. No data migration is required.

## Dependencies

- OTLP-compatible collector and operator-provided trusted proxy ranges.

## Success Criteria

- [ ] Sentry activates from canonical backend and deployable frontend configuration.
- [ ] OTLP traces export when configured and remain disabled otherwise.
- [ ] Spoofed forwarded headers cannot determine the recorded client IP.
- [ ] Consecutive nginx responses use distinct, matching CSP nonces.
- [ ] `Stripe__SecretKey` selects the real Billing gateway.
