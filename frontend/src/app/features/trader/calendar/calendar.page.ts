import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe, NgClass } from '@angular/common';
import {
  CalendarDayDto,
  CalendarDto,
  TradeApiService,
} from '@core/api/trade-api.service';

const MONTH_NAMES = [
  'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
  'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
];
const WEEKDAY_NAMES = ['L', 'M', 'X', 'J', 'V', 'S', 'D'];

interface DayCell {
  day: number | null; // null = empty padding
  date: string | null;
  pnl: number;
  tradeCount: number;
}

@Component({
  selector: 'jcs-calendar',
  standalone: true,
  imports: [DecimalPipe, NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="cal-page">
      <!-- ============== Header ============== -->
      <header class="cal-head">
        <div>
          <p class="jcs-muted cal-eyebrow">Heatmap mensual</p>
          <h1 class="cal-title">Calendario de P&amp;L</h1>
          <p class="jcs-muted cal-sub">Días verdes = ganancia · días rojos = pérdida · escala por intensidad</p>
        </div>
        <div class="cal-actions">
          <button class="cal-nav" (click)="prevMonth()" aria-label="Mes anterior" [disabled]="loading()">‹</button>
          <span class="cal-month">{{ monthLabel() }}</span>
          <button class="cal-nav" (click)="nextMonth()" aria-label="Mes siguiente" [disabled]="loading()">›</button>
          <button class="jcs-btn jcs-btn--ghost jcs-btn--sm" (click)="goCurrent()" [disabled]="loading()">Hoy</button>
        </div>
      </header>

      <!-- ============== Error banner ============== -->
      @if (error()) {
        <div class="cal-error" role="alert">
          <span>No se pudo cargar el calendario: {{ error() }}</span>
          <button type="button" class="cal-error-retry" (click)="reload()">Reintentar</button>
        </div>
      }

      <!-- ============== Summary ============== -->
      <section class="cal-summary">
        <article class="kpi" style="animation-delay: 0.05s">
          <span class="kpi-label">P&amp;L del mes</span>
          <span class="kpi-value jcs-num" [ngClass]="monthPnl() >= 0 ? 'jcs-pos' : 'jcs-neg'">
            {{ monthPnl() >= 0 ? '+' : '' }}{{ monthPnl() | number:'1.2-2' }}
          </span>
          <span class="kpi-foot jcs-muted">{{ pnlCurrency() }}</span>
        </article>
        <article class="kpi" style="animation-delay: 0.10s">
          <span class="kpi-label">Días con profit</span>
          <span class="kpi-value jcs-num jcs-pos">{{ greenDays() }}</span>
          <span class="kpi-foot jcs-muted">de {{ activeDays() }} días operados</span>
        </article>
        <article class="kpi" style="animation-delay: 0.15s">
          <span class="kpi-label">Días con loss</span>
          <span class="kpi-value jcs-num jcs-neg">{{ redDays() }}</span>
          <span class="kpi-foot jcs-muted">peor día: {{ worstDayPnl() | number:'1.2-2' }}</span>
        </article>
        <article class="kpi" style="animation-delay: 0.20s">
          <span class="kpi-label">Operaciones</span>
          <span class="kpi-value jcs-num">{{ totalTrades() }}</span>
          <span class="kpi-foot jcs-muted">total del mes</span>
        </article>
      </section>

      <!-- ============== Calendar grid ============== -->
      <section class="jcs-card cal-card">
        <div class="cal-grid">
          @for (wd of weekdayNames; track wd) {
            <div class="cal-weekday jcs-muted">{{ wd }}</div>
          }
          @for (cell of grid(); track $index) {
            @if (cell.day === null) {
              <div class="cal-cell cal-cell--empty"></div>
            } @else {
              <div
                class="cal-cell"
                [ngClass]="cellIntensityClass(cell.pnl, cell.tradeCount)"
                [title]="cellTooltip(cell)">
                <span class="cal-day">{{ cell.day }}</span>
                @if (cell.tradeCount > 0) {
                  <span class="cal-pnl jcs-num"
                    [ngClass]="cell.pnl > 0 ? 'jcs-pos' : cell.pnl < 0 ? 'jcs-neg' : ''">
                    {{ cell.pnl > 0 ? '+' : '' }}{{ cell.pnl | number:'1.2-2' }}
                  </span>
                  <span class="cal-count jcs-muted">{{ cell.tradeCount }} ops</span>
                }
              </div>
            }
          }
        </div>
        <footer class="cal-legend">
          <span class="jcs-muted">Intensidad:</span>
          <span class="legend-cell legend-cell--pos-3"></span>
          <span class="legend-cell legend-cell--pos-2"></span>
          <span class="legend-cell legend-cell--pos-1"></span>
          <span class="legend-cell legend-cell--neutral"></span>
          <span class="legend-cell legend-cell--neg-1"></span>
          <span class="legend-cell legend-cell--neg-2"></span>
          <span class="legend-cell legend-cell--neg-3"></span>
        </footer>
      </section>
    </div>
  `,
  styles: [`
    :host { display: block; }

    .cal-page {
      display: flex;
      flex-direction: column;
      gap: var(--sp-6);
      animation: fade-up 0.4s ease-out;
    }

    .cal-error {
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
    .cal-error-retry {
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
    .cal-error-retry:hover { background: rgba(255, 64, 87, 0.15); }

    .cal-head {
      display: flex;
      align-items: end;
      justify-content: space-between;
      gap: var(--sp-4);
      flex-wrap: wrap;
    }
    .cal-eyebrow { font-size: var(--fs-sm); margin: 0 0 var(--sp-1); font-family: var(--font-mono); }
    .cal-title { font-size: var(--fs-3xl); font-weight: 700; letter-spacing: -0.03em; margin: 0; }
    .cal-sub { font-size: var(--fs-sm); margin: var(--sp-2) 0 0; }

    .cal-actions { display: flex; align-items: center; gap: var(--sp-2); }
    .cal-nav {
      width: 32px;
      height: 32px;
      background: var(--bg-card);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      color: var(--text-main);
      font-size: var(--fs-lg);
      font-weight: 700;
      cursor: pointer;
      transition: all 150ms;
    }
    .cal-nav:hover:not(:disabled) {
      border-color: var(--border-active);
      background: var(--bg-hover);
    }
    .cal-nav:disabled { opacity: 0.4; cursor: not-allowed; }
    .cal-month {
      min-width: 160px;
      text-align: center;
      font-size: var(--fs-base);
      font-weight: 600;
      padding: var(--sp-2) var(--sp-3);
      background: var(--bg-card);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      font-family: var(--font-mono);
    }

    /* KPIs */
    .cal-summary {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: var(--sp-4);
    }
    @media (max-width: 960px) { .cal-summary { grid-template-columns: repeat(2, 1fr); } }
    @media (max-width: 540px) { .cal-summary { grid-template-columns: 1fr; } }
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

    /* Grid */
    .cal-card { padding: var(--sp-5) var(--sp-6); }
    .cal-grid {
      display: grid;
      grid-template-columns: repeat(7, 1fr);
      gap: var(--sp-2);
    }
    .cal-weekday {
      text-align: center;
      font-size: var(--fs-xs);
      font-weight: 600;
      padding: var(--sp-1) 0;
      text-transform: uppercase;
      letter-spacing: 0.06em;
    }
    .cal-cell {
      position: relative;
      min-height: 90px;
      padding: var(--sp-2);
      background: var(--bg-card-soft);
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-sm);
      display: flex;
      flex-direction: column;
      gap: 2px;
      transition: transform 150ms, border-color 150ms;
      cursor: default;
    }
    .cal-cell:hover {
      transform: translateY(-1px);
      border-color: var(--border-active);
    }
    .cal-cell--empty {
      background: transparent;
      border-color: transparent;
    }
    .cal-day {
      font-size: var(--fs-xs);
      color: var(--text-muted);
      font-family: var(--font-mono);
      font-weight: 600;
    }
    .cal-pnl {
      font-size: var(--fs-sm);
      font-weight: 700;
      letter-spacing: -0.01em;
    }
    .cal-count {
      font-size: 0.65rem;
      margin-top: auto;
    }

    /* Intensity buckets. */
    .cal-cell--neutral {
      background: var(--bg-card-soft);
      border-color: var(--border-soft);
    }
    .cal-cell--pos-1 { background: rgba(47, 219, 120, 0.10); border-color: rgba(47, 219, 120, 0.25); }
    .cal-cell--pos-2 { background: rgba(47, 219, 120, 0.22); border-color: rgba(47, 219, 120, 0.45); }
    .cal-cell--pos-3 { background: rgba(47, 219, 120, 0.38); border-color: rgba(47, 219, 120, 0.65); }
    .cal-cell--neg-1 { background: rgba(255, 64, 87, 0.10); border-color: rgba(255, 64, 87, 0.25); }
    .cal-cell--neg-2 { background: rgba(255, 64, 87, 0.22); border-color: rgba(255, 64, 87, 0.45); }
    .cal-cell--neg-3 { background: rgba(255, 64, 87, 0.38); border-color: rgba(255, 64, 87, 0.65); }

    .cal-legend {
      display: flex;
      align-items: center;
      gap: var(--sp-2);
      margin-top: var(--sp-5);
      padding-top: var(--sp-4);
      border-top: 1px solid var(--border-soft);
      font-size: var(--fs-xs);
      flex-wrap: wrap;
    }
    .legend-cell {
      width: 24px;
      height: 14px;
      border-radius: 3px;
      border: 1px solid var(--border-soft);
    }
    .legend-cell--neutral { background: var(--bg-card-soft); }
    .legend-cell--pos-1   { background: rgba(47, 219, 120, 0.10); }
    .legend-cell--pos-2   { background: rgba(47, 219, 120, 0.22); }
    .legend-cell--pos-3   { background: rgba(47, 219, 120, 0.38); }
    .legend-cell--neg-1   { background: rgba(255, 64, 87, 0.10); }
    .legend-cell--neg-2   { background: rgba(255, 64, 87, 0.22); }
    .legend-cell--neg-3   { background: rgba(255, 64, 87, 0.38); }

    @keyframes fade-up {
      from { opacity: 0; transform: translateY(8px); }
      to   { opacity: 1; transform: translateY(0); }
    }
  `],
})
export class CalendarPage {
  private readonly api = inject(TradeApiService);

  readonly weekdayNames = WEEKDAY_NAMES;

  readonly year = signal(new Date().getFullYear());
  readonly month = signal(new Date().getMonth() + 1); // 1-12

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly days = signal<CalendarDayDto[]>([]);
  readonly pnlCurrency = signal<string>('USD');

  readonly monthLabel = computed(() => `${MONTH_NAMES[this.month() - 1]} ${this.year()}`);

  // Mapa para lookup rápido por día del mes.
  private readonly dayByNum = computed<Map<number, CalendarDayDto>>(() => {
    const m = new Map<number, CalendarDayDto>();
    for (const d of this.days()) {
      const day = Number(d.date.slice(8, 10));
      m.set(day, d);
    }
    return m;
  });

  // Grid 7×N con offset lunes=0.
  readonly grid = computed<DayCell[]>(() => {
    const y = this.year();
    const m = this.month();
    const firstDow = this.firstDayOfWeek(y, m); // 0=Mon
    const totalDays = this.daysInMonth(y, m);
    const lookup = this.dayByNum();

    const cells: DayCell[] = [];
    for (let i = 0; i < firstDow; i++) {
      cells.push({ day: null, date: null, pnl: 0, tradeCount: 0 });
    }
    for (let d = 1; d <= totalDays; d++) {
      const hit = lookup.get(d);
      cells.push({
        day: d,
        date: hit?.date ?? null,
        pnl: hit?.pnl ?? 0,
        tradeCount: hit?.tradeCount ?? 0,
      });
    }
    // Padding al final para completar la última semana.
    while (cells.length % 7 !== 0) {
      cells.push({ day: null, date: null, pnl: 0, tradeCount: 0 });
    }
    return cells;
  });

  readonly monthPnl = computed(() => this.days().reduce((acc, d) => acc + d.pnl, 0));
  readonly greenDays = computed(() => this.days().filter(d => d.pnl > 0).length);
  readonly redDays = computed(() => this.days().filter(d => d.pnl < 0).length);
  readonly activeDays = computed(() => this.days().filter(d => d.tradeCount > 0).length);
  readonly totalTrades = computed(() => this.days().reduce((acc, d) => acc + d.tradeCount, 0));
  readonly worstDayPnl = computed(() => {
    const losses = this.days().filter(d => d.pnl < 0).map(d => d.pnl);
    return losses.length === 0 ? 0 : Math.min(...losses);
  });

  constructor() {
    void this.reload();
  }

  prevMonth(): void {
    let m = this.month() - 1;
    let y = this.year();
    if (m < 1) { m = 12; y -= 1; }
    this.month.set(m);
    this.year.set(y);
    void this.reload();
  }

  nextMonth(): void {
    let m = this.month() + 1;
    let y = this.year();
    if (m > 12) { m = 1; y += 1; }
    this.month.set(m);
    this.year.set(y);
    void this.reload();
  }

  goCurrent(): void {
    const now = new Date();
    this.year.set(now.getFullYear());
    this.month.set(now.getMonth() + 1);
    void this.reload();
  }

  async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const resp: CalendarDto = await this.api.calendar(this.year(), this.month());
      this.days.set(resp.days);
      this.pnlCurrency.set('USD');
    } catch (e) {
      this.error.set(this.toMessage(e));
      this.days.set([]);
    } finally {
      this.loading.set(false);
    }
  }

  cellIntensityClass(pnl: number, trades: number): string {
    if (trades === 0) return 'cal-cell--neutral';
    const abs = Math.abs(pnl);
    const bucket = abs >= 300 ? 3 : abs >= 100 ? 2 : 1;
    return pnl > 0 ? `cal-cell--pos-${bucket}` : pnl < 0 ? `cal-cell--neg-${bucket}` : 'cal-cell--neutral';
  }

  cellTooltip(cell: DayCell): string {
    if (cell.tradeCount === 0) return `${cell.day}`;
    return `${cell.day} · ${cell.tradeCount} ops · P&L ${cell.pnl.toFixed(2)}`;
  }

  // Devuelve 0=Mon ... 6=Sun.
  private firstDayOfWeek(year: number, month1to12: number): number {
    const d = new Date(Date.UTC(year, month1to12 - 1, 1));
    const dow = d.getUTCDay(); // 0=Sun..6=Sat
    return (dow + 6) % 7; // shift a lunes=0
  }

  private daysInMonth(year: number, month1to12: number): number {
    return new Date(Date.UTC(year, month1to12, 0)).getUTCDate();
  }

  private toMessage(e: unknown): string {
    if (e instanceof Error && e.message) return e.message;
    if (typeof e === 'string') return e;
    return 'Error inesperado.';
  }
}
