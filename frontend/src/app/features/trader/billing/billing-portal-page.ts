import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { BillingPortalState } from './state/billing-portal.state';
import { BillingPortalService } from './api/billing-portal.service';

// ============================================================================
//  BillingPortalPage — slice 6b.2 frontend.
//
//  Self-service billing portal page mounted at /app/billing. Reads the
//  caller's subscription, payment methods, and invoices from the 3 BE
//  endpoints added in 6b.1, and exposes a "Manage in Stripe" button that
//  POSTs to /api/billing/stripe/portal (slice 6a.2) and redirects the
//  browser to the returned Stripe-hosted URL.
//
//  Layout:
//   - Header: title + subtitle.
//   - Plan card: current plan + status + next billing date + cancel flag.
//   - "Manage in Stripe" button (primary CTA) — disabled while no sub.
//   - Payment methods list (brand + last 4 + expiry + default badge).
//   - Invoices list (number + amount + status + pdf link).
//
//  States (driven by BillingPortalState):
//   - isLoading()                  → spinner
//   - error()                      → red banner
//   - subscription() === null      → "no subscription yet" empty state
//   - subscription() !== null      → plan card + lists
// ============================================================================

@Component({
  selector: 'jcs-billing-portal-page',
  standalone: true,
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="bp-page" data-testid="billing-portal-page">
      <header class="bp-head">
        <p class="jcs-muted bp-eyebrow">Suscripción</p>
        <h1 class="bp-title">Billing Portal</h1>
        <p class="jcs-muted bp-sub">
          Tu plan, métodos de pago y facturas. Pagos seguros a través de Stripe.
        </p>
      </header>

      <!-- ============== Error banner ============== -->
      @if (state.error(); as err) {
        <div class="bp-error" role="alert" data-testid="billing-portal-error">
          <span>{{ err }}</span>
          <button type="button" class="bp-error-dismiss" (click)="state.clearError()">×</button>
        </div>
      }

      <!-- ============== Loading ============== -->
      @if (state.isLoading()) {
        <div class="bp-loading" aria-live="polite" data-testid="billing-portal-loading">
          <span class="bp-spinner" aria-hidden="true"></span>
          <span class="jcs-muted">Cargando tu suscripción…</span>
        </div>
      }

      <!-- ============== Empty state — no subscription yet ============== -->
      @if (!state.isLoading() && !state.hasSubscription() && !state.error()) {
        <div class="bp-empty" data-testid="billing-portal-empty">
          <p>Aún no tienes una suscripción activa.</p>
          <p class="jcs-muted">Empezá con un plan desde la página de precios.</p>
          <a class="bp-btn bp-btn--primary" href="/pricing">Ver planes</a>
        </div>
      }

      <!-- ============== Subscribed — plan card + lists ============== -->
      @if (state.hasSubscription() && !state.isLoading()) {
        <article class="bp-plan-card" data-testid="billing-portal-plan-card">
          <header class="bp-plan-head">
            <div>
              <p class="bp-plan-label">Plan actual</p>
              <h2 class="bp-plan-name">{{ planDisplay() }}</h2>
            </div>
            <span class="bp-status" [attr.data-status]="subscription()!.status">
              {{ statusLabel() }}
            </span>
          </header>

          <dl class="bp-plan-meta">
            <div>
              <dt>Próximo cobro</dt>
              <dd>{{ subscription()!.currentPeriodEnd | date:'longDate' }}</dd>
            </div>
            @if (subscription()!.cancelAtPeriodEnd) {
              <div class="bp-plan-meta--warn">
                <dt>Cancelación</dt>
                <dd>Programada al final del período</dd>
              </div>
            }
          </dl>

          <div class="bp-plan-actions">
            <button
              type="button"
              class="bp-btn bp-btn--primary"
              data-testid="billing-portal-manage"
              [disabled]="navigatingToPortal()"
              (click)="onManageInStripe()">
              @if (navigatingToPortal()) {
                Redirigiendo…
              } @else {
                Manage in Stripe
              }
            </button>
          </div>
        </article>

        <!-- ============== Payment methods ============== -->
        <section class="bp-section" data-testid="billing-portal-methods">
          <h3 class="bp-section-title">Métodos de pago</h3>
          @if (state.paymentMethods().length === 0) {
            <p class="jcs-muted">No tenés métodos de pago guardados.</p>
          } @else {
            <ul class="bp-methods">
              @for (m of state.paymentMethods(); track m.id) {
                <li class="bp-method">
                  <span class="bp-method-brand">{{ m.brand }}</span>
                  <span class="bp-method-last4">•••• {{ m.last4 }}</span>
                  <span class="jcs-muted bp-method-expiry">
                    @if (m.expiresAt) { vence {{ m.expiresAt | date:'MM/yy' }} } @else { sin vencimiento }
                  </span>
                  @if (m.isDefault) {
                    <span class="bp-badge">Predeterminado</span>
                  }
                </li>
              }
            </ul>
          }
        </section>

        <!-- ============== Invoices ============== -->
        <section class="bp-section" data-testid="billing-portal-invoices">
          <h3 class="bp-section-title">Facturas</h3>
          @if (state.invoices().length === 0) {
            <p class="jcs-muted">Aún no hay facturas emitidas.</p>
          } @else {
            <ul class="bp-invoices">
              @for (inv of state.invoices(); track inv.id) {
                <li class="bp-invoice">
                  <span class="bp-invoice-number">{{ inv.number }}</span>
                  <span class="bp-invoice-amount">{{ formatAmount(inv.amountCents, inv.currency) }}</span>
                  <span class="bp-status" [attr.data-status]="inv.status">{{ inv.status }}</span>
                  <a [href]="inv.pdfUrl" target="_blank" rel="noopener" class="bp-link">PDF</a>
                </li>
              }
            </ul>
          }
        </section>
      }
    </section>
  `,
  styles: [`
    :host { display: block; }
    .bp-page { display: flex; flex-direction: column; gap: var(--sp-4, 16px); max-width: 960px; }

    .bp-head { margin-bottom: var(--sp-2, 8px); }
    .bp-eyebrow { font-size: 0.7rem; text-transform: uppercase; letter-spacing: 0.08em; margin: 0; }
    .bp-title { font-size: 1.8rem; font-weight: 700; margin: var(--sp-1, 4px) 0; letter-spacing: -0.02em; }
    .bp-sub { margin: 0; font-size: 0.9rem; }

    .bp-error {
      display: flex; align-items: center; justify-content: space-between;
      padding: var(--sp-3, 12px) var(--sp-4, 16px);
      background: rgba(255, 64, 87, 0.12);
      border: 1px solid var(--red, #ff4057);
      border-radius: var(--radius-md, 8px);
      color: var(--red, #ff4057);
      font-size: var(--fs-sm, 0.875rem);
    }
    .bp-error-dismiss { background: transparent; border: 0; color: inherit; cursor: pointer; font-size: 1.2rem; line-height: 1; }

    .bp-loading { display: flex; align-items: center; gap: var(--sp-3, 12px); padding: var(--sp-4, 16px); }
    .bp-spinner {
      display: inline-block; width: 16px; height: 16px;
      border: 2px solid var(--border, #e5e5e5); border-top-color: var(--green, #2fdb78);
      border-radius: 50%; animation: bp-rotate 0.7s linear infinite;
    }
    @keyframes bp-rotate { to { transform: rotate(360deg); } }

    .bp-empty {
      padding: var(--sp-5, 24px);
      background: var(--bg-card-soft, #f7f7f7);
      border: 1px dashed var(--border, #e5e5e5);
      border-radius: var(--radius-md, 8px);
      text-align: center;
      display: flex; flex-direction: column; gap: var(--sp-2, 8px); align-items: center;
    }
    .bp-empty p { margin: 0; }

    .bp-plan-card {
      padding: var(--sp-5, 24px);
      background: var(--bg-card, #fff);
      border: 1px solid var(--border, #e5e5e5);
      border-radius: var(--radius-md, 8px);
      display: flex; flex-direction: column; gap: var(--sp-3, 12px);
    }
    .bp-plan-head { display: flex; align-items: center; justify-content: space-between; gap: var(--sp-3, 12px); }
    .bp-plan-label { font-size: 0.7rem; text-transform: uppercase; letter-spacing: 0.08em; margin: 0; color: var(--text-muted, #777); }
    .bp-plan-name { font-size: 1.4rem; font-weight: 700; margin: var(--sp-1, 4px) 0 0 0; letter-spacing: -0.02em; }

    .bp-status {
      display: inline-block; padding: 2px 10px;
      border-radius: 999px; font-size: 0.75rem; font-weight: 600;
      background: var(--bg-card-soft, #f7f7f7); color: var(--text-muted, #777);
      text-transform: capitalize;
    }
    .bp-status[data-status="active"] { background: var(--green-soft, rgba(47,219,120,0.16)); color: var(--green, #2fdb78); }
    .bp-status[data-status="trialing"] { background: rgba(74, 168, 255, 0.12); color: var(--blue, #4aa8ff); }
    .bp-status[data-status="past_due"] { background: rgba(245, 165, 36, 0.12); color: #f5a524; }
    .bp-status[data-status="canceled"] { background: rgba(255, 64, 87, 0.12); color: var(--red, #ff4057); }
    .bp-status[data-status="paid"] { background: var(--green-soft, rgba(47,219,120,0.16)); color: var(--green, #2fdb78); }
    .bp-status[data-status="open"] { background: rgba(245, 165, 36, 0.12); color: #f5a524; }

    .bp-plan-meta { display: flex; gap: var(--sp-5, 24px); margin: 0; flex-wrap: wrap; }
    .bp-plan-meta div { display: flex; flex-direction: column; gap: 2px; }
    .bp-plan-meta dt { font-size: 0.7rem; text-transform: uppercase; letter-spacing: 0.06em; color: var(--text-muted, #777); margin: 0; }
    .bp-plan-meta dd { font-size: 0.95rem; font-weight: 600; margin: 0; }
    .bp-plan-meta--warn dd { color: var(--red, #ff4057); }

    .bp-plan-actions { display: flex; justify-content: flex-end; }

    .bp-btn {
      display: inline-flex; align-items: center; gap: var(--sp-2, 8px);
      padding: var(--sp-2, 8px) var(--sp-4, 16px);
      border: 1px solid transparent; border-radius: var(--radius-sm, 6px);
      font-size: var(--fs-sm, 0.875rem); font-weight: 500;
      cursor: pointer; transition: background 150ms;
      text-decoration: none;
    }
    .bp-btn--primary { background: var(--green, #2fdb78); color: #050B10; }
    .bp-btn--primary:hover { background: var(--green-hover, #28c068); }
    .bp-btn--primary:disabled { opacity: 0.5; cursor: not-allowed; }

    .bp-section { display: flex; flex-direction: column; gap: var(--sp-2, 8px); }
    .bp-section-title { font-size: 1rem; font-weight: 600; margin: 0; }

    .bp-methods, .bp-invoices {
      list-style: none; padding: 0; margin: 0;
      display: flex; flex-direction: column; gap: var(--sp-1, 4px);
    }
    .bp-method, .bp-invoice {
      display: flex; align-items: center; gap: var(--sp-3, 12px);
      padding: var(--sp-3, 12px) var(--sp-4, 16px);
      background: var(--bg-card, #fff);
      border: 1px solid var(--border, #e5e5e5);
      border-radius: var(--radius-sm, 6px);
    }
    .bp-method-brand { font-weight: 600; text-transform: capitalize; }
    .bp-method-last4 { font-family: ui-monospace, monospace; font-size: 0.9rem; }
    .bp-method-expiry { font-size: 0.8rem; flex: 1; }
    .bp-badge {
      display: inline-block; padding: 2px 8px;
      background: var(--green-soft, rgba(47,219,120,0.16));
      color: var(--green, #2fdb78);
      border-radius: 999px; font-size: 0.7rem; font-weight: 600;
    }
    .bp-invoice-number { font-family: ui-monospace, monospace; font-weight: 600; }
    .bp-invoice-amount { flex: 1; font-weight: 600; }
    .bp-link { color: var(--green, #2fdb78); text-decoration: none; font-size: 0.85rem; }
    .bp-link:hover { text-decoration: underline; }
  `],
})
export class BillingPortalPage {
  readonly state = inject(BillingPortalState);
  private readonly api = inject(BillingPortalService);

  /** Local navigation flag — keep the redirect-in-flight state out of the global store. */
  private readonly _navigating = signal(false);
  readonly navigatingToPortal = this._navigating.asReadonly();

  readonly subscription = this.state.subscription;

  readonly planDisplay = computed(() => {
    const sub = this.subscription();
    if (!sub) return '';
    return sub.planCode.toUpperCase();
  });

  readonly statusLabel = computed(() => {
    const sub = this.subscription();
    if (!sub) return '';
    return sub.status;
  });

  constructor() {
    void this.state.loadAll();
  }

  /**
   * Test seam: explicit helper for assertions. The component exposes what
   * the page actually does (delegates to the state). The `canNavigateToPortal`
   * flag is true once the subscription has loaded.
   */
  canNavigateToPortal(): boolean {
    return this.state.canNavigateToPortal();
  }

  /** Format integer cents → "$19.99" / "USD 19.99" for the invoice list. */
  formatAmount(cents: number, currency: string): string {
    const amount = cents / 100;
    const symbol = currency.toUpperCase() === 'USD' ? '$' : `${currency.toUpperCase()} `;
    return `${symbol}${amount.toFixed(2)}`;
  }

  /** Test seam: format helper exposed so the spec can pin the USD wire format. */
  formatDate(iso: string): string {
    if (!iso) return '';
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return iso;
    return d.toLocaleDateString();
  }

  async onManageInStripe(): Promise<void> {
    if (this._navigating()) return;
    if (!this.canNavigateToPortal()) return;
    this._navigating.set(true);
    try {
      const { url } = await this.api.createPortalSession();
      if (url) {
        window.location.href = url;
      }
    } catch {
      this._navigating.set(false);
      // The error.interceptor logs the failure. We deliberately do NOT
      // overwrite the page-level error banner — the global state already
      // has whatever the initial loadAll() surfaced.
    }
  }
}
