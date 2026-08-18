import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  BillingPortalInvoiceDto,
  BillingPortalPaymentMethodDto,
  BillingPortalSubscriptionDto,
  StripePortalSessionDto,
} from './billing-portal.types';

// ============================================================================
//  BillingPortalService — slice 6b.2 frontend.
//
//  Wraps the 4 endpoints the billing portal page consumes:
//    - GET  /api/billing/portal/subscription      → BillingPortalSubscriptionDto
//    - GET  /api/billing/portal/payment-methods   → BillingPortalPaymentMethodDto[]
//    - GET  /api/billing/portal/invoices          → BillingPortalInvoiceDto[]
//    - POST /api/billing/stripe/portal            → StripePortalSessionDto
//      (mounted by slice 6a.2 — used to redirect the user to Stripe's
//       hosted Customer Portal so they can manage cards / cancel sub / etc.)
//
//  Auth: all 4 endpoints require a JWT bearer token. The auth.interceptor
//  adds the header automatically — we never set it here.
//
//  Rate limit: the 3 GET endpoints live under the `api-billing` policy
//  (10 calls/hour/user). The slice 6b.1 backend adds the limit; the FE
//  does not need to throttle.
// ============================================================================

@Injectable({ providedIn: 'root' })
export class BillingPortalService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/billing/portal';

  /** Returns the caller's current subscription summary. 404 if no Stripe customer. */
  async getSubscription(): Promise<BillingPortalSubscriptionDto> {
    return firstValueFrom(this.http.get<BillingPortalSubscriptionDto>(`${this.base}/subscription`));
  }

  /** Returns the caller's saved payment methods. Empty array if the user has none. */
  async getPaymentMethods(): Promise<BillingPortalPaymentMethodDto[]> {
    return firstValueFrom(this.http.get<BillingPortalPaymentMethodDto[]>(`${this.base}/payment-methods`));
  }

  /** Returns the caller's invoices (newest first per BE). Empty array if none. */
  async getInvoices(): Promise<BillingPortalInvoiceDto[]> {
    return firstValueFrom(this.http.get<BillingPortalInvoiceDto[]>(`${this.base}/invoices`));
  }

  /**
   * Creates a Stripe Customer Portal session for the caller. The FE
   * redirects to `result.url` so Stripe hosts the self-service UI.
   *
   * 404 if the caller has no Stripe Customer mapping yet (must complete a
   * Checkout first — slice 6a.2 endpoint).
   */
  async createPortalSession(): Promise<StripePortalSessionDto> {
    return firstValueFrom(this.http.post<StripePortalSessionDto>('/api/billing/stripe/portal', {}));
  }
}
