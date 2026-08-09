import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { DecimalPipe, NgClass } from '@angular/common';

interface TradeDto {
  id: string;
  symbol: string;
  direction: 'Long' | 'Short';
  volume: number;
  pnl: number;
  openedAt: string;
}

// 24 operaciones mock — secuencia diseñada para que la equity curve tenga
// un max drawdown cercano a -$450 USD (peak trade 9 = +$1633, trough
// trade 13 = +$1182 → DD ≈ -$451). 16 wins / 8 losses → win rate 66.7%.
const MOCK_TRADES: TradeDto[] = [
  { id: '1',  symbol: 'EUR/USD',  direction: 'Long',  volume: 1.5, pnl:  125.00, openedAt: '2026-06-01' },
  { id: '2',  symbol: 'XAU/USD',  direction: 'Short', volume: 0.5, pnl:  700.00, openedAt: '2026-06-02' },
  { id: '3',  symbol: 'BTC/USD',  direction: 'Long',  volume: 0.1, pnl:  210.00, openedAt: '2026-06-03' },
  { id: '4',  symbol: 'GBP/USD',  direction: 'Long',  volume: 2.0, pnl:   85.00, openedAt: '2026-06-04' },
  { id: '5',  symbol: 'USD/JPY',  direction: 'Short', volume: 1.0, pnl:   45.00, openedAt: '2026-06-05' },
  { id: '6',  symbol: 'ETH/USD',  direction: 'Long',  volume: 2.0, pnl:   95.00, openedAt: '2026-06-07' },
  { id: '7',  symbol: 'AUD/USD',  direction: 'Short', volume: 1.5, pnl:  180.00, openedAt: '2026-06-08' },
  { id: '8',  symbol: 'USD/CHF',  direction: 'Long',  volume: 1.0, pnl:   38.00, openedAt: '2026-06-09' },
  { id: '9',  symbol: 'XAU/USD',  direction: 'Short', volume: 0.4, pnl:  155.00, openedAt: '2026-06-10' },
  { id: '10', symbol: 'BTC/USD',  direction: 'Long',  volume: 0.1, pnl:  -78.00, openedAt: '2026-06-12' },
  { id: '11', symbol: 'EUR/USD',  direction: 'Long',  volume: 1.0, pnl: -110.00, openedAt: '2026-06-13' },
  { id: '12', symbol: 'GBP/USD',  direction: 'Long',  volume: 1.5, pnl:  -95.00, openedAt: '2026-06-15' },
  { id: '13', symbol: 'USD/JPY',  direction: 'Short', volume: 1.0, pnl: -168.00, openedAt: '2026-06-16' },
  { id: '14', symbol: 'ETH/USD',  direction: 'Long',  volume: 1.5, pnl:   28.00, openedAt: '2026-06-17' },
  { id: '15', symbol: 'AUD/USD',  direction: 'Short', volume: 1.0, pnl:   52.00, openedAt: '2026-06-18' },
  { id: '16', symbol: 'EUR/USD',  direction: 'Long',  volume: 1.5, pnl:  155.00, openedAt: '2026-06-20' },
  { id: '17', symbol: 'XAU/USD',  direction: 'Short', volume: 0.3, pnl:  -65.00, openedAt: '2026-06-22' },
  { id: '18', symbol: 'BTC/USD',  direction: 'Long',  volume: 0.1, pnl:   85.00, openedAt: '2026-06-23' },
  { id: '19', symbol: 'GBP/USD',  direction: 'Long',  volume: 1.0, pnl:  -42.00, openedAt: '2026-06-24' },
  { id: '20', symbol: 'USD/JPY',  direction: 'Short', volume: 1.5, pnl:   95.00, openedAt: '2026-06-25' },
  { id: '21', symbol: 'ETH/USD',  direction: 'Long',  volume: 1.0, pnl:   27.00, openedAt: '2026-06-26' },
  { id: '22', symbol: 'XAU/USD',  direction: 'Short', volume: 0.3, pnl:  -78.00, openedAt: '2026-06-27' },
  { id: '23', symbol: 'BTC/USD',  direction: 'Long',  volume: 0.1, pnl:  -88.00, openedAt: '2026-06-28' },
  { id: '24', symbol: 'USD/CHF',  direction: 'Long',  volume: 1.0, pnl:   38.00, openedAt: '2026-06-29' },
];

interface SymbolStat {
  symbol: string;
  trades: number;
  wins: number;
  winRate: number;
  netPnl: number;
  bestTrade: number;
  worstTrade: number;
}

