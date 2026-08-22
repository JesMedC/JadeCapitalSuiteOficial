# Apply Progress: Wave 12 Observability and Security

> **Canonical apply-progress ledger.** Historical slice ledgers are superseded by this file and are retained only for provenance.

## Delivery State

- Mode: Strict TDD
- Delivery: auto-chain, feature-branch-chain
- Current work unit: PR5 based on PR4 — Per-request CSP nonce
- Review budget: 800 changed lines
- Authored slice delta: 311 changed lines (181 additions, 130 deletions) across 8 files, including SDD ledger updates
- Completed tasks: 1.1–1.3, 2.1–2.3, 3.1–3.3, 4.1–4.3, 5.1–5.3
- Remaining tasks: None

## Completed Tasks

- [x] 1.1 RED — Replaced configuration-only Sentry tests with behavioral configuration, Sentry SDK envelope, Kestrel request, and OpenTelemetry exporter tests.
- [x] 1.2 GREEN — Added canonical Sentry configuration and endpoint-gated ASP.NET Core/OTLP composition.
- [x] 1.3 REFACTOR — Centralized composition in `TelemetryConfiguration`, normalized sources, and verified focused tests plus the solution build.
- [x] 2.1 RED — Added frontend configuration/deployment tests and a real SDK uncaught-error transport test before production changes.
- [x] 2.2 GREEN — Added generated build configuration, PII-safe SDK initialization, hidden source maps, build-only upload credentials, and production deployment guards.
- [x] 2.3 REFACTOR — Removed placeholder globals, normalized the generated development default, and verified the production image contains release metadata but no maps or upload token.
- [x] 3.1 RED — Added startup validation, trusted/untrusted/direct runtime proxy tests, and consent characterization coverage; reproduced the preserved RED compile failure against the pre-change production baseline.
- [x] 3.2 GREEN — Added validated Host-owned reverse-proxy options and changed Identity to consume only processed `RemoteIpAddress`.
- [x] 3.3 REFACTOR — Preserved first-middleware ordering, empty-trust fail-safe behavior, and verified focused Host/Identity tests plus the solution build.
- [x] 4.1 RED — Added canonical options/DI tests, file-backed secret tests, smoke-contract tests, and full named stub-shape characterization before production changes.
- [x] 4.2 GREEN — Replaced Billing `ApiKey` with `SecretKey`, materialized `:File` configuration into canonical keys, and aligned production composition and smoke configuration.
- [x] 4.3 REFACTOR — Removed stale alias comments, normalized changed C# files, and verified focused Billing/Host tests plus the solution build.
- [x] 5.1 RED — Replaced the header-only smoke verifier with a production-image/runtime harness covering two responses, exact header-to-script nonce equality, nonce rotation, marker absence, and CSP regressions.
- [x] 5.2 GREEN — Used nginx's 128-bit `$request_id` in the CSP and `sub_filter`, disabled upstream compression, reused the injected nonce in the meta fallback, and wired the deployed edge/frontend configs.
- [x] 5.3 REFACTOR — Removed duplicate MIME configuration, normalized the verifier and CSP regression test, then ran the image harness, frontend suite, backend suites, and solution build.

## TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|---|---|---|---|---|---|---|---|
| 1.1 | `tests/UnitTests/JadeCapital.Host.UnitTests/Configuration/TelemetryConfigurationTests.cs` | Unit + runtime integration | 37/37 passed before edits | ✅ Written — Focused test compile failed because OpenTelemetry and `TelemetryConfiguration` did not exist | ✅ Passed — 9/9 passed after task 1.2 | Canonical/legacy DSN paths, four disabled OTLP inputs, enabled/disabled transports, safe Sentry and OTel payloads | Old non-behavioral `SentryHookTests.cs` removed |
| 1.2 | Same | Unit + runtime integration | Covered by 1.1 baseline | ✅ Written — 1.1 RED suite defined required production contract first | ✅ Passed — 9/9 focused tests passed | Empty configuration and configured Sentry/OTLP runtime paths both exercised | Sentry diagnostic integration disabled so OTel remains the single tracing owner |
| 1.3 | Same | Unit + runtime integration | 43/43 Host tests passed | ✅ Written — Existing RED suite guarded the extraction | ✅ Passed — 9/9 focused and 43/43 Host tests passed after extraction | Kestrel requests proved exception and trace behavior through deterministic local exporters | `dotnet format` ran before final tests; solution build succeeded |
| 2.1 | `sentry-init.spec.ts`, `sentry-deployment.spec.ts` | Unit + SDK transport + deployment contract | 4/4 prior Sentry tests passed after `npm ci` | ✅ Written — Missing generator/config API failed; PII test then exposed BrowserSession leakage; deployment tests failed before hidden maps/build secrets existed | ✅ Passed — 9/9 focused tests passed | Empty/whitespace DSNs, two missing-variable paths, explicit disablement, safe serialization, uncaught transport, upload skip/partial credentials, and image contract | Assertions were tightened to inspect the real event envelope and all transport output |
| 2.2 | Same | Unit + build integration | Covered by 2.1 baseline | ✅ Written — 2.1 RED suite defined the corrected deployment and transport behavior | ✅ Passed — 9/9 focused tests and configured production build passed | Configured and explicitly disabled generation paths plus missing DSN/release failures were exercised | BrowserSession integration removed because it bypassed event sanitization |
| 2.3 | Same | Build + image runtime | Configured Angular build passed | ✅ Written — Deployment RED guarded direct-build enforcement and runtime hygiene | ✅ Passed — Final 9/9 focused tests, production image build, `nginx -t`, and served HTML passed | Hidden maps resolved `sentry-init.ts` before deletion; runtime retained release but no maps/token | Generated source was normalized to a credential-free disabled development default |
| 3.1 | `ReverseProxyConfigurationTests.cs`, `ClientIpEndpointTests.cs`, `ConsentEndpointTests.cs` | Unit + TestServer integration | Consent characterization passed 4/4 against the pre-change production baseline | ✅ Reproduced — proxy tests failed compilation on baseline because `AddJadeCapitalReverseProxy` did not exist | ✅ Passed — 17/17 focused Host tests and 6/6 existing Identity consent tests | Malformed proxy/network/limit, trusted proxy/network, forward limit, untrusted spoof, empty trust, direct peer, both consent choices, auth, invalid choice, idempotency, and changed timestamp | Assertions were kept behavioral; existing Identity handler coverage supplies idempotency/timestamp proof |
| 3.2 | Same | Unit + TestServer integration | Covered by 3.1 baseline | ✅ Written before preserved production changes | ✅ Passed — processed addresses and validated options passed through real middleware | Proxy and network trust plus direct and spoofed requests exercise distinct paths | Identity header parsing and trust-all Host configuration were removed |
| 3.3 | Same | Source contract + integration | 16/16 focused Host tests passed before final ordering guard | ✅ Ordering contract added before final verification | ✅ Passed — 17/17 focused Host tests | Empty trust and configured trust paths both exercised | Removed the obsolete Host import and guarded first-middleware placement |
| 4.1 | `StripeConfigurationTests.cs`, `StubStripeGatewayTests.cs`, `DockerSecretConfigurationProviderTests.cs` | Unit + DI/runtime configuration | 94/94 Billing Stripe and 16/16 Host configuration tests passed | ✅ Reproduced — 5 Billing contract failures plus Host compile failure because `AddFileBackedSecrets` did not exist | ✅ Passed — 13/13 initial contract tests | Null/empty/whitespace keys, canonical-vs-legacy precedence, direct/file configuration, and every named stub response shape | Assertions verify concrete gateway types, Stripe client key, and complete response fields |
| 4.2 | Same | DI + file-backed runtime configuration | Covered by 4.1 baseline | ✅ Written before production changes | ✅ Passed — canonical key selects `StripeGateway`; empty key selects `StubStripeGateway`; file contents materialize as `Stripe:SecretKey` | Direct and file-backed paths plus legacy-key rejection exercise distinct paths | Centralized generic `:File` materialization at Host configuration composition |
| 4.3 | Same | Unit + runtime contract | 13/13 focused tests passed before cleanup | ✅ Existing RED suite guarded cleanup | ✅ Passed — 104/104 Billing Stripe and 18/18 Host configuration tests | Canonical smoke input rejects malformed keys and the legacy variable is not accepted | `dotnet format` and `git diff --check` succeeded; solution build succeeded |
| 5.1 | `scripts/verify-security-headers.sh` | Deployment integration | 6/6 existing nginx tests passed; legacy harness exposed a pre-existing case-sensitive `DENY` assertion | ✅ Confirmed — production image returned literal `{request_nonce}`, so the new 128-bit nonce assertion failed | ✅ Passed — image build, `nginx -t`, and two-response harness succeeded | Two real responses plus all generated executable script tags force request-specific matching rather than a hardcoded value | Consolidated assertions in one Python parser and made header matching case-insensitive |
| 5.2 | Same + `IndexHtmlMetaCspTests.cs` | Runtime + source contract | Covered by 5.1 RED and frontend image baseline | ✅ Written before nginx/index/deployment changes | ✅ Passed — `$request_id` matched the CSP and every script nonce | Header/body equality, rotation, marker absence, compression handling, meta fallback reuse, and deployed config mounts cover distinct paths | Removed stale placeholder/browser-global behavior and retained the development crypto fallback |
| 5.3 | Same | Regression + build | Focused runtime already green | ✅ Existing RED harness guarded cleanup | ✅ Passed — focused nginx 6/6, frontend 198/198, image harness, Compose config, and solution build | Full solution tests were also executed and exposed unrelated MinIO credential failures | Removed duplicate `sub_filter_types`; `git diff --check` passed |

