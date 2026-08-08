import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe, DatePipe, NgClass } from '@angular/common';
import { AuthState } from '@core/state/auth.state';

// Mock data — el módulo /api/trades es scaffold, se va a reemplazar
// cuando llegue el vertical de Trading (Sprint 1).
interface TradeDto {
  id: string;
  symbol: string;
  direction: 'Long' | 'Short';
  status: 'Open' | 'Closed';
  volume: number;
  entryPrice: number;
  exitPrice: number | null;
  pnl: number | null;
  pnlCurrency: string;
  openedAt: string;
  closedAt: string | null;
}

const MOCK_TRADES: TradeDto[] = [
  { id: '1', symbol: 'EUR/USD', direction: 'Long',  status: 'Closed', volume: 1.5, entryPrice: 1.0842, exitPrice: 1.0872, pnl: 45.00,  pnlCurrency: 'USD', openedAt: '2026-06-15', closedAt: '2026-06-15' },
  { id: '2', symbol: 'XAU/USD', direction: 'Short', status: 'Closed', volume: 0.5, entryPrice: 2362.40, exitPrice: 2348.40, pnl: 700.00, pnlCurrency: 'USD', openedAt: '2026-06-14', closedAt: '2026-06-14' },
  { id: '3', symbol: 'BTC/USD', direction: 'Long',  status: 'Closed', volume: 0.1,  entryPrice: 67200, exitPrice: 66420, pnl: -78.00, pnlCurrency: 'USD', openedAt: '2026-06-13', closedAt: '2026-06-13' },
  { id: '4', symbol: 'GBP/USD', direction: 'Long',  status: 'Closed', volume: 2.0, entryPrice: 1.2621, exitPrice: 1.2641, pnl: 40.00,  pnlCurrency: 'USD', openedAt: '2026-06-12', closedAt: '2026-06-12' },
  { id: '5', symbol: 'USD/JPY', direction: 'Short', status: 'Closed', volume: 1.0, entryPrice: 154.92, exitPrice: 154.27, pnl: 4.21,   pnlCurrency: 'USD', openedAt: '2026-06-11', closedAt: '2026-06-11' },
  { id: '6', symbol: 'EUR/USD', direction: 'Long',  status: 'Open',   volume: 1.0, entryPrice: 1.0868, exitPrice: null,   pnl: null,   pnlCurrency: 'USD', openedAt: '2026-06-16', closedAt: null },
  { id: '7', symbol: 'ETH/USD', direction: 'Long',  status: 'Closed', volume: 2.0, entryPrice: 3168, exitPrice: 3182, pnl: 28.00, pnlCurrency: 'USD', openedAt: '2026-06-10', closedAt: '2026-06-10' },
  { id: '8', symbol: 'AUD/USD', direction: 'Short', status: 'Closed', volume: 1.5, entryPrice: 0.6632, exitPrice: 0.6614, pnl: 27.00, pnlCurrency: 'USD', openedAt: '2026-06-09', closedAt: '2026-06-09' },
];

// Serie mock de equity curve (P&L acumulado en el tiempo).
function buildEquityCurve(): number[] {
  const points: number[] = [];
  let acc = 0;
  for (let i = 0; i < 30; i++) {
    const drift = 50 + Math.sin(i * 0.4) * 80;
    const noise = Math.cos(i * 1.7) * 60;
    acc += drift + noise;
    points.push(Math.round(acc));
  }
  return points;
}
const EQUITY_POINTS = buildEquityCurve();

