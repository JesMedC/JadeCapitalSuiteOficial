import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { AttachmentsService } from '../api/attachments.service';
import { AttachmentQuotaState } from '../state/attachment-quota.state';

jest.spyOn(console, 'warn').mockImplementation(() => {});

describe('AttachmentsService + AttachmentQuotaState (slice 4d)', () => {
  let service: AttachmentsService;
  let state: AttachmentQuotaState;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AttachmentsService);
    state = TestBed.inject(AttachmentQuotaState);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('service exposes getUsage + getThumbnail (signature smoke)', () => {
    expect(typeof service.getUsage).toBe('function');
    expect(typeof service.getThumbnail).toBe('function');
  });

  it('state.refresh() sets usage signal from /api/attachments/usage', async () => {
    const pending = state.refresh();
    const req = http.expectOne('/api/attachments/usage');
    expect(req.request.method).toBe('GET');
    req.flush({
      totalBytes: 8_500_000,
      attachmentCount: 12,
      quotaBytes: 52_428_800,
      quotaCount: 100,
      percentFull: 16.21,
    });

    await pending;

    expect(state.usedBytes()).toBe(8_500_000);
    expect(state.attachmentCount()).toBe(12);
    expect(state.quotaBytes()).toBe(52_428_800);
    expect(state.quotaCount()).toBe(100);
    expect(state.percentFull()).toBe(16.21);
    expect(state.remainingBytes()).toBe(43_928_800);
  });

  it('state.refresh() surfaces error on failure', async () => {
    const pending = state.refresh();
    const req = http.expectOne('/api/attachments/usage');
    req.flush({ detail: 'boom' }, { status: 500, statusText: 'Server Error' });

    await pending;

    expect(state.usage()).toBeNull();
    expect(state.error()).toContain('boom');
  });
});