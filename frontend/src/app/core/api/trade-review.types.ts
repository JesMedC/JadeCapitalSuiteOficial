// ============================================================================
//  Trade Review API types — slice 1d.2 frontend.
//
//  Mirror of:
//  - src/2.Modules/Trading/JadeCapital.Trading.Application/_Common/TradeReviewDtos.cs
//  - src/2.Modules/Trading/JadeCapital.Trading.Api/Endpoints/TradeReviewEndpoints.cs
//
//  The backend follows an upsert pattern: POST /api/trades/{tradeId}/review
//  either creates a new review (200 with DTO) or updates the existing one.
//  Attachments use a presigned-URL flow: the FE asks for a slot, uploads
//  directly to MinIO, then calls /complete to flip the status to 'uploaded'.
// ============================================================================

export type ReviewStatus = 'pending' | 'uploaded' | 'failed';

export interface TradeReviewDto {
  id: string;
  tradeId: string;
  userId: string;
  /** Emotionality 1..5. Domain maps: 1=Confident, 2=Calm, 3=Anxious, 4=Neutral, 5=Tilted. */
  emotionality: number;
  setupUsed: string | null;
  lessons: string | null;
  /** Optional 1..5 rating. */
  rating: number | null;
  createdAt: string;
  updatedAt: string | null;
  attachments: TradeAttachmentDto[];
}

export interface TradeAttachmentDto {
  id: string;
  reviewId: string;
  objectKey: string;
  contentType: string;
  sizeBytes: number;
  sha256: string | null;
  status: ReviewStatus;
  createdAt: string;
  uploadedAt: string | null;
}

export interface UpsertTradeReviewRequest {
  emotionality: number;
  rating: number | null;
  setupUsed: string | null;
  lessons: string | null;
}

export interface RequestAttachmentUploadRequest {
  contentType: string;
  sizeBytes: number;
  filename?: string;
}

export interface RequestAttachmentUploadResult {
  attachmentId: string;
  objectKey: string;
  /** Presigned PUT URL — el FE hace PUT directo aqui. */
  putUrl: string;
  expiresInSeconds: number;
}

/** RFC 7807 problem shape returned by the backend. */
export interface TradeReviewProblem {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
}

/** Normalized error type used by the state and the component. */
export interface TradeReviewError {
  status: number;
  code: string;
  message: string;
}

/** Whitelisted MIME types for post-trade review attachments. */
export const ALLOWED_ATTACHMENT_CONTENT_TYPES: ReadonlySet<string> = new Set([
  'image/png',
  'image/jpeg',
  'image/webp',
  'application/pdf',
]);

/** Max size for a single attachment, in bytes (10 MB). */
export const MAX_ATTACHMENT_SIZE_BYTES = 10 * 1024 * 1024;

/** Max attachments per review (matches server cap). */
export const MAX_ATTACHMENTS_PER_REVIEW = 5;
