# Delta for GDPR Compliance

## ADDED Requirements

### Requirement: Consent client IP honors configured proxy trust

The Host MUST trust forwarding from configured proxies or networks only. Identity MUST record the resolved address and MUST NOT parse `X-Forwarded-For`. Consent MUST remain unchanged.

#### Scenario: Trusted proxy resolves client
- GIVEN a trusted proxy forwards a client address
- WHEN consent is recorded
- THEN consent IP MUST equal the originating address

#### Scenario: Untrusted sender cannot spoof client IP
- GIVEN an untrusted peer claims `203.0.113.9`
- WHEN consent is recorded
- THEN consent IP MUST equal the peer address, not `203.0.113.9`

#### Scenario: Direct request uses peer
- GIVEN a direct request
- WHEN consent is recorded
- THEN consent IP MUST equal the direct remote address
