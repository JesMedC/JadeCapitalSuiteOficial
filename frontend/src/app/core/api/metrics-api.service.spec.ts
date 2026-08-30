import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpParams } from '@angular/common/http';
import { of } from 'rxjs';
import { MetricsApiService } from '@core/api/metrics-api.service';

describe('MetricsApiService', () => {
  let service: MetricsApiService;
  let http: { get: jest.Mock };

  beforeEach(() => {
    http = { get: jest.fn() };
    TestBed.configureTestingModule({
      providers: [MetricsApiService, { provide: HttpClient, useValue: http }],
    });
    service = TestBed.inject(MetricsApiService);
  });

  it('get(period) calls GET /api/trades/metrics with the period query string', async () => {
    const dto = sampleMetricsDto();
    http.get.mockReturnValue(of(dto));

    const result = await service.get('30d');

    expect(http.get).toHaveBeenCalledWith(
      '/api/trades/metrics',
      expect.objectContaining({ params: expect.any(HttpParams) }),
    );
    const calledParams = http.get.mock.calls[0][1].params as HttpParams;
    expect(calledParams.get('period')).toBe('30d');
    expect(result).toEqual(dto);
  });

  it('get(\'7d\') forwards the 7d period verbatim', async () => {
    http.get.mockReturnValue(of(sampleMetricsDto()));
    await service.get('7d');
    const calledParams = http.get.mock.calls[0][1].params as HttpParams;
    expect(calledParams.get('period')).toBe('7d');
  });

  it('get(\'all\') forwards the all period verbatim', async () => {
    http.get.mockReturnValue(of(sampleMetricsDto()));
    await service.get('all');
    const calledParams = http.get.mock.calls[0][1].params as HttpParams;
    expect(calledParams.get('period')).toBe('all');
  });

  it('returns the server DTO structure unchanged (equityCurve + symbolStats)', async () => {
    const dto = sampleMetricsDto();
    http.get.mockReturnValue(of(dto));

    const result = await service.get('30d');

    expect(result.equityCurve).toEqual(dto.equityCurve);
    expect(result.symbolStats).toEqual(dto.symbolStats);
    expect(result.maxDrawdownAmount).toBe(dto.maxDrawdownAmount);
    expect(result.maxDrawdownPercent).toBe(dto.maxDrawdownPercent);
  });
});

function sampleMetricsDto() {
  return {
    period: '30d',
    totalTrades: 4,
    totalClosedTrades: 3,
    totalOpenTrades: 1,
    winRate: 66.67,
    expectancy: 25.5,
    profitFactor: 1.5,
    payoff: 1.2,
    sqn: 1.8,
    maxDrawdown: -50,
    maxDrawdownAmount: -50,
    maxDrawdownPercent: -33.33,
    equityCurve: [
      { timestamp: '2026-07-01T10:00:00Z', equity: 100, drawdown: 0 },
      { timestamp: '2026-07-02T10:00:00Z', equity: 50, drawdown: -50 },
    ],
    symbolStats: [
      { symbol: 'EUR/USD', trades: 2, totalPnl: 100, winRate: 100 },
    ],
    currency: 'USD',
  };
}
