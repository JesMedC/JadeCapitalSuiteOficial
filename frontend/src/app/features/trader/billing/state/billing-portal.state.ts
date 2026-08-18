import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import {
  BillingPortalInvoiceDto,
  BillingPortalPaymentMethodDto,
  BillingPortalProblem,
  BillingPortalSubscriptionDto,
} from '../api/billing-portal.types';
import { BillingPortalService } from '../api/billing-portal.service';

// ============================================================================
//  BillingPortalState — slice 6b.2 frontend.
//
//  Signal store for the billing portal page. Renders four states from the
//  signal set:
//    - isLoading()                                       → spinner
//    - error()                                           → red banner
//    - subscription() === null && !error() && !loading()  → "no subscription yet"
//    - subscription() !== null                           → plan card + lists
//
//  All three reads happen in parallel on `loadAll()` (Promise.all). The page
//  only renders the lists once subscription has resolved (cards block on
//  sub), so a sub 404 still short-circuits the page into the empty state
//  without flashing a payment-methods / invoices flash.
// ============================================================================

@Injectable({ providedIn: 'root' })
export class BillingPortalState {
  private readonly api = inject(BillingPortalService);

  readonly subscription = signal<BillingPortalSubscriptionDto | null>(null);
  readonly paymentMethods = signal<BillingPortalPaymentMethodDto[]>([]);
  readonly invoices = signal<BillingPortalInvoiceDto[]>([]);
  readonly isLoading = signal(false);
  readonly error = signal<string | null>(null);

  readonly hasSubscription = computed(() => this.subscription() !== null);
  readonly canNavigateToPortal = computed(() => this.subscription() !== null);

  /** Top-level load: 3 GETs in parallel. */
  async loadAll(): Promise<void> {
    this.isLoading.set(true);
    this.error.set(null);
    try {
      const [sub, methods, invoices] = await Promise.all([
        this.api.getSubscription(),
        this.api.getPaymentMethods(),
        this.api.getInvoices(),
      ]);
      this.subscription.set(sub);
      this.paymentMethods.set(methods);
      this.invoices.set(invoices);
    } catch (e) {
      // 404 on /subscription must NOT collapse the whole page into an error
      // banner — it is the "no subscription yet" UX (the user has not
      // completed a checkout). We treat it as an empty state by leaving
      // subscription() at null and setting a friendly error.
      if (e instanceof HttpErrorResponse && e.status === 404) {
        this.subscription.set(null);
        // Clear so the empty-state branch can render instead of the error banner.
      } else {
        this.error.set(this.formatError(e));
      }
    } finally {
      this.isLoading.set(false);
    }
  }

  clearError(): void {
    this.error.set(null);
  }

  formatError(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      const body = (err.error ?? {}) as BillingPortalProblem;
      const detail = body.detail ?? body.title ?? err.message;
      if (detail) return detail;
      return `Error HTTP ${err.status}.`;
    }
    if (err instanceof Error && err.message) return err.message;
    return 'Ocurrió un error inesperado.';
  }
}
