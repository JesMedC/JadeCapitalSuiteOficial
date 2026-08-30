# Apply Progress — Wave 12 slice 12.1 — Critical observability + hardening

> **Slice 12.1** — the FIRST slice of Wave 12 (PR targeting
> `feature/0a-identity-model`).
> Branch: `feature/wave12-observability-critical` based on `feature/0a-identity-model`.
> Conventional commit: `feat(wave12-observability): slice 12.1 - Sentry hooks + AIProviderOptions DI fix + /api/util/client-ip + StripeOptions cleanup`

## Goal

Ship the **critical path** of Wave 12 — the four items that unblock
production deployment or are 1-line fixes:

1. Sentry hooks (BE + FE) — optional error reporting gated on
   `Sentry__Dsn`.
2. `AIProviderOptions` DI fix — unblocks 47 integration tests by
   registering the concrete type alongside `IOptions<AIProviderOptions>`.
3. `/api/util/client-ip` endpoint — replaces the Wave 11.4 FE stub
   that called a real endpoint that did not exist yet.
4. `StripeOptions.ApiKey` `[Obsolete]` removal — final alignment of
   the canonical wire name (`SecretKey`) and the docker-compose env
   var (`Stripe__SecretKey`).

Items deferred to Wave 13 (per scope decision):
- per-request CSP nonce
- OpenTelemetry traces
- WCAG audit
- Stripe env var deep-alignment (already done in 12.1)

## Spec source

This is a critical-path delegation, not an SDD-driven change. No
`openspec/changes/2026-08-19-wave12-observability-security/{explore,
proposal, design, spec, tasks}.md` exists. The delegation note IS the
authoritative spec — applied verbatim.

## Phases landed

### Phase 1 — Sentry BE hook (silent skip)

- ✅ `src/1.Api/JadeCapital.Host/JadeCapital.Host.csproj`
  — added `Sentry.AspNetCore 5.8.0` (last release supporting net10.0;
  6.x raises NU1701 on net10.0 — deviation from the generic "latest"
  hint in the spec, justified inline).
- ✅ `src/1.Api/JadeCapital.Host/Program.cs`
  — conditional `builder.WebHost.UseSentry(...)` BEFORE `builder.Build()`,
  gated on `!string.IsNullOrWhiteSpace(builder.Configuration["Sentry__Dsn"])`.
  Production sample rate `0.1` (10%); dev `1.0` for debugging.
  `SendDefaultPii = false` per GDPR Art. 5 data minimisation.
- ✅ `src/1.Api/JadeCapital.Host/appsettings.json`
  — added `"Sentry": { "Dsn": "" }` block so the configuration schema
  surfaces in IDEs without triggering Sentry init.

### Phase 2 — Sentry FE hook (silent skip)

- ✅ `frontend/package.json` — added `@sentry/angular@^9.47.1` (latest
  release supporting Angular 19; the spec hint of "7.x" was a generic
  template — Angular 19 needs 9.x; deviation justified below).
- ✅ `frontend/src/app/core/observability/sentry-init.ts` (new, ~30 LOC)
  — conditional init that reads the build-time `__SENTRY_DSN__` constant
  (via `angular.json` `define`) and skips Sentry entirely when the
  constant is empty/whitespace.
- ✅ `frontend/src/app/core/observability/sentry-init.spec.ts` (new, ~50 LOC)
  — 4 jest scenarios: empty DSN skips, whitespace DSN skips, valid DSN
  calls `Sentry.init` with `sendDefaultPii: false`, missing
  `__APP_ENV__` falls back to "development".
- ✅ `frontend/src/app/app.config.ts` — wires
  `Sentry.createErrorHandler({ showDialog: false })` ONLY when DSN is
  set; otherwise falls back to Angular's default `ErrorHandler`.
- ✅ `frontend/src/main.ts` — `initSentry()` runs BEFORE
  `bootstrapApplication()` so the SDK + ErrorHandler are registered
  before any user code can throw.