## Work Unit Evidence

| Evidence | Result |
|---|---|
| Focused test | `MISE_DOTNET_VERSION=10.0.400 mise exec -- dotnet test tests/UnitTests/JadeCapital.Host.UnitTests/JadeCapital.Host.UnitTests.csproj --filter "FullyQualifiedName~Telemetry" --nologo --verbosity minimal` → exit 0; 9 passed, 0 failed, 0 skipped |
| Runtime harness | Same command starts local Kestrel instances; an unhandled exception reaches an SDK-generated Sentry envelope through an in-memory `ITransport`, and an HTTP request reaches an in-memory OTel `BaseExporter<Activity>` → 2 runtime scenarios passed without external network |
| Host regression | `MISE_DOTNET_VERSION=10.0.400 mise exec -- dotnet test tests/UnitTests/JadeCapital.Host.UnitTests/JadeCapital.Host.UnitTests.csproj --nologo --verbosity minimal` → exit 0; 43 passed, 0 failed, 0 skipped |
| Build | `MISE_DOTNET_VERSION=10.0.400 mise exec -- dotnet build JadeCapital.slnx --nologo --verbosity minimal` → exit 0; 0 errors, 3 pre-existing CA2263 warnings in Shared Kernel tests |
| Rollback boundary | Revert `TelemetryConfiguration.cs`, the one-line `Program.cs` registration, telemetry package/config additions, and `TelemetryConfigurationTests.cs`; no other Wave 12 phase is coupled to this unit |

### PR2 Frontend Sentry

| Evidence | Result |
|---|---|
| Focused test | `npm --prefix frontend test -- --runInBand sentry` → exit 0; 2 suites, 9 tests passed |
| Missing-config guard | `env -u FRONTEND_SENTRY_DSN -u SENTRY_RELEASE -u ALLOW_FRONTEND_SENTRY_DISABLED npm --prefix frontend run build -- --configuration production` → expected exit 1 naming both missing variables |
| Production build | Configured `npm --prefix frontend run build -- --configuration production` → exit 0; 61 hidden JavaScript maps; map sources include original `src/app/core/observability/sentry-init.ts`; bundles expose no `sourceMappingURL` |
| Runtime harness | Built `jade-frontend-sentry-wave12:test`; `nginx -t` passed; image served `/`; runtime bundle retained `jade-web@12.2.0`; no `*.map`, `SENTRY_AUTH_TOKEN`, or test token was present |
| Rollback boundary | Revert frontend observability config/scripts/tests, Angular/package changes, frontend Docker build args/stages, compose frontend args, and documented env values; backend telemetry and later phases remain intact |

### Phase 2 Surgical Correction — Compose Build Secret

Tasks 2.1–2.3 remain complete and unchanged in scope. The correction wires the Dockerfile's existing BuildKit secret into Compose without adding a token build argument or runtime environment value.

| Task | Safety Net | RED | GREEN | REFACTOR |
|---|---|---|---|---|
| 2.2 correction | `sentry-deployment.spec.ts` → 6/6 passed | New Compose build-secret contract failed: 1 failed, 6 passed | Added `build.secrets` plus environment-backed top-level secret: 7/7 passed | Normalized test slicing; final 7/7 passed; `git diff --check` exited 0 |

