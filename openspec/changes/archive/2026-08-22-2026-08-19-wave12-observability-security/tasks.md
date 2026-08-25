# Tasks: Wave 12 Observability and Security

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines | 1,400–2,000 |
| Review budget | 800 |
| Delivery | auto-chain; feature-branch-chain |

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: feature-branch-chain
400-line budget risk: High

### Suggested Work Units

| Unit | Goal / base | Focused test command | Runtime harness | Rollback boundary |
|---|---|---|---|---|
| 1 | Telemetry; PR1 base=tracker | `dotnet test --filter Telemetry` | Host + local Sentry/OTLP | Host telemetry files |
| 2 | Web Sentry; PR2 base=PR1 | `npm --prefix frontend test -- sentry` | Image + local Sentry | Frontend build files |
| 3 | Client IP; PR3 base=PR2 | `dotnet test --filter ClientIp` | Direct/trusted/untrusted requests | Proxy/endpoint files |
| 4 | Stripe; PR4 base=PR3 | `dotnet test --filter Stripe` | Empty/key/file secret | Billing/config files |
| 5 | Nonce; PR5 base=PR4 | `bash scripts/verify-security-headers.sh` | Nginx; fetch twice | Nginx/image/index |

## Phase 1: Backend Telemetry

- [x] 1.1 RED — Add Host tests for canonical `Sentry:Dsn`, release/stacks/PII defaults, disabled OTLP, and real-Sentry/fake-OTLP capture with resolvable frames/safe route fields but no seeded email, IP, query, body, auth, cookie, or credentials.
- [x] 1.2 GREEN — Update `Program.cs`, `appsettings.json`, `JadeCapital.Host.csproj`, and `Configuration/**` for validated Sentry and endpoint-gated ASP.NET/OTLP tracing.
- [x] 1.3 REFACTOR — Centralize telemetry composition; run focused tests and `dotnet build JadeCapital.slnx`.

## Phase 2: Frontend Sentry

- [x] 2.1 RED — Extend `sentry-init.spec.ts` and deployment tests for required DSN/release, explicit disablement/no-op, uncaught-error transport, release/original-TS frames, PII exclusion, and no runtime token/maps.
- [x] 2.2 GREEN — Add `frontend/scripts/**`; update app config, `angular.json`, packages, Docker/compose/env for public settings and build-only hidden-map upload.
- [x] 2.3 REFACTOR — Remove placeholders, keep credentials build-only, and verify Jest plus production image harness.

## Phase 3: Proxy Trust and Consent

- [x] 3.1 RED — Test malformed proxy/CIDR startup, trusted origin, untrusted spoof, direct peer, and unchanged consent route/status/response, `all`/`essential`, idempotency, changed timestamp, and invalid choice.
- [x] 3.2 GREEN — Add `ReverseProxyOptions`; update Host middleware and `ClientIpEndpoint.cs` to use only processed `RemoteIpAddress`.
- [x] 3.3 REFACTOR — Keep `UseForwardedHeaders` first, empty trust fail-safe, then run focused tests.

## Phase 4: Stripe Contract

- [x] 4.1 RED — Test no `ApiKey` binding, empty/key DI, `__File`, and unchanged `cus_stub_*`, `evt_stub_*`/`ping`, checkout/portal IDs-URLs-expiry, active `pro`, Visa `4242`, and three newest-first paid USD invoices.
- [x] 4.2 GREEN — Replace Billing `ApiKey` with `SecretKey` in options, registration, gateway, secret provider, compose, env, and smoke script.
- [x] 4.3 REFACTOR — Remove aliases/comments and run Stripe-focused tests.

## Phase 5: Nginx Nonce

- [x] 5.1 RED — Extend `verify-security-headers.sh`: build/test nginx; fetch twice; match header nonce to every script; require distinct responses/no marker, style `unsafe-inline`, `frame-ancestors 'none'`, and no script `unsafe-inline`.
- [x] 5.2 GREEN — Update `frontend/src/index.html`, nginx config, and frontend Dockerfile to inject one request ID into CSP and HTML with upstream compression disabled.
- [x] 5.3 REFACTOR — Run `nginx -t`, harness, full backend/frontend tests, and solution build.
