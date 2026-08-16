import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { AlertsService } from '../api/alerts.service';
import { AlertsState } from '../state/alerts.state';
import { AlertsPage } from '../alerts-page';
import { AlertDto } from '../api/alerts.types';

// Suppress the harmless zone.js deprecation warning emitted by the jest-preset-angular bootstrap.
jest.spyOn(console, 'warn').mockImplementation(() => {});

// ============================================================================
//  AlertsPage — slice 3b frontend tests.
//
//  4 user-required specs:
//   1. Renders empty state copy when the API returns [].
//   2. Active count badge shows the right number; severity color stripe
//      appears per card (High=red, Medium=yellow, Low=blue).
//   3. Click "Marcar como leído" → PATCH /ack flow runs and the card
//      flips to acked (badge "Leída" appears).
//   4. Toggle "Mostrar todas" reveals acked alerts.
// ============================================================================

describe('AlertsPage', () => {
  let api: {
    list: jest.Mock;
    getById: jest.Mock;
    acknowledge: jest.Mock;
  };
  let state: AlertsState;

  const sampleAlert = (
    overrides: Partial<AlertDto> = {},
  ): AlertDto => ({
    id: '11111111-1111-1111-1111-111111111111',
    ruleId: 'NoTradesInDays',
    severity: 'Low',
    title: '5 días sin operar',
    body: 'Revisa tu plan.',
    cta: { route: '/app/journal', label: 'Reflexionar' },
    acknowledgedAt: null,
    expiresAt: null,
    createdAt: '2026-08-18T13:00:00.000Z',
    ...overrides,
  });

  const fakeApi = () => ({
    list: jest.fn().mockResolvedValue([]),
    getById: jest.fn().mockResolvedValue(sampleAlert()),
    acknowledge: jest.fn().mockResolvedValue(sampleAlert({ acknowledgedAt: '2026-08-18T14:00:00.000Z' })),
  });

  beforeEach(async () => {
    api = fakeApi();

    await TestBed.configureTestingModule({
      imports: [AlertsPage],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        AlertsState,
        { provide: AlertsService, useValue: api },
      ],
    }).compileComponents();

    state = TestBed.inject(AlertsState);
    state.reset();
  });

  async function settle(fixture: { detectChanges: () => void }) {
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();
  }

  it('renders empty state copy when the API returns no alerts', async () => {
    api.list.mockResolvedValue([]);

    const fixture = TestBed.createComponent(AlertsPage);
    await settle(fixture);

    expect(api.list).toHaveBeenCalled();
    expect(state.list().length).toBe(0);

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Sin alertas activas');
    expect(html.querySelector('[data-testid="alerts-empty"]')).not.toBeNull();
  });

  it('renders the severity color stripe + active count when alerts exist', async () => {
    api.list.mockResolvedValue([
      sampleAlert({ id: 'a1', severity: 'High', title: 'DD alto' }),
      sampleAlert({ id: 'a2', severity: 'Medium', title: 'R/R bajo' }),
      sampleAlert({ id: 'a3', severity: 'Low', title: 'Sin trades' }),
    ]);

    const fixture = TestBed.createComponent(AlertsPage);
    await settle(fixture);

    const html = fixture.nativeElement as HTMLElement;
    const count = html.querySelector('[data-testid="alerts-active-count"]');
    expect(count?.textContent).toContain('3 activa(s)');

    const cards = html.querySelectorAll('[data-testid="alerts-list"] .ap-card');
    expect(cards.length).toBe(3);

    const highCard = cards[0] as HTMLElement;
    const medCard = cards[1] as HTMLElement;
    const lowCard = cards[2] as HTMLElement;
    expect(highCard.getAttribute('data-severity')).toBe('High');
    expect(medCard.getAttribute('data-severity')).toBe('Medium');
    expect(lowCard.getAttribute('data-severity')).toBe('Low');

    // Each card has a colored stripe element.
    expect(highCard.querySelector('.ap-card-stripe')).not.toBeNull();
    expect(medCard.querySelector('.ap-card-stripe')).not.toBeNull();
    expect(lowCard.querySelector('.ap-card-stripe')).not.toBeNull();
  });

  it('ack flow: clicking "Marcar como leído" PATCHes the alert and removes it from the active view', async () => {
    const alert = sampleAlert();
    api.list.mockResolvedValue([alert]);
    api.acknowledge.mockResolvedValue({
      ...alert,
      acknowledgedAt: '2026-08-18T14:00:00.000Z',
    });

    const fixture = TestBed.createComponent(AlertsPage);
    await settle(fixture);

    // Sanity: before ack the alert is visible.
    let cards = (fixture.nativeElement as HTMLElement).querySelectorAll(
      '[data-testid="alerts-list"] .ap-card',
    );
    expect(cards.length).toBe(1);

    const component = fixture.componentInstance;
    await component.onAck(alert);

    // Signal-based CD with OnPush: explicitly trigger a few cycles so the
    // template picks up the new acknowledgedAt value.
    for (let i = 0; i < 3; i++) {
      fixture.detectChanges();
      await Promise.resolve();
    }
    fixture.detectChanges();

    expect(api.acknowledge).toHaveBeenCalledWith(alert.id);

    // The active view no longer shows the acked alert. Toggle "Mostrar
    // todas" so the card reappears with the "Leída" badge.
    component.state.toggleShowAll();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    cards = html.querySelectorAll('[data-testid="alerts-list"] .ap-card');
    expect(cards.length).toBe(1);

    const ackedBadge = html.querySelector('[data-testid="alerts-acked-badge"]');
    expect(ackedBadge).not.toBeNull();
    expect(ackedBadge?.textContent).toContain('Leída');

    // The ack button should no longer be present on the now-acked card.
    const ackButton = html.querySelector('[data-testid="alerts-ack"]');
    expect(ackButton).toBeNull();
  });

  it('toggle "Mostrar todas" reveals acked alerts', async () => {
    api.list.mockResolvedValue([
      sampleAlert({ id: 'a1', acknowledgedAt: null }),
      sampleAlert({ id: 'a2', acknowledgedAt: '2026-08-18T14:00:00.000Z' }),
    ]);

    const fixture = TestBed.createComponent(AlertsPage);
    await settle(fixture);

    // Initially only the active alert is visible.
    let cards = (fixture.nativeElement as HTMLElement).querySelectorAll(
      '[data-testid="alerts-list"] .ap-card',
    );
    expect(cards.length).toBe(1);

    // Toggle "Mostrar todas" → both alerts visible.
    const component = fixture.componentInstance;
    component.state.toggleShowAll();
    fixture.detectChanges();

    cards = (fixture.nativeElement as HTMLElement).querySelectorAll(
      '[data-testid="alerts-list"] .ap-card',
    );
    expect(cards.length).toBe(2);
  });
});