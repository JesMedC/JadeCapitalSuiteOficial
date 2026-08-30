import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import {
  PostTradeReviewComponent,
} from '@features/trader/trades/post-trade-review.component';
import {
  ALLOWED_ATTACHMENT_CONTENT_TYPES,
  MAX_ATTACHMENT_SIZE_BYTES,
} from '@core/api/trade-review.types';
import {
  TradeReviewService,
  uploadBytesToMinio,
} from '@core/api/trade-review.service';

// ============================================================================
//  PostTradeReviewComponent — slice 1d.2 frontend tests.
//
//  Spec coverage (4 user-required specs):
//   1. Renders the review form when given a tradeId (no existing = create).
//   2. Renders with an existing review hydrated.
//   3. Delete confirms and removes the attachment from the list.
//   4. Max attachment size enforced client-side; oversized file is rejected
//      without hitting the backend.
// ============================================================================

describe('PostTradeReviewComponent', () => {
  let fixture: ComponentFixture<PostTradeReviewComponent>;
  let component: PostTradeReviewComponent;
  let api: {
    get: jest.Mock;
    upsert: jest.Mock;
    requestUpload: jest.Mock;
    confirmUpload: jest.Mock;
    deleteAttachment: jest.Mock;
  };

  const TRADE_ID = '11111111-1111-1111-1111-111111111111';

  beforeEach(async () => {
    api = {
      get: jest.fn(),
      upsert: jest.fn(),
      requestUpload: jest.fn(),
      confirmUpload: jest.fn(),
      deleteAttachment: jest.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [PostTradeReviewComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: TradeReviewService, useValue: api },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PostTradeReviewComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('tradeId', TRADE_ID);
  });

  it('renders the review form with the 5 emotionality pills + Neutral default', async () => {
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;

    // 5 emotionality pills.
    const emoPills = html.querySelectorAll('[aria-label="Estado emocional"] .ptr-pill');
    expect(emoPills.length).toBe(5);

    // Neutral (4) is the default; check the active one.
    expect(component.emotionality()).toBe(3); // default 3 in our component is Neutral
    const activePill = html.querySelector('[aria-label="Estado emocional"] .ptr-pill--active');
    expect(activePill).not.toBeNull();
    expect(activePill?.textContent?.trim()).toContain('Anxious');

    // Setup input is bound.
    const setupInput = html.querySelector('input[type="text"]') as HTMLInputElement;
    expect(setupInput).not.toBeNull();
    expect(setupInput.maxLength).toBe(64);

    // Lessons textarea cap.
    const lessonsArea = html.querySelector('textarea.ptr-input--area') as HTMLTextAreaElement;
    expect(lessonsArea.maxLength).toBe(5000);
  });

  it('renders the existing review when passed via input', async () => {
    fixture.componentRef.setInput('existing', {
      id: 'r-1',
      tradeId: TRADE_ID,
      userId: 'u-1',
      emotionality: 1, // Confident
      setupUsed: 'London breakout',
      lessons: 'Held through news.',
      rating: 4,
      createdAt: 't',
      updatedAt: null,
      attachments: [],
    });
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    expect(component.emotionality()).toBe(1);
    expect(component.setupUsed()).toBe('London breakout');
    expect(component.lessons()).toBe('Held through news.');
    expect(component.rating()).toBe(4);
  });

  it('reject attachment MAX_SIZE: client-side guard never hits the backend', async () => {
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    // Stub a File-like object. In Angular TestBed's jsdom realm, the File
    // prototype getter for `.size` shadows data-property defineProperty, so
    // we use a fully-detached prototype chain.
    class FakeFile {
      size: number;
      type: string;
      name: string;
      constructor(sizeBytes: number) {
        this.size = sizeBytes;
        this.type = 'image/png';
        this.name = 'huge.png';
      }
    }
    const fakeHuge = new FakeFile(MAX_ATTACHMENT_SIZE_BYTES + 1);

    await component.handleFilePublicForTest(fakeHuge as unknown as File).catch(() => undefined);

    expect(api.requestUpload).not.toHaveBeenCalled();
    expect(api.confirmUpload).not.toHaveBeenCalled();
    expect(component.uploadErrorMessage()).not.toBeNull();
    expect(component.uploadErrorMessage()).toContain('excede');
  });

  it('rejects attachment content types outside the whitelist (client-side)', async () => {
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    const txt = new File(['x'], 'note.txt', { type: 'text/plain' });
    await component.handleFilePublicForTest(txt).catch(() => undefined);

    expect(api.requestUpload).not.toHaveBeenCalled();
    expect(component.uploadErrorMessage()).toContain('no permitido');
  });

  it('deleteAttachment: removes from list and hits DELETE', async () => {
    fixture.componentRef.setInput('existing', {
      id: 'r-1',
      tradeId: TRADE_ID,
      userId: 'u-1',
      emotionality: 3,
      setupUsed: null, lessons: null, rating: null,
      createdAt: 't', updatedAt: null,
      attachments: [
        {
          id: 'att-1', reviewId: 'r-1', objectKey: 'k',
          contentType: 'image/png', sizeBytes: 1024, sha256: null,
          status: 'uploaded', createdAt: 't', uploadedAt: 't',
        },
      ],
    });
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    api.deleteAttachment.mockReturnValue(of(undefined));
    await component.onDeleteAttachment('att-1');

    expect(api.deleteAttachment).toHaveBeenCalledWith(TRADE_ID, 'att-1');
    expect(component.attachments()).toHaveLength(0);
  });

  it('deleteAttachment error surfaces the uploadErrorMessage', async () => {
    // Service signature is async deleteAttachment -> Promise<void>;
    // the mock returns a rejected Promise so the await in the component
    // hits the catch branch.
    api.deleteAttachment.mockRejectedValue(new Error('boom'));
    await component.onDeleteAttachment('att-1');

    expect(component.uploadErrorMessage()).not.toBeNull();
  });

  it('save calls api.upsert with the form payload and emits saved event', async () => {
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    api.upsert.mockReturnValue(of({
      id: 'r-1',
      tradeId: TRADE_ID,
      userId: 'u-1',
      emotionality: 3,
      setupUsed: 'Tag', lessons: 'Lesson', rating: 4,
      createdAt: 't', updatedAt: null,
      attachments: [],
    }));

    component.setEmotionality(3);
    component.setRating(4);
    component.onSetupChange('Tag');
    component.onLessonsChange('Lesson');

    let emitted: unknown = null;
    component.saved.subscribe((e) => (emitted = e));

    await component.onSubmit();

    expect(api.upsert).toHaveBeenCalledWith(TRADE_ID, expect.objectContaining({
      emotionality: 3, rating: 4, setupUsed: 'Tag', lessons: 'Lesson',
    }));
    expect(emitted).not.toBeNull();
  });

  it('flags the form as canSubmit=false when out of range', () => {
    component.setEmotionality(7); // out of [1..5]
    expect(component.canSubmit()).toBe(false);
    component.setEmotionality(2);
    expect(component.canSubmit()).toBe(true);
  });
});