@Component({
  selector: 'jcs-dashboard',
  standalone: true,
  imports: [DecimalPipe, DatePipe, NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="dash">
      <!-- ============== Ticker tape ============== -->
      <div class="ticker" aria-hidden="true">
        <div class="ticker-track">
          @for (i of [0,1]; track i) {
            <span class="ticker-row">
              <span class="tick"><b>EUR/USD</b><span class="up">1.0872</span><span class="up jcs-num">+0.34%</span></span>
              <span class="tick"><b>GBP/USD</b><span class="up">1.2641</span><span class="up jcs-num">+0.18%</span></span>
              <span class="tick"><b>USD/JPY</b><span class="dn">154.27</span><span class="dn jcs-num">-0.21%</span></span>
              <span class="tick"><b>XAU/USD</b><span class="up">2,348.40</span><span class="up jcs-num">+0.82%</span></span>
              <span class="tick"><b>BTC/USD</b><span class="dn">66,420</span><span class="dn jcs-num">-1.12%</span></span>
              <span class="tick"><b>ETH/USD</b><span class="up">3,182</span><span class="up jcs-num">+0.45%</span></span>
            </span>
          }
        </div>
      </div>

      <!-- ============== Header ============== -->
      <header class="dash-head">
        <div>
          <p class="jcs-muted dash-eyebrow">{{ greeting() }}, {{ auth.user()?.email }}</p>
          <h1 class="dash-title">Tu sesión de trading</h1>
        </div>
        <div class="dash-actions">
          <span class="dash-period">Últimos 30 días</span>
          <button class="jcs-btn jcs-btn--primary" (click)="reload()">
            <span class="reload-dot" [class.spin]="refreshing()"></span>
            Actualizar
          </button>
        </div>
      </header>

      <!-- ============== KPIs ============== -->
      <section class="kpi-row">
        <article class="kpi" style="animation-delay: 0.05s">
          <span class="kpi-label">Operaciones totales</span>
          <span class="kpi-value jcs-num">{{ totalCount() }}</span>
          <span class="kpi-foot jcs-muted">En este período</span>
        </article>
        <article class="kpi" style="animation-delay: 0.1s">
          <span class="kpi-label">Abiertas</span>
          <span class="kpi-value jcs-num">{{ openCount() }}</span>
          <span class="kpi-foot jcs-muted">Posiciones activas</span>
        </article>
        <article class="kpi" style="animation-delay: 0.15s">
          <span class="kpi-label">Win rate</span>
          <span class="kpi-value jcs-num jcs-pos">{{ winRate() | number:'1.0-1' }}%</span>
          <span class="kpi-foot jcs-muted">{{ closedCount() }} cerradas</span>
        </article>
        <article class="kpi kpi--strong" style="animation-delay: 0.2s">
          <span class="kpi-label">P&amp;L neto</span>
          <span class="kpi-value jcs-num" [ngClass]="{ 'jcs-pos': totalPnL() >= 0, 'jcs-neg': totalPnL() < 0 }">
            {{ totalPnL() >= 0 ? '+' : '' }}{{ totalPnL() | number:'1.2-2' }}
          </span>
          <span class="kpi-foot jcs-muted">{{ pnlCurrency() }} · últimos 30 días</span>
        </article>
      </section>

      <!-- ============== Equity Chart ============== -->
      <section class="jcs-card chart-card">
        <header class="chart-head">
          <div>
            <h2>Curva de equity</h2>
            <p class="jcs-muted">P&amp;L acumulado en los últimos 30 días</p>
          </div>
          <div class="chart-legend">
            <span class="dot"></span>
            <span class="jcs-muted">P&amp;L acumulado</span>
          </div>
        </header>
        <svg class="equity-svg" viewBox="0 0 600 200" preserveAspectRatio="none" aria-hidden="true">
          <defs>
            <linearGradient id="equityFill" x1="0" x2="0" y1="0" y2="1">
              <stop offset="0%" stop-color="#2FDB78" stop-opacity="0.35"/>
              <stop offset="100%" stop-color="#2FDB78" stop-opacity="0"/>
            </linearGradient>
            <linearGradient id="equityLine" x1="0" x2="1" y1="0" y2="0">
              <stop offset="0%" stop-color="#2FDB78" stop-opacity="0.5"/>
              <stop offset="100%" stop-color="#2FDB78" stop-opacity="1"/>
            </linearGradient>
          </defs>
          <!-- Grid lines -->
          <g stroke="#1C2A33" stroke-width="0.5" stroke-dasharray="4 4">
            <line x1="0" y1="40" x2="600" y2="40"/>
            <line x1="0" y1="100" x2="600" y2="100"/>
            <line x1="0" y1="160" x2="600" y2="160"/>
          </g>
          <!-- Area fill -->
          <path [attr.d]="areaPath()" fill="url(#equityFill)"/>
          <!-- Line -->
          <path [attr.d]="linePath()" fill="none" stroke="url(#equityLine)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
      </section>

      <!-- ============== Resumen + Trades ============== -->
      <section class="grid-row">
        <!-- Resumen panel -->
        <div class="jcs-card summary">
          <header class="summary-head">
            <h2>Resumen</h2>
            <p class="jcs-muted">Tu performance agregada</p>
          </header>
          <div class="summary-body">
            <div class="summary-stat">
              <span class="summary-stat-label">Mejor trade</span>
              <span class="summary-stat-value jcs-pos jcs-num">+{{ bestTrade() | number:'1.2-2' }}</span>
              <span class="summary-stat-foot jcs-muted">{{ bestSymbol() }}</span>
            </div>
            <div class="summary-stat">
              <span class="summary-stat-label">Peor trade</span>
              <span class="summary-stat-value jcs-num" [ngClass]="worstTrade() < 0 ? 'jcs-neg' : 'jcs-pos'">
                {{ worstTrade() | number:'1.2-2' }}
              </span>
              <span class="summary-stat-foot jcs-muted">{{ worstSymbol() }}</span>
            </div>
            <div class="summary-stat">
              <span class="summary-stat-label">Promedio</span>
              <span class="summary-stat-value jcs-num" [ngClass]="avgTrade() >= 0 ? 'jcs-pos' : 'jcs-neg'">
                {{ avgTrade() >= 0 ? '+' : '' }}{{ avgTrade() | number:'1.2-2' }}
              </span>
              <span class="summary-stat-foot jcs-muted">Por operación</span>
            </div>
          </div>
          <div class="quick-actions">
            <a class="quick" href="#">
              <span class="quick-icon" aria-hidden="true">
                <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <line x1="12" y1="5" x2="12" y2="19"/>
                  <line x1="5" y1="12" x2="19" y2="12"/>
                </svg>
              </span>
              <span class="quick-text">
                <strong>Nueva operación</strong>
                <span class="jcs-muted">Registra un trade manualmente</span>
              </span>
            </a>
            <a class="quick" href="#">
              <span class="quick-icon" aria-hidden="true">
                <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <rect x="3" y="4" width="18" height="18" rx="2"/>
                  <line x1="16" y1="2" x2="16" y2="6"/>
                  <line x1="8" y1="2" x2="8" y2="6"/>
                  <line x1="3" y1="10" x2="21" y2="10"/>
                </svg>
              </span>
              <span class="quick-text">
                <strong>Calendario</strong>
                <span class="jcs-muted">Visualiza días verdes / rojos</span>
              </span>
            </a>
          </div>
        </div>

        <!-- Trades table -->
        <div class="jcs-card trades-card">
          <header class="trades-head">
            <h2>Operaciones recientes</h2>
            <span class="jcs-badge jcs-badge--neutral">Mock data</span>
          </header>
          <div class="trades-wrap">
            <table class="trades">
              <thead>
                <tr>
                  <th>Fecha</th>
                  <th>Símbolo</th>
                  <th>Sentido</th>
                  <th class="num">Vol</th>
                  <th class="num">Entrada</th>
                  <th class="num">Salida</th>
                  <th class="num">P&amp;L</th>
                </tr>
              </thead>
              <tbody>
                @for (t of items(); track t.id) {
                  <tr>
                    <td class="jcs-num">{{ t.openedAt | date:'shortDate' }}</td>
                    <td class="symbol">{{ t.symbol }}</td>
                    <td>
                      <span class="jcs-badge" [ngClass]="t.direction === 'Long' ? 'jcs-pos long' : 'jcs-badge--neutral short'">
                        {{ t.direction === 'Long' ? '↑ Long' : '↓ Short' }}
                      </span>
                    </td>
                    <td class="num jcs-num">{{ t.volume }}</td>
                    <td class="num jcs-num">{{ t.entryPrice | number:'1.2-5' }}</td>
                    <td class="num jcs-num">{{ t.exitPrice === null ? '—' : (t.exitPrice | number:'1.2-5') }}</td>
                    <td class="num jcs-num" [ngClass]="(t.pnl ?? 0) >= 0 ? 'jcs-pos' : 'jcs-neg'">
                      {{ t.pnl === null ? '—' : ((t.pnl >= 0 ? '+' : '') + (t.pnl | number:'1.2-2')) }}
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      </section>
    </div>
  `,
  styles: [`
    :host { display: block; }

    .dash {
      display: flex;
      flex-direction: column;
      gap: var(--sp-6);
      animation: fade-up 0.4s ease-out;
    }

    /* Ticker tape. */
    .ticker {
      position: relative;
      background: linear-gradient(180deg, rgba(8,16,24,0.7) 0%, rgba(8,16,24,0.3) 100%);
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-md);
      overflow: hidden;
      padding: var(--sp-2) 0;
      margin: calc(-1 * var(--sp-4)) calc(-1 * var(--sp-4)) 0;
    }
    @media (max-width: 720px) {
      .ticker { margin: 0; }
    }
    .ticker-track {
      display: flex;
      width: max-content;
      animation: ticker-scroll 60s linear infinite;
    }
    .ticker-row {
      display: flex;
      gap: var(--sp-8);
      padding-right: var(--sp-8);
      flex-shrink: 0;
    }
    .tick {
      display: inline-flex;
      gap: var(--sp-2);
      font-size: var(--fs-xs);
      font-family: var(--font-mono);
      white-space: nowrap;
    }
    .tick b { color: var(--text-secondary); font-weight: 600; }
    .tick .up { color: var(--green); }
    .tick .dn { color: var(--text-main); }
    @keyframes ticker-scroll {
      from { transform: translateX(0); }
      to   { transform: translateX(-50%); }
    }
    .ticker:hover .ticker-track { animation-play-state: paused; }

    /* Header. */
    .dash-head {
      display: flex;
      align-items: end;
      justify-content: space-between;
      gap: var(--sp-4);
      flex-wrap: wrap;
    }
    .dash-eyebrow { font-size: var(--fs-sm); margin: 0 0 var(--sp-1); font-family: var(--font-mono); }
    .dash-title { font-size: var(--fs-3xl); font-weight: 700; letter-spacing: -0.03em; margin: 0; }
    .dash-actions { display: flex; gap: var(--sp-3); align-items: center; }
    .dash-period {
      font-size: var(--fs-xs);
      font-family: var(--font-mono);
      color: var(--text-muted);
      padding: var(--sp-2) var(--sp-3);
      background: var(--bg-card);
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-sm);
    }
    .reload-dot {
      width: 8px; height: 8px;
      border-radius: 50%;
      background: currentColor;
      display: inline-block;
      margin-right: var(--sp-2);
    }
    .reload-dot.spin { animation: spin 1s linear infinite; }
    @keyframes spin { from { transform: rotate(0); } to { transform: rotate(360deg); } }

    /* KPIs. */
    .kpi-row {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: var(--sp-4);
    }
    @media (max-width: 960px) { .kpi-row { grid-template-columns: repeat(2, 1fr); } }
    @media (max-width: 540px) { .kpi-row { grid-template-columns: 1fr; } }
    .kpi {
      background: var(--bg-card);
      border: 1px solid var(--border);
      border-radius: var(--radius-md);
      padding: var(--sp-5);
      display: flex;
      flex-direction: column;
      gap: 6px;
      min-height: 130px;
      transition: transform 200ms ease, border-color 200ms ease, box-shadow 200ms ease;
      animation: fade-up 0.5s ease-out both;
    }
    .kpi:hover {
      transform: translateY(-2px);
      border-color: var(--border-active);
      box-shadow: var(--shadow-soft);
    }
    .kpi--strong {
      background: linear-gradient(180deg, rgba(47,219,120,0.06) 0%, var(--bg-card) 100%);
      border-color: rgba(47,219,120,0.25);
    }
    .kpi-label {
      font-size: var(--fs-xs);
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.08em;
      font-weight: 600;
    }
    .kpi-value {
      font-size: var(--fs-3xl);
      font-weight: 700;
      letter-spacing: -0.025em;
      line-height: 1.1;
    }
    .kpi-foot { font-size: var(--fs-xs); }

    /* Equity chart. */
    .chart-card {
      padding: var(--sp-5) var(--sp-6);
    }
    .chart-head {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      margin-bottom: var(--sp-4);
    }
    .chart-head h2 { font-size: var(--fs-lg); margin: 0 0 var(--sp-1); }
    .chart-head p { font-size: var(--fs-sm); margin: 0; }
    .chart-legend {
      display: flex;
      align-items: center;
      gap: var(--sp-2);
      font-size: var(--fs-xs);
    }
    .chart-legend .dot {
      width: 8px; height: 8px;
      border-radius: 50%;
      background: var(--green);
      box-shadow: 0 0 0 4px rgba(47, 219, 120, 0.15);
    }
    .equity-svg {
      width: 100%;
      height: 200px;
      display: block;
    }

    /* Grid row: resumen + trades. */
    .grid-row {
      display: grid;
      grid-template-columns: 1fr 1.6fr;
      gap: var(--sp-4);
    }
    @media (max-width: 960px) { .grid-row { grid-template-columns: 1fr; } }

    /* Summary. */
    .summary { padding: var(--sp-5) var(--sp-6); }
    .summary-head h2 { font-size: var(--fs-lg); margin: 0 0 var(--sp-1); }
    .summary-head p { font-size: var(--fs-sm); margin: 0 0 var(--sp-5); }
    .summary-body {
      display: flex;
      flex-direction: column;
      gap: var(--sp-3);
      margin-bottom: var(--sp-5);
    }
    .summary-stat {
      display: flex;
      flex-direction: column;
      gap: 2px;
      padding: var(--sp-3);
      background: var(--bg-card-soft);
      border-radius: var(--radius-sm);
      border-left: 3px solid var(--border-active);
    }
    .summary-stat-label {
      font-size: var(--fs-xs);
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.08em;
      font-weight: 600;
    }
    .summary-stat-value {
      font-size: var(--fs-xl);
      font-weight: 700;
      letter-spacing: -0.02em;
    }
    .summary-stat-foot {
      font-size: var(--fs-xs);
    }

    .quick-actions {
      display: flex;
      flex-direction: column;
      gap: var(--sp-2);
    }
    .quick {
      display: flex;
      align-items: center;
      gap: var(--sp-3);
      padding: var(--sp-3);
      background: var(--bg-card-soft);
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-sm);
      transition: all 200ms ease;
      color: var(--text-main);
    }
    .quick:hover {
      border-color: var(--border-active);
      background: var(--bg-hover);
      transform: translateX(2px);
    }
    .quick-icon {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 32px;
      height: 32px;
      border-radius: var(--radius-sm);
      background: var(--green-soft);
      color: var(--green);
      flex-shrink: 0;
    }
    .quick-text {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }
    .quick-text strong { font-weight: 600; font-size: var(--fs-sm); }
    .quick-text span { font-size: var(--fs-xs); }

    /* Trades. */
    .trades-card { padding: var(--sp-5) var(--sp-6); }
    .trades-head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: var(--sp-4);
    }
    .trades-head h2 { font-size: var(--fs-lg); margin: 0; }
    .trades-wrap { overflow-x: auto; }
    .trades { width: 100%; border-collapse: collapse; }
    .trades th, .trades td {
      padding: var(--sp-3) var(--sp-3);
      text-align: left;
      border-bottom: 1px solid var(--border-soft);
      font-size: var(--fs-sm);
    }
    .trades th {
      font-size: var(--fs-xs);
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.06em;
      font-weight: 600;
    }
    .trades td.num, .trades th.num { text-align: right; }
    .trades .symbol { font-weight: 600; }
    .trades tr { transition: background 150ms; }
    .trades tbody tr:hover td { background: var(--bg-hover); }

    .jcs-badge.long {
      background: rgba(47, 219, 120, 0.15);
      color: var(--green);
    }
    .jcs-badge.short {
      background: rgba(255, 64, 87, 0.12);
      color: var(--red);
    }

    @keyframes fade-up {
      from { opacity: 0; transform: translateY(8px); }
      to   { opacity: 1; transform: translateY(0); }
    }
  `],
})
export class DashboardPage {
  readonly auth = inject(AuthState);

  readonly refreshing = signal(false);
  readonly items = signal<TradeDto[]>(MOCK_TRADES);

  readonly totalCount = computed(() => this.items().length);
  readonly openCount = computed(() => this.items().filter(t => t.status === 'Open').length);
  readonly closedCount = computed(() => this.items().filter(t => t.status === 'Closed').length);
  readonly winsCount = computed(() => this.items().filter(t => t.status === 'Closed' && (t.pnl ?? 0) > 0).length);
  readonly winRate = computed(() => {
    const c = this.closedCount();
    return c === 0 ? 0 : (this.winsCount() / c) * 100;
  });
  readonly totalPnL = computed(() => this.items().reduce((acc, t) => acc + (t.pnl ?? 0), 0));
  readonly pnlCurrency = computed(() => this.items()[0]?.pnlCurrency ?? 'USD');
  readonly bestTrade = computed(() => Math.max(0, ...this.items().map(t => t.pnl ?? 0)));
  readonly worstTrade = computed(() => Math.min(0, ...this.items().map(t => t.pnl ?? 0)));
  readonly bestSymbol = computed(() => {
    const t = this.items().reduce((best, x) => ((x.pnl ?? 0) > (best?.pnl ?? 0) ? x : best), null as TradeDto | null);
    return t?.symbol ?? '—';
  });
  readonly worstSymbol = computed(() => {
    const t = this.items().reduce((worst, x) => ((x.pnl ?? 0) < (worst?.pnl ?? 0) ? x : worst), null as TradeDto | null);
    return t?.symbol ?? '—';
  });
  readonly avgTrade = computed(() => {
    const n = this.closedCount();
    return n === 0 ? 0 : this.totalPnL() / n;
  });

  // Build SVG path for equity curve.
  readonly linePath = computed(() => {
    const pts = EQUITY_POINTS;
    const w = 600, h = 200;
    const min = Math.min(...pts);
    const max = Math.max(...pts);
    const range = max - min || 1;
    const step = w / (pts.length - 1);
    return pts
      .map((v, i) => {
        const x = i * step;
        const y = h - ((v - min) / range) * (h - 20) - 10;
        return `${i === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`;
      })
      .join(' ');
  });

  readonly areaPath = computed(() => {
    const line = this.linePath();
    return `${line} L600,200 L0,200 Z`;
  });

  readonly greeting = computed(() => {
    const h = new Date().getHours();
    if (h < 6) return 'Buenas noches';
    if (h < 12) return 'Buenos días';
    if (h < 19) return 'Buenas tardes';
    return 'Buenas noches';
  });

  reload(): void {
    // Mock: refresco visual. Cuando llegue el endpoint real, llamar a /api/trades acá.
    this.refreshing.set(true);
    setTimeout(() => this.refreshing.set(false), 800);
  }
}
