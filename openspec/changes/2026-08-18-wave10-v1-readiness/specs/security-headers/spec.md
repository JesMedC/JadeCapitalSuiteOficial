# Security Headers Specification

**Change**: 2026-08-18-wave10-v1-readiness
**Wave**: 10 (v1 readiness)
**Slice**: 10.3 — Security headers + TLS termination
**Status**: NEW spec (no prior canonical)
**Strict TDD**: ACTIVE — every Requirement + Scenario here must be covered by tests in slice 10.3

## Purpose

Ship browser-hardening HTTP response headers on every nginx-served response and terminate TLS at the edge via Let's Encrypt. The current `infrastructure/nginx/nginx.conf` only sets `X-Content-Type-Options`, `X-Frame-Options`, and `Referrer-Policy` — `Content-Security-Policy`, `Strict-Transport-Security`, and `Permissions-Policy` are missing entirely. For v1.0.0-rc1 the CSP ships in **relaxed mode** (`script-src 'self' 'nonce-{per-request}'; style-src 'self' 'unsafe-inline'`) — strict nonce-only CSP is deferred to Wave 11+ because Angular injects `<script>` tags at build time and per-request nonce requires Angular template + build orchestration changes. HSTS ships preloaded with `max-age=63072000; includeSubDomains; preload`. Caddy handles auto-TLS via the Let's Encrypt DNS-01 challenge for `jadecapital.com`.

## ADDED Requirements

### Requirement: Content-Security-Policy header on all nginx responses

The system MUST add a `Content-Security-Policy` header to every nginx-served response in `infrastructure/nginx/nginx.conf`. The CSP MUST include: `default-src 'self'; script-src 'self' 'nonce-{per-request}'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; connect-src 'self' https://api.jadecapital.com wss:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'`. The nonce MUST be unique per request and emitted by a small nginx Lua or `sub_filter` snippet that injects `<meta http-equiv="Content-Security-Policy" content="script-src 'self' 'nonce-{nonce}'">` into the HTML. Strict nonce-only CSP (no `'unsafe-inline'`) is OUT OF SCOPE for v1.0.0-rc1.

#### Scenario: CSP header includes script-src 'self' 'nonce-{per-request}'

- GIVEN nginx serves a response
- WHEN `curl -I https://jadecapital.com/` runs
- THEN the `Content-Security-Policy` header MUST contain `script-src 'self' 'nonce-`
- AND the nonce value MUST differ across two consecutive requests

#### Scenario: CSP header includes style-src 'self' 'unsafe-inline'

- GIVEN nginx serves a response
- WHEN the response is inspected
- THEN `style-src 'self' 'unsafe-inline'` MUST be present in the CSP
- AND `'unsafe-inline'` for scripts MUST NOT be present (only nonce + 'self')

#### Scenario: CSP header includes frame-ancestors 'none'

- GIVEN nginx serves any response
- WHEN the CSP header is parsed
- THEN `frame-ancestors 'none'` MUST be present
- AND the page MUST NOT be embeddable in an `<iframe>` (clickjacking defense)

### Requirement: Strict-Transport-Security header on all HTTPS responses

The system MUST add `Strict-Transport-Security: max-age=63072000; includeSubDomains; preload` to every HTTPS response served by nginx. The header MUST be emitted only on `listen 443 ssl` blocks (NOT on the :80 redirect). The `preload` directive MUST enable submission to the Chromium HSTS preload list (https://hstspreload.org). HTTP→HTTPS redirect MUST be configured at the edge.

#### Scenario: HSTS header includes max-age + includeSubDomains + preload

- GIVEN nginx serves an HTTPS response
- WHEN `curl -I https://jadecapital.com/` runs
- THEN the response MUST include `Strict-Transport-Security: max-age=63072000; includeSubDomains; preload`

### Requirement: Permissions-Policy header disables unused browser features

The system MUST add `Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=(), usb=(), magnetometer=(), gyroscope=(), accelerometer=()` to every nginx response. Features not used by Jade Capital Suite MUST be denied by default. Future feature additions MUST remove the relevant directive.

#### Scenario: Permissions-Policy disables camera, microphone, geolocation, payment

- GIVEN nginx serves any response
- WHEN the `Permissions-Policy` header is parsed
- THEN `camera=()`, `microphone=()`, `geolocation=()`, `payment=()` MUST each be present with empty allowlist
- AND the browser MUST block any attempt to invoke these APIs

### Requirement: TLS termination via Let's Encrypt certbot with auto-renewal

The system MUST ship `infrastructure/caddy/Caddyfile` (preferred path — Caddy auto-renews) OR `scripts/setup-tls.sh` (certbot helper for legacy nginx) to terminate TLS. Production MUST use the DNS-01 challenge for `jadecapital.com` + `*.jadecapital.com` (wildcard cert). The certbot cron MUST run twice daily (`0 3,15 * * *`). Self-signed certs MUST be used in dev only (`caddy trust` workflow documented).

#### Scenario: HTTPS serves valid Let's Encrypt cert

- GIVEN the prod Caddyfile is deployed with the DNS-01 challenge
- WHEN `curl -vI https://jadecapital.com/` runs
- THEN the TLS handshake MUST succeed with a cert issued by Let's Encrypt
- AND `openssl s_client -connect jadecapital.com:443` MUST show `issuer=CN = R3, O = Let's Encrypt`

#### Scenario: HTTP→HTTPS redirect works

- GIVEN a client hits `http://jadecapital.com/`
- WHEN the request reaches the edge
- THEN it MUST be redirected to `https://jadecapital.com/` with HTTP 301
- AND the HSTS header MUST NOT be set on the :80 response (HSTS is HTTPS-only)

## Cross-references

- Closes gaps A3 (CSP + HSTS missing), A7 (no TLS termination)
- Related Wave 10 spec: `openspec/changes/2026-08-18-wave10-v1-readiness/specs/deployment-automation/spec.md` (Caddy/nginx runs as a compose service)
- Related Wave 10 spec: `openspec/changes/2026-08-18-wave10-v1-readiness/specs/observability-light/spec.md` (Sentry captures CSP violations via `securitypolicyviolation` events)
- Source: `openspec/changes/2026-08-18-wave10-v1-readiness/explore.md` §A3, §A7

## Out of scope

- Strict nonce-only CSP (no `'unsafe-inline'` for styles) — Wave 11+ (Angular template nonce orchestration)
- HSTS submission to https://hstspreload.org — ops decision at deploy time, NOT automated
- DNSSEC / DNS CAA records (Wave 11+, gap G-C5)
- mTLS between internal services (Wave 11+, gap G-C4)
- Reporting API for CSP violations (`report-uri` directive) — Wave 11+ (defer until violations accumulate)
- Subresource Integrity (SRI) hashes for CDN scripts — n/a (no CDN in v1.0)
