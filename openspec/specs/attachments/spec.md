# Attachments Specification

## Purpose

Complete the wiring of `IAttachmentStorage` (introduced in Wave 1d) with lifecycle management, thumbnail generation, quota enforcement, and a virus scan interface stub. Wave 1d shipped the upload/download/confirmation handlers; Wave 4d adds the operational hygiene: auto-cleanup of old attachments, on-demand thumbnails for images, per-user quota enforcement, and a pluggable virus scan hook.

This spec covers the storage behavior, the quota contract, the lifecycle sweep, and the virus scan stub. The base attachment upload flow (Request → Upload → Confirm) is unchanged from Wave 1d.

## Requirements

### Requirement: Attachment quota contract

The system MUST enforce a per-user quota defined by `AttachmentQuota { MaxTotalBytes: 52_428_800 (50 MiB), MaxAttachmentCount: 100, ExpirationDays: 90 }`. Quotas are evaluated BEFORE presigning an upload. If the user would exceed the quota, the system MUST return `413 Payload Too Large` with `error.code = "attachment.quota_exceeded"`.

#### Scenario: Quota check passes

- GIVEN user A has 5 attachments totaling 10 MB
- WHEN `POST /api/trades/{tradeId}/attachments/request-upload` with a 5 MB file
- THEN the quota MUST pass (total would be 15 MB < 50 MB)
- AND the response MUST be 201 + presigned PUT URL

#### Scenario: Total size exceeds quota

- GIVEN user A has 1 attachment totaling 48 MB
- WHEN `POST /api/trades/{tradeId}/attachments/request-upload` with a 5 MB file
- THEN the response MUST be 413
- AND `error.code` MUST be `"attachment.quota_exceeded"`
- AND `error.message` MUST indicate which limit was exceeded

#### Scenario: Attachment count exceeds quota

- GIVEN user A has 99 attachments
- WHEN `POST /api/trades/{tradeId}/attachments/request-upload` with any file
- THEN the response MUST be 413 (count 100 reached)

#### Scenario: Quota check considers only confirmed attachments

- GIVEN user A has 3 unconfirmed uploads (not yet in DB)
- WHEN a 4th upload is requested
- THEN only the confirmed attachments count toward quota
- AND the 4th upload MUST succeed if it fits

### Requirement: Attachment lifecycle (90 days)

A `AttachmentLifecycleService` (BackgroundService) MUST run daily at 02:00 UTC (±30min jitter). It MUST soft-delete attachments where `expires_at < now()` AND `is_active = true`. Soft-delete sets `is_active = false` and removes the MinIO object via `IAttachmentStorage.DeleteAsync(objectKey)`. The `expires_at` column is set at upload confirmation time as `confirmed_at + 90 days`.

#### Scenario: Daily sweep

- GIVEN 3 attachments exist: 1 with `expires_at = today - 1d` (expired), 1 with `expires_at = today + 1d` (active), 1 with `expires_at = today - 30d` (expired)
- WHEN the daily sweep runs
- THEN the 2 expired attachments MUST be soft-deleted (is_active = false)
- AND their MinIO objects MUST be deleted
- AND the active attachment MUST be untouched

#### Scenario: MinIO bucket policy backup

- GIVEN the MinIO bucket has a lifecycle policy with `Expiration: Days=90`
- WHEN an attachment's MinIO object reaches 90 days
- THEN MinIO MUST delete the object independently of the DB sweep
- AND the DB sweep is belt-and-suspenders — not a hard requirement for cleanup

#### Scenario: Sweep survives transient MinIO errors

- GIVEN MinIO returns 503 for one delete call
- WHEN the sweep processes that attachment
- THEN the service MUST log the error and continue with the next
- AND the failed attachment MUST be retried on the next sweep (idempotent soft-delete)

### Requirement: Thumbnail generation

`GET /api/attachments/{attachmentId}/thumbnail?width=200&height=200` MUST return 302 + presigned GET URL pointing to a MinIO object with transform parameters (`?width=200&height=200`). The endpoint MUST work only for image MIME types (`image/png, image/jpeg, image/webp`). Non-images MUST return 415 Unsupported Media Type.

#### Scenario: Image thumbnail

- GIVEN attachment `id = A1` is `image/png`
- WHEN `GET /api/attachments/A1/thumbnail?width=200&height=200` is called
- THEN the response MUST be 302 with `Location` header = presigned URL containing transform params
- AND following the Location MUST return the resized image

#### Scenario: Non-image thumbnail rejected

- GIVEN attachment `id = A1` is `application/pdf`
- WHEN `GET /api/attachments/A1/thumbnail?width=200&height=200` is called
- THEN the response MUST be 415 with `error.code = "attachment.thumbnail_not_supported"`

