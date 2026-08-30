# Security Policy

## Reporting a vulnerability

Email: security@jadecapital.example.com

We will acknowledge within 48 hours and provide a remediation timeline within 7 days.

## Supported versions

| Version | Supported          |
|---------|--------------------|
| 1.0.0   | :white_check_mark: |
| < 1.0.0 | :x:                |

## Security features

- JWT HS256 + PBKDF2 password hashing
- Refresh token rotation
- 5 rate-limit policies (auth-strict, api-general, api-quotes, api-billing, recovery)
- PiiLogScrubber in Program.cs
- CSP + HSTS + Permissions-Policy headers
- Multi-tenant query filter + tenant_id JWT claim
- Audit log retention (90 days) + tamper-evident pattern (Wave 11+)
- GDPR Art. 17 cascade deletor (30-day grace)

## Disclosure timeline

- T+0: Report received
- T+48h: Acknowledgement
- T+7d: Remediation timeline
- T+30d: Patch released
- T+90d: Public disclosure (if critical)
