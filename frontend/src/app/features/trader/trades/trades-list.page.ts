import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe, DatePipe, NgClass } from '@angular/common';
import { TradeApiService, TradeDto, TradeStatus } from '@core/api/trade-api.service';

type DirectionFilter = 'All' | 'Long' | 'Short';
type StatusFilter = 'All' | TradeStatus;

@Component({
  selector: 'jcs-trades-list',
  standalone: true,
  imports: [DecimalPipe, DatePipe, NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="trades-page">
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
      <header class="page-head">
        <div>
          <p class="jcs-muted page-eyebrow">Historial completo</p>
          <h1 class="page-title">Operaciones</h1>
        </div>
        <div class="page-actions">
          <span class="count-badge jcs-num">{{ filteredCount() }} / {{ totalCount() }} trades</span>
          <button class="jcs-btn jcs-btn--primary" (click)="reload()" [disabled]="refreshing()">
            <span class="reload-dot" [class.spin]="refreshing()"></span>
            Actualizar
          </button>
        </div>
      </header>

      <!-- ============== Error banner ============== -->
      @if (error()) {
        <div class="trades-error" role="alert">
          <span>No se pudo cargar el listado: {{ error() }}</span>
          <button type="button" class="trades-error-retry" (click)="reload()">Reintentar</button>
        </div>
      }

      <!-- ============== KPIs ============== -->
      <section class="kpi-row">
        <article class="kpi kpi--strong" style="animation-delay: 0.05s">
          <span class="kpi-label">P&amp;L neto</span>
          <span class="kpi-value jcs-num" [ngClass]="periodPnL() >= 0 ? 'jcs-pos' : 'jcs-neg'">
            {{ periodPnL() >= 0 ? '+' : '' }}{{ periodPnL() | number:'1.2-2' }}
          </span>
          <span class="kpi-foot jcs-muted">{{ pnlCurrency() }} · período filtrado</span>
        </article>
        <article class="kpi" style="animation-delay: 0.1s">
          <span class="kpi-label">Win rate</span>
          <span class="kpi-value jcs-num jcs-pos">{{ winRate() | number:'1.0-1' }}%</span>
          <span class="kpi-foot jcs-muted">{{ winsCount() }} wins / {{ closedCount() }} cerradas</span>
        </article>
        <article class="kpi" style="animation-delay: 0.15s">
          <span class="kpi-label">Avg win</span>
          <span class="kpi-value jcs-num jcs-pos">
            {{ winsCount() === 0 ? '—' : ('+' + (avgWin() | number:'1.2-2')) }}
          </span>
          <span class="kpi-foot jcs-muted">Promedio por trade ganador</span>
        </article>
        <article class="kpi" style="animation-delay: 0.2s">
          <span class="kpi-label">Avg loss</span>
          <span class="kpi-value jcs-num" [ngClass]="lossesCount() === 0 ? '' : 'jcs-neg'">
            {{ lossesCount() === 0 ? '—' : ((avgLoss() | number:'1.2-2')) }}
          </span>
          <span class="kpi-foot jcs-muted">Promedio por trade perdedor</span>
        </article>
      </section>

      <!-- ============== Filters card ============== -->
      <section class="jcs-card filters-card" style="animation: fade-up 0.5s 0.25s ease-out both">
        <div class="filters-row">
          <div class="search-wrap">
            <span class="search-icon" aria-hidden="true">
              <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <circle cx="11" cy="11" r="8"/>
                <line x1="21" y1="21" x2="16.65" y2="16.65"/>
              </svg>
            </span>
            <input
              class="jcs-input search-input"
              type="text"
              placeholder="Buscar por símbolo (ej. EUR, XAU)…"
              [value]="search()"
              (input)="setSearch($any($event.target).value)"
              aria-label="Buscar operación" />
          </div>

          <div class="dir-toggle" role="tablist" aria-label="Dirección">
            <button
              class="dir-pill"
              role="tab"
              [attr.aria-selected]="directionFilter() === 'All'"
              [ngClass]="directionFilter() === 'All' ? 'dir-pill--active' : ''"
              (click)="setDirection('All')">Todos</button>
            <button
              class="dir-pill"
              role="tab"
              [attr.aria-selected]="directionFilter() === 'Long'"
              [ngClass]="directionFilter() === 'Long' ? 'dir-pill--active' : ''"
              (click)="setDirection('Long')">
              <span class="dot dot--long" aria-hidden="true"></span> Long
            </button>
            <button
              class="dir-pill"
              role="tab"
              [attr.aria-selected]="directionFilter() === 'Short'"
              [ngClass]="directionFilter() === 'Short' ? 'dir-pill--active' : ''"
              (click)="setDirection('Short')">
              <span class="dot dot--short" aria-hidden="true"></span> Short
            </button>
          </div>

          <div class="dir-toggle" role="tablist" aria-label="Estado">
            <button
              class="dir-pill"
              role="tab"
              [attr.aria-selected]="statusFilter() === 'All'"
              [ngClass]="statusFilter() === 'All' ? 'dir-pill--active' : ''"
              (click)="setStatus('All')">Todos</button>
            <button
              class="dir-pill"
              role="tab"
              [attr.aria-selected]="statusFilter() === 1"
              [ngClass]="statusFilter() === 1 ? 'dir-pill--active' : ''"
              (click)="setStatus(1)">Abiertas</button>
            <button
              class="dir-pill"
              role="tab"
              [attr.aria-selected]="statusFilter() === 2"
              [ngClass]="statusFilter() === 2 ? 'dir-pill--active' : ''"
              (click)="setStatus(2)">Cerradas</button>
            <button
              class="dir-pill"
              role="tab"
              [attr.aria-selected]="statusFilter() === 3"
              [ngClass]="statusFilter() === 3 ? 'dir-pill--active' : ''"
              (click)="setStatus(3)">Canceladas</button>
          </div>
        </div>

        <div class="symbol-pills" role="tablist" aria-label="Símbolo">
          <button
            class="sym-pill"
            role="tab"
            [attr.aria-selected]="symbolFilter() === null"
            [ngClass]="symbolFilter() === null ? 'sym-pill--active' : ''"
            (click)="setSymbol(null)">
            Todos <span class="sym-count jcs-num">{{ totalCount() }}</span>
          </button>
          @for (s of symbols(); track s) {
            <button
              class="sym-pill"
              role="tab"
              [attr.aria-selected]="symbolFilter() === s"
              [ngClass]="symbolFilter() === s ? 'sym-pill--active' : ''"
              (click)="setSymbol(s)">
              {{ s }}
            </button>
          }
        </div>
      </section>

      <!-- ============== Trades table ============== -->
      <section class="jcs-card table-card" style="animation: fade-up 0.5s 0.3s ease-out both">
        <header class="table-head">
          <div>
            <h2 class="table-title">Listado de trades</h2>
            <p class="jcs-muted table-sub">
              {{ filteredCount() }} operaciones coinciden con los filtros activos en esta página
            </p>
          </div>
          @if (hasActiveFilters()) {
            <button class="jcs-btn jcs-btn--ghost jcs-btn--sm" (click)="clearFilters()">
              Limpiar filtros
            </button>
          }
        </header>

        <div class="table-wrap">
          <table class="trades">
            <thead>
              <tr>
                <th>Fecha</th>
                <th>Símbolo</th>
                <th>Sentido</th>
                <th>Estado</th>
                <th class="num">Vol</th>
                <th class="num">Entrada</th>
                <th class="num">Salida</th>
                <th class="num">P&amp;L</th>
              </tr>
            </thead>
            <tbody>
              @for (t of displayed(); track t.id) {
                <tr>
                  <td class="jcs-num">{{ t.openedAt | date:'shortDate' }}</td>
                  <td class="symbol">{{ t.symbol }}</td>
                  <td>
                    <span class="jcs-badge" [ngClass]="t.direction === 1 ? 'dir-long' : 'dir-short'">
                      {{ t.direction === 1 ? '↑ Long' : '↓ Short' }}
                    </span>
                  </td>
                  <td>
                    <span class="jcs-badge"
                      [ngClass]="t.status === 1 ? 'status-open' : (t.status === 2 ? 'status-closed' : 'status-cancelled')">
                      @switch (t.status) {
                        @case (1) { ● Abierta }
                        @case (2) { ✓ Cerrada }
                        @case (3) { ⊘ Cancelada }
                      }
                    </span>
                  </td>
                  <td class="num jcs-num">{{ t.volume | number:'1.2-5' }}</td>
                  <td class="num jcs-num">{{ t.entryPrice | number:'1.2-5' }}</td>
                  <td class="num jcs-num">{{ t.exitPrice === null ? '—' : (t.exitPrice | number:'1.2-5') }}</td>
                  <td class="num jcs-num" [ngClass]="t.pnl === null ? 'jcs-soft' : (t.pnl >= 0 ? 'jcs-pos' : 'jcs-neg')">
                    @if (t.pnl === null) {
                      —
                    } @else {
                      {{ t.pnl >= 0 ? '+' : '' }}{{ t.pnl | number:'1.2-2' }}
                    }
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="8" class="empty">
                    @if (loading()) {
                      Cargando operaciones…
                    } @else {
                      No hay operaciones que coincidan con los filtros activos.
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <!-- ============== Pagination ============== -->
        @if (totalCount() > 0) {
          <footer class="pagination">
            <span class="page-info jcs-muted jcs-num">
              Mostrando {{ range().start }}–{{ range().end }} de {{ range().total }}
            </span>
            <div class="page-controls">
              <button
                class="page-btn"
                [disabled]="currentPage() === 1"
                (click)="prevPage()"
                aria-label="Página anterior">
                <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <polyline points="15 18 9 12 15 6"/>
                </svg>
              </button>
              @for (p of pageNumbers(); track p) {
                <button
                  class="page-num jcs-num"
                  [ngClass]="p === currentPage() ? 'page-num--active' : ''"
                  (click)="gotoPage(p)">
                  {{ p }}
                </button>
              }
              <button
                class="page-btn"
                [disabled]="currentPage() === totalPages()"
                (click)="nextPage()"
                aria-label="Página siguiente">
                <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <polyline points="9 18 15 12 9 6"/>
                </svg>
              </button>
            </div>
          </footer>
        }
      </section>
    </div>
  `,
  styles: [`
    :host { display: block; }

    .trades-page {
      display: flex;
      flex-direction: column;
      gap: var(--sp-6);
      animation: fade-up 0.4s ease-out;
    }

    .trades-error {
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
    .trades-error-retry {
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
    .trades-error-retry:hover { background: rgba(255, 64, 87, 0.15); }

    /* ============== Ticker tape ============== */
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

    /* ============== Header ============== */
    .page-head {
      display: flex;
      align-items: end;
      justify-content: space-between;
      gap: var(--sp-4);
      flex-wrap: wrap;
    }
    .page-eyebrow { font-size: var(--fs-sm); margin: 0 0 var(--sp-1); font-family: var(--font-mono); }
    .page-title { font-size: var(--fs-3xl); font-weight: 700; letter-spacing: -0.03em; margin: 0; }
    .page-actions { display: flex; gap: var(--sp-3); align-items: center; }
    .count-badge {
      font-size: var(--fs-xs);
      font-family: var(--font-mono);
      color: var(--text-secondary);
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

    /* ============== KPIs ============== */
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

    /* ============== Filters card ============== */
    .filters-card {
      padding: var(--sp-5) var(--sp-6);
      display: flex;
      flex-direction: column;
      gap: var(--sp-4);
    }
    .filters-row {
      display: flex;
      gap: var(--sp-3);
      align-items: center;
      flex-wrap: wrap;
    }
    .search-wrap {
      position: relative;
      flex: 1;
      min-width: 220px;
      max-width: 360px;
    }
    .search-icon {
      position: absolute;
      left: var(--sp-3);
      top: 50%;
      transform: translateY(-50%);
      color: var(--text-muted);
      pointer-events: none;
      display: inline-flex;
    }
    .search-input { padding-left: calc(var(--sp-3) * 2 + 16px); }

    .dir-toggle {
      display: inline-flex;
      gap: var(--sp-1);
      background: var(--bg-card-soft);
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-sm);
      padding: var(--sp-1);
    }
    .dir-pill {
      display: inline-flex;
      align-items: center;
      gap: var(--sp-2);
      padding: var(--sp-2) var(--sp-3);
      background: transparent;
      border: 1px solid transparent;
      border-radius: var(--radius-sm);
      color: var(--text-secondary);
      font: inherit;
      font-size: var(--fs-xs);
      font-weight: 600;
      cursor: pointer;
      transition: background 150ms, color 150ms, border-color 150ms;
    }
    .dir-pill:hover { color: var(--text-main); }
    .dir-pill--active {
      background: var(--bg-elevated);
      color: var(--text-main);
      border-color: var(--border-active);
    }
    .dir-toggle .dot {
      width: 6px; height: 6px;
      border-radius: 50%;
    }
    .dir-toggle .dot--long { background: var(--green); }
    .dir-toggle .dot--short { background: var(--red); }

    .symbol-pills {
      display: flex;
      flex-wrap: wrap;
      gap: var(--sp-2);
    }
    .sym-pill {
      display: inline-flex;
      align-items: center;
      gap: var(--sp-2);
      padding: var(--sp-1) var(--sp-3);
      background: var(--bg-card-soft);
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-pill, 9999px);
      color: var(--text-secondary);
      font: inherit;
      font-size: var(--fs-xs);
      font-weight: 600;
      cursor: pointer;
      transition: all 150ms;
    }
    .sym-pill:hover {
      border-color: var(--border-active);
      color: var(--text-main);
    }
    .sym-pill--active {
      background: var(--green-soft);
      border-color: var(--border-active);
      color: var(--green);
    }
    .sym-count {
      font-size: 0.65rem;
      padding: 0 var(--sp-2);
      background: var(--bg-elevated);
      color: var(--text-muted);
      border-radius: 999px;
    }

    /* ============== Table card ============== */
    .table-card {
      padding: var(--sp-5) var(--sp-6);
    }
    .table-head {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: var(--sp-3);
      margin-bottom: var(--sp-4);
      flex-wrap: wrap;
    }
    .table-title { font-size: var(--fs-lg); margin: 0 0 var(--sp-1); }
    .table-sub { font-size: var(--fs-sm); margin: 0; }
    .table-wrap { overflow-x: auto; }
    .trades { width: 100%; border-collapse: collapse; min-width: 760px; }
    .trades th, .trades td {
      padding: var(--sp-3) var(--sp-3);
      text-align: left;
      border-bottom: 1px solid var(--border-soft);
      font-size: var(--fs-sm);
      white-space: nowrap;
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
    .trades .empty {
      text-align: center;
      color: var(--text-muted);
      padding: var(--sp-8) var(--sp-4);
      font-size: var(--fs-sm);
    }

    /* Direction badges. */
    .jcs-badge.dir-long {
      background: rgba(47, 219, 120, 0.15);
      color: var(--green);
    }
    .jcs-badge.dir-short {
      background: rgba(255, 64, 87, 0.12);
      color: var(--red);
    }
    /* Status badges. */
    .jcs-badge.status-open {
      background: rgba(74, 168, 255, 0.12);
      color: var(--blue);
    }
    .jcs-badge.status-closed {
      background: var(--bg-elevated);
      color: var(--text-muted);
    }
    .jcs-badge.status-cancelled {
      background: rgba(255, 64, 87, 0.10);
      color: var(--red);
    }

    /* ============== Pagination ============== */
    .pagination {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--sp-4);
      padding-top: var(--sp-4);
      margin-top: var(--sp-2);
      border-top: 1px solid var(--border-soft);
      flex-wrap: wrap;
    }
    .page-info { font-size: var(--fs-xs); }
    .page-controls {
      display: flex;
      gap: var(--sp-1);
      align-items: center;
    }
    .page-btn, .page-num {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      min-width: 32px;
      height: 32px;
      padding: 0 var(--sp-2);
      background: var(--bg-card-soft);
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-sm);
      color: var(--text-secondary);
      font: inherit;
      font-size: var(--fs-xs);
      font-weight: 600;
      cursor: pointer;
      transition: all 150ms;
    }
    .page-btn:hover:not(:disabled), .page-num:hover {
      border-color: var(--border-active);
      color: var(--text-main);
    }
    .page-btn:disabled { opacity: 0.4; cursor: not-allowed; }
    .page-num--active {
      background: var(--green-soft);
      border-color: var(--border-active);
      color: var(--green);
    }

    @keyframes fade-up {
      from { opacity: 0; transform: translateY(8px); }
      to   { opacity: 1; transform: translateY(0); }
    }
  `],
})
export class TradesListPage {
  private readonly api = inject(TradeApiService);

  readonly refreshing = signal(false);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly items = signal<TradeDto[]>([]);
  readonly total = signal(0);

  readonly search = signal('');
  readonly symbolFilter = signal<string | null>(null);
  readonly directionFilter = signal<DirectionFilter>('All');
  readonly statusFilter = signal<StatusFilter>('All');
  readonly page = signal(1);
  readonly pageSize = 10;

  // Catálogo de símbolos en la página actual — alimenta los pills.
  readonly symbols = computed(() => {
    const set = new Set<string>();
    this.items().forEach(t => set.add(t.symbol));
    return Array.from(set).sort();
  });

  // Filtros client-side sobre la página actual del backend.
  readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const dir = this.directionFilter();
    return this.items().filter(t => {
      if (dir !== 'All' && t.direction !== (dir === 'Long' ? 1 : 2)) return false;
      if (q && !t.symbol.toLowerCase().includes(q)) return false;
      return true;
    });
  });

  readonly totalCount = computed(() => this.total());
  readonly filteredCount = computed(() => this.filtered().length);

  // Tabla = lo que sobrevivió a los filtros client-side (server ya paginó).
  readonly displayed = computed(() => this.filtered());

  readonly closedCount = computed(() => this.filtered().filter(t => t.status === 2).length);
  readonly winsCount = computed(() => this.filtered().filter(t => t.status === 2 && (t.pnl ?? 0) > 0).length);
  readonly lossesCount = computed(() => this.filtered().filter(t => t.status === 2 && (t.pnl ?? 0) < 0).length);

  readonly winRate = computed(() => {
    const c = this.closedCount();
    return c === 0 ? 0 : (this.winsCount() / c) * 100;
  });

  readonly periodPnL = computed(() => this.filtered().reduce((acc, t) => acc + (t.pnl ?? 0), 0));
  readonly pnlCurrency = computed(() => this.items()[0]?.pnlCurrency ?? this.items()[0]?.accountCurrency ?? 'USD');

  readonly avgWin = computed(() => {
    const n = this.winsCount();
    if (n === 0) return 0;
    const wins = this.filtered().filter(t => t.status === 2 && (t.pnl ?? 0) > 0);
    return wins.reduce((acc, t) => acc + (t.pnl ?? 0), 0) / n;
  });

  readonly avgLoss = computed(() => {
    const n = this.lossesCount();
    if (n === 0) return 0;
    const losses = this.filtered().filter(t => t.status === 2 && (t.pnl ?? 0) < 0);
    return losses.reduce((acc, t) => acc + (t.pnl ?? 0), 0) / n;
  });

  // Paginación server-side: totalPages en base al total del backend.
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize)));

  readonly currentPage = computed(() => {
    const total = this.totalPages();
    return Math.min(Math.max(1, this.page()), total);
  });

  readonly range = computed(() => {
    const total = this.total();
    if (total === 0) return { start: 0, end: 0, total: 0 };
    const p = this.currentPage();
    const start = (p - 1) * this.pageSize + 1;
    const end = Math.min(p * this.pageSize, total);
    return { start, end, total };
  });

  readonly pageNumbers = computed(() => {
    const total = this.totalPages();
    return total === 0 ? [] : Array.from({ length: total }, (_, i) => i + 1);
  });

  readonly hasActiveFilters = computed(() =>
    this.search() !== '' || this.symbolFilter() !== null || this.directionFilter() !== 'All' || this.statusFilter() !== 'All'
  );

  constructor() {
    void this.reload();
  }

  setSearch(value: string): void {
    this.search.set(value);
    // search es client-side sobre la página actual, no requiere reload.
  }

  setSymbol(symbol: string | null): void {
    this.symbolFilter.set(symbol);
    this.page.set(1);
    void this.reload();
  }

  setDirection(dir: DirectionFilter): void {
    this.directionFilter.set(dir);
    // client-side filter, sin reload.
  }

  setStatus(status: StatusFilter): void {
    this.statusFilter.set(status);
    this.page.set(1);
    void this.reload();
  }

  clearFilters(): void {
    const hadServerFilter = this.symbolFilter() !== null || this.statusFilter() !== 'All';
    this.search.set('');
    this.symbolFilter.set(null);
    this.directionFilter.set('All');
    this.statusFilter.set('All');
    this.page.set(1);
    if (hadServerFilter) {
      void this.reload();
    }
  }

  gotoPage(p: number): void {
    this.page.set(p);
    void this.reload();
  }

  nextPage(): void {
    const next = Math.min(this.currentPage() + 1, this.totalPages());
    if (next !== this.currentPage()) {
      this.page.set(next);
      void this.reload();
    }
  }

  prevPage(): void {
    const prev = Math.max(this.currentPage() - 1, 1);
    if (prev !== this.currentPage()) {
      this.page.set(prev);
      void this.reload();
    }
  }

  async reload(): Promise<void> {
    if (this.refreshing()) return;
    this.refreshing.set(true);
    this.error.set(null);
    try {
      const status = this.statusFilter() === 'All' ? undefined : (this.statusFilter() as TradeStatus);
      const symbol = this.symbolFilter() ?? undefined;
      const resp = await this.api.list(this.page(), this.pageSize, status, symbol);
      this.items.set(resp.items);
      this.total.set(resp.total);
      // Si el backend devolvió una página fuera de rango, recargar al final.
      if (resp.page !== this.page()) {
        this.page.set(resp.page);
      }
    } catch (e) {
      this.error.set(this.toMessage(e));
      this.items.set([]);
      this.total.set(0);
    } finally {
      this.refreshing.set(false);
      this.loading.set(false);
    }
  }

  private toMessage(e: unknown): string {
    if (e instanceof Error && e.message) return e.message;
    if (typeof e === 'string') return e;
    return 'Error inesperado.';
  }
}
