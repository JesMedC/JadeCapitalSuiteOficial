import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  RequestAttachmentUploadRequest,
  RequestAttachmentUploadResult,
  TradeAttachmentDto,
  TradeReviewDto,
  TradeReviewError,
  TradeReviewProblem,
  UpsertTradeReviewRequest,
} from './trade-review.types';

// ============================================================================
//  TradeReviewService — slice 1d.2 frontend.
//
//  Wraps the five endpoints from slice 1d.1:
//  - GET /api/trades/{tradeId}/review
//  - POST /api/trades/{tradeId}/review              (upsert)
//  - POST /api/trades/{tradeId}/review/attachments (request slot, returns presigned URL)
//  - POST /api/trades/{tradeId}/review/attachments/{id}/complete
//  - DELETE /api/trades/{tradeId}/review/attachments/{id}
//
//  Auth: the `auth.interceptor` adds the Bearer token automatically; do not
//  repeat it here.
// ============================================================================

@Injectable({ providedIn: 'root' })
export class TradeReviewService {
  private readonly http = inject(HttpClient);

  /** Returns the review for a trade, or `null` when no review exists yet (404). */
  async get(tradeId: string): Promise<TradeReviewDto | null> {
    try {
      return await firstValueFrom(
        this.http.get<TradeReviewDto>(`/api/trades/${tradeId}/review`)
      );
    } catch (e) {
      if (e instanceof HttpErrorResponse && e.status === 404) {
        return null;
      }
      throw toTradeReviewError(e);
    }
  }

  /** Upserts the review (create or update). Returns the saved DTO. */
  async upsert(tradeId: string, payload: UpsertTradeReviewRequest): Promise<TradeReviewDto> {
    try {
      return await firstValueFrom(
        this.http.post<TradeReviewDto>(`/api/trades/${tradeId}/review`, payload)
      );
    } catch (e) {
      throw toTradeReviewError(e);
    }
  }

  /**
   * Requests a slot for a new attachment. Returns the presigned PUT URL that
   * the FE will use to upload bytes directly to MinIO. After the PUT, the
   * FE MUST call `confirmUpload` so the backend verifies size + flips status.
   */
  async requestUpload(
    tradeId: string,
    payload: RequestAttachmentUploadRequest
  ): Promise<RequestAttachmentUploadResult> {
    try {
      return await firstValueFrom(
        this.http.post<RequestAttachmentUploadResult>(
          `/api/trades/${tradeId}/review/attachments`,
          payload
        )
      );
    } catch (e) {
      throw toTradeReviewError(e);
    }
  }

  /** Confirms the upload. Backend verifies size + flips status to 'uploaded'. */
  async confirmUpload(
    tradeId: string,
    attachmentId: string,
    sha256?: string
  ): Promise<TradeAttachmentDto> {
    try {
      return await firstValueFrom(
        this.http.post<TradeAttachmentDto>(
          `/api/trades/${tradeId}/review/attachments/${attachmentId}/complete`,
          { sha256: sha256 ?? null }
        )
      );
    } catch (e) {
      throw toTradeReviewError(e);
    }
  }

  /** Deletes an attachment (best-effort storage cleanup on the backend). */
  async deleteAttachment(tradeId: string, attachmentId: string): Promise<void> {
    try {
      await firstValueFrom(
        this.http.delete<void>(
          `/api/trades/${tradeId}/review/attachments/${attachmentId}`
        )
      );
    } catch (e) {
      throw toTradeReviewError(e);
    }
  }
}

/**
 * Uploads the file bytes to MinIO using the presigned PUT URL. The FE hits
 * MinIO directly (NOT through the .NET backend, which never proxies bytes
 * — see spec requirement "Backend never proxies bytes").
 *
 * @returns `true` on a 200/204; throws on any other status.
 */
export async function uploadBytesToMinio(
  putUrl: string,
  file: Blob,
  contentType: string
): Promise<boolean> {
  const response = await fetch(putUrl, {
    method: 'PUT',
    body: file,
    headers: { 'Content-Type': contentType },
  });
  return response.ok;
}

/** Normalizes an HttpErrorResponse (or any error) into a `TradeReviewError`. */
export function toTradeReviewError(e: unknown): TradeReviewError {
  if (e instanceof HttpErrorResponse) {
    const body = (e.error ?? {}) as TradeReviewProblem;
    const code = body.code ?? '';
    const message = body.detail ?? body.title ?? e.message ?? `Error HTTP ${e.status}.`;
    return { status: e.status, code, message };
  }
  if (e instanceof Error) {
    return { status: 0, code: 'unknown', message: e.message };
  }
  return { status: 0, code: 'unknown', message: 'Ocurrió un error inesperado.' };
}
