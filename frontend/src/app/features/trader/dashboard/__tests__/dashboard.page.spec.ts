import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import {
  DashboardSummaryDto,
  PagedTradesDto,
  TradeApiService,
} from '@core/api/trade-api.service';
import { DashboardPage } from '../dashboard.page';

jest.spyOn(console, 'warn').mockImplementation(() => {});

describe('DashboardPage — Wave 3 shortcuts', () => {
  let api: {
    dashboard: jest.Mock;
    list: jest.Mock;
    calendar: jest.Mock;
    getById: jest.Mock;
    open: jest.Mock;
    close: jest.Mock;
    updateNotes: jest.Mock;
    delete: jest.Mock;
  };

  const emptyPaged = (): PagedTradesDto => ({
    total: 0,
    page: 1,
    pageSize: 100,
    items: [],
  });

  const emptySummary = (): DashboardSummaryDto => ({
    totalCount: 0,
    openCount: 0,
    closedCount: 0,
    winsCount: 0,
    winRate: 0,
    totalPnl: 0,
    bestTrade: 0,
    worstTrade: 0,
    avgTrade: 0,
    currency: 'USD',
  });

  beforeEach(async () => {
    api = {
      dashboard: jest.fn().mockResolvedValue(emptySummary()),
      list: jest.fn().mockResolvedValue(emptyPaged()),
      calendar: jest.fn(),
      getById: jest.fn(),
      open: jest.fn(),
      close: jest.fn(),
      updateNotes: jest.fn(),
      delete: jest.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [DashboardPage],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: TradeApiService, useValue: api },
      ],
    }).compileComponents();
  });

  async function settle(fixture: { detectChanges: () => void }) {
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();
  }

  it('renders the three Wave-3 shortcut cards with their "Ver X →" labels', async () => {
    const fixture = TestBed.createComponent(DashboardPage);
    await settle(fixture);

    const html = fixture.nativeElement as HTMLElement;
    const grid = html.querySelector('[data-testid="dash-shortcuts"]');
    expect(grid).not.toBeNull();

    const cards = html.querySelectorAll('[data-testid="dash-shortcuts"] .link-card');
    expect(cards.length).toBe(3);

    expect(html.querySelector('[data-testid="dash-shortcut-strategies"]')).not.toBeNull();
    expect(html.querySelector('[data-testid="dash-shortcut-alertas"]')).not.toBeNull();
    expect(html.querySelector('[data-testid="dash-shortcut-planner"]')).not.toBeNull();

    const text = html.textContent ?? '';
    expect(text).toContain('Ver strategies →');
    expect(text).toContain('Ver alertas →');
    expect(text).toContain('Ver planner →');
  });

  it('click on the Strategies shortcut navigates to /app/strategies', async () => {
    const fixture = TestBed.createComponent(DashboardPage);
    await settle(fixture);

    const router = TestBed.inject(Router);
    const navigateSpy = jest.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    const card = fixture.nativeElement.querySelector(
      '[data-testid="dash-shortcut-strategies"]',
    ) as HTMLButtonElement;
    expect(card).not.toBeNull();

    card.click();

    expect(navigateSpy).toHaveBeenCalledTimes(1);
    expect(navigateSpy).toHaveBeenCalledWith('/app/strategies');
  });

  /**
   * Slice 4e — Phase 2 verification.
   * The dashboard embeds a `<jcs-watchlist-page>` with 5 symbols so the
   * trader sees live quotes without leaving the home page. Per design.md
   * 4e.2.1 the slice must surface exactly 5 default symbols (EURUSD,
   * GBPJPY, BTCUSD, USDJPY, AUDUSD) wired into the embed.
   */
  it('embeds a watchlist with exactly 5 default symbols', async () => {
    const fixture = TestBed.createComponent(DashboardPage);
    const component = fixture.componentInstance;
    await settle(fixture);

    expect(component.dashboardWatchlist.length).toBe(5);
    expect(component.dashboardWatchlist).toEqual([
      'EURUSD', 'GBPJPY', 'BTCUSD', 'USDJPY', 'AUDUSD',
    ]);

    const html = fixture.nativeElement as HTMLElement;
    const watchlist = html.querySelector('[data-testid="dash-watchlist"]');
    expect(watchlist).not.toBeNull();
  });
});