| Work Unit Evidence | Result |
|---|---|
| Focused test | `npm --prefix frontend test -- --runInBand sentry-deployment.spec.ts` → exit 0; 1 suite, 7 tests passed |
| Compose build contract | With `SENTRY_AUTH_TOKEN` unset, `docker compose -f docker-compose.prod.yml config --quiet` and resolved-JSON assertions passed; frontend build mounts `sentry_auth_token`, its source is operator environment `SENTRY_AUTH_TOKEN`, and no token build arg exists |
| Runtime harness | N/A — declarative Compose build wiring has no runtime boundary; resolved Compose configuration validates the BuildKit mount without building, uploading, requiring a real token, or using the network |
| Rollback boundary | Revert only the `frontend.build.secrets` and top-level `sentry_auth_token` entries in `docker-compose.prod.yml` plus the focused contract test; all other Wave 12 behavior remains intact |

### PR3 Proxy Trust and Consent

| Evidence | Result |
|---|---|
| Focused test | `MISE_DOTNET_VERSION=10.0.400 mise exec -- dotnet test tests/UnitTests/JadeCapital.Host.UnitTests/JadeCapital.Host.UnitTests.csproj --filter "FullyQualifiedName~ReverseProxyConfigurationTests|FullyQualifiedName~ClientIpEndpointTests|FullyQualifiedName~ConsentEndpointTests" --nologo --verbosity minimal` → exit 0; 17 passed, 0 failed, 0 skipped |
| Consent regression | `MISE_DOTNET_VERSION=10.0.400 mise exec -- dotnet test tests/UnitTests/JadeCapital.Identity.UnitTests/JadeCapital.Identity.UnitTests.csproj --filter "FullyQualifiedName~ConsentHandlerTests" --nologo --verbosity minimal` → exit 0; 6 passed, 0 failed, 0 skipped |
| Runtime harness | The focused Host command runs TestServer requests for trusted proxy/network, capped forwarding, spoofed untrusted peer, empty trust, direct peer, anonymous client-IP response, and authenticated consent routes → all scenarios passed |
| Build | `MISE_DOTNET_VERSION=10.0.400 mise exec -- dotnet build JadeCapital.slnx --nologo --verbosity minimal` → exit 0; 0 errors and 3 pre-existing CA2263 warnings in Shared Kernel tests |
| Review budget | 615 changed lines for this slice, including preserved work and SDD ledger updates (455 additions, 160 deletions), within the 800-line budget |
| Rollback boundary | Revert the seven proxy/consent files in Host, Identity, and Host tests; telemetry/frontend slices and later Stripe/nonce work remain independent |

### PR4 Canonical Stripe Contract

| Evidence | Result |
|---|---|
| Focused Billing test | `MISE_DOTNET_VERSION=10.0.400 mise exec -- dotnet test tests/UnitTests/JadeCapital.Billing.UnitTests/JadeCapital.Billing.UnitTests.csproj --filter "FullyQualifiedName~Stripe" --nologo --verbosity minimal` → exit 0; 104 passed, 0 failed, 0 skipped |
| Host/config regression | `MISE_DOTNET_VERSION=10.0.400 mise exec -- dotnet test tests/UnitTests/JadeCapital.Host.UnitTests/JadeCapital.Host.UnitTests.csproj --filter "FullyQualifiedName~DockerSecretConfigurationProviderTests|FullyQualifiedName~StripeOptionsValidatorTests" --nologo --verbosity minimal` → exit 0; 18 passed, 0 failed, 0 skipped |
| Runtime harness | Focused tests build the real Billing service provider for null/empty/whitespace/canonical keys and read a temporary secret file through Host configuration; all gateway-selection, Stripe client, direct-key, and file-key scenarios passed |
| Smoke contract | `Stripe__SecretKey=invalid bash scripts/stripe-test-smoke.sh` rejected the malformed canonical key before network access; supplying only legacy `Stripe__ApiKey` failed because canonical `Stripe__SecretKey` was required |
| Build | `MISE_DOTNET_VERSION=10.0.400 mise exec -- dotnet build JadeCapital.slnx --nologo --verbosity minimal` → exit 0; 0 errors and 0 warnings |
| Rollback boundary | Revert the Billing Stripe option/registration/docs files, Host file-backed configuration and tests, production compose/smoke wiring, and Stripe tests; Phases 1–3 and pending Phase 5 remain independent |

