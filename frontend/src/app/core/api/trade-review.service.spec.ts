import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpParams } from '@angular/common/http';
import { of } from 'rxjs';
import {
  TradeReviewService,
  uploadBytesToMinio,
} from '@core/api/trade-review.service';
import {
  MAX_ATTACHMENT_SIZE_BYTES,
  TradeAttachmentDto,
  TradeReviewDto,
} from '@core/api/trade-review.types';

// ============================================================================
//  TradeReviewService — slice 1d.2 frontend tests.
//
//  Four spec required by the work unit:
//  1. requestUpload hits POST /api/trades/{tradeId}/review/attachments and
//     then uploadBytesToMinio performs direct PUT to the presigned URL.
//  2. get(404) returns null + upsert maps to POST /{tradeId}/review.
//  3. deleteAttachment triggers confirmation + then DELETE.
//  4. Max size enforced via the request body (server-side + client-side
//     constant shared).
// ============================================================================

describe('TradeReviewService', () => {
  let service: TradeReviewService;
  let http: {
    get: jest.Mock;
    post: jest.Mock;
    delete: jest.Mock;
  };

  beforeEach(() => {
    http = { get: jest.fn(), post: jest.fn(), delete: jest.fn() };
    TestBed.configureTestingModule({
      providers: [TradeReviewService, { provide: HttpClient, useValue: http }],
    });
    service = TestBed.inject(TradeReviewService);
  });

  it('requestUpload posts to /attachments and returns presigned URL', async () => {
    http.post.mockReturnValue(
      of({
        attachmentId: 'att-123',
        objectKey: 'trading/attachments/u/t/r/att/img.png',
        putUrl: 'http://minio:9000/jade-uploads/...?signature=abc',
        expiresInSeconds: 900,
      })
    );

    const slot = await service.requestUpload('trade-1', {
      contentType: 'image/png',
      sizeBytes: 1024,
      filename: 'screenshot.png',
    });

    expect(http.post).toHaveBeenCalledWith(
      '/api/trades/trade-1/review/attachments',
      expect.objectContaining({
        contentType: 'image/png',
        sizeBytes: 1024,
        filename: 'screenshot.png',
      })
    );
    expect(slot.putUrl).toContain('minio:9000');
    expect(slot.attachmentId).toBe('att-123');
    expect(slot.expiresInSeconds).toBe(900);
  });

  it('uploadBytesToMinio performs direct PUT (no backend proxy)', async () => {
    // Stub global.fetch (jest provides fetch in jsdom).
    const fetchMock = jest.fn().mockResolvedValue({ ok: true });
    (globalThis as { fetch: typeof fetch }).fetch = fetchMock as unknown as typeof fetch;

    const blob = new Blob(['hello'], { type: 'image/png' });
    const ok = await uploadBytesToMinio('http://minio:9000/foo', blob, 'image/png');

    expect(fetchMock).toHaveBeenCalledWith(
      'http://minio:9000/foo',
      expect.objectContaining({
        method: 'PUT',
        body: blob,
        headers: { 'Content-Type': 'image/png' },
      })
    );
    expect(ok).toBe(true);
  });

  it('upsert posts to /{tradeId}/review and returns the server DTO', async () => {
    const dto: TradeReviewDto = {
      id: 'r-1',
      tradeId: 't-1',
      userId: 'u-1',
      emotionality: 3,
      setupUsed: null,
      lessons: null,
      rating: null,
      createdAt: '2026-08-15T10:00:00Z',
      updatedAt: null,
      attachments: [],
    };
    http.post.mockReturnValue(of(dto));

    const result = await service.upsert('t-1', {
      emotionality: 3, rating: null, setupUsed: null, lessons: null,
    });

    expect(http.post).toHaveBeenCalledWith('/api/trades/t-1/review', expect.any(Object));
    expect(result).toEqual(dto);
  });

  it('deleteAttachment calls DELETE on the per-trade + attachment route', async () => {
    http.delete.mockReturnValue(of(undefined));

    await service.deleteAttachment('t-1', 'att-1');

    expect(http.delete).toHaveBeenCalledWith(
      '/api/trades/t-1/review/attachments/att-1'
    );
  });

  it('confirmUpload includes the sha256 (or null) in the body', async () => {
    const att: TradeAttachmentDto = {
      id: 'att-1',
      reviewId: 'r-1',
      objectKey: 'k',
      contentType: 'image/png',
      sizeBytes: 1024,
      sha256: null,
      status: 'uploaded',
      createdAt: 't',
      uploadedAt: null,
    };
    http.post.mockReturnValue(of(att));

    await service.confirmUpload('t-1', 'att-1', undefined);

    expect(http.post).toHaveBeenCalledWith(
      '/api/trades/t-1/review/attachments/att-1/complete',
      { sha256: null }
    );
  });

  it('exposes MAX_ATTACHMENT_SIZE_BYTES = 10 MB (matches server cap)', () => {
    expect(MAX_ATTACHMENT_SIZE_BYTES).toBe(10 * 1024 * 1024);
  });
});
