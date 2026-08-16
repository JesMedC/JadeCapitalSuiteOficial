# Post-Trade Review Specification

## Purpose

A structured post-trade review captures emotionality, setup tags, lessons learned and attachments for every closed trade. Attachments are stored in MinIO via presigned URLs — the client uploads bytes directly to MinIO and the backend persists only the object key, mime type, size and SHA-256 hash.

## Requirements

### Requirement: One review per closed trade

Every closed trade MUST be allowed exactly one review. Creating a review for an `Open` or `Cancelled` trade MUST be rejected with `409 Conflict`. Creating a second review for a closed trade MUST be rejected with `409 Conflict`. Updating the existing review MUST be allowed via `PUT /api/trades/{id}/review`.

#### Scenario: First review of a closed trade

- GIVEN an authenticated user and a trade they own with `status = Closed`
- WHEN `POST /api/trades/{id}/review` is submitted with valid fields
- THEN a single review MUST be persisted linked to the trade
- AND the response MUST include the review id, the trade id and the persisted fields

#### Scenario: Review of an open trade

- GIVEN an authenticated user and a trade they own with `status = Open`
- WHEN `POST /api/trades/{id}/review` is submitted
- THEN the response MUST be `409 Conflict` with `code = "conflict:trade_not_closed"`
- AND no review row MUST be persisted

#### Scenario: Duplicate review attempt

- GIVEN a closed trade that already has a review
- WHEN `POST /api/trades/{id}/review` is submitted
- THEN the response MUST be `409 Conflict` with `code = "conflict:review_exists"`

### Requirement: Validated review fields

`Emotionality` MUST be one of `Confident | Calm | Anxious | Neutral | Tilted | Frustrated`. `Setup` MUST be a free-text tag of `≤ 80` chars (trimmed). `Lessons` MUST be `≤ 2000` chars. `Rating` (1–5) MUST be in `[1, 5]` when present. Out-of-range or over-long values MUST be rejected with a `validation` error.

#### Scenario: Out-of-range rating

- GIVEN a review payload with `rating = 7`
- WHEN the review is validated
- THEN the response MUST be `422` with a `validation` error for `rating`
- AND no review row MUST be persisted

### Requirement: Attachment presigned upload

The attachment flow MUST be:

1. `POST /api/trades/{id}/review/attachments` with `{ fileName, contentType, sizeBytes }` returns a presigned upload URL, an `attachmentId`, an `objectKey`, the upload TTL (≤ 15 minutes), and the required headers (e.g. `x-amz-acl` if applicable).
2. The client uploads bytes directly to MinIO using the URL — the .NET backend MUST NOT proxy the bytes.
3. `POST /api/trades/{id}/review/attachments/{attachmentId}/complete` with the SHA-256 hash confirms the upload; the backend MUST verify the object exists and its size matches the original `sizeBytes`; the persisted row MUST record `objectKey`, `contentType`, `sizeBytes`, `sha256`.

#### Scenario: Full upload flow

- GIVEN an authenticated user and a closed trade
- WHEN they request an attachment slot, upload the bytes to MinIO, and call `complete`
- THEN a row MUST be persisted in `trading.trade_attachments` linked to the review
- AND the row MUST record `objectKey` like `trading/attachments/{userId}/{tradeId}/{attachmentId}/{fileName}`, `contentType`, `sizeBytes`, `sha256`

#### Scenario: Backend never proxies bytes

- GIVEN any attachment upload
- WHEN the system records the operation
- THEN the .NET backend MUST NOT log or persist the file bytes
- AND MUST NOT add an endpoint that accepts `multipart/form-data` for attachments

### Requirement: MinIO bucket isolation

All attachments MUST live under the key prefix `trading/attachments/{userId}/{tradeId}/` inside the configured bucket. The bucket name MUST come from configuration (`Minio:Bucket`); the backend MUST reject writes that would escape the prefix. Bucket provisioning MUST be idempotent on startup.

#### Scenario: Cross-user prefix attempt

- GIVEN an authenticated user
- WHEN they request an attachment slot
- THEN the returned `objectKey` MUST begin with `trading/attachments/{theirUserId}/`
- AND the request MUST NOT succeed if the payload attempts to override the prefix

### Requirement: Authorization and isolation

The review and attachment endpoints MUST require an authenticated Trader identity and MUST scope every operation to the caller's user id. Cross-user reads, writes, attachment uploads or completions MUST be impossible.

#### Scenario: Cross-user review attempt

- GIVEN an authenticated user submits a review for another user's trade
- WHEN the handler runs
- THEN the response MUST be `404 Not Found`
- AND no row MUST be persisted

#### Scenario: Attachment for another user's review

- GIVEN an authenticated user requests an attachment slot for another user's review
- WHEN the handler runs
- THEN the response MUST be `404 Not Found`
- AND no attachment row MUST be created
- AND no presigned URL MUST be returned

### Requirement: Domain events

The backend MUST raise `TradeReviewCreatedDomainEvent` when a review is first created and `TradeAttachmentUploadedDomainEvent` when an attachment is marked complete. Events MUST NOT contain file bytes, presigned URLs or SHA-256 hashes (only `attachmentId`, `objectKey`, `sizeBytes`, `contentType`).

#### Scenario: Event emission

- GIVEN a review and attachment are persisted
- WHEN the handler commits
- THEN exactly one `TradeReviewCreatedDomainEvent` and one `TradeAttachmentUploadedDomainEvent` MUST be raised per operation
- AND the events MUST NOT include PII, presigned URLs or file bytes