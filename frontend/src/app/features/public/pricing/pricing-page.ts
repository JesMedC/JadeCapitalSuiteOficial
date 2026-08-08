import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthState } from '@core/state/auth.state';

interface Plan { code: string; price: number; trades: number; features: readonly string[]; }

const PLANS: readonly Plan[] = [
  { code: 'starter', price: 19, trades: 200, features: ['200 trades/mes', 'Calendario P&L', 'Metricas basicas', 'Soporte email'] },
  { code: 'pro',     price: 49, trades: 2000, features: ['2.000 trades/mes', 'Metricas avanzadas', 'Screenshots', 'Soporte prioritario'] },
  { code: 'elite',   price: 99, trades: -1, features: ['Trades ilimitados', 'Exportacion CSV', 'Soporte 1:1'] },
] as const;

@Component({
  selector: 'jcs-pricing-page',
  standalone: true,
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="jcs-container">
      <header><h1>Planes</h1><p class="jcs-muted">Sin permanencia. Cancela cuando quieras.</p></header>
      <section class="grid">
        @for (plan of plans; track plan.code) {
          <article class="jcs-card plan" [class.featured]="plan.code === 'pro'">
            <h3>{{ plan.code }}</h3>
            <p class="price"><span class="jcs-num">{{ '$' + plan.price }}</span> / mes</p>
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
  readonly plans = PLANS;
}
