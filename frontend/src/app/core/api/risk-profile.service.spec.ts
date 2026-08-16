import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { RiskProfileService, toRiskProfileError } from './risk-profile.service';
import { RiskProfileDto, UpsertRiskProfileRequest } from './risk-profile.types';

describe('RiskProfileService', () => {
  let service: RiskProfileService;
  let http: { get: jest.Mock; put: jest.Mock };

  beforeEach(() => {
    http = { get: jest.fn(), put: jest.fn() };
    TestBed.configureTestingModule({
      providers: [RiskProfileService, { provide: HttpClient, useValue: http }],
    });
    service = TestBed.inject(RiskProfileService);
  });

  it('getActive() calls GET /api/risk-profile and returns the DTO on 200', async () => {
    const dto = sampleDto();
    http.get.mockReturnValue(of(dto));

    const result = await service.getActive();

    expect(http.get).toHaveBeenCalledWith('/api/risk-profile');
    expect(result).toEqual(dto);
  });

  it('getActive() returns null when the backend returns 404 (no active profile)', async () => {
    http.get.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 404,
            statusText: 'Not Found',
            error: { code: 'notfound.risk_profile.not_found', detail: '...' },
          }),
      ),
    );

    const result = await service.getActive();

    expect(result).toBeNull();
  });

  it('getActive() rethrows a normalized RiskProfileError on 500', async () => {
    http.get.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 500,
            statusText: 'Internal Server Error',
            error: { code: 'unknown', detail: 'boom' },
          }),
      ),
    );

    await expect(service.getActive()).rejects.toMatchObject({
      status: 500,
      code: 'unknown',
      message: 'boom',
    });
  });

  it('upsert() calls PUT /api/risk-profile with the request body and returns the DTO', async () => {
    const dto = sampleDto();
    const req: UpsertRiskProfileRequest = {
      capitalAmount: 10_000,
      capitalCurrency: 'USD',
      maxDrawdownPercent: 10,
      riskPerTradePercent: 1,
      riskRewardTarget: 2,
    };
    http.put.mockReturnValue(of(dto));

    const result = await service.upsert(req);

    expect(http.put).toHaveBeenCalledWith('/api/risk-profile', req);
    expect(result).toEqual(dto);
  });

  it('upsert() normalizes 422 range errors into a RiskProfileError with the field hint', async () => {
    http.put.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 422,
            statusText: 'Unprocessable Entity',
            error: {
              code: 'validation.risk_profile.risk_per_trade_percent_out_of_range',
              detail: 'Risk-per-trade percent must be between 0.01 and 5.00.',
            },
          }),
      ),
    );

    await expect(
      service.upsert({
        capitalAmount: 1000,
        capitalCurrency: 'USD',
        maxDrawdownPercent: 10,
        riskPerTradePercent: 7.5,
        riskRewardTarget: 2,
      }),
    ).rejects.toMatchObject({
      status: 422,
      code: 'validation.risk_profile.risk_per_trade_percent_out_of_range',
      field: 'riskPerTradePercent',
    });
  });

  it('upsert() normalizes 409 conflicts into a RiskProfileError', async () => {
    http.put.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            statusText: 'Conflict',
            error: {
              code: 'conflict.risk_profile.concurrent_supersede',
              detail: 'Another update is in progress.',
            },
          }),
      ),
    );

    await expect(
      service.upsert({
        capitalAmount: 1000,
        capitalCurrency: 'USD',
        maxDrawdownPercent: 10,
        riskPerTradePercent: 1,
        riskRewardTarget: 2,
      }),
    ).rejects.toMatchObject({
      status: 409,
      code: 'conflict.risk_profile.concurrent_supersede',
    });
  });
});

describe('toRiskProfileError', () => {
  it('falls back to a generic message when the body has no detail', () => {
    const err = new HttpErrorResponse({ status: 502, statusText: 'Bad Gateway', error: {} });
    expect(toRiskProfileError(err)).toMatchObject({ status: 502, code: '', message: 'Http failure response for (unknown url): 502 Bad Gateway' });
  });

  it('handles non-HTTP errors', () => {
    expect(toRiskProfileError(new Error('boom'))).toEqual({
      status: 0,
      code: 'unknown',
      message: 'boom',
    });
    expect(toRiskProfileError('raw string')).toEqual({
      status: 0,
      code: 'unknown',
      message: 'Ocurrió un error inesperado.',
    });
  });
});

function sampleDto(): RiskProfileDto {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    userId: '22222222-2222-2222-2222-222222222222',
    capitalAmount: 10_000,
    capitalCurrency: 'USD',
    maxDrawdownPercent: 10,
    riskPerTradePercent: 1,
    riskRewardTarget: 2,
    isActive: true,
    supersededAt: null,
    createdAt: '2026-08-15T10:00:00.000Z',
    updatedAt: '2026-08-15T10:00:00.000Z',
  };
}
