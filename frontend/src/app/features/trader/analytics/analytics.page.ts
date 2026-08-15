import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe, NgClass } from '@angular/common';
import {
  ASSET_CLASS_LABEL,
  TRADE_DIRECTION_LABEL,
  TRADE_STATUS_LABEL,
  TradeApiService,
  TradeDto,
} from '@core/api/trade-api.service';
import { MetricsApiService, MetricsDto, SymbolStat as ServerSymbolStat } from '@core/api/metrics-api.service';

interface SymbolStat {
  symbol: string;
  trades: number;
  winRate: number;
  netPnl: number;
}

interface DayPoint {
  date: string;
  pnl: number;
}

// Construye puntos diarios agregados (P&L por día) ordenados por fecha.
function buildDailyPoints(items: TradeDto[]): DayPoint[] {
  const closed = items.filter(t => t.status === 2 && t.pnl !== null && t.closedAt !== null);
  const byDate = new Map<string, number>();
  for (const t of closed) {
    const day = (t.closedAt ?? '').slice(0, 10);
    byDate.set(day, (byDate.get(day) ?? 0) + (t.pnl ?? 0));
  }
  return Array.from(byDate.entries())
    .sort((a, b) => a[0].localeCompare(b[0]))
    .map(([date, pnl]) => ({ date, pnl: Number(pnl.toFixed(2)) }));
}

function buildEquityCurve(points: DayPoint[]): number[] {
  let acc = 0;
  return points.map(p => {
    acc += p.pnl;
    return Number(acc.toFixed(2));
  });
}

// Adapta los SymbolStat del server (MetricsDto) al shape SymbolStat del template.
function toUiSymbolStats(server: ServerSymbolStat[]): SymbolStat[] {
  return server.map(s => ({
    symbol: s.symbol,
    trades: s.trades,
    winRate: s.winRate,
    netPnl: s.totalPnl,
  }));
}

type Period = '7d' | '30d' | '90d' | 'all';
const PERIODS: ReadonlyArray<{ key: Period; label: string }> = [
  { key: '7d',  label: '7 días' },
  { key: '30d', label: '30 días' },
  { key: '90d', label: '90 días' },
  { key: 'all', label: 'Todo'    },
];

function daysForPeriod(p: Period): number | null {
  switch (p) {
    case '7d':  return 7;
    case '30d': return 30;
    case '90d': return 90;
    case 'all': return null;
  }
}

