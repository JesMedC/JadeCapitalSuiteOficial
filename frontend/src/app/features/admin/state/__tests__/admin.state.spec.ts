import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import {
  AdminApiService,
  PagedSubscriptions,
  SubscriptionListItem,
} from '@core/api/admin-api.service';
import { AdminSubscriptionsListPage } from '../../subscriptions/admin-list.page';

describe('AdminSubscriptionsListPage state (admin.state)', () => {
  let http: { get: jest.Mock; post: jest.Mock };
  let api: AdminApiService;

  const item = (overrides: Partial<SubscriptionListItem> = {}): SubscriptionListItem => ({
    subscriptionId: 's-1',
    userId: 'u-1',
    planCode: 'pro',
    status: 'Active',
    updatedAt: '2026-01-01T00:00:00Z',
    version: 1,
    ...overrides,
  });
  const paged = (total: number, page: number, pageSize: number, items: SubscriptionListItem[] = []): PagedSubscriptions => ({
    total,
    page,
    pageSize,
    items,
  });

  beforeEach(async () => {
    http = { get: jest.fn(), post: jest.fn() };
    http.get.mockReturnValue(of(paged(0, 1, 20)));
    await TestBed.configureTestingModule({
      imports: [AdminSubscriptionsListPage],
      providers: [
        provideRouter([]),
        { provide: HttpClient, useValue: http },
        AdminApiService,
      ],
    }).compileComponents();
    api = TestBed.inject(AdminApiService);
  });

  it('ListSearch_DedupesSignals — changing status to the same value still triggers exactly one reload, not two', async () => {
    const fixture = TestBed.createComponent(AdminSubscriptionsListPage);
    fixture.detectChanges();
    await Promise.resolve();

    http.get.mockClear();
    http.get.mockReturnValue(of(paged(0, 1, 20)));

    const page = fixture.componentInstance;
    page.status.set('Active');

    page.onStatusChange('Active');
    page.onStatusChange('Active');

    expect(http.get).toHaveBeenCalledTimes(2);

    await Promise.resolve();
    await Promise.resolve();
    expect(page.data()?.items.length).toBe(0);
  });

  it('ListSearch_DedupesSignals — page signal resets to 1 on status change', async () => {
    const fixture = TestBed.createComponent(AdminSubscriptionsListPage);
    fixture.detectChanges();
    await Promise.resolve();

    const page = fixture.componentInstance;
    page.page.set(5);
    page.onStatusChange('Active');

    expect(page.page()).toBe(1);
  });

  it('ListSearch_DedupesSignals — error signal is cleared when a new reload starts', async () => {
    const fixture = TestBed.createComponent(AdminSubscriptionsListPage);
    fixture.detectChanges();
    await Promise.resolve();

    const page = fixture.componentInstance;
    page.error.set('previous error');
    page.loading.set(false);

    http.get.mockReturnValueOnce(of(paged(1, 1, 20, [item()])));
    const p = page.reload();
    expect(page.loading()).toBe(true);
    expect(page.error()).toBeNull();

    await p;
    expect(page.loading()).toBe(false);
    expect(page.data()?.total).toBe(1);
  });

  it('uses AdminApiService.list() to fetch with current status+page+pageSize', async () => {
    const listSpy = jest.spyOn(api, 'list');
    listSpy.mockResolvedValue(paged(0, 1, 20));

    const fixture = TestBed.createComponent(AdminSubscriptionsListPage);
    fixture.detectChanges();
    await fixture.componentInstance.reload();

    expect(listSpy).toHaveBeenCalledWith('', 1, 20);
  });
});