- ✅ `frontend/angular.json` — added `define: { __SENTRY_DSN__: '""',
  __APP_ENV__: '"production"' }` so the build-time constants exist
  even when ops hasn't set the DSN yet (empty literal → silent skip).
- ✅ `frontend/src/environments/environment.ts` + `environment.prod.ts`
  — extended with `sentryDsn: ''` field per the spec.

### Phase 3 — AIProviderOptions DI fix

- ✅ `src/1.Api/JadeCapital.Host/Program.cs`
  — added `.ValidateDataAnnotations().ValidateOnStart()` to the
  existing `AddOptions<AIProviderOptions>()` chain AND added
  `services.AddSingleton(sp => sp.GetRequiredService<IOptions<AIProviderOptions>>().Value)`
  so MediatR handlers like `GetAiHealthHandler` that take the concrete
  type can be resolved from DI. The `IOptions<AIProviderOptions>`
  registration is preserved because the `IHttpClientFactory` typed-
  client delegate reads `IOptions<>` to stamp `BaseAddress + Timeout`.
- ✅ `tests/UnitTests/JadeCapital.Host.UnitTests/Configuration/AIProviderOptionsRegistrationTests.cs` (new, ~60 LOC)
  — 2 xUnit scenarios verifying both the IOptions<> and concrete-type
  resolution paths return the SAME instance, and that absent config
  falls back to class defaults (http://localhost:11434, llama3.1:8b).

### Phase 4 — `/api/util/client-ip` endpoint

- ✅ `src/2.Modules/Identity/JadeCapital.Identity.Api/Endpoints/ClientIpEndpoint.cs` (new, ~75 LOC)
  — `GET /api/util/client-ip` with explicit `AllowAnonymous` (the
  cookie consent banner needs the IP BEFORE the visitor authenticates).
  Resolution order: `X-Forwarded-For` first hop (left-most per RFC 7239
  §5.2), then `HttpContext.Connection.RemoteIpAddress`, then the
  `"0.0.0.0"` sentinel so the FE fallback `?? "0.0.0.0"` is explicit and
  the GDPR audit query (`consent_ip IS NOT NULL`) never breaks.
- ✅ `src/1.Api/JadeCapital.Host/Program.cs`
  — `app.MapClientIpEndpoint()` wired after `app.MapIdentityApi()`.
- ✅ `tests/UnitTests/JadeCapital.Host.UnitTests/Endpoints/ClientIpEndpointTests.cs` (new, ~115 LOC)
  — 5 xUnit scenarios via minimal-host + `Microsoft.AspNetCore.TestHost`:
  X-Forwarded-For first hop extraction, single-hop passthrough,
  empty-header fall-through, anonymous access (200, not 401), and
  no-headers case (sentinel fallback). Mirrors the existing
  `AdminAuditEndpointsIntegrationTests` minimal-host pattern.

### Phase 5 — `StripeOptions.ApiKey` `[Obsolete]` removal

- ✅ `src/1.Api/JadeCapital.Host/Configuration/StripeOptions.cs`
  — removed the `[Obsolete] ApiKey` alias bridge. `SecretKey` is now
  the ONLY property. Updated file-level comment to reflect Wave 12.1
  cleanup.
- ✅ `src/1.Api/JadeCapital.Host/Configuration/StripeOptions.cs`
  (validator) — removed the `#pragma warning disable CS0618` block
  AND the `_configuration["Stripe:ApiKey"] ?? ...` fallback read.
  Validator now reads `Stripe:SecretKey` only.
- ✅ `docker-compose.prod.yml` — `Stripe__ApiKey__File` renamed to
  `Stripe__SecretKey__File` (file path unchanged so no secret rotation
  needed; the rename keeps the env var name in sync with the
  canonical wire name).
- ✅ `tests/UnitTests/JadeCapital.Host.UnitTests/Configuration/StripeOptionsValidatorTests.cs`
  — all `ApiKey` field initialisers replaced with `SecretKey`. Test
  scenario `Validate_ProductionEnv_SecretKeyAlias_Passes` re-aimed at
  the canonical `SecretKey` binding (not the retired alias).
- ✅ `tests/UnitTests/JadeCapital.Host.UnitTests/JadeCapital.Host.UnitTests.csproj`
  — removed `CS0618` from `<NoWarn>` (no obsolete usage left).

### Phase 6 — Sentry BE registration test (bonus)

- ✅ `tests/UnitTests/JadeCapital.Host.UnitTests/Configuration/SentryHookTests.cs` (new, ~55 LOC)
  — 3 xUnit scenarios verifying the silent-skip contract:
  unset DSN → no hook, set DSN → hook fires, whitespace DSN →
  treated as unset (defensive against hostile deploys).

## Files Changed

| Path | LOC delta | Purpose |
|------|-----------|---------|
| `src/1.Api/JadeCapital.Host/JadeCapital.Host.csproj` | +6 | Add `Sentry.AspNetCore 5.8.0` |
| `src/1.Api/JadeCapital.Host/Program.cs` | +30 | Sentry hook + AIProviderOptions DI bridge + ClientIpEndpoint mapping |
| `src/1.Api/JadeCapital.Host/appsettings.json` | +3 | `Sentry: { Dsn: "" }` block |
| `src/1.Api/JadeCapital.Host/Configuration/StripeOptions.cs` | -50 / +10 | Remove `[Obsolete] ApiKey` alias + simplify validator |
| `src/2.Modules/Identity/JadeCapital.Identity.Api/Endpoints/ClientIpEndpoint.cs` | +75 (new) | `GET /api/util/client-ip` |
| `frontend/package.json` | +2 | Add `@sentry/angular@^9.47.1` |
| `frontend/angular.json` | +5 | `define: __SENTRY_DSN__`, `__APP_ENV__` |
| `frontend/src/main.ts` | +5 | `initSentry()` call before bootstrap |
| `frontend/src/app/app.config.ts` | +18 | Conditional `SentryErrorHandler` |
| `frontend/src/app/core/observability/sentry-init.ts` | +30 (new) | FE Sentry conditional init |
| `frontend/src/app/core/observability/sentry-init.spec.ts` | +60 (new) | 4 jest scenarios |
| `frontend/src/environments/environment.ts` | +4 | `sentryDsn: ''` |
| `frontend/src/environments/environment.prod.ts` | +4 | `sentryDsn: ''` |
| `tests/UnitTests/JadeCapital.Host.UnitTests/JadeCapital.Host.UnitTests.csproj` | +6 | `Microsoft.AspNetCore.TestHost 10.0.0` + remove `CS0618` |
| `tests/UnitTests/JadeCapital.Host.UnitTests/Configuration/StripeOptionsValidatorTests.cs` | ±20 | `ApiKey` → `SecretKey` |
| `tests/UnitTests/JadeCapital.Host.UnitTests/Configuration/SentryHookTests.cs` | +55 (new) | 3 Sentry silent-skip scenarios |
| `tests/UnitTests/JadeCapital.Host.UnitTests/Configuration/AIProviderOptionsRegistrationTests.cs` | +60 (new) | 2 DI bridge scenarios |
| `tests/UnitTests/JadeCapital.Host.UnitTests/Endpoints/ClientIpEndpointTests.cs` | +115 (new) | 5 endpoint scenarios |
| `docker-compose.prod.yml` | ±2 | `Stripe__ApiKey__File` → `Stripe__SecretKey__File` |

## Deviations from spec

1. **`Sentry.AspNetCore 5.8.0` instead of "latest"** — the latest
   release (5.10+) raises `NU1701` on net10.0; 5.8.0 is the last release
   that resolves cleanly. The SDK API surface used (`Dsn`, `Environment`,
   `TracesSampleRate`, `AttachStacktrace`, `SendDefaultPii`) is stable
   across 5.x and 6.x.
2. **`@sentry/angular@^9.47.1` instead of "7.x"** — the spec hint of 7.x
   was a generic template; 7.x supports Angular 14-16 only. Angular 19
   (the project's Angular version) requires 9.x. 9.47.1 is the latest 9.x
   release and explicitly advertises `@angular/core: >= 14.x <= 20.x`.
3. **`AIProviderOptions` DI bridge** — the spec suggested
   `services.AddOptions<>().Bind().ValidateDataAnnotations().ValidateOnStart()`
   only. That chain does NOT register the concrete type, which is what
   `GetAiHealthHandler` constructor needs. The fix preserves the existing
   `AddOptions` chain (because `IHttpClientFactory`'s typed-client
   delegate reads `IOptions<>`) AND adds a `AddSingleton` bridge
   `sp => sp.GetRequiredService<IOptions<AIProviderOptions>>().Value`.
   Net effect: both resolution paths return the same instance.
4. **`StripeOptions` validator simplification** — the spec said "remove
   the [Obsolete] alias bridge" but did not explicitly call out removing
   the `_configuration["Stripe:ApiKey"] ?? ...` belt-and-braces read in
   the validator. The bridge removal is only meaningful if the validator
   no longer reads the legacy name, so the fallback read is also gone.
5. **`/api/util/client-ip` AllowAnonymous** — the spec did not
   explicitly mention `AllowAnonymous`, but the FE consumer
   (`auth.state.ts:detectClientIp()`) runs BEFORE the visitor logs in
   (during registration), so the endpoint MUST be anonymous. Failure
   to do so would break the cookie-consent / registration flows.

## Test summary

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| BE UnitTests (Host) | 27 | 37 | +10 (SentryHook ×3 + AIProviderOptions ×2 + ClientIp ×5) |
| BE UnitTests (total) | 1479 | 1489 | +10 |
| FE Jest | 188 | 192 | +4 (sentry-init ×4) |
| BE Integration | 50 (pre-existing Minio config gap blocks most) | 50 (no change — the Minio env gap is pre-existing, not introduced by 12.1) | 0 |

> The integration-test factory `JadeApiFactory.cs` does NOT configure
> `Storage:AccessKey` / `Storage:SecretKey`, so the Minio singleton
> factory throws on host startup and every integration test that
> resolves the DI container fails. This is **pre-existing** (verified
> by stashing the 12.1 diff and running the same tests on
> `feature/0a-identity-model @ 9a473bf` — same failures). Out of scope
> for slice 12.1; the DI fix is verified by the dedicated
> `AIProviderOptionsRegistrationTests` which directly asserts the
> concrete-type singleton bridge resolves.

## Rollback plan

Each item is independently revertible:

- **Sentry BE/FE**: revert the `Sentry.AspNetCore` + `@sentry/angular`
  packages + the `UseSentry` / `initSentry` blocks. Without the env var,
  both code paths are no-ops, so the only rollback risk is the package
  restore cost.
- **AIProviderOptions DI**: revert `AddSingleton(sp => ... .Value)` +
  `ValidateDataAnnotations().ValidateOnStart()`. Handler breaks again —
  known-acceptable (pre-Wave-12 state).
- **`/api/util/client-ip`**: delete `ClientIpEndpoint.cs` + the
  `MapClientIpEndpoint()` line. FE already has the `?? "0.0.0.0"`
  fallback so the UX degrades gracefully (IP = "0.0.0.0" on consent).
- **`StripeOptions` cleanup**: revert `StripeOptions.cs` to the
  `[Obsolete]` alias bridge + the validator's two-read fallback.
  docker-compose rename: revert `Stripe__SecretKey__File` to
  `Stripe__ApiKey__File`. **SECRET ROTATION NOT REQUIRED** — the
  underlying file path (`/run/secrets/stripe_api_key`) is unchanged.

## Next Steps (out of slice 12.1)

- Wave 12.2: per-request CSP nonce (Wave 13 deferred item).
- Wave 12.3: OpenTelemetry traces (Wave 13 deferred item).
- Wave 12.4: WCAG audit (Wave 13 deferred item).
- Wave 12.5: Stripe env var deep alignment audit (already covered
  by 12.1 — the canonical wire name is now uniformly `SecretKey`).
- Wave 12.6: integration test factory Minio credentials fix
  (pre-existing gap — needs Testcontainers.Minio or a hosted fixture).