// Mapear las 24 operaciones a 30 días (algunos días sin trades → plateau
// en la equity curve). El índice es "día desde hoy - 29".
const TRADE_BY_DAY: Record<number, TradeDto> = {
  1: MOCK_TRADES[0],
  2: MOCK_TRADES[1],
  3: MOCK_TRADES[2],
  4: MOCK_TRADES[3],
  5: MOCK_TRADES[4],
  7: MOCK_TRADES[5],
  8: MOCK_TRADES[6],
  9: MOCK_TRADES[7],
  10: MOCK_TRADES[8],
  12: MOCK_TRADES[9],
  13: MOCK_TRADES[10],
  15: MOCK_TRADES[11],
  16: MOCK_TRADES[12],
  17: MOCK_TRADES[13],
  18: MOCK_TRADES[14],
  20: MOCK_TRADES[15],
  22: MOCK_TRADES[16],
  23: MOCK_TRADES[17],
  24: MOCK_TRADES[18],
  25: MOCK_TRADES[19],
  26: MOCK_TRADES[20],
  27: MOCK_TRADES[21],
  28: MOCK_TRADES[22],
  29: MOCK_TRADES[23],
};

// P&L acumulado día por día (30 días).
function buildEquityCurve(): number[] {
  const pts: number[] = [];
  let cum = 0;
  for (let i = 0; i < 30; i++) {
    cum += TRADE_BY_DAY[i]?.pnl ?? 0;
    pts.push(Number(cum.toFixed(2)));
  }
  return pts;
}

// Balance mock: deposit inicial + P&L acumulado + yield diario compuesto.
// Lo normalizamos contra el inicial para que viva en la misma escala que
// la equity curve en el line chart.
function buildBalanceCurve(): number[] {
  const pts: number[] = [];
  const initial = 10000;
  let bal = initial;
  for (let i = 0; i < 30; i++) {
    bal += TRADE_BY_DAY[i]?.pnl ?? 0;
    bal += bal * 0.0006; // ~0.06% diario (mock)
    pts.push(Number((bal - initial).toFixed(2)));
  }
  return pts;
}

const EQUITY_POINTS = buildEquityCurve();
const BALANCE_POINTS = buildBalanceCurve();

