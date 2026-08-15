import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AdminApiService, SubscriptionDetail, SubscriptionHistoryItem } from '@core/api/admin-api.service';
import { AdminSubscriptionDetailPage } from '../admin-detail.page';

describe('AdminSubscriptionDetailPage history ordering', () => {
  let http: { get: jest.Mock; post: jest.Mock };

  const hist = (
    id: string,
    occurredAt: string,
    action: string,
    priorPlanCode = 'starter',
    resultingPlanCode = 'starter',
    priorStatus = 'Active',
    resultingStatus = 'Active',
  ): SubscriptionHistoryItem => ({
    id,
    action,
    priorPlanCode,
    resultingPlanCode,
    priorStatus,
    resultingStatus,
    actor: 'admin@example.com',
    occurredAt,
    version: 1,
    reason: null,
    priorTrialEndsAt: null,
    newTrialEndsAt: null,
  });

  const detail = (history: SubscriptionHistoryItem[]): SubscriptionDetail => ({
    subscriptionId: 's-1',
    userId: 'u-1',
    planCode: 'pro',
    planName: 'Pro',
    status: 'Active',
    trialEndsAt: null,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: null,
    version: 1,
    owner: { email: 'u@example.com', displayName: 'Owner' },
    history,
  });

  beforeEach(async () => {
    http = { get: jest.fn(), post: jest.fn() };
    await TestBed.configureTestingModule({
      imports: [AdminSubscriptionDetailPage],
      providers: [
        provideRouter([]),
        { provide: HttpClient, useValue: http },
        AdminApiService,
      ],
    }).compileComponents();
  });

  it('HistoryTable_NewestFirst_StableTieBreaker — preserves API-provided order: newest first by occurredAt, id DESC as tie-breaker', async () => {
    const events = [
      hist('a-3', '2026-03-01T10:00:00Z', 'Cancelled'),
      hist('a-2', '2026-02-01T10:00:00Z', 'TierChanged'),
      hist('a-1', '2026-01-01T10:00:00Z', 'TierChanged'),
    ];
    http.get.mockReturnValueOnce(of(detail(events)));

    const fixture = TestBed.createComponent(AdminSubscriptionDetailPage);
    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('id', 's-1');
    });
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    const page = fixture.componentInstance;
    const rendered = page.history();
    const ids = rendered.map((h) => h.id);

    expect(ids).toEqual(['a-3', 'a-2', 'a-1']);
  });

  it('HistoryTable_NewestFirst_StableTieBreaker — preserves API-provided tie-breaker order when occurredAt ties', async () => {
    const events = [
      hist('h-high', '2026-02-01T10:00:00Z', 'TierChanged'),
      hist('h-low', '2026-02-01T10:00:00Z', 'TierChanged'),
      hist('h-other', '2026-01-01T10:00:00Z', 'TierChanged'),
    ];
    http.get.mockReturnValueOnce(of(detail(events)));

    const fixture = TestBed.createComponent(AdminSubscriptionDetailPage);
    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('id', 's-1');
    });
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    const page = fixture.componentInstance;
    const rendered = page.history();
    const ids = rendered.map((h) => h.id);

    expect(ids).toEqual(['h-high', 'h-low', 'h-other']);
  });

  it('HistoryTable_NewestFirst_StableTieBreaker — when history is empty, renders the empty-state copy', async () => {
    http.get.mockReturnValueOnce(of(detail([])));

    const fixture = TestBed.createComponent(AdminSubscriptionDetailPage);
    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('id', 's-1');
    });
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Sin eventos');
    expect(html.querySelector('ol.timeline')).toBeNull();
  });
});
