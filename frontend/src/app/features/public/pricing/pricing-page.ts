import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { from } from 'rxjs';
import { PlanApiService } from '@core/api/plan-api.service';
import { PlanInfo } from '@core/api/plan-info';

interface Plan {
  code: string;
  name: string;
  monthly: number;
  currency: string;
  features: readonly string[];
  featured: boolean;
}

// Marketing copy per plan code. The backend does NOT carry these — they are
// owned by the marketing surface. Pricing/name/currency come from the API
// (single source of truth: billing.plans). The "is featured" decision
// (highlighted plan) lives here too, keyed by code.
const FEATURES_BY_CODE: Record<string, { features: readonly string[]; featured: boolean }> = {
  starter: { features: ['200 trades/mes', 'Calendario P&L', 'Metricas basicas', 'Soporte email'], featured: false },
  pro:     { features: ['2.000 trades/mes', 'Metricas avanzadas', 'Screenshots', 'Soporte prioritario'], featured: true },
  elite:   { features: ['Trades ilimitados', 'Exportacion CSV', 'Soporte 1:1'], featured: false },
};

@Component({
  selector: 'jcs-pricing-page',
  standalone: true,
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="jcs-container">
      <header><h1>Planes</h1><p class="jcs-muted">Sin permanencia. Cancela cuando quieras.</p></header>
      <section class="grid">
        @for (plan of plans(); track plan.code) {
          <article class="jcs-card plan" [class.featured]="plan.featured">
            <h3>{{ plan.name }}</h3>
            <p class="price">
              <span class="jcs-num">{{ plan.currency === 'USD' ? '$' : plan.currency + ' ' }}{{ plan.monthly }}</span> / mes
            </p>
            <ul>
              @for (f of plan.features; track f) { <li>{{ f }}</li> }
            </ul>
            <a class="jcs-btn jcs-btn--primary" routerLink="/auth/register">Empezar</a>
          </article>
        }
      </section>
    </div>
  `,
  styles: [`
    header { padding: var(--space-12) 0 var(--space-8); text-align: center; }
    .grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: var(--space-6); padding-bottom: var(--space-16); }
    .plan { display: flex; flex-direction: column; gap: var(--space-3); }
    .plan.featured { border-color: var(--border-active); box-shadow: var(--shadow-green); }
    .price { font-size: var(--font-size-3xl); }
    @media (max-width: 768px) { .grid { grid-template-columns: 1fr; } }
  `],
})
export class PricingPage {
  private readonly api = inject(PlanApiService);
  private readonly destroyRef = inject(DestroyRef);
  readonly plans = signal<readonly Plan[]>([]);

  constructor() {
    from(this.api.list())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(plans => this.plans.set(this.mergeWithMarketing(plans)));
  }

  private mergeWithMarketing(api: readonly PlanInfo[]): readonly Plan[] {
    return api.map(p => {
      const meta = FEATURES_BY_CODE[p.code] ?? { features: [], featured: false };
      return {
        code: p.code,
        name: p.name,
        monthly: p.monthlyPrice,
        currency: p.currency,
        features: meta.features,
        featured: meta.featured,
      };
    });
  }
}