#### Scenario: Cross-user thumbnail access

- GIVEN user A has attachment `id = A1`
- WHEN user B calls `GET /api/attachments/A1/thumbnail?width=200&height=200`
- THEN the response MUST be 404

### Requirement: Storage usage endpoint

`GET /api/attachments/usage` MUST return the user's current usage `{ totalBytes, attachmentCount, quotaBytes, quotaCount, percentFull }`. This is the data source for a UI indicator (Wave 4d ships the endpoint; the UI indicator is a frontend task in 4d.2).

#### Scenario: Usage report

- GIVEN user A has 12 attachments totaling 8_500_000 bytes
- WHEN `GET /api/attachments/usage` is called
- THEN the response MUST be `{ totalBytes: 8_500_000, attachmentCount: 12, quotaBytes: 52_428_800, quotaCount: 100, percentFull: 16.21 }`

### Requirement: Virus scan stub

The system MUST define `IVirusScanner` interface in `Shared.Kernel.Storage`. The default implementation (`VirusScannerNoOp`) MUST always return `ScanResult.Clean`. Wave 6 will swap in a real ClamAV impl. The interface is invoked at upload confirmation time; if the scanner throws, the upload is rejected with `503 Service Unavailable`.

#### Scenario: No-op scanner passes

- GIVEN the default `VirusScannerNoOp` is registered
- WHEN a user confirms an upload
- THEN the scan MUST return `Clean`
- AND the attachment MUST be confirmed normally

#### Scenario: Real scanner throws

- GIVEN a real `IVirusScanner` impl is registered (Wave 6+)
- WHEN the scanner throws `ScannerUnavailableException`
- THEN the confirm endpoint MUST return 503
- AND the upload MUST NOT be persisted
- AND the partial MinIO object MUST be deleted

## Data Model

```
trading.trade_attachments (additive columns)
  thumbnail_object_key  VARCHAR(255) NULL     -- MinIO key for cached thumbnail (optional)
  bytes                 BIGINT NOT NULL DEFAULT 0  -- file size
  expires_at            TIMESTAMPTZ NULL       -- confirmed_at + 90 days
  virus_scanned_at      TIMESTAMPTZ NULL       -- set after scan passes
  scan_result           SMALLINT NOT NULL DEFAULT 0  -- 0=NotScanned, 1=Clean, 2=Infected, 3=Error

Indexes:
  ix_trade_attachments_user_active (user_id) WHERE is_active = true
  ix_trade_attachments_expires_sweep (expires_at) WHERE is_active = true AND expires_at IS NOT NULL
```

The `ix_trade_attachments_expires_sweep` index makes the daily sweep efficient (range scan on `expires_at`).

## Endpoints

- `POST /api/trades/{tradeId}/attachments/request-upload` — quota check + presign (existing from Wave 1d, now quota-aware).
- `POST /api/trades/{tradeId}/attachments/{attachmentId}/confirm` — virus scan + persist (existing from Wave 1d, now virus-scan-aware).
- `GET /api/attachments/{attachmentId}/thumbnail?width=N&height=N` — presigned thumbnail URL (NEW).
- `GET /api/attachments/usage` — storage usage report (NEW).
- Existing: `DELETE /api/trades/{tradeId}/attachments/{attachmentId}` (Wave 1d).

All require `RequireAuthorization` and `api-attachments` rate limit.

## Architecture

- **Shared.Kernel/Storage/AttachmentQuota.cs** — record.
- **Shared.Kernel/Storage/IVirusScanner.cs** — interface with `ScanResult Scan(Stream, string mimeType, CancellationToken)`.
- **Trading.Infrastructure/Storage/VirusScannerNoOp.cs** — default impl.
- **Trading.Infrastructure/Storage/MinioAttachmentStore.cs** — extension: `GetThumbnailAsync(objectKey, width, height)` returns presigned GET URL with `?width=&height=` params.
- **Trading.Infrastructure/Storage/AttachmentLifecycleService.cs** — BackgroundService, daily sweep.
- **Trading.Application/Storage/AttachmentQuotaEnforcer.cs** — pre-upload check.
- **Trading.Application/Storage/AttachmentUsageQuery.cs** — read-side query.
- **Trading.Api/Endpoints/AttachmentEndpoints.cs** — new thumbnail + usage endpoints.

## MinIO Bucket Policy

The MinIO bucket MUST be configured with a lifecycle rule (applied via `mc admin` or `mc ilm`):

```json
{
  "Rules": [
    {
      "ID": "expire-attachments-90d",
      "Status": "Enabled",
      "Filter": { "Prefix": "trade-attachments/" },
      "Expiration": { "Days": 90 }
    }
  ]
}
```

This is infrastructure-as-code in the `docker-compose.yml` MinIO init script (`minio-init` container).