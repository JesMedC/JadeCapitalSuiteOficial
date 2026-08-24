import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AdminApiService, PagedSubscriptions } from '@core/api/admin-api.service';
import { AdminSubscriptionsListPage } from '../admin-list.page';

describe('AdminSubscriptionsListPage', () => {
  let http: { get: jest.Mock; post: jest.Mock };
  const paged = (items: PagedSubscriptions['items'] = []): PagedSubscriptions => ({
    total: items.length,
    page: 1,
    pageSize: 20,
    items,
  });

  beforeEach(async () => {
    http = { get: jest.fn(), post: jest.fn() };
    await TestBed.configureTestingModule({
      imports: [AdminSubscriptionsListPage],
      providers: [
        provideRouter([]),
        { provide: HttpClient, useValue: http },
        AdminApiService,
      ],
    }).compileComponents();
  });

  it('LoadingEmptyErrorStates_NonOverlapping — loading state shows only loading copy, not empty/error/table', async () => {
    http.get.mockReturnValueOnce(of(paged([])));
    const fixture = TestBed.createComponent(AdminSubscriptionsListPage);
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    const page = fixture.componentInstance;
    page.loading.set(true);
    page.error.set(null);
    page.data.set(null);
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Cargando');
    expect(html.textContent).not.toContain('Sin suscripciones');
    expect(html.querySelector('table')).toBeNull();
  });

  it('LoadingEmptyErrorStates_NonOverlapping — empty state shows only empty copy, not loading/error/table', async () => {
    http.get.mockReturnValueOnce(of(paged([])));
    const fixture = TestBed.createComponent(AdminSubscriptionsListPage);
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Sin suscripciones');
    expect(html.textContent).not.toContain('Cargando');
    expect(html.querySelector('table')).toBeNull();
  });

  it('LoadingEmptyErrorStates_NonOverlapping — error state shows only error copy, not loading/empty/table', async () => {
    http.get.mockReturnValueOnce(throwError(() => new Error('boom')));
    const fixture = TestBed.createComponent(AdminSubscriptionsListPage);
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('boom');
    expect(html.textContent).not.toContain('Cargando');
    expect(html.textContent).not.toContain('Sin suscripciones');
    expect(html.querySelector('table')).toBeNull();
  });

  it('LoadingEmptyErrorStates_NonOverlapping — data state shows table and hides loading/empty/error', async () => {
    http.get.mockReturnValueOnce(
      of(
        paged([
          {
            subscriptionId: 's-1',
            userId: 'u-1',
            planCode: 'pro',
            status: 'Active',
            updatedAt: '2026-01-01T00:00:00Z',
            version: 1,
          },
        ]),
      ),
    );
    const fixture = TestBed.createComponent(AdminSubscriptionsListPage);
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.querySelector('table')).not.toBeNull();
    expect(html.textContent).toContain('u-1');
    const link = html.querySelector('a.jcs-link') as HTMLAnchorElement | null;
    expect(link?.getAttribute('href')).toBe('/admin/subscriptions/s-1');
    expect(html.textContent).not.toContain('Cargando');
    expect(html.textContent).not.toContain('Sin suscripciones');
  });
});
