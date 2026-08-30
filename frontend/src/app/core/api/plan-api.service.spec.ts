import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { PlanApiService } from './plan-api.service';

describe('PlanApiService', () => {
  let service: PlanApiService;
  let http: { get: jest.Mock };

  beforeEach(() => {
    http = { get: jest.fn() };
    TestBed.configureTestingModule({
      providers: [PlanApiService, { provide: HttpClient, useValue: http }],
    });
    service = TestBed.inject(PlanApiService);
  });

  it('list() calls GET /api/billing/plans', async () => {
    const sample = [
      { code: 'starter', name: 'Starter', monthlyPrice: 9.99, currency: 'USD', isEligibleForSelfService: true },
      { code: 'pro',     name: 'Pro',     monthlyPrice: 29.99, currency: 'USD', isEligibleForSelfService: true },
      { code: 'elite',   name: 'Elite',   monthlyPrice: 99.99, currency: 'USD', isEligibleForSelfService: true },
    ];
    http.get.mockReturnValue(of(sample));

    const result = await service.list();

    expect(http.get).toHaveBeenCalledWith('/api/billing/plans');
    expect(result).toEqual(sample);
  });

  it('list() returns an empty array when the catalog is empty', async () => {
    http.get.mockReturnValue(of([]));
    const result = await service.list();
    expect(result).toEqual([]);
  });
});
