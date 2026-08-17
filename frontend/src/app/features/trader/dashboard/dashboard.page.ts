import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe, DatePipe, NgClass } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { AuthState } from '@core/state/auth.state';
import {
  ASSET_CLASS_LABEL,
  TRADE_DIRECTION_LABEL,
  TRADE_STATUS_LABEL,
  TradeApiService,
  TradeDto,
} from '@core/api/trade-api.service';
import { CoachingPromptsComponent } from '../coaching/coaching-prompts.component';
import { CoachingState } from '../coaching/state/coaching.state';

// Construye una serie de equity curve (P&L acumulado) a partir de trades
// cerrados ordenados por fecha. Devuelve `number[]` con un punto por trade.
function buildEquityCurve(items: TradeDto[]): number[] {
  const closed = items
    .filter(t => t.status === 2 && t.pnl !== null && t.closedAt !== null)
    .sort((a, b) => (a.closedAt ?? '').localeCompare(b.closedAt ?? ''));
  if (closed.length === 0) return [];
  let acc = 0;
  return closed.map(t => {
    acc += t.pnl ?? 0;
    return Number(acc.toFixed(2));
  });
}

type DirectionLabel = 'Long' | 'Short';
type StatusLabel = 'Open' | 'Closed' | 'Cancelled';

