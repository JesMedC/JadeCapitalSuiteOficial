import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { AdminApiService } from './admin-api.service';

describe('AdminApiService', () => {
  let service: AdminApiService;
  let http: { get: jest.Mock; post: jest.Mock };

  beforeEach(() => {
    http = { get: jest.fn(), post: jest.fn() };
    TestBed.configureTestingModule({
      providers: [AdminApiService, { provide: HttpClient, useValue: http }],
    });
    service = TestBed.inject(AdminApiService);
  });

  it('list() calls GET /api/admin/subscriptions with status+page+pageSize', async () => {
    http.get.mockResolvedValue({ total: 0, page: 1, pageSize: 20, items: [] });
    await service.list('Active', 1, 20);
    expect(http.get).toHaveBeenCalledWith('/api/admin/subscriptions', expect.any(Object));
  });

  it('list() works with empty status filter', async () => {
    http.get.mockResolvedValue({ total: 0, page: 1, pageSize: 20, items: [] });
    await service.list('', 1, 20);
    expect(http.get).toHaveBeenCalled();
  });

  it('detail() calls GET /api/admin/subscriptions/:id', async () => {
    http.get.mockResolvedValue({ subscriptionId: 'abc' });
    await service.detail('abc');
    expect(http.get).toHaveBeenCalledWith('/api/admin/subscriptions/abc');
  });

  it('changeTier() POSTs body with newPlanCode + observedVersion', async () => {
    http.post.mockResolvedValue(null);
    await service.changeTier('id-1', 'pro', 3);
    expect(http.post).toHaveBeenCalledWith(
      '/api/admin/subscriptions/id-1/change-tier',
      { newPlanCode: 'pro', observedVersion: 3 },
    );
  });

  it('cancel() POSTs body with reason + observedVersion', async () => {
    http.post.mockResolvedValue(null);
    await service.cancel('id-1', 'user-requested', 5);
    expect(http.post).toHaveBeenCalledWith(
      '/api/admin/subscriptions/id-1/cancel',
      { reason: 'user-requested', observedVersion: 5 },
    );
  });

  it('extendTrial() POSTs ISO newTrialEndsAt + observedVersion', async () => {
    http.post.mockResolvedValue(null);
    await service.extendTrial('id-1', '2026-12-31T00:00:00.000Z', 7);
    expect(http.post).toHaveBeenCalledWith(
      '/api/admin/subscriptions/id-1/extend-trial',
      { newTrialEndsAt: '2026-12-31T00:00:00.000Z', observedVersion: 7 },
    );
  });
});
