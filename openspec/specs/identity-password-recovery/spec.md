# Identity Password Recovery Specification

## Purpose

Secure self-service recovery and mandatory credential rotation without granting normal portal access prematurely.

## Requirements

### Requirement: Uniform recovery request

The system MUST accept an email, apply a dedicated recovery throttle, and preserve the same generic response and externally observable timing semantics regardless of account existence, eligibility, state, throttle outcome, or email-delivery result. It MUST NOT expose plaintext credentials through logs, telemetry, errors, or persistence.

#### Scenario: Existing or unknown email
- GIVEN any syntactically valid email
- WHEN recovery is requested
- THEN the response status, body, and externally observable timing MUST be indistinguishable
- AND only an eligible existing account MAY receive the Spanish Jade-branded email

#### Scenario: Throttled request or transport failure
- GIVEN the recovery limit is exceeded or email transport fails
- WHEN recovery is requested
- THEN the uniform response and generic timing semantics MUST still be preserved without account or transport disclosure
- AND no plaintext temporary password MUST appear outside the selected email transport

### Requirement: Temporary credential lifecycle

The recovery artifact MUST remain a cryptographically generated temporary password with at least 128 bits of entropy, encoded as exactly 26 Crockford base-32 characters. It MUST expire after 24 hours, be stored only as a salted hash, be single-use, and remain valid only for the latest committed generation. For an eligible account, the database commit of that generation in the Activated state MUST complete before SMTP submission. Once submission begins, ambiguous SMTP acceptance MUST NOT invalidate the committed credential or change the generic public outcome. Failed temporary-password attempts MUST count toward the existing five-attempt lockout.

#### Scenario: Repeated or concurrent resets
- GIVEN multiple recovery requests for one account, including concurrent requests
- WHEN issuance completes
- THEN exactly one latest committed temporary password MUST remain valid
- AND all earlier temporary passwords MUST fail

#### Scenario: Commit before delivery
- GIVEN an eligible account has a new temporary password ready for delivery
- WHEN recovery issuance reaches SMTP submission
- THEN the latest generation MUST already be committed in the Activated state
- AND ambiguous SMTP acceptance MUST NOT invalidate that credential or alter the public outcome

#### Scenario: Expiry, reuse, and lockout
- GIVEN a temporary password is expired, consumed, incorrect, or attempted against a locked account
- WHEN login is attempted
- THEN authentication MUST fail without revealing the cause
- AND each incorrect attempt MUST contribute to the shared five-attempt lockout

### Requirement: Restricted forced-change authentication

The system MUST expose a public temporary-login entry point. Successful temporary-password login MUST consume the credential and grant only the capability required to change that user's password. The restricted session MUST carry no normal role, MUST NOT include a refresh token, and MUST NOT authorize Trader, Admin, or any unrelated authenticated endpoint. Signed claims and server-held grant/session state MUST be authoritative; client-supplied grant or session values MUST NOT establish or broaden authority.

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
- AND client-supplied grant or session values MUST NOT alter authorization

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

After a successful password change, the system MUST revoke all access/refresh tokens and sessions created before the change. A successful forced change MUST return one clean normal replacement session with the user's existing role and MUST replace the restricted recovery session. No pre-change token MUST regain access through refresh or race conditions.

#### Scenario: Rotation completes
- GIVEN multiple pre-change sessions exist
- WHEN password change succeeds
- THEN every pre-change session/token MUST be unusable
- AND the response MUST establish one replacement session with normal access appropriate to the user's existing role

### Requirement: Environment-specific email delivery

Production MUST use MailKit SMTP, local development MUST capture SMTP mail in Mailpit, and tests MUST use deterministic in-memory capture. All transports MUST preserve identical recovery semantics and MUST NOT log message bodies or credentials. Production SMTP provider, port, and explicit TLS-mode configuration remain separate in issue #86 and are outside this specification.

#### Scenario: Transport selection
- GIVEN production, local, or test configuration
- WHEN a recovery email is sent
- THEN the configured transport MUST be used without changing security behavior

### Requirement: Accessible forced-change user experience

Forgot-password, temporary-login, and forced-change views MUST preserve Angular 19 strict TypeScript, Signals, OnPush, and SCSS conventions; provide loading, success, validation, safe error, and responsive states; and expose labeled controls, keyboard operation, focus management, and announced status messages. The recovery email's next action MUST open a runnable public temporary-login continuation. After forced change, the frontend MUST replace restricted recovery state with the returned normal session and continue to the dashboard.

#### Scenario: Submit and recover from error
- GIVEN a keyboard or mobile user submits a recovery form
- WHEN processing succeeds or fails
- THEN duplicate submission MUST be prevented while loading
- AND focus and an accessible status message MUST identify the next action without enumeration

#### Scenario: Runnable recovery continuation
- GIVEN a user follows the next action from the recovery email
- WHEN temporary login and forced password change succeed
- THEN the frontend MUST replace the restricted state with the returned normal session
- AND the user MUST continue to the dashboard as a normally authenticated user

### Requirement: Portal regression boundary

PublicPortal and existing Trader behavior MUST remain unchanged except that forced-change authentication is intercepted before Trader access. Registration and ordinary login MUST retain existing behavior.

#### Scenario: Existing journey
- GIVEN a normal authenticated user or public visitor
- WHEN existing Trader or PublicPortal journeys are used
- THEN routes, behavior, and visible content MUST remain unchanged