@Component({
  selector: 'jcs-dashboard',
  standalone: true,
  imports: [DecimalPipe, DatePipe, NgClass, RouterLink, CoachingPromptsComponent],
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
          <button class="jcs-btn jcs-btn--primary" (click)="reload()" [disabled]="refreshing()">
            <span class="reload-dot" [class.spin]="refreshing()"></span>
            Actualizar
          </button>
        </div>
      </header>

      <!-- ============== Error banner ============== -->
      @if (error()) {
        <div class="dash-error" role="alert">
          <span>No se pudo cargar el dashboard: {{ error() }}</span>
          <button type="button" class="dash-error-retry" (click)="reload()">Reintentar</button>
        </div>
      }

      <!-- ============== Coaching prompts (slice 2d embed) ============== -->
      <jcs-coaching-prompts [prompts]="coachingState.prompts()"></jcs-coaching-prompts>

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
        @if (equityPoints().length > 1) {
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
            <g stroke="#1C2A33" stroke-width="0.5" stroke-dasharray="4 4">
              <line x1="0" y1="40" x2="600" y2="40"/>
              <line x1="0" y1="100" x2="600" y2="100"/>
              <line x1="0" y1="160" x2="600" y2="160"/>
            </g>
            <path [attr.d]="areaPath()" fill="url(#equityFill)"/>
            <path [attr.d]="linePath()" fill="none" stroke="url(#equityLine)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
          </svg>
        } @else {
          <div class="chart-empty">
            <p class="jcs-muted">Aún no hay operaciones cerradas para graficar.</p>
          </div>
        }
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
            <a class="quick" routerLink="/app/trades">
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
            <a class="quick" routerLink="/app/calendar">
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
            <span class="jcs-badge jcs-badge--neutral">{{ recentItems().length }} / {{ totalCount() }}</span>
          </header>
          <div class="trades-wrap">
            @if (recentItems().length > 0) {
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
                  @for (t of recentItems(); track t.id) {
                    <tr>
                      <td class="jcs-num">{{ t.openedAt | date:'shortDate' }}</td>
                      <td class="symbol">{{ t.symbol }}</td>
                      <td>
                        <span class="jcs-badge" [ngClass]="directionLabel(t.direction) === 'Long' ? 'jcs-pos long' : 'jcs-badge--neutral short'">
                          {{ directionLabel(t.direction) === 'Long' ? '↑ Long' : '↓ Short' }}
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
            } @else {
              <div class="trades-empty">
                <p class="jcs-muted">Tu primera operación</p>
                <p class="jcs-muted trades-empty-sub">Aún no registraste ningún trade. Empezá creando una operación.</p>
                <a class="jcs-btn jcs-btn--primary" routerLink="/app/trades">Crear operación</a>
              </div>
            }
          </div>
        </div>
      </section>

      <section class="link-cards-grid" data-testid="dash-shortcuts">
        @for (s of shortcuts; track s.link) {
          <button
            type="button"
            class="jcs-card jcs-card--hover link-card"
            [attr.data-testid]="'dash-shortcut-' + s.label.toLowerCase()"
            (click)="goTo(s.link)">
            <span class="link-card-icon" aria-hidden="true">
              @switch (s.icon) {
                @case ('list') {
                  <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <path d="M3 4h6a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H3z"/>
                    <line x1="11" y1="6" x2="21" y2="6"/>
                    <line x1="11" y1="12" x2="21" y2="12"/>
                    <line x1="11" y1="18" x2="21" y2="18"/>
                  </svg>
                }
                @case ('tag') {
                  <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <path d="M20.59 13.41 13.42 20.58a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z"/>
                    <line x1="7" y1="7" x2="7.01" y2="7"/>
                  </svg>
                }
                @case ('calendar') {
                  <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <rect x="3" y="4" width="18" height="18" rx="2"/>
                    <line x1="16" y1="2" x2="16" y2="6"/>
                    <line x1="8" y1="2" x2="8" y2="6"/>
                    <line x1="3" y1="10" x2="21" y2="10"/>
                  </svg>
                }
              }
            </span>
            <span class="link-card-title">Ver {{ s.label.toLowerCase() }} →</span>
            <span class="link-card-desc jcs-muted">{{ s.description }}</span>
          </button>
        }
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

    .dash-error {
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
    .dash-error-retry {
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
    .dash-error-retry:hover { background: rgba(255, 64, 87, 0.15); }

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
    .chart-empty {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 200px;
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

    .trades-empty {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: var(--sp-2);
      padding: var(--sp-8) var(--sp-4);
      text-align: center;
    }
    .trades-empty p:first-child {
      font-size: var(--fs-lg);
      font-weight: 600;
      color: var(--text-main);
    }
    .trades-empty-sub { font-size: var(--fs-sm); margin: 0; }
    .trades-empty .jcs-btn { margin-top: var(--sp-3); }

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

    .link-cards-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
      gap: var(--sp-4);
      margin-top: var(--sp-6);
    }
    .link-card {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: var(--sp-2);
      padding: var(--sp-5);
      cursor: pointer;
      font: inherit;
      color: var(--text-main);
      text-align: left;
      transition: transform 200ms ease, border-color 200ms ease, box-shadow 200ms ease;
    }
    .link-card:hover {
      transform: translateY(-2px);
      box-shadow: var(--shadow-soft);
    }
    .link-card-icon {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 36px;
      height: 36px;
      border-radius: var(--radius-sm);
      background: var(--green-soft);
      color: var(--green);
      flex-shrink: 0;
    }
    .link-card-title {
      font-weight: 600;
      font-size: var(--fs-base);
      color: var(--green);
      letter-spacing: -0.01em;
    }
    .link-card-desc {
      font-size: var(--fs-xs);
      line-height: 1.5;
    }
  `],
})
export class DashboardPage {
  readonly auth = inject(AuthState);
  private readonly api = inject(TradeApiService);
  private readonly router = inject(Router);
  readonly coachingState = inject(CoachingState);

  readonly shortcuts: ReadonlyArray<{
    readonly label: string;
    readonly link: string;
    readonly description: string;
    readonly icon: 'list' | 'tag' | 'calendar';
  }> = [
    { label: 'Strategies', link: '/app/strategies', description: 'Setups nombrados y sus reglas.', icon: 'list' },
    { label: 'Alertas',    link: '/app/alerts',     description: 'Señales activas del motor de reglas.', icon: 'tag' },
    { label: 'Planner',    link: '/app/planner',    description: 'Sesiones semanales plan vs realidad.', icon: 'calendar' },
  ];

  readonly refreshing = signal(false);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly items = signal<TradeDto[]>([]);
  readonly summary = signal<{
    totalCount: number;
    openCount: number;
    closedCount: number;
    winsCount: number;
    winRate: number;
    totalPnL: number;
    bestTrade: number;
    worstTrade: number;
    avgTrade: number;
    currency: string;
  } | null>(null);

  // 30 días por defecto (alineado con `from` que usa el endpoint dashboard).
  private readonly fromDate = this.isoDaysAgo(30);
  private readonly toDate = this.isoNow();

  // ===== UI mappers =====
  directionLabel(d: 1 | 2): DirectionLabel { return TRADE_DIRECTION_LABEL[d]; }
  statusLabel(s: 1 | 2 | 3): StatusLabel { return TRADE_STATUS_LABEL[s]; }
  assetClassLabel(a: 1 | 2 | 3 | 4 | 5): string { return ASSET_CLASS_LABEL[a]; }

  // ===== KPIs — preferimos el summary autoritativo del backend; caemos a
  // los items cargados solo cuando el summary aún no llegó.
  readonly totalCount = computed(() => this.summary()?.totalCount ?? this.items().length);
  readonly openCount = computed(() => this.summary()?.openCount ?? this.items().filter(t => t.status === 1).length);
  readonly closedCount = computed(() => this.summary()?.closedCount ?? this.items().filter(t => t.status === 2).length);
  readonly winsCount = computed(() => this.summary()?.winsCount ?? this.items().filter(t => t.status === 2 && (t.pnl ?? 0) > 0).length);
  readonly winRate = computed(() => {
    const s = this.summary();
    if (s) return s.winRate;
    const c = this.closedCount();
    return c === 0 ? 0 : (this.winsCount() / c) * 100;
  });
  readonly totalPnL = computed(() => this.summary()?.totalPnL ?? this.items().reduce((acc, t) => acc + (t.pnl ?? 0), 0));
  readonly pnlCurrency = computed(() =>
    this.summary()?.currency ?? this.items()[0]?.pnlCurrency ?? this.items()[0]?.accountCurrency ?? 'USD'
  );
  readonly bestTrade = computed(() => {
    const s = this.summary();
    if (s) return s.bestTrade;
    const pnls = this.items().map(t => t.pnl ?? 0);
    return pnls.length === 0 ? 0 : Math.max(0, ...pnls);
  });
  readonly worstTrade = computed(() => {
    const s = this.summary();
    if (s) return s.worstTrade;
    const pnls = this.items().map(t => t.pnl ?? 0);
    return pnls.length === 0 ? 0 : Math.min(0, ...pnls);
  });
  readonly avgTrade = computed(() => {
    const s = this.summary();
    if (s) return s.avgTrade;
    const n = this.closedCount();
    return n === 0 ? 0 : this.totalPnL() / n;
  });

  // Símbolos del mejor/peor trade requieren items (el summary no los incluye).
  readonly bestSymbol = computed(() => {
    const t = this.items().reduce<TradeDto | null>((best, x) => {
      if (x.pnl === null) return best;
      if (best === null || x.pnl > (best.pnl ?? 0)) return x;
      return best;
    }, null);
    return t?.symbol ?? '—';
  });
  readonly worstSymbol = computed(() => {
    const t = this.items().reduce<TradeDto | null>((worst, x) => {
      if (x.pnl === null) return worst;
      if (worst === null || x.pnl < (worst.pnl ?? 0)) return x;
      return worst;
    }, null);
    return t?.symbol ?? '—';
  });

  // Solo los últimos N trades para la tabla "Operaciones recientes".
  readonly recentItems = computed(() =>
    [...this.items()]
      .sort((a, b) => b.openedAt.localeCompare(a.openedAt))
      .slice(0, 8)
  );

  // ===== Equity curve =====
  readonly equityPoints = computed(() => buildEquityCurve(this.items()));

  readonly linePath = computed(() => {
    const pts = this.equityPoints();
    if (pts.length < 2) return '';
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
    if (!line) return '';
    return `${line} L600,200 L0,200 Z`;
  });

  readonly greeting = computed(() => {
    const h = new Date().getHours();
    if (h < 6) return 'Buenas noches';
    if (h < 12) return 'Buenos días';
    if (h < 19) return 'Buenas tardes';
    return 'Buenas noches';
  });

  constructor() {
    void this.reload();
    void this.coachingState.load('30d');
  }

  goTo(link: string): void {
    void this.router.navigateByUrl(link);
  }

  async reload(): Promise<void> {
    if (this.refreshing()) return;
    this.refreshing.set(true);
    this.error.set(null);
    try {
      // 1) Summary autoritativa del backend para KPIs principales.
      const summary = await this.api.dashboard(this.fromDate, this.toDate);
      // 2) Lista paginada para tabla + equity curve.
      const page = await this.api.list(1, 100);
      this.summary.set(summary);
      this.items.set(page.items);
    } catch (e) {
      this.error.set(this.toMessage(e));
      this.items.set([]);
      this.summary.set(null);
    } finally {
      this.refreshing.set(false);
      this.loading.set(false);
    }
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