### PR5 Per-request CSP Nonce

| Evidence | Result |
|---|---|
| Focused test | `bash scripts/verify-security-headers.sh 18085` → exit 0; production frontend image built, `nginx -t` passed, and two fetched responses had distinct 32-hex nonces matching every executable script tag |
| Runtime harness | Real frontend and edge nginx containers ran on an isolated Docker network; no nonce marker leaked, style `unsafe-inline` and `frame-ancestors 'none'` remained, and script `unsafe-inline` remained absent |
| Frontend regression | `npm --prefix frontend test -- --runInBand` → exit 0; 45 suites and 198 tests passed; the production Angular build also passed inside the image |
| Backend/build | Focused nginx tests passed 6/6; `dotnet build JadeCapital.slnx` passed with 0 warnings/errors. Full solution tests ran but integration tests failed on unrelated missing MinIO credentials; Host also retained one pre-existing OTel exporter failure outside this slice |
| Deployment contract | `docker compose -f docker-compose.prod.yml config --quiet` → exit 0; edge mounts `jade.conf`, while frontend uses the nginx config baked by `Dockerfile.frontend.prod` instead of overriding it |
| Rollback boundary | Revert the CSP/index/verifier test files and the two Compose mount changes; Phases 1–4 remain intact |

## Behavioral Proof

- Only `Sentry:Dsn` is read; empty or whitespace DSNs do not initialize Sentry, and the legacy literal `Sentry__Dsn` configuration key is ignored.
- Sentry envelopes retain configured release and resolvable source frames while excluding seeded email, IP, query, body, authorization, cookie, server, and user values.
- OpenTelemetry is registered only for a valid absolute HTTP(S) endpoint; absent, blank, or invalid endpoints register no `TracerProvider` or exporter.
- Exported ASP.NET Core spans retain trace identity, route, method, status, and duration while sensitive HTTP attributes are removed before export.
- Production frontend builds generate DSN, release, and environment safely; missing DSN/release fail unless only DSN is explicitly disabled with `ALLOW_FRONTEND_SENTRY_DISABLED=true`.
- A real uncaught browser error reaches a deterministic Sentry transport with release and original TypeScript frame context while user, email, IP, authorization, cookie, and body values are absent.
- Sentry auth token, organization, and project metadata are consumed only in the build stage; hidden maps are deleted before the nginx runtime stage.
- Forwarded headers are processed only for validated configured proxies/networks, respect `ForwardLimit`, and are ignored when trust is empty.
- Identity reads only middleware-processed `RemoteIpAddress`; untrusted spoofing cannot control the client-IP response.
- Consent route/status/response, both choices, same-choice idempotency, changed-choice timestamp, and invalid-choice behavior remain unchanged.
- Billing binds only `Stripe:SecretKey`; null, empty, or whitespace values select `StubStripeGateway`, while a canonical key selects `StripeGateway` and reaches the real `StripeClient`.
- `Stripe__SecretKey__File` is materialized into `Stripe:SecretKey` from trimmed file contents, and legacy `ApiKey` configuration cannot select the real gateway.
- Stub customer, webhook, checkout, portal, subscription, payment-method, and invoice response shapes remain unchanged.
- Nginx now uses one cryptographically random 128-bit request ID for both the CSP `script-src` nonce and every executable script tag, and consecutive HTML responses rotate it.
- The deployed edge receives uncompressed frontend HTML for substitution, while the production frontend image retains its baked nginx configuration.

## Limitation

No external Sentry service was available or contacted. The runtime proof uses the real Sentry ASP.NET Core SDK and serializes its actual envelope through a deterministic local `ITransport`; this proves SDK behavior and payload safety but not external ingestion, symbol-server processing, or vendor availability.

The frontend proof likewise uses the real Angular SDK with an in-memory transport and locally validates hidden source-map resolution. No source maps were uploaded and no external Sentry ingestion or symbolication service was contacted.

The full solution test command is not green in this environment: 49/58 API integration tests fail during Host startup because MinIO credentials are not initialized, and the Host suite has one unrelated OpenTelemetry exporter assertion failure. Focused CSP tests, the real nginx runtime harness, all 198 frontend tests, and the solution build are green.
