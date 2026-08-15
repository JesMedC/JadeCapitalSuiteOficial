import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AdminApiService, SubscriptionDetail } from '@core/api/admin-api.service';
import { AdminSubscriptionDetailPage } from '../admin-detail.page';

describe('AdminSubscriptionDetailPage', () => {
  let http: { get: jest.Mock; post: jest.Mock };

  const detail = (overrides: Partial<SubscriptionDetail> = {}): SubscriptionDetail => ({
    subscriptionId: 's-1',
    userId: 'u-1',
    planCode: 'pro',
    planName: 'Pro',
    status: 'Active',
    trialEndsAt: null,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: null,
    version: 1,
    owner: { email: 'user@example.com', displayName: 'Test User' },
    history: [],
    ...overrides,
  });

  beforeEach(async () => {
    http = { get: jest.fn(), post: jest.fn() };
    await TestBed.configureTestingModule({
      imports: [AdminSubscriptionDetailPage],
      providers: [
        provideRouter([]),
        { provide: HttpClient, useValue: http },
        AdminApiService,
      ],
    }).compileComponents();
  });

  it('StaleConflict_PreservesInput — when changeTier fails with a stale version, the user-typed plan and reason stay editable', async () => {
    http.get.mockReturnValueOnce(of(detail({ version: 3 })));
    http.post.mockReturnValueOnce(
      throwError(() => ({ status: 409, message: 'stale version' })),
    );

    const fixture = TestBed.createComponent(AdminSubscriptionDetailPage);
    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('id', 's-1');
    });
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    const page = fixture.componentInstance;
    page.newPlan = 'elite';
    page.cancelReason = 'requested by user';
    page.newTrialDate = '2026-12-31';

    await page.changeTier();

    expect(page.actionError()).toContain('Cambiar tier');
    expect(page.busy()).toBe(false);

    expect(page.newPlan).toBe('elite');
    expect(page.cancelReason).toBe('requested by user');
    expect(page.newTrialDate).toBe('2026-12-31');
  });

  it('StaleConflict_PreservesInput — after a 409 the user input remains intact across multiple actions', async () => {
    http.get.mockReturnValueOnce(of(detail({ version: 5 })));
    http.post.mockReturnValueOnce(throwError(() => ({ status: 409, message: 'conflict' })));

    const fixture = TestBed.createComponent(AdminSubscriptionDetailPage);
    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('id', 's-1');
    });
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    const page = fixture.componentInstance;
    page.newPlan = 'starter';
    page.cancelReason = 'admin initiated';

    await page.changeTier();
    expect(page.actionError()).toContain('Cambiar tier');

    expect(page.newPlan).toBe('starter');
    expect(page.cancelReason).toBe('admin initiated');
  });

  it('renders the owner display name + email in the detail header when data loads', async () => {
    http.get.mockReturnValueOnce(of(detail({ owner: { email: 'x@y.com', displayName: 'Jane Doe' } })));
    const fixture = TestBed.createComponent(AdminSubscriptionDetailPage);
    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('id', 's-1');
    });
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Jane Doe');
    expect(html.textContent).toContain('x@y.com');
  });
});
