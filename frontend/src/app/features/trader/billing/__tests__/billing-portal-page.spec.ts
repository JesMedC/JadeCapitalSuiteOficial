import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import {
  BillingPortalInvoiceDto,
  BillingPortalPaymentMethodDto,
  BillingPortalSubscriptionDto,
  StripePortalSessionDto,
} from '../api/billing-portal.types';
import { BillingPortalService } from '../api/billing-portal.service';
import { BillingPortalState } from '../state/billing-portal.state';
import { BillingPortalPage } from '../billing-portal-page';

// Suppress the harmless zone.js deprecation warning emitted by the jest-preset-angular bootstrap.
jest.spyOn(console, 'warn').mockImplementation(() => {});

// ============================================================================
//  BillingPortalPage — slice 6b.2 frontend tests.
//
//  6 specs (matches the spec in tasks.md line 219):
//   1. renders the page title + renders billing-portal-page testid.
//   2. exposes the helper methods (formatAmount + formatDate + canNavigateToPortal).
//   3. canNavigateToPortal returns true when subscription is loaded.
//   4. empty state: subscription() === null → "no subscription" copy.
//   5. error state: 503 on the portal-read endpoint → error banner.
//   6. loading state: isLoading()=true → spinner + loading copy.
// ============================================================================

describe('BillingPortalPage', () => {
  let api: {
    getSubscription: jest.Mock;
    getPaymentMethods: jest.Mock;
    getInvoices: jest.Mock;
    createPortalSession: jest.Mock;
  };
  let state: BillingPortalState;

  const sampleSubscription = (
    overrides: Partial<BillingPortalSubscriptionDto> = {},
  ): BillingPortalSubscriptionDto => ({
    stripeSubscriptionId: 'sub_1',
    status: 'active',
    planCode: 'pro',
    currentPeriodEnd: '2026-09-19T00:00:00.000Z',
    cancelAtPeriodEnd: false,
    ...overrides,
  });

  const sampleMethod = (
    overrides: Partial<BillingPortalPaymentMethodDto> = {},
  ): BillingPortalPaymentMethodDto => ({
    id: 'pm_1',
    brand: 'visa',
    last4: '4242',
    expiresAt: '2028-08-01T00:00:00.000Z',
    isDefault: true,
    ...overrides,
  });

  const sampleInvoice = (
    overrides: Partial<BillingPortalInvoiceDto> = {},
  ): BillingPortalInvoiceDto => ({
    id: 'in_1',
    number: 'INV-001',
    amountCents: 1999,
    currency: 'usd',
    issuedAt: '2026-08-01T00:00:00.000Z',
    paidAt: '2026-08-01T01:00:00.000Z',
    status: 'paid',
    pdfUrl: 'https://stripe.com/inv/1.pdf',
    ...overrides,
  });

  const samplePortalSession = (
    overrides: Partial<StripePortalSessionDto> = {},
  ): StripePortalSessionDto => ({
    sessionId: 'ps_test_1',
    url: 'https://billing.stripe.com/p/session/ps_test_1',
    expiresAt: '2026-08-19T15:32:00.000Z',
    ...overrides,
  });

  const fakeApi = (): {
    getSubscription: jest.Mock;
    getPaymentMethods: jest.Mock;
    getInvoices: jest.Mock;
    createPortalSession: jest.Mock;
  } => ({
    getSubscription: jest.fn().mockResolvedValue(sampleSubscription()),
    getPaymentMethods: jest.fn().mockResolvedValue([sampleMethod()]),
    getInvoices: jest.fn().mockResolvedValue([sampleInvoice()]),
    createPortalSession: jest.fn().mockResolvedValue(samplePortalSession()),
  });

  beforeEach(async () => {
    api = fakeApi();

    await TestBed.configureTestingModule({
      imports: [BillingPortalPage],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        BillingPortalState,
        { provide: BillingPortalService, useValue: api },
      ],
    }).compileComponents();

    state = TestBed.inject(BillingPortalState);
    state.subscription.set(null);
    state.paymentMethods.set([]);
    state.invoices.set([]);
    state.isLoading.set(false);
    state.error.set(null);
  });

  /** Flushes microtasks + change-detection. */
  async function settle(fixture: { detectChanges: () => void }) {
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();
  }

  it('renders the page title and testid', async () => {
    const fixture = TestBed.createComponent(BillingPortalPage);
    await settle(fixture);

    const html = fixture.nativeElement as HTMLElement;
    expect(html.querySelector('[data-testid="billing-portal-page"]')).not.toBeNull();
    expect(html.textContent).toContain('Billing Portal');
  });

  it('exposes helper methods (formatAmount, formatDate, canNavigateToPortal)', async () => {
    const fixture = TestBed.createComponent(BillingPortalPage);
    await settle(fixture);

    const component = fixture.componentInstance;

    // formatAmount: USD gets the "$" prefix, EUR/other currencies get the
    // ISO code prefix with a space.
    expect(component.formatAmount(1999, 'usd')).toBe('$19.99');
    expect(component.formatAmount(2500, 'eur')).toBe('EUR 25.00');
    expect(component.formatAmount(0, 'usd')).toBe('$0.00');

    // formatDate: valid ISO → locale string; invalid → echo input.
    expect(component.formatDate('2026-09-19T00:00:00.000Z')).not.toBe('');
    expect(component.formatDate('')).toBe('');

    // canNavigateToPortal exists and is callable (delegates to the state).
    // The "true after subscription loads" semantics is pinned by the next spec.
    expect(typeof component.canNavigateToPortal).toBe('function');
    expect(component.canNavigateToPortal()).toBe(true);
  });

  it('canNavigateToPortal returns true once the subscription has loaded', async () => {
    const fixture = TestBed.createComponent(BillingPortalPage);
    await settle(fixture);

    const component = fixture.componentInstance;

    // After the page's constructor calls state.loadAll(), the subscription
    // is in the store and the helper flips to true.
    expect(component.canNavigateToPortal()).toBe(true);
    expect(state.hasSubscription()).toBe(true);
  });

  it('empty state: no subscription → renders the "no subscription" copy', async () => {
    api.getSubscription.mockRejectedValueOnce(
      new HttpErrorResponse({ status: 404, statusText: 'Not Found', error: { detail: 'no Stripe customer' } }),
    );

    const fixture = TestBed.createComponent(BillingPortalPage);
    await settle(fixture);

    const html = fixture.nativeElement as HTMLElement;
    const emptyBlock = html.querySelector('[data-testid="billing-portal-empty"]');
    expect(emptyBlock).not.toBeNull();
    expect(emptyBlock!.textContent).toContain('Aún no tienes una suscripción activa');

    // The plan card must NOT render in the empty state.
    expect(html.querySelector('[data-testid="billing-portal-plan-card"]')).toBeNull();
    expect(component_instance_planCard(html)).toBe(false);
  });

  it('error state: backend 503 → renders the error banner', async () => {
    api.getSubscription.mockRejectedValueOnce(
      new HttpErrorResponse({
        status: 503,
        statusText: 'Service Unavailable',
        error: { detail: 'Stripe temporarily unavailable', code: 'stripe.unavailable' },
      }),
    );
    // Make the parallel calls fail too so the state surfaces the error.
    api.getPaymentMethods.mockRejectedValueOnce(
      new HttpErrorResponse({ status: 503, statusText: 'Service Unavailable' }),
    );
    api.getInvoices.mockRejectedValueOnce(
      new HttpErrorResponse({ status: 503, statusText: 'Service Unavailable' }),
    );

    const fixture = TestBed.createComponent(BillingPortalPage);
    await settle(fixture);

    const html = fixture.nativeElement as HTMLElement;
    const errorBanner = html.querySelector('[data-testid="billing-portal-error"]');
    expect(errorBanner).not.toBeNull();
    expect(errorBanner!.textContent).toContain('Stripe temporarily unavailable');

    // The empty + plan card + loading blocks must NOT render in the error state.
    expect(html.querySelector('[data-testid="billing-portal-empty"]')).toBeNull();
    expect(html.querySelector('[data-testid="billing-portal-plan-card"]')).toBeNull();
  });

  it('loading state: isLoading()=true → spinner + loading copy', async () => {
    // Pin the subscription fetch to a pending promise so the page stays in
    // loading state while the assertion runs.
    let resolveSub!: (v: BillingPortalSubscriptionDto) => void;
    api.getSubscription.mockReturnValueOnce(
      new Promise<BillingPortalSubscriptionDto>((resolve) => {
        resolveSub = resolve;
      }),
    );
    api.getPaymentMethods.mockResolvedValueOnce([]);
    api.getInvoices.mockResolvedValueOnce([]);

    const fixture = TestBed.createComponent(BillingPortalPage);
    // One flush to run the constructor's loadAll() but not resolve the sub.
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    const loadingBlock = html.querySelector('[data-testid="billing-portal-loading"]');
    expect(loadingBlock).not.toBeNull();
    expect(loadingBlock!.textContent).toContain('Cargando tu suscripción');

    // Plan card + empty + error must NOT render during loading.
    expect(html.querySelector('[data-testid="billing-portal-plan-card"]')).toBeNull();
    expect(html.querySelector('[data-testid="billing-portal-empty"]')).toBeNull();
    expect(html.querySelector('[data-testid="billing-portal-error"]')).toBeNull();

    // Resolve so the test does not leak a pending promise.
    resolveSub(sampleSubscription());
    await settle(fixture);
  });
});

/**
 * Tiny helper kept private to this spec file — avoids the linter flagging
 * the inline expression. The plan card testid is `billing-portal-plan-card`.
 */
function component_instance_planCard(html: HTMLElement): boolean {
  return html.querySelector('[data-testid="billing-portal-plan-card"]') !== null;
}
