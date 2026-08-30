import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { MetricsApiService } from '@core/api/metrics-api.service';
import { AnalyticsPage } from '../analytics.page';

describe('AnalyticsPage — server-side metrics binding (slice 1f)', () => {
  let http: { get: jest.Mock };

  beforeEach(async () => {
    http = { get: jest.fn() };
    await TestBed.configureTestingModule({
      imports: [AnalyticsPage],
      providers: [
        { provide: HttpClient, useValue: http },
        MetricsApiService,
      ],
    }).compileComponents();
  });

  it('fetches /api/trades/metrics with the selected period on load', async () => {
    const dto = sampleMetricsDto();
    const pagedEmpty = { items: [], total: 0, page: 1, pageSize: 100 };
    const dashEmpty = {
      totalCount: 0, openCount: 0, closedCount: 0, winsCount: 0, lossesCount: 0,
      winRate: 0, totalPnL: 0, bestTrade: 0, worstTrade: 0, avgTrade: 0, currency: 'USD',
    };
    http.get.mockImplementation((url: string) => {
      if (url === '/api/trades/metrics') return of(dto);
      if (url === '/api/trades/dashboard') return of(dashEmpty);
      if (url === '/api/trades') return of(pagedEmpty);
      throw new Error(`unexpected GET ${url}`);
    });

    const fixture = TestBed.createComponent(AnalyticsPage);
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    const calls = http.get.mock.calls.map(c => c[0]);
    expect(calls).toContain('/api/trades/metrics');
    const metricsCall = http.get.mock.calls.find(c => c[0] === '/api/trades/metrics')!;
    const params = metricsCall[1].params;
    expect(params.get('period')).toBe('30d');
  });

  it('does NOT contain the dropped initialBalance / dailyYield mocks', () => {
    const source = require('fs').readFileSync(
      require('path').join(__dirname, '..', 'analytics.page.ts'),
      'utf8'
    );
    expect(source).not.toMatch(/initialBalance\s*=\s*10000/);
    expect(source).not.toMatch(/dailyYield\s*=\s*0\.0006/);
    expect(source).not.toMatch(/function\s+buildBalanceCurve/);
  });

  it('renders the server equity curve (no buildBalanceCurve fallback)', async () => {
    const dto = sampleMetricsDto();
    const pagedEmpty = { items: [], total: 0, page: 1, pageSize: 100 };
    const dashEmpty = {
      totalCount: 0, openCount: 0, closedCount: 0, winsCount: 0, lossesCount: 0,
      winRate: 0, totalPnL: 0, bestTrade: 0, worstTrade: 0, avgTrade: 0, currency: 'USD',
    };
    http.get.mockImplementation((url: string) => {
      if (url === '/api/trades/metrics') return of(dto);
      if (url === '/api/trades/dashboard') return of(dashEmpty);
      if (url === '/api/trades') return of(pagedEmpty);
      throw new Error(`unexpected GET ${url}`);
    });

    const fixture = TestBed.createComponent(AnalyticsPage);
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    const instance = fixture.componentInstance as any;
    expect(instance.metrics()).not.toBeNull();
    const serverCurve = instance.serverEquityCurve();
    expect(serverCurve).toHaveLength(dto.equityCurve.length);
    expect(serverCurve[0].equity).toBe(dto.equityCurve[0].equity);
  });

  it('renders empty state when backend returns empty curve and zero closed', async () => {
    const emptyDto = sampleMetricsDto();
    emptyDto.totalClosedTrades = 0;
    emptyDto.totalOpenTrades = 0;
    emptyDto.totalTrades = 0;
    emptyDto.equityCurve = [];
    emptyDto.symbolStats = [];
    emptyDto.winRate = 0;
    emptyDto.expectancy = 0;
    emptyDto.profitFactor = 0;
    emptyDto.maxDrawdown = 0;
    emptyDto.maxDrawdownAmount = 0;

    const pagedEmpty = { items: [], total: 0, page: 1, pageSize: 100 };
    const dashEmpty = {
      totalCount: 0, openCount: 0, closedCount: 0, winsCount: 0, lossesCount: 0,
      winRate: 0, totalPnL: 0, bestTrade: 0, worstTrade: 0, avgTrade: 0, currency: 'USD',
    };
    http.get.mockImplementation((url: string) => {
      if (url === '/api/trades/metrics') return of(emptyDto);
      if (url === '/api/trades/dashboard') return of(dashEmpty);
      if (url === '/api/trades') return of(pagedEmpty);
      throw new Error(`unexpected GET ${url}`);
    });

    const fixture = TestBed.createComponent(AnalyticsPage);
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    const instance = fixture.componentInstance as any;
    expect(instance.metrics()).not.toBeNull();
    expect(instance.serverEquityCurve()).toEqual([]);
    expect(instance.symbolStats()).toEqual([]);
    expect(instance.expectancy()).toBe(0);
    expect(instance.profitFactor()).toBe(0);
    expect(instance.maxDrawdown()).toBe(0);
  });
});

function sampleMetricsDto() {
  return {
    period: '30d',
    totalTrades: 2,
    totalClosedTrades: 2,
    totalOpenTrades: 0,
    winRate: 50,
    expectancy: 25,
    profitFactor: 2,
    payoff: 1.5,
    sqn: 0.8,
    maxDrawdown: -50,
    maxDrawdownAmount: -50,
    maxDrawdownPercent: -50,
    equityCurve: [
      { timestamp: '2026-07-10T10:00:00Z', equity: 100, drawdown: 0 },
      { timestamp: '2026-07-12T10:00:00Z', equity: 50, drawdown: -50 },
    ],
    symbolStats: [
      { symbol: 'EUR/USD', trades: 2, totalPnl: 50, winRate: 50 },
    ],
    currency: 'USD',
  };
}
