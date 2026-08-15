# Identity Password Recovery Specification

## Purpose

Secure self-service recovery and mandatory credential rotation without granting normal portal access prematurely.

## Requirements

### Requirement: Uniform recovery request

The system MUST accept an email, apply a dedicated recovery throttle, and return the same response regardless of account existence, state, throttle outcome, or email-delivery result. It MUST NOT expose plaintext credentials through logs, telemetry, errors, or persistence.

#### Scenario: Existing or unknown email
- GIVEN any syntactically valid email
- WHEN recovery is requested
- THEN the response status, body, and externally observable timing MUST be indistinguishable
- AND only an eligible existing account MAY receive the Spanish Jade-branded email

#### Scenario: Throttled request or transport failure
- GIVEN the recovery limit is exceeded or email transport fails
- WHEN recovery is requested
- THEN the uniform response MUST still be returned without account or transport disclosure
- AND no plaintext temporary password MUST appear outside the selected email transport

### Requirement: Temporary credential lifecycle

The system MUST email a cryptographically generated temporary password (at least 128 bits of entropy, encoded as 26 Crockford base-32 characters) that expires after 24 hours, is stored only as a hash, is single-use, and supersedes every earlier temporary password. Failed temporary-password attempts MUST count toward the existing five-attempt lockout.

#### Scenario: Repeated or concurrent resets
- GIVEN multiple recovery requests for one account, including concurrent requests
- WHEN issuance completes
- THEN exactly one latest committed temporary password MUST remain valid
- AND all earlier temporary passwords MUST fail

#### Scenario: Expiry, reuse, and lockout
- GIVEN a temporary password is expired, consumed, incorrect, or attempted against a locked account
- WHEN login is attempted
- THEN authentication MUST fail without revealing the cause
- AND each incorrect attempt MUST contribute to the shared five-attempt lockout

### Requirement: Restricted forced-change authentication

Successful temporary-password login MUST consume the credential and grant only the capability required to change that user's password. The resulting token/session MUST NOT authorize Trader, Admin, refresh into broader access, or any unrelated authenticated endpoint.

#### Scenario: Temporary login
- GIVEN a valid unconsumed temporary password
- WHEN the user logs in
- THEN the system MUST mark password change as required and issue restricted authentication
- AND navigation MUST resolve only to the forced-change experience

#### Scenario: Restricted-token misuse
- GIVEN forced-change authentication
- WHEN Trader, Admin, or another normal API is requested
- THEN access MUST be denied without executing domain behavior
- AND only password change and logout MAY remain available

### Requirement: Password policy and ordered history

Every password change MUST verify the current credential, enforce password policy, and reject equality with the current password or any of the five most recent prior passwords. On success, the displaced current hash MUST become newest history and only the five newest prior hashes, ordered deterministically by change time and tie-breaker, MUST be retained.

#### Scenario: Valid change
- GIVEN the current credential is valid and the new password meets policy and history rules
- WHEN password change succeeds
- THEN the previous current hash MUST be inserted first in ordered history
- AND older entries beyond the newest five MUST be removed

#### Scenario: Policy, reuse, or concurrent change failure
- GIVEN the new password is weak/reused or another change wins concurrently
- WHEN the change is committed
- THEN it MUST fail atomically with no history, credential, or token partial update
- AND the client MUST receive a safe actionable error without credential content

### Requirement: Session replacement

After a successful password change, the system MUST revoke all access/refresh tokens and sessions created before the change, then establish one clean unrestricted post-change session according to the design. No pre-change token MUST regain access through refresh or race conditions.

#### Scenario: Rotation completes
- GIVEN multiple pre-change sessions exist
- WHEN password change succeeds
- THEN every pre-change session/token MUST be unusable
- AND the new session MUST have normal access appropriate to the user's existing role

### Requirement: Environment-specific email delivery

Production MUST use MailKit SMTP, local development MUST capture SMTP mail in Mailpit, and tests MUST use deterministic in-memory capture. All transports MUST preserve identical recovery semantics and MUST NOT log message bodies or credentials.

#### Scenario: Transport selection
- GIVEN production, local, or test configuration
- WHEN a recovery email is sent
- THEN the configured transport MUST be used without changing security behavior

### Requirement: Accessible forced-change user experience

Forgot-password and forced-change views MUST preserve Angular 19 strict TypeScript, Signals, OnPush, and SCSS conventions; provide loading, success, validation, safe error, and responsive states; and expose labeled controls, keyboard operation, focus management, and announced status messages.

#### Scenario: Submit and recover from error
- GIVEN a keyboard or mobile user submits either form
- WHEN processing succeeds or fails
- THEN duplicate submission MUST be prevented while loading
- AND focus and an accessible status message MUST identify the next action without enumeration

### Requirement: Portal regression boundary

PublicPortal and existing Trader behavior MUST remain unchanged except that forced-change authentication is intercepted before Trader access. Registration and ordinary login MUST retain existing behavior.

#### Scenario: Existing journey
- GIVEN a normal authenticated user or public visitor
- WHEN existing Trader or PublicPortal journeys are used
- THEN routes, behavior, and visible content MUST remain unchanged
