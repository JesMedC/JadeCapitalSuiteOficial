import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import {
  BehavioralAnalysisDto,
  BehavioralEventDto,
  EmotionalityBucketDto,
} from '../api/patterns.types';
import { PatternsService } from '../api/patterns.service';
import { PatternsState } from '../state/patterns.state';
import { PatternsPage } from '../patterns-page';

// Suppress the harmless zone.js deprecation warning emitted by the jest-preset-angular bootstrap.
jest.spyOn(console, 'warn').mockImplementation(() => {});

// ============================================================================
//  PatternsPage — slice 2b.2 frontend tests.
//
//  3 user-required specs:
//   1. Renders 2 event cards with severity-based color modifiers.
//   2. Renders 3 aggregation buckets (low/mid/high) with the correct counts.
//   3. Empty state copy renders when events=[] AND aggregations are zeros.
//
//  Strict TDD: these specs reference production code (PatternsPage,
//  PatternsState, PatternsService) that does not exist yet. RED phase
//  becomes GREEN once the service + state + page land.
// ============================================================================

describe('PatternsPage', () => {
  let api: { getAnalysis: jest.Mock };
  let state: PatternsState;

  const eventDto = (overrides: Partial<BehavioralEventDto> = {}): BehavioralEventDto => ({
    ruleId: 'RevengeTrade',
    severity: 'medium',
    tradeIds: ['22222222-2222-2222-2222-222222222222'],
    occurredAt: '2026-08-12T10:00:00.000Z',
    message: 'EUR/USD con tamaño 1.5× la anterior perdedora — posible revenge trading',
    ...overrides,
  });

  const bucketDto = (
    count: number,
    winRate: number,
    totalPnl: number,
  ): EmotionalityBucketDto => ({ count, winRate, totalPnl });

  const analysisDto = (overrides: Partial<BehavioralAnalysisDto> = {}): BehavioralAnalysisDto => ({
    period: '30d',
    windowStart: '2026-07-17T00:00:00Z',
    windowEnd: '2026-08-17T00:00:00Z',
    events: [],
    aggregations: {
      byEmotionality: {
        low_1_2: bucketDto(0, 0, 0),
        mid_3: bucketDto(0, 0, 0),
        high_4_5: bucketDto(0, 0, 0),
      },
    },
    ...overrides,
  });

  /** Mirrors the real service signature (Promise-returning). */
  const fakeApi = (): { getAnalysis: jest.Mock } => ({
    getAnalysis: jest.fn().mockResolvedValue(analysisDto()),
  });

  beforeEach(async () => {
    api = fakeApi();

    await TestBed.configureTestingModule({
      imports: [PatternsPage],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        PatternsState,
        { provide: PatternsService, useValue: api },
      ],
    }).compileComponents();

    state = TestBed.inject(PatternsState);
    state.reset();
  });

  /** Flushes microtasks + change-detection. Mirrors the journal-page spec pattern. */
  async function settle(fixture: { detectChanges: () => void }) {
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();
  }

  it('renders 2 event cards with severity-based color modifiers', async () => {
    api.getAnalysis.mockResolvedValue(
      analysisDto({
        events: [
          eventDto({ ruleId: 'RevengeTrade', severity: 'medium' }),
          eventDto({ ruleId: 'TiltSequence', severity: 'high' }),
        ],
      }),
    );

    const fixture = TestBed.createComponent(PatternsPage);
    await settle(fixture);

    expect(api.getAnalysis).toHaveBeenCalled();
    const html = fixture.nativeElement as HTMLElement;

    // 2 cards rendered.
    const cards = html.querySelectorAll('[data-testid="event-card"]');
    expect(cards.length).toBe(2);

    // Severity-based class modifiers (high=red border-left, medium=yellow).
    expect(cards[0].classList.contains('pp-event--medium')).toBe(true);
    expect(cards[1].classList.contains('pp-event--high')).toBe(true);

    // Each card shows its message and rule label.
    expect(cards[0].textContent).toContain('revenge trading');
    expect(cards[1].textContent).toContain('Tilt');

    // TradeId links → routerLink to /app/trades/{id}.
    const firstLink = cards[0].querySelector('a.pp-event-link') as HTMLAnchorElement;
    expect(firstLink).not.toBeNull();
    expect(firstLink.getAttribute('href')).toBe('/app/trades/22222222-2222-2222-2222-222222222222');
  });

  it('renders 3 aggregation buckets (low / mid / high) with the correct counts', async () => {
    api.getAnalysis.mockResolvedValue(
      analysisDto({
        aggregations: {
          byEmotionality: {
            low_1_2: bucketDto(12, 0.33, -150),
            mid_3: bucketDto(28, 0.61, 420.5),
            high_4_5: bucketDto(8, 0.5, -80.2),
          },
        },
      }),
    );

    const fixture = TestBed.createComponent(PatternsPage);
    await settle(fixture);

    const html = fixture.nativeElement as HTMLElement;
    const buckets = html.querySelectorAll('[data-testid="bucket-card"]');
    expect(buckets.length).toBe(3);

    // Each bucket renders its label + count.
    expect(buckets[0].textContent).toContain('Bajo (1-2)');
    expect(buckets[0].textContent).toContain('12 ops');
    expect(buckets[1].textContent).toContain('Medio (3)');
    expect(buckets[1].textContent).toContain('28 ops');
    expect(buckets[2].textContent).toContain('Alto (4-5)');
    expect(buckets[2].textContent).toContain('8 ops');

    // Win-rate rendered as percent (no decimals).
    expect(buckets[0].textContent).toContain('33%');
    expect(buckets[1].textContent).toContain('61%');
    expect(buckets[2].textContent).toContain('50%');

    // Total PnL signs (positive vs negative).
    expect(buckets[0].classList.contains('pp-bucket-pnl--negative') ||
            buckets[0].querySelector('.pp-bucket-pnl--negative')).toBeTruthy();
    expect(buckets[1].querySelector('.pp-bucket-pnl--positive')).not.toBeNull();
  });

  it('shows "Sin patrones detectados en este período" when events=[] and buckets are zero', async () => {
    // Default analysisDto has empty events + zero buckets.
    api.getAnalysis.mockResolvedValue(analysisDto());

    const fixture = TestBed.createComponent(PatternsPage);
    await settle(fixture);

    const html = fixture.nativeElement as HTMLElement;
    const empty = html.querySelector('[data-testid="patterns-empty"]');
    expect(empty).not.toBeNull();
    expect(empty?.textContent).toContain('Sin patrones detectados en este período');

    // No event cards rendered.
    expect(html.querySelectorAll('[data-testid="event-card"]').length).toBe(0);
  });
});