@Component({
  selector: 'jcs-analytics',
  standalone: true,
  imports: [DecimalPipe, NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
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
              <span class="tick"><b>USD/CHF</b><span class="up">0.9012</span><span class="up jcs-num">+0.09%</span></span>
              <span class="tick"><b>AUD/USD</b><span class="dn">0.6614</span><span class="dn jcs-num">-0.27%</span></span>
            </span>
          }
        </div>
      </div>

      <!-- ============== Header ============== -->
      <header class="head">
        <div>
          <p class="jcs-muted eyebrow">Métricas y rendimiento</p>
          <h1 class="title">Analítica de trading</h1>
          <p class="jcs-muted subtitle">Win rate, expectancy, drawdown y distribución por símbolo.</p>
        </div>
        <div class="period" role="tablist" aria-label="Período">
          @for (p of periods; track p.key) {
            <button
              class="period-pill"
              [class.is-active]="selectedPeriod() === p.key"
              (click)="setPeriod(p.key)"
              role="tab"
              [attr.aria-selected]="selectedPeriod() === p.key"
              [disabled]="refreshing()">
              {{ p.label }}
            </button>
          }
        </div>
      </header>

      <!-- ============== Error banner ============== -->
      @if (error()) {
        <div class="analytics-error" role="alert">
          <span>No se pudo cargar la analítica: {{ error() }}</span>
          <button type="button" class="analytics-error-retry" (click)="reload()">Reintentar</button>
        </div>
      }

      <!-- ============== KPIs ============== -->
      <section class="kpi-row">
        <article class="kpi" style="animation-delay: 0.05s">
          <span class="kpi-label">Win rate</span>
          <span class="kpi-value jcs-num jcs-pos">{{ winRate() | number:'1.0-1' }}%</span>
          <span class="kpi-foot jcs-muted">{{ winsCount() }} wins · {{ lossesCount() }} losses</span>
        </article>
        <article class="kpi" style="animation-delay: 0.1s">
          <span class="kpi-label">Expectancy</span>
          <span class="kpi-value jcs-num" [ngClass]="expectancy() >= 0 ? 'jcs-pos' : 'jcs-neg'">
            {{ expectancy() >= 0 ? '+' : '' }}{{ expectancy() | number:'1.2-2' }}
          </span>
          <span class="kpi-foot jcs-muted">P&amp;L promedio por trade</span>
        </article>
        <article class="kpi" style="animation-delay: 0.15s">
          <span class="kpi-label">Profit factor</span>
          <span class="kpi-value jcs-num jcs-pos">{{ profitFactor() | number:'1.2-2' }}</span>
          <span class="kpi-foot jcs-muted">gross wins / gross losses</span>
        </article>
        <article class="kpi kpi--danger" style="animation-delay: 0.2s">
          <span class="kpi-label">Max drawdown</span>
          <span class="kpi-value jcs-num jcs-neg">{{ maxDrawdown() | number:'1.2-2' }}</span>
          <span class="kpi-foot jcs-muted">{{ pnlCurrency() }} · peor caída</span>
        </article>
      </section>

      <!-- ============== Performance por símbolo + Distribución ============== -->
      <section class="dual-grid">
        <!-- Performance por símbolo -->
        <div class="jcs-card panel">
          <header class="panel-head">
            <div>
              <h2>Performance por símbolo</h2>
              <p class="jcs-muted">{{ totalCount() }} operaciones · ordenado por P&amp;L neto</p>
            </div>
          </header>
          <div class="sym-wrap">
            <table class="sym">
              <thead>
                <tr>
                  <th>Símbolo</th>
                  <th class="num">Trades</th>
                  <th class="num">Win rate</th>
                  <th class="num">P&amp;L neto</th>
                </tr>
              </thead>
              <tbody>
                @for (s of symbolStats(); track s.symbol) {
                  <tr>
                    <td class="sym-name">{{ s.symbol }}</td>
                    <td class="num jcs-num">{{ s.trades }}</td>
                    <td class="num">
                      <div class="wr-cell">
                        <svg class="mini-bar" viewBox="0 0 100 6" preserveAspectRatio="none" aria-hidden="true">
                          <rect x="0" y="0" width="100" height="6" rx="3" fill="var(--bg-elevated)"/>
                          <rect x="0" y="0" [attr.width]="s.winRate" height="6" rx="3"
                            [attr.fill]="s.winRate >= 60 ? 'var(--green)' : s.winRate >= 40 ? 'var(--yellow)' : 'var(--red)'"/>
                        </svg>
                        <span class="jcs-num wr-num"
                          [ngClass]="s.winRate >= 60 ? 'jcs-pos' : s.winRate >= 40 ? '' : 'jcs-neg'">
                          {{ s.winRate | number:'1.0-1' }}%
                        </span>
                      </div>
                    </td>
                    <td class="num jcs-num" [ngClass]="s.netPnl >= 0 ? 'jcs-pos' : 'jcs-neg'">
                      {{ s.netPnl >= 0 ? '+' : '' }}{{ s.netPnl | number:'1.2-2' }}
                    </td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="4" class="sym-empty">
                      @if (loading()) {
                        Cargando…
                      } @else {
                        Sin datos para el período seleccionado.
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>

        <!-- Distribución donut -->
        <div class="jcs-card panel donut-card">
          <header class="panel-head">
            <div>
              <h2>Distribución de P&amp;L</h2>
              <p class="jcs-muted">{{ winsCount() }} wins vs {{ lossesCount() }} losses</p>
            </div>
          </header>
          <div class="donut-wrap">
            <div class="donut-stage">
              <svg class="donut-svg" viewBox="0 0 100 100" aria-hidden="true">
                <defs>
                  <linearGradient id="winGrad" x1="0" x2="1" y1="0" y2="1">
                    <stop offset="0%"   stop-color="#2FDB78" stop-opacity="0.95"/>
                    <stop offset="100%" stop-color="#46E68B" stop-opacity="1"/>
                  </linearGradient>
                  <linearGradient id="lossGrad" x1="0" x2="1" y1="0" y2="1">
                    <stop offset="0%"   stop-color="#FF4057" stop-opacity="0.85"/>
                    <stop offset="100%" stop-color="#FF6B7E" stop-opacity="0.95"/>
                  </linearGradient>
                </defs>
                <circle cx="50" cy="50" r="40" fill="none"
                  stroke="var(--bg-elevated)" stroke-width="14"/>
                <circle cx="50" cy="50" r="40" fill="none"
                  stroke="url(#winGrad)" stroke-width="14"
                  [attr.stroke-dasharray]="donutWinDash()"
                  stroke-dashoffset="0"
                  stroke-linecap="butt"
                  transform="rotate(-90 50 50)"/>
                <circle cx="50" cy="50" r="40" fill="none"
                  stroke="url(#lossGrad)" stroke-width="14"
                  [attr.stroke-dasharray]="donutLossDash()"
                  [attr.stroke-dashoffset]="donutLossOffset()"
                  stroke-linecap="butt"
                  transform="rotate(-90 50 50)"/>
              </svg>
              <div class="donut-center" aria-hidden="true">
                <span class="donut-num jcs-num jcs-pos">{{ winRate() | number:'1.0-0' }}%</span>
                <span class="donut-lbl jcs-muted">win rate</span>
              </div>
            </div>
            <ul class="donut-legend">
              <li>
                <span class="dot dot--win"></span>
                <span class="lbl">Wins</span>
                <span class="val jcs-num jcs-pos">{{ winsCount() }}</span>
                <span class="pct jcs-muted jcs-num">{{ winRate() | number:'1.0-1' }}%</span>
              </li>
              <li>
                <span class="dot dot--loss"></span>
                <span class="lbl">Losses</span>
                <span class="val jcs-num jcs-neg">{{ lossesCount() }}</span>
                <span class="pct jcs-muted jcs-num">{{ lossRate() | number:'1.0-1' }}%</span>
              </li>
            </ul>
          </div>
        </div>
      </section>

      <!-- ============== Line chart: P&L acumulado vs balance ============== -->
      <section class="jcs-card panel">
        <header class="panel-head">
          <div>
            <h2>P&amp;L acumulado vs balance</h2>
            <p class="jcs-muted">{{ periodLabel() }} · series normalizadas</p>
          </div>
          <div class="line-legend">
            <span class="leg">
              <span class="dot dot--pnl"></span>
              <span class="jcs-muted">P&amp;L acumulado</span>
            </span>
            <span class="leg">
              <span class="dot dot--bal"></span>
              <span class="jcs-muted">Balance</span>
            </span>
          </div>
        </header>
        @if (equityPoints().length > 1) {
          <svg class="line-svg" viewBox="0 0 600 200" preserveAspectRatio="none" aria-hidden="true">
            <defs>
              <linearGradient id="lineFill" x1="0" x2="0" y1="0" y2="1">
                <stop offset="0%"   stop-color="#2FDB78" stop-opacity="0.30"/>
                <stop offset="100%" stop-color="#2FDB78" stop-opacity="0"/>
              </linearGradient>
              <linearGradient id="lineStroke" x1="0" x2="1" y1="0" y2="0">
                <stop offset="0%"   stop-color="#2FDB78" stop-opacity="0.55"/>
                <stop offset="100%" stop-color="#2FDB78" stop-opacity="1"/>
              </linearGradient>
            </defs>
            <g stroke="#1C2A33" stroke-width="0.5" stroke-dasharray="4 4">
              <line x1="0" y1="40"  x2="600" y2="40"/>
              <line x1="0" y1="100" x2="600" y2="100"/>
              <line x1="0" y1="160" x2="600" y2="160"/>
            </g>
            <path [attr.d]="pnlAreaPath()" fill="url(#lineFill)"/>
            <path [attr.d]="pnlLinePath()" fill="none"
              stroke="url(#lineStroke)" stroke-width="2"
              stroke-linecap="round" stroke-linejoin="round"/>
            <path [attr.d]="balanceLinePath()" fill="none"
              stroke="var(--blue)" stroke-width="2"
              stroke-dasharray="5 4"
              stroke-linecap="round" stroke-linejoin="round"
              opacity="0.85"/>
          </svg>
        } @else {
          <div class="line-empty">
            <p class="jcs-muted">Sin trades cerrados en este período para graficar.</p>
          </div>
        }
      </section>

      <!-- ============== Top 5 operaciones ============== -->
      <section class="jcs-card panel">
        <header class="panel-head">
          <div>
            <h2>Top 5 operaciones</h2>
            <p class="jcs-muted">Las 5 mejores por P&amp;L neto</p>
          </div>
          <span class="jcs-badge jcs-badge--neutral">Top performers</span>
        </header>
        <ol class="top-list">
          @for (t of topTrades(); track t.id; let i = $index) {
            <li class="top-row">
              <span class="rank jcs-num">#{{ i + 1 }}</span>
              <span class="jcs-badge"
                [ngClass]="t.direction === 1 ? 'long' : 'short'">
                {{ t.direction === 1 ? '↑ Long' : '↓ Short' }}
              </span>
              <span class="top-symbol">{{ t.symbol }}</span>
              <span class="top-vol jcs-muted jcs-num">vol {{ t.volume }}</span>
              <span class="top-spacer"></span>
              <span class="top-pnl jcs-num"
                [ngClass]="(t.pnl ?? 0) >= 0 ? 'jcs-pos' : 'jcs-neg'">
                {{ t.pnl === null ? '—' : ((t.pnl >= 0 ? '+' : '') + (t.pnl | number:'1.2-2')) }} {{ pnlCurrency() }}
              </span>
            </li>
          } @empty {
            <li class="top-empty jcs-muted">Sin operaciones cerradas en el período.</li>
          }
        </ol>
      </section>
    </div>
  `,
  styles: [`
    :host { display: block; }

    .page {
      display: flex;
      flex-direction: column;
      gap: var(--sp-6);
      animation: fade-up 0.4s ease-out;
    }

    .analytics-error {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--sp-3);
      padding: var(--sp-3) var(--sp-4);
      background: rgba(255, 64, 87, 0.08);
      border: 1px solid rgba(255, 64, 87, 0.35);
      border-radius: var(--radius-md);
      color: var(--red);
      font-size: var(--fs-sm);
    }
    .analytics-error-retry {
      background: transparent;
      border: 1px solid var(--red);
      color: var(--red);
      padding: var(--sp-1) var(--sp-3);
      border-radius: var(--radius-sm);
      cursor: pointer;
      font: inherit;
      font-size: var(--fs-xs);
      font-weight: 600;
      transition: background 150ms;
    }
    .analytics-error-retry:hover { background: rgba(255, 64, 87, 0.15); }

    /* Ticker tape — mismo patrón que dashboard. */
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
    .head {
      display: flex;
      align-items: end;
      justify-content: space-between;
      gap: var(--sp-4);
      flex-wrap: wrap;
    }
    .eyebrow {
      font-size: var(--fs-sm);
      font-family: var(--font-mono);
      margin: 0 0 var(--sp-1);
    }
    .title {
      font-size: var(--fs-3xl);
      font-weight: 700;
      letter-spacing: -0.03em;
      margin: 0;
    }
    .subtitle {
      font-size: var(--fs-sm);
      margin: var(--sp-2) 0 0;
    }

    /* Period pills. */
    .period {
      display: inline-flex;
      gap: 2px;
      padding: 4px;
      background: var(--bg-card);
      border: 1px solid var(--border);
      border-radius: var(--radius-md);
    }
    .period-pill {
      appearance: none;
      background: transparent;
      border: 0;
      color: var(--text-muted);
      font: inherit;
      font-size: var(--fs-xs);
      font-weight: 600;
      padding: var(--sp-2) var(--sp-4);
      border-radius: var(--radius-sm);
      cursor: pointer;
      transition: background 150ms, color 150ms;
    }
    .period-pill:hover:not(:disabled) {
      color: var(--text-main);
      background: var(--bg-hover);
    }
    .period-pill:disabled { opacity: 0.5; cursor: not-allowed; }
    .period-pill.is-active {
      background: var(--green-soft);
      color: var(--green);
      box-shadow: inset 0 0 0 1px var(--border-active);
    }

    /* KPI row. */
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
    .kpi--danger {
      background: linear-gradient(180deg, rgba(255,64,87,0.05) 0%, var(--bg-card) 100%);
      border-color: rgba(255,64,87,0.20);
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

    /* Dual grid: panel symbols + donut. */
    .dual-grid {
      display: grid;
      grid-template-columns: 1.5fr 1fr;
      gap: var(--sp-4);
    }
    @media (max-width: 960px) { .dual-grid { grid-template-columns: 1fr; } }

    .panel { padding: var(--sp-5) var(--sp-6); }
    .panel-head {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: var(--sp-4);
      margin-bottom: var(--sp-4);
    }
    .panel-head h2 { font-size: var(--fs-lg); margin: 0 0 var(--sp-1); }
    .panel-head p { font-size: var(--fs-sm); margin: 0; }

    /* Symbol performance table. */
    .sym-wrap { overflow-x: auto; }
    .sym { width: 100%; border-collapse: collapse; }
    .sym th, .sym td {
      padding: var(--sp-3);
      text-align: left;
      border-bottom: 1px solid var(--border-soft);
      font-size: var(--fs-sm);
    }
    .sym th {
      font-size: var(--fs-xs);
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.06em;
      font-weight: 600;
    }
    .sym td.num, .sym th.num { text-align: right; }
    .sym-name { font-weight: 600; }
    .sym tr { transition: background 150ms; }
    .sym tbody tr:hover td { background: var(--bg-hover); }
    .sym-empty {
      text-align: center;
      color: var(--text-muted);
      padding: var(--sp-6);
      font-size: var(--fs-sm);
    }

    /* Mini win rate bar. */
    .wr-cell {
      display: inline-flex;
      align-items: center;
      gap: var(--sp-2);
      justify-content: flex-end;
      width: 100%;
    }
    .mini-bar { width: 70px; height: 6px; display: block; }
    .wr-num { font-size: var(--fs-xs); min-width: 38px; text-align: right; }

    /* Badge variants para dirección. */
    .jcs-badge.long {
      background: rgba(47, 219, 120, 0.15);
      color: var(--green);
    }
    .jcs-badge.short {
      background: rgba(255, 64, 87, 0.12);
      color: var(--red);
    }

    /* Donut chart. */
    .donut-card { display: flex; flex-direction: column; }
    .donut-wrap {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--sp-5);
      flex: 1;
    }
    .donut-stage {
      position: relative;
      width: 200px;
      height: 200px;
      display: flex;
      align-items: center;
      justify-content: center;
    }
    .donut-svg { width: 100%; height: 100%; display: block; }
    .donut-center {
      position: absolute;
      inset: 0;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      pointer-events: none;
    }
    .donut-num {
      font-size: var(--fs-3xl);
      font-weight: 700;
      letter-spacing: -0.025em;
      line-height: 1;
    }
    .donut-lbl {
      font-size: var(--fs-xs);
      text-transform: uppercase;
      letter-spacing: 0.08em;
      font-weight: 600;
      margin-top: var(--sp-1);
    }

    .donut-legend {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: var(--sp-2);
      width: 100%;
    }
    .donut-legend li {
      display: grid;
      grid-template-columns: 12px 1fr auto auto;
      align-items: center;
      gap: var(--sp-3);
      padding: var(--sp-3);
      background: var(--bg-card-soft);
      border-radius: var(--radius-sm);
      border-left: 3px solid transparent;
      transition: background 150ms;
    }
    .donut-legend li:hover { background: var(--bg-hover); }
    .donut-legend li:nth-child(1) { border-left-color: var(--green); }
    .donut-legend li:nth-child(2) { border-left-color: var(--red); }
    .dot {
      width: 10px; height: 10px;
      border-radius: 50%;
      display: inline-block;
    }
    .dot--win   { background: var(--green); box-shadow: 0 0 0 4px rgba(47, 219, 120, 0.15); }
    .dot--loss  { background: var(--red);   box-shadow: 0 0 0 4px rgba(255, 64, 87, 0.15); }
    .donut-legend .lbl { font-size: var(--fs-sm); color: var(--text-secondary); font-weight: 500; }
    .donut-legend .val { font-size: var(--fs-sm); font-weight: 700; }
    .donut-legend .pct { font-size: var(--fs-xs); min-width: 44px; text-align: right; }

    /* Line chart legend. */
    .line-legend {
      display: flex;
      gap: var(--sp-4);
      flex-wrap: wrap;
    }
    .leg {
      display: inline-flex;
      align-items: center;
      gap: var(--sp-2);
      font-size: var(--fs-xs);
    }
    .dot--pnl { background: var(--green); box-shadow: 0 0 0 4px rgba(47, 219, 120, 0.15); }
    .dot--bal { background: var(--blue);  box-shadow: 0 0 0 4px rgba(74, 168, 255, 0.15); }
    .line-svg {
      width: 100%;
      height: 200px;
      display: block;
    }
    .line-empty {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 200px;
    }

    /* Top 5 list. */
    .top-list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: var(--sp-2);
    }
    .top-row {
      display: flex;
      align-items: center;
      gap: var(--sp-3);
      padding: var(--sp-3) var(--sp-4);
      background: var(--bg-card-soft);
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-sm);
      transition: background 150ms, border-color 150ms, transform 150ms;
      animation: fade-up 0.4s ease-out both;
    }
    .top-row:hover {
      background: var(--bg-hover);
      border-color: var(--border-active);
      transform: translateX(2px);
    }
    .top-row:nth-child(1) { animation-delay: 0.05s; }
    .top-row:nth-child(2) { animation-delay: 0.10s; }
    .top-row:nth-child(3) { animation-delay: 0.15s; }
    .top-row:nth-child(4) { animation-delay: 0.20s; }
    .top-row:nth-child(5) { animation-delay: 0.25s; }
    .top-empty {
      text-align: center;
      padding: var(--sp-6);
      font-size: var(--fs-sm);
    }
    .rank {
      font-size: var(--fs-xs);
      font-weight: 700;
      color: var(--text-muted);
      letter-spacing: 0.05em;
      min-width: 28px;
    }
    .top-symbol {
      font-weight: 600;
      font-size: var(--fs-sm);
      min-width: 80px;
    }
    .top-vol {
      font-size: var(--fs-xs);
    }
    .top-spacer { flex: 1; }
    .top-pnl {
      font-size: var(--fs-sm);
      font-weight: 700;
      letter-spacing: -0.01em;
    }
    @media (max-width: 540px) {
      .top-vol { display: none; }
      .top-symbol { min-width: 0; }
    }

    @keyframes fade-up {
      from { opacity: 0; transform: translateY(8px); }
      to   { opacity: 1; transform: translateY(0); }
    }
  `],
})
export class AnalyticsPage {
  private readonly api = inject(TradeApiService);
  private readonly metricsApi = inject(MetricsApiService);

  readonly periods = PERIODS;
  readonly selectedPeriod = signal<Period>('30d');

  readonly refreshing = signal(false);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly items = signal<TradeDto[]>([]);
  readonly summary = signal<{
    totalCount: number;
    winsCount: number;
    lossesCount: number;
    winRate: number;
    totalPnL: number;
    bestTrade: number;
    worstTrade: number;
    avgTrade: number;
    currency: string;
  } | null>(null);

  // ===== Server-side metrics (slice 1f) =====
  readonly metrics = signal<MetricsDto | null>(null);

  // ===== Computeds base =====
  readonly totalCount = computed(() => this.metrics()?.totalTrades ?? this.items().length);
  readonly pnlCurrency = computed(() =>
    this.metrics()?.currency ?? this.summary()?.currency
    ?? this.items()[0]?.pnlCurrency ?? this.items()[0]?.accountCurrency ?? 'USD'
  );

  readonly winsCount = computed(() => {
    const m = this.metrics();
    if (m) return Math.round((m.winRate / 100) * m.totalClosedTrades);
    return this.items().filter(t => t.status === 2 && (t.pnl ?? 0) > 0).length;
  });
  readonly lossesCount = computed(() => {
    const m = this.metrics();
    if (m) return Math.max(0, m.totalClosedTrades - this.winsCount());
    return this.items().filter(t => t.status === 2 && (t.pnl ?? 0) < 0).length;
  });

  readonly winRate = computed(() => {
    const m = this.metrics();
    if (m) return m.winRate;
    const s = this.summary();
    if (s) return s.winRate;
    const n = this.winsCount() + this.lossesCount();
    return n === 0 ? 0 : (this.winsCount() / n) * 100;
  });

  readonly lossRate = computed(() => 100 - this.winRate());

  // ===== KPIs server-side =====
  readonly expectancy = computed(() => this.metrics()?.expectancy ?? 0);
  readonly profitFactor = computed(() => this.metrics()?.profitFactor ?? 0);
  readonly maxDrawdown = computed(() => this.metrics()?.maxDrawdownAmount ?? 0);

  // ===== Stats por símbolo (server) =====
  readonly symbolStats = computed<SymbolStat[]>(() =>
    this.metrics() ? toUiSymbolStats(this.metrics()!.symbolStats) : []
  );

  // ===== Donut chart =====
  private readonly CIRCUMFERENCE = 2 * Math.PI * 40;

  readonly donutWinFraction = computed(() => {
    const n = this.winsCount() + this.lossesCount();
    return n === 0 ? 0 : this.winsCount() / n;
  });

  readonly donutWinDash = computed(() => {
    const arc = this.CIRCUMFERENCE * this.donutWinFraction();
    return `${arc.toFixed(2)} ${this.CIRCUMFERENCE.toFixed(2)}`;
  });
  readonly donutLossDash = computed(() => {
    const arc = this.CIRCUMFERENCE * (1 - this.donutWinFraction());
    return `${arc.toFixed(2)} ${this.CIRCUMFERENCE.toFixed(2)}`;
  });
  readonly donutLossOffset = computed(() => {
    const winArc = this.CIRCUMFERENCE * this.donutWinFraction();
    return -Number(winArc.toFixed(2));
  });

  // ===== Line chart =====
  private readonly chartW = 600;
  private readonly chartH = 200;

  readonly dailyPoints = computed(() => buildDailyPoints(this.items()));
  readonly equityPoints = computed(() => buildEquityCurve(this.dailyPoints()));

  // Slice 1f — la curva de balance usa los puntos que el server manda en
  // MetricsDto.equityCurve (cada {timestamp, equity, drawdown}). El cliente
  // ya NO compone el balance con initialBalance/dailyYield (mocks).
  readonly serverEquityCurve = computed(() =>
    (this.metrics()?.equityCurve ?? []).map(p => ({
      timestamp: p.timestamp,
      equity: p.equity,
      drawdown: p.drawdown,
    }))
  );

  readonly pnlLinePath = computed(() => this.buildPath(this.equityPoints(), this.serverEquityCurve()));
  readonly pnlAreaPath = computed(() => {
    const line = this.pnlLinePath();
    if (!line) return '';
    return `${line} L${this.chartW},${this.chartH} L0,${this.chartH} Z`;
  });
  // El "balance" del chart ahora es el equity acumulado que viene del server
  // (no se compone con yield diario). Si el server devuelve una curva vacia
  // caemos al fallback client-side (dailyPoints -> equityPoints) para que el
  // grafico siga siendo util mientras el endpoint termina de hidratar.
  readonly balanceLinePath = computed(() => {
    const server = this.serverEquityCurve();
    const values = server.length > 0
      ? server.map(p => p.equity)
      : this.equityPoints();
    return this.buildPath(values, server.length > 0 ? server : this.equityPoints().map(v => ({ equity: v, drawdown: 0 })));
  });

  // ===== Top 5 =====
  readonly topTrades = computed(() =>
    [...this.items()]
      .filter(t => t.status === 2 && t.pnl !== null)
      .sort((a, b) => (b.pnl ?? 0) - (a.pnl ?? 0))
      .slice(0, 5)
  );

  readonly periodLabel = computed(() => PERIODS.find(p => p.key === this.selectedPeriod())?.label ?? '');

  constructor() {
    void this.reload();
  }

  setPeriod(p: Period): void {
    if (p === this.selectedPeriod() && !this.loading()) return;
    this.selectedPeriod.set(p);
    void this.reload();
  }

  async reload(): Promise<void> {
    if (this.refreshing()) return;
    this.refreshing.set(true);
    this.error.set(null);
    try {
      const days = daysForPeriod(this.selectedPeriod());
      const from = days === null ? undefined : this.isoDaysAgo(days);
      const to = days === null ? undefined : this.isoNow();

      const [summary, page, metrics] = await Promise.all([
        this.api.dashboard(from, to),
        this.api.list(1, 100),
        this.metricsApi.get(this.selectedPeriod()),
      ]);

      this.summary.set({
        totalCount: summary.totalCount,
        winsCount: summary.winsCount,
        lossesCount: summary.lossesCount,
        winRate: summary.winRate,
        totalPnL: summary.totalPnL,
        bestTrade: summary.bestTrade,
        worstTrade: summary.worstTrade,
        avgTrade: summary.avgTrade,
        currency: summary.currency,
      });
      this.items.set(page.items);
      this.metrics.set(metrics);
    } catch (e) {
      this.error.set(this.toMessage(e));
      this.items.set([]);
      this.summary.set(null);
      this.metrics.set(null);
    } finally {
      this.refreshing.set(false);
      this.loading.set(false);
    }
  }

  // ===== Internals =====
  private buildPath(values: number[], allPoints: { equity: number }[]): string {
    if (values.length < 2) return '';
    if (allPoints.length === 0) return '';
    const min = Math.min(...allPoints.map(p => p.equity));
    const max = Math.max(...allPoints.map(p => p.equity));
    const range = max - min || 1;
    const step = this.chartW / (values.length - 1);
    return values
      .map((v, i) => {
        const x = i * step;
        const y = this.chartH - ((v - min) / range) * (this.chartH - 20) - 10;
        return `${i === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`;
      })
      .join(' ');
  }

  private isoDaysAgo(days: number): string {
    const d = new Date();
    d.setUTCDate(d.getUTCDate() - days);
    return d.toISOString();
  }

  private isoNow(): string {
    return new Date().toISOString();
  }

  private toMessage(e: unknown): string {
    if (e instanceof Error && e.message) return e.message;
    if (typeof e === 'string') return e;
    return 'Error inesperado.';
  }
}
