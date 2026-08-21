# Design: Complete Wave 12 Observability and Security

## Technical Approach

Keep existing module boundaries: Host owns telemetry/proxy trust, Identity uses the resolved address, Billing binds `SecretKey`, frontend Sentry is build-configured, and nginx injects nonces.

## Architecture Decisions

| Decision | Alternatives | Choice and rationale |
|---|---|---|
| Frontend Sentry contract | Runtime substitution; `sed` | A Node script serializes public `FRONTEND_SENTRY_DSN`, `SENTRY_RELEASE`, and `APP_ENV` into shared build-time configuration. Production fails when DSN or release is empty unless the operator explicitly sets `ALLOW_FRONTEND_SENTRY_DISABLED=true` for rollback/intentional disablement. CI uploads hidden source maps using build-only credentials; neither enters runtime. |
| Backend telemetry | Sentry tracing; unconditional exporter | Read only `Sentry:Dsn`, apply `Observability:Release`, attach stacks, and disable default PII. Add ASP.NET Core tracing and OTLP export only for a valid `OpenTelemetry:Otlp:Endpoint`; otherwise startup is silent. Standard HTTP attributes provide required trace fields without custom enrichment. |
| Proxy authority | Trust all proxies; parse `X-Forwarded-For` in Identity | Bind Host-owned `ReverseProxyOptions` (`KnownProxies`, `KnownNetworks`, `ForwardLimit`), fail startup on malformed entries, and leave trust lists empty by default. `UseForwardedHeaders` remains first; Identity returns only `Connection.RemoteIpAddress`. |
| Nonce source | Browser-only nonce; njs/Lua | Use nginx `$request_id` and replace `__CSP_NONCE__` with `sub_filter`. Header and HTML share one 128-bit value. Disable upstream HTML compression; preserve style `unsafe-inline` and omit script `unsafe-inline`. |
| Stripe key | Preserve `ApiKey` alias; Host adapter | Rename Billing `StripeOptions.ApiKey` to `SecretKey` and use it for client/gateway selection. Extend the existing Docker secret provider's documented `__File` contract so `Stripe__SecretKey__File` materializes canonical `Stripe:SecretKey`; no alias remains. |

## Data Flow and Trust Boundaries

```text
CI inputs -> Angular generated config -> Sentry SDK -> Sentry (public DSN; token never shipped)
request -> edge nginx nonce/header+HTML -> known proxy -> ForwardedHeaders -> RemoteIpAddress -> Identity
API request -> ASP.NET instrumentation -> batch exporter (only with endpoint) -> trusted OTLP collector
Docker secret file -> Host configuration -> Billing SecretKey -> StripeClient
```

Sentry/OTel exclude user ID, IP, query, headers, body, credentials, and cookies. Source-map credentials remain build-only.

## File Changes

| File(s) | Action | Description |
|---|---|---|
| `src/1.Api/JadeCapital.Host/{Program.cs,appsettings.json,JadeCapital.Host.csproj,Configuration/**}` | Create/Modify | Compose and validate Sentry, OTel, proxy, and `__File` configuration. |
| `src/2.Modules/Identity/.../ClientIpEndpoint.cs` | Modify | Remove header parsing; return processed remote address. |
| `src/2.Modules/Billing/.../{StripeOptions.cs,BillingModuleRegistration.cs,StripeGateway.cs}` and tests | Modify | Replace `ApiKey` with `SecretKey`. |
| `frontend/{src/app/**,scripts/**,angular.json,package*.json}` | Create/Modify | Generated Sentry config, release, and hidden source-map upload. |
| `frontend/src/index.html`, `infrastructure/{Dockerfile.frontend.prod,nginx/**}`, `docker-compose.prod.yml`, `.env.example` | Modify | Deployment guard, nonce injection, and edge configuration. |
| `tests/UnitTests/{JadeCapital.Host.UnitTests,JadeCapital.Billing.UnitTests}/**`, `frontend/**/*.spec.ts`, `scripts/verify-security-headers.sh` | Modify/Create | RED and deployment coverage. |

## Interfaces / Contracts

- Backend: `Sentry__Dsn`, `Observability__Release`, `OpenTelemetry__Otlp__Endpoint`; empty DSN disables Sentry and an empty OTLP endpoint disables tracing export.
- Frontend: `FRONTEND_SENTRY_DSN`, `SENTRY_RELEASE`, `APP_ENV`; upload uses build-only auth/org/project. Production requires DSN and release unless `ALLOW_FRONTEND_SENTRY_DISABLED=true`; the guard logs intentional disablement or fails before image publication.
- Proxy: `ReverseProxy__KnownProxies__N`, `ReverseProxy__KnownNetworks__N` (CIDR), `ReverseProxy__ForwardLimit`; empty trust means headers are ignored.
- Stripe: only `Stripe:SecretKey` / `Stripe__SecretKey` / `Stripe__SecretKey__File` reaches Billing `SecretKey`.

## Testing Strategy and Sequence

Strict TDD applies: write each listed RED test before production changes.

| Layer | Planned RED proof |
|---|---|
| Unit | Canonical Sentry key, release, stacktrace, and PII options; no OTLP exporter without endpoint; span snapshot excludes seeded secrets; proxy/CIDR validation; `ApiKey` cannot bind. Frontend config tests prove production rejects missing DSN/release, explicit disablement permits empty DSN, and SDK init remains a no-op when disabled. |
| Integration | Capture backend transport output from an unhandled endpoint exception and frontend output from an uncaught Angular error. Both events must contain the configured release and resolvable frames (backend symbols; frontend original TypeScript via source maps), while seeded email, IP, auth/cookie headers, query, and body are absent. Fake OTLP captures safe fields only. Proxy tests cover trusted, spoofed-untrusted, and direct requests; direct uses the transport peer. Consent characterization remains green for route/status/response, `all`/`essential`, same-choice idempotency, changed-choice timestamp, and invalid choice. Billing DI selects stub/real; stub contract tests preserve customer `cus_stub_*`, webhook `evt_stub_*`/`ping`, checkout/portal IDs-URLs-expiry, active `pro` subscription, Visa `4242` default card, and three newest-first paid USD invoices. Runtime contains release metadata but no upload token/maps. |
| Deployment | Build nginx image, run `nginx -t`, request the same HTML twice, and assert each response's CSP nonce equals every script nonce, differs across responses, contains no marker, and preserves required directives. |

## Migration / Rollout

No data migration. Configure production DSN/release and proxy trust before rollout; deploy nginx atomically; deploy Stripe after a secret smoke test. To roll back frontend Sentry without reverting the image, set `ALLOW_FRONTEND_SENTRY_DISABLED=true` with an empty DSN and record the deployment exception; remove the override when telemetry is restored. Other parts roll back independently, without restoring the Stripe `ApiKey` alias.

## Open Questions

None.