type Period = '7d' | '30d' | '90d' | 'all';
const PERIODS: ReadonlyArray<{ key: Period; label: string }> = [
  { key: '7d',  label: '7 días' },
  { key: '30d', label: '30 días' },
  { key: '90d', label: '90 días' },
  { key: 'all', label: 'Todo'    },
];

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
              [attr.aria-selected]="selectedPeriod() === p.key">
              {{ p.label }}
            </button>
          }
        </div>
      </header>

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
          <span class="kpi-foot jcs-muted">USD · peor caída</span>
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
                  <th class="num">Mejor</th>
                  <th class="num">Peor</th>
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
                    <td class="num jcs-num jcs-pos">+{{ s.bestTrade | number:'1.2-2' }}</td>
                    <td class="num jcs-num" [ngClass]="s.worstTrade < 0 ? 'jcs-neg' : 'jcs-pos'">
                      {{ s.worstTrade | number:'1.2-2' }}
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
                <!-- Track -->
                <circle cx="50" cy="50" r="40" fill="none"
                  stroke="var(--bg-elevated)" stroke-width="14"/>
                <!-- Wins arc -->
                <circle cx="50" cy="50" r="40" fill="none"
                  stroke="url(#winGrad)" stroke-width="14"
                  [attr.stroke-dasharray]="donutWinDash()"
                  stroke-dashoffset="0"
                  stroke-linecap="butt"
                  transform="rotate(-90 50 50)"/>
                <!-- Losses arc -->
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
            <p class="jcs-muted">Últimos 30 días · series normalizadas</p>
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
          <!-- Grid -->
          <g stroke="#1C2A33" stroke-width="0.5" stroke-dasharray="4 4">
            <line x1="0" y1="40"  x2="600" y2="40"/>
            <line x1="0" y1="100" x2="600" y2="100"/>
            <line x1="0" y1="160" x2="600" y2="160"/>
          </g>
          <!-- Area bajo P&L -->
          <path [attr.d]="pnlAreaPath()" fill="url(#lineFill)"/>
          <!-- P&L line -->
          <path [attr.d]="pnlLinePath()" fill="none"
            stroke="url(#lineStroke)" stroke-width="2"
            stroke-linecap="round" stroke-linejoin="round"/>
          <!-- Balance line (dashed) -->
          <path [attr.d]="balanceLinePath()" fill="none"
            stroke="var(--blue)" stroke-width="2"
            stroke-dasharray="5 4"
            stroke-linecap="round" stroke-linejoin="round"
            opacity="0.85"/>
        </svg>
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
                [ngClass]="t.direction === 'Long' ? 'long' : 'short'">
                {{ t.direction === 'Long' ? '↑ Long' : '↓ Short' }}
              </span>
              <span class="top-symbol">{{ t.symbol }}</span>
              <span class="top-vol jcs-muted jcs-num">vol {{ t.volume }}</span>
              <span class="top-spacer"></span>
              <span class="top-pnl jcs-num"
                [ngClass]="t.pnl >= 0 ? 'jcs-pos' : 'jcs-neg'">
                {{ t.pnl >= 0 ? '+' : '' }}{{ t.pnl | number:'1.2-2' }} USD
              </span>
            </li>
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
    .period-pill:hover {
      color: var(--text-main);
      background: var(--bg-hover);
    }
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
  readonly periods = PERIODS;
  readonly selectedPeriod = signal<Period>('30d');

  readonly items = signal<TradeDto[]>(MOCK_TRADES);

  // Métricas base.
  readonly totalCount = computed(() => this.items().length);
  readonly winsCount = computed(() => this.items().filter(t => t.pnl > 0).length);
  readonly lossesCount = computed(() => this.items().filter(t => t.pnl < 0).length);
  readonly winRate = computed(() => {
    const n = this.totalCount();
    return n === 0 ? 0 : (this.winsCount() / n) * 100;
  });
  readonly lossRate = computed(() => 100 - this.winRate());

  readonly grossWins = computed(() =>
    this.items().filter(t => t.pnl > 0).reduce((acc, t) => acc + t.pnl, 0)
  );
  readonly grossLosses = computed(() =>
    Math.abs(this.items().filter(t => t.pnl < 0).reduce((acc, t) => acc + t.pnl, 0))
  );

  readonly expectancy = computed(() => {
    const n = this.totalCount();
    return n === 0 ? 0 : (this.grossWins() - this.grossLosses()) / n;
  });
  readonly profitFactor = computed(() => {
    const loss = this.grossLosses();
    return loss === 0 ? 0 : this.grossWins() / loss;
  });

  // Max drawdown sobre la equity curve cumulative.
  readonly maxDrawdown = computed(() => {
    let peak = -Infinity;
    let maxDD = 0;
    for (const v of EQUITY_POINTS) {
      if (v > peak) peak = v;
      const dd = v - peak; // dd ≤ 0
      if (dd < maxDD) maxDD = dd;
    }
    return Number(maxDD.toFixed(2));
  });

  // Stats por símbolo.
  readonly symbolStats = computed<SymbolStat[]>(() => {
    const map = new Map<string, SymbolStat>();
    for (const t of this.items()) {
      const s = map.get(t.symbol) ?? {
        symbol: t.symbol,
        trades: 0,
        wins: 0,
        winRate: 0,
        netPnl: 0,
        bestTrade: 0,
        worstTrade: 0,
      };
      s.trades += 1;
      if (t.pnl > 0) s.wins += 1;
      s.netPnl += t.pnl;
      s.bestTrade = Math.max(s.bestTrade, t.pnl);
      s.worstTrade = Math.min(s.worstTrade, t.pnl);
      map.set(t.symbol, s);
    }
    return [...map.values()]
      .map(s => ({ ...s, winRate: (s.wins / s.trades) * 100 }))
      .sort((a, b) => b.netPnl - a.netPnl);
  });

  // Donut chart: geometría sobre r=40, circumference ≈ 251.327.
  private readonly CIRCUMFERENCE = 2 * Math.PI * 40;

  readonly donutWinFraction = computed(() => {
    const n = this.totalCount();
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

  // Line chart paths (comparten min/max para vivir en la misma escala).
  private readonly chartW = 600;
  private readonly chartH = 200;

  readonly pnlLinePath = computed(() => this.buildPath(EQUITY_POINTS));
  readonly pnlAreaPath = computed(() => {
    const line = this.pnlLinePath();
    return `${line} L${this.chartW},${this.chartH} L0,${this.chartH} Z`;
  });
  readonly balanceLinePath = computed(() => this.buildPath(BALANCE_POINTS));

  // Top 5 operaciones (por P&L).
  readonly topTrades = computed(() =>
    [...this.items()]
      .sort((a, b) => b.pnl - a.pnl)
      .slice(0, 5)
  );

  setPeriod(p: Period): void {
    // Cuando llegue el endpoint real, acá filtramos `items` según `p`.
    this.selectedPeriod.set(p);
  }

  // Genera el `d` de un polyline SVG compartido entre las dos series.
  private buildPath(values: number[]): string {
    const all = [...EQUITY_POINTS, ...BALANCE_POINTS];
    const min = Math.min(...all);
    const max = Math.max(...all);
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
}