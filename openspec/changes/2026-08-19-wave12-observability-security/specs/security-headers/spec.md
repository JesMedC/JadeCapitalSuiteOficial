# Delta for Security Headers

## MODIFIED Requirements

### Requirement: Content-Security-Policy header on all nginx responses

Nginx MUST retain CSP, generate a cryptographically random per-request nonce, and inject it into scripts. Header and HTML MUST match and MUST NOT expose `{request_nonce}`. Strict style nonces remain out of scope.
(Previously: nonce substitution did not prove header-to-HTML equality.)

#### Scenario: Header nonce matches HTML
- GIVEN nginx serves HTML with executable scripts
- WHEN the response is inspected
- THEN every script nonce MUST equal the `script-src` nonce

#### Scenario: Requests use distinct nonces
- GIVEN two requests for the same HTML
- WHEN both responses arrive
- THEN each nonce MUST match its response and differ between responses

#### Scenario: Existing CSP protections remain
- GIVEN any nginx response
- WHEN its CSP is parsed
- THEN style `unsafe-inline` and `frame-ancestors 'none'` MUST remain; script `unsafe-inline` MUST remain absent
