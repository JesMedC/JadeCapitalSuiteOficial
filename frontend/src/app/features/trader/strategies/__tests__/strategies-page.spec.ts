import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import {
  StrategyAnalyticsDto,
  StrategyDto,
  UpsertStrategyRequest,
} from '../api/strategies.types';
import { StrategiesService } from '../api/strategies.service';
import { StrategiesState } from '../state/strategies.state';
import { StrategiesPage } from '../strategies-page';

// Suppress the harmless zone.js deprecation warning emitted by the jest-preset-angular bootstrap.
jest.spyOn(console, 'warn').mockImplementation(() => {});

// ============================================================================
//  StrategiesPage — slice 3a frontend tests.
//
//  4 user-required specs:
//   1. Renders empty state copy when the API returns [].
//   2. Create success: list updates with the new strategy after submit.
//   3. Click on a card expands the analytics block (count, winRate, pnl).
//   4. Archive flow: clicking Archivar + confirm removes the card.
//
//  Strict TDD: these specs reference production code that does not exist
//  yet — they are RED. GREEN happens once service + state + page land.
// ============================================================================

describe('StrategiesPage', () => {
  let api: {
    list: jest.Mock;
    create: jest.Mock;
    update: jest.Mock;
    archive: jest.Mock;
    getAnalytics: jest.Mock;
    setTradeStrategy: jest.Mock;
  };
  let state: StrategiesState;

  const sampleStrategy = (
    overrides: Partial<StrategyDto> = {},
  ): StrategyDto => ({
    id: '11111111-1111-1111-1111-111111111111',
    userId: '22222222-2222-2222-2222-222222222222',
    name: 'London Break',
    description: 'Breakout during London session',
    symbol: 'EUR/USD',
    timeframe: 5,
    rules: 'Enter on 1H close above range high',
    isActive: true,
    createdAt: '2026-08-18T13:00:00.000Z',
    updatedAt: '2026-08-18T13:00:00.000Z',
    ...overrides,
  });

  const sampleAnalytics = (
    overrides: Partial<StrategyAnalyticsDto> = {},
  ): StrategyAnalyticsDto => ({
    strategyId: '11111111-1111-1111-1111-111111111111',
    name: 'London Break',
    tradeCount: 10,
    winCount: 6,
    lossCount: 4,
    winRate: 0.6,
    totalPnl: '300.00',
    expectancy: '30.00',
    profitFactor: 2.5,
    avgMfe: '60.00',
    avgMae: '-15.00',
    lastTradeAt: '2026-08-17T14:00:00.000Z',
    ...overrides,
  });

  /** Mirrors the real service signatures (Promise-returning). */
  const fakeApi = (): {
    list: jest.Mock;
    create: jest.Mock;
    update: jest.Mock;
    archive: jest.Mock;
    getAnalytics: jest.Mock;
    setTradeStrategy: jest.Mock;
  } => ({
    list: jest.fn().mockResolvedValue([]),
    create: jest.fn().mockResolvedValue(sampleStrategy()),
    update: jest.fn().mockResolvedValue(sampleStrategy()),
    archive: jest.fn().mockResolvedValue(undefined),
    getAnalytics: jest.fn().mockResolvedValue(sampleAnalytics()),
    setTradeStrategy: jest.fn().mockResolvedValue(sampleStrategy()),
  });

  beforeEach(async () => {
    api = fakeApi();

    await TestBed.configureTestingModule({
      imports: [StrategiesPage],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        StrategiesState,
        { provide: StrategiesService, useValue: api },
      ],
    }).compileComponents();

    state = TestBed.inject(StrategiesState);
    state.reset();
  });

  /** Flushes microtasks + change-detection. */
  async function settle(fixture: { detectChanges: () => void }) {
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();
  }

  it('renders empty state copy when the API returns no strategies', async () => {
    api.list.mockResolvedValue([]);

    const fixture = TestBed.createComponent(StrategiesPage);
    await settle(fixture);

    expect(api.list).toHaveBeenCalled();
    expect(state.list().length).toBe(0);

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Aún no creaste ninguna strategy');
    expect(html.querySelector('[data-testid="strategies-empty"]')).not.toBeNull();
  });

  it('create success: submits the form and prepends the new strategy to the list', async () => {
    api.list.mockResolvedValue([]);
    const newStrategy = sampleStrategy({ id: 'new-id', name: 'New Strategy' });
    api.create.mockResolvedValue(newStrategy);

    const fixture = TestBed.createComponent(StrategiesPage);
    await settle(fixture);

    const component = fixture.componentInstance;
    component.openCreate();
    fixture.detectChanges();

    component.formName.set('New Strategy');
    component.formDescription.set('Desc');
    component.formSymbol.set('EUR/USD');
    component.formTimeframe.set(5 as any);
    component.formRules.set('Rules here');

    await component.onCreate();
    fixture.detectChanges();

    expect(api.create).toHaveBeenCalledTimes(1);
    const payload: UpsertStrategyRequest = api.create.mock.calls[0][0];
    expect(payload.name).toBe('New Strategy');
    expect(payload.symbol).toBe('EUR/USD');

    // The list must now contain the created strategy.
    expect(state.list().length).toBe(1);
    expect(state.list()[0].name).toBe('New Strategy');
  });

  it('click on a card expands the analytics block with counts and rates', async () => {
    const s = sampleStrategy();
    api.list.mockResolvedValue([s]);
    api.getAnalytics.mockResolvedValue(sampleAnalytics());

    const fixture = TestBed.createComponent(StrategiesPage);
    await settle(fixture);

    const component = fixture.componentInstance;

    // Click the card head to expand.
    await component.toggleExpand(s);
    await settle(fixture);

    expect(api.getAnalytics).toHaveBeenCalledWith(s.id);

    const html = fixture.nativeElement as HTMLElement;
    const analyticsBlock = html.querySelector('[data-testid="strategies-analytics"]');
    expect(analyticsBlock).not.toBeNull();
    expect(analyticsBlock!.textContent).toContain('10');
    expect(analyticsBlock!.textContent).toContain('60.0%');
    expect(analyticsBlock!.textContent).toContain('300.00');
  });

  it('archive flow: clicking Archivar with confirm=true removes the card', async () => {
    const s = sampleStrategy();
    api.list.mockResolvedValue([s]);
    api.archive.mockResolvedValue(undefined);

    const fixture = TestBed.createComponent(StrategiesPage);
    await settle(fixture);

    const component = fixture.componentInstance;

    // Spy on confirm so the test never blocks on a native dialog.
    jest.spyOn(window, 'confirm').mockReturnValue(true);

    await component.onArchive(s);
    fixture.detectChanges();

    expect(api.archive).toHaveBeenCalledWith(s.id);
    expect(state.list().length).toBe(0);
  });
});