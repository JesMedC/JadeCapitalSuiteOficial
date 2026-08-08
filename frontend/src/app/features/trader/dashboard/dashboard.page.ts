import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { DecimalPipe, DatePipe, NgClass } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { AuthState } from '@core/state/auth.state';

interface TradeDto {
  id: string;
  symbol: string;
  assetClass: number;
  direction: number;
  status: number;
  volume: number;
  entryPrice: number;
  exitPrice: number | null;
  pnl: number | null;
  pnlCurrency: string;
  openedAt: string;
  closedAt: string | null;
}
interface Paged<T> { items: T[]; total: number; page: number; pageSize: number; }

@Component({
  selector: 'jcs-dashboard',
  standalone: true,
  imports: [DecimalPipe, DatePipe, NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="dash">
      <!-- ============== Header ============== -->
      <header class="dash-head">
        <div>
          <p class="jcs-muted dash-eyebrow">{{ greeting() }}, trader</p>
          <h1 class="dash-title">Tu sesion de trading</h1>
        </div>
        <div class="dash-actions">
          <button class="jcs-btn jcs-btn--primary" (click)="reload()">
            <span class="reload-dot" [class.spin]="loading()"></span>
            Actualizar
          </button>
        </div>
      </header>

      <!-- ============== KPIs ============== -->
      <section class="kpi-row">
        <article class="kpi">
          <span class="kpi-label">Operaciones totales</span>
          <span class="kpi-value jcs-num">{{ totalCount() }}</span>
          <span class="kpi-foot jcs-muted">En este periodo</span>
        </article>
        <article class="kpi">
          <span class="kpi-label">Abiertas</span>
          <span class="kpi-value jcs-num">{{ openCount() }}</span>
          <span class="kpi-foot jcs-muted">Posiciones activas</span>
        </article>
        <article class="kpi">
          <span class="kpi-label">Cerradas</span>
          <span class="kpi-value jcs-num">{{ closedCount() }}</span>
          <span class="kpi-foot jcs-muted">Win rate {{ winRate() | number:'1.0-1' }}%</span>
        </article>
        <article class="kpi kpi--strong">
          <span class="kpi-label">P&amp;L neto</span>
          <span class="kpi-value jcs-num" [ngClass]="{ 'jcs-pos': totalPnL() >= 0, 'jcs-neg': totalPnL() < 0 }">
            {{ totalPnL() >= 0 ? '+' : '' }}{{ totalPnL() | number:'1.2-2' }}
          </span>
          <span class="kpi-foot jcs-muted">{{ pnlCurrency() }}</span>
        </article>
      </section>

      <!-- ============== Resumen ============== -->
      <section class="jcs-card summary">
        <header class="summary-head">
          <h2>Resumen de actividad</h2>
          <p class="jcs-muted">Ultimos 20 trades. P&amp;L en {{ pnlCurrency() }}.</p>
        </header>

        <div class="summary-body">
          <div class="summary-stat">
            <span class="summary-stat-label">Mejor trade</span>
            <span class="summary-stat-value jcs-pos jcs-num">+{{ bestTrade() | number:'1.2-2' }}</span>
          </div>
          <div class="summary-stat">
            <span class="summary-stat-label">Peor trade</span>
            <span class="summary-stat-value" [class]="worstTrade() < 0 ? 'jcs-neg' : 'jcs-pos'" class="jcs-num">
              {{ worstTrade() | number:'1.2-2' }}
            </span>
          </div>
          <div class="summary-stat">
            <span class="summary-stat-label">Promedio</span>
            <span class="summary-stat-value jcs-num" [ngClass]="avgTrade() >= 0 ? 'jcs-pos' : 'jcs-neg'">
              {{ avgTrade() | number:'1.2-2' }}
            </span>
          </div>
        </div>

        @if (loading()) {
          <p class="jcs-muted summary-state">Cargando operaciones...</p>
        } @else if (error()) {
          <p class="jcs-neg summary-state">{{ error() }}</p>
        } @else if (items().length === 0) {
          <div class="empty">
            <h3>Todavia no registras operaciones</h3>
            <p class="jcs-muted">Cuando abras tu primer trade, aparecera aqui con P&amp;L exacto.</p>
          </div>
        } @else {
          <table class="trades">
            <thead>
              <tr>
                <th>Fecha</th>
                <th>Simbolo</th>
                <th>Sentido</th>
                <th>Volumen</th>
                <th>Entrada</th>
                <th>Salida</th>
                <th class="num">P&amp;L</th>
              </tr>
            </thead>
            <tbody>
              @for (t of items(); track t.id) {
                <tr>
                  <td class="jcs-num">{{ t.openedAt | date:'shortDate' }}</td>
                  <td class="symbol">{{ t.symbol }}</td>
                  <td>
                    <span class="jcs-badge" [ngClass]="t.direction === 1 ? 'jcs-badge' : 'jcs-badge--neutral'">
                      {{ t.direction === 1 ? 'Long' : 'Short' }}
                    </span>
                  </td>
                  <td class="jcs-num">{{ t.volume }}</td>
                  <td class="jcs-num">{{ t.entryPrice | number:'1.5' }}</td>
                  <td class="jcs-num">{{ t.exitPrice | number:'1.5' }}</td>
                  <td class="num jcs-num" [ngClass]="(t.pnl ?? 0) >= 0 ? 'jcs-pos' : 'jcs-neg'">
                    {{ t.pnl === null ? '—' : ((t.pnl >= 0 ? '+' : '') + (t.pnl | number:'1.2-2')) }}
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      </section>
    </div>
  `,
  styles: [`
    .dash { max-width: var(--container-max); margin: 0 auto; padding: var(--sp-8) var(--sp-6); display: flex; flex-direction: column; gap: var(--sp-6); }

    .dash-head { display: flex; align-items: end; justify-content: space-between; gap: var(--sp-4); flex-wrap: wrap; }
    .dash-eyebrow { font-size: var(--fs-sm); margin: 0 0 var(--sp-1); }
    .dash-title { font-size: var(--fs-3xl); font-weight: 700; letter-spacing: -0.03em; margin: 0; }
    .dash-actions { display: flex; gap: var(--sp-2); }
    .reload-dot { width: 8px; height: 8px; border-radius: 50%; background: currentColor; }
    .reload-dot.spin { animation: spin 1s linear infinite; }
    @keyframes spin { from { transform: rotate(0); } to { transform: rotate(360deg); } }

    .kpi-row { display: grid; grid-template-columns: repeat(4, 1fr); gap: var(--sp-4); }
    @media (max-width: 960px) { .kpi-row { grid-template-columns: repeat(2, 1fr); } }
    @media (max-width: 540px) { .kpi-row { grid-template-columns: 1fr; } }
    .kpi { background: var(--bg-card); border: 1px solid var(--border); border-radius: var(--radius-md); padding: var(--sp-5); display: flex; flex-direction: column; gap: 6px; min-height: 130px; }
    .kpi--strong { background: linear-gradient(180deg, rgba(47,219,120,0.06) 0%, var(--bg-card) 100%); border-color: rgba(47,219,120,0.25); }
    .kpi-label { font-size: var(--fs-xs); color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.08em; }
    .kpi-value { font-size: var(--fs-3xl); font-weight: 700; letter-spacing: -0.025em; line-height: 1.1; }
    .kpi-foot { font-size: var(--fs-xs); }

    .summary-head h2 { font-size: var(--fs-xl); margin: 0 0 var(--sp-1); }
    .summary-head p { font-size: var(--fs-sm); margin: 0 0 var(--sp-6); }
    .summary-body { display: grid; grid-template-columns: repeat(3, 1fr); gap: var(--sp-4); padding: var(--sp-4); background: var(--bg-card-soft); border-radius: var(--radius-md); margin-bottom: var(--sp-6); }
    @media (max-width: 720px) { .summary-body { grid-template-columns: 1fr; } }
    .summary-stat { display: flex; flex-direction: column; gap: 4px; padding: 0 var(--sp-4); border-left: 1px solid var(--border-soft); }
    .summary-stat:first-child { border-left: none; padding-left: 0; }
    .summary-stat-label { font-size: var(--fs-xs); color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.08em; }
    .summary-stat-value { font-size: var(--fs-2xl); font-weight: 700; letter-spacing: -0.02em; }

    .summary-state { text-align: center; padding: var(--sp-12); font-size: var(--fs-sm); }
    .empty { text-align: center; padding: var(--sp-12); }
    .empty h3 { font-size: var(--fs-lg); margin-bottom: var(--sp-2); }

    .trades { width: 100%; border-collapse: collapse; }
    .trades th, .trades td { padding: var(--sp-3) var(--sp-3); text-align: left; border-bottom: 1px solid var(--border-soft); font-size: var(--fs-sm); }
    .trades th { font-size: var(--fs-xs); color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.06em; font-weight: 600; }
    .trades td.num, .trades th.num { text-align: right; }
    .trades .symbol { font-weight: 600; }
    .trades tr:hover td { background: var(--bg-hover); }
  `],
})
export class DashboardPage {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthState);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly items = signal<TradeDto[]>([]);

  readonly totalCount = computed(() => this.items().length);
  readonly openCount = computed(() => this.items().filter(t => t.status === 1).length);
  readonly closedCount = computed(() => this.items().filter(t => t.status === 2).length);
  readonly winsCount = computed(() => this.items().filter(t => t.status === 2 && (t.pnl ?? 0) > 0).length);
  readonly winRate = computed(() => {
    const c = this.closedCount();
    return c === 0 ? 0 : (this.winsCount() / c) * 100;
  });
  readonly totalPnL = computed(() => this.items().reduce((acc, t) => acc + (t.pnl ?? 0), 0));
  readonly pnlCurrency = computed(() => this.items()[0]?.pnlCurrency ?? 'USD');
  readonly bestTrade = computed(() => Math.max(0, ...this.items().map(t => t.pnl ?? 0)));
  readonly worstTrade = computed(() => Math.min(0, ...this.items().map(t => t.pnl ?? 0)));
  readonly avgTrade = computed(() => {
    const n = this.closedCount();
    return n === 0 ? 0 : this.totalPnL() / n;
  });

  readonly greeting = computed(() => {
    const h = new Date().getHours();
    if (h < 6) return 'Buenas noches';
    if (h < 12) return 'Buenos dias';
    if (h < 19) return 'Buenas tardes';
    return 'Buenas noches';
  });

  constructor() { void this.reload(); }

  async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const resp = await firstValueFrom(this.http.get<Paged<TradeDto>>('/api/trades?page=1&pageSize=20'));
      this.items.set(resp.items);
    } catch {
      this.error.set('No se pudieron cargar las operaciones.');
    } finally {
      this.loading.set(false);
    }
  }
}
