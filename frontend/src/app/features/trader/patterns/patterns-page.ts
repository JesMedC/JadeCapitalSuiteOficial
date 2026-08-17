import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
} from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { PatternsState } from './state/patterns.state';
import {
  EMOTIONALITY_BUCKET_KEYS,
  EMOTIONALITY_BUCKET_LABELS,
  EmotionalityBucketDto,
  PERIODS,
  Period,
  Severity,
} from './api/patterns.types';

// ============================================================================
//  PatternsPage — slice 2b.2 frontend.
//
//  Mobile-first standalone page (Signals + OnPush + SCSS).
//
//  Layout (top → bottom):
//   - Header: title "Patrones conductuales" + period selector (7d/30d/90d/all).
//   - Section "Eventos detectados": one card per BehavioralEvent, severity
//     color (high=red, medium=yellow, low=blue). Each card has a
//     "Ver trade" link per tradeId → /app/trades/{id} (the trade detail
//     page from slice 1d.2 — the link resolves once that route is wired
//     into the trader shell).
//   - Section "P&L por emocionalidad": 3 buckets (low 1-2 / mid 3 /
//     high 4-5) with count, win-rate and total PnL rendered as 3 inline
//     bars sized by count.
//   - State: loading (spinner), error (red banner), empty (no events
//     AND all aggregations at zero → "Sin patrones detectados en este
//     período").
//
//  Aggregations bars normalize against the max count so the largest
//  bucket fills 100% of the bar width — this keeps the visual comparison
//  useful even when totals vary across periods.
// ============================================================================

@Component({
  selector: 'jcs-patterns-page',
  standalone: true,
  imports: [RouterLink, DatePipe, DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="pp-page">
      <!-- ============== Header ============== -->
      <header class="pp-head">
        <p class="jcs-muted pp-eyebrow">Análisis conductual</p>
        <h1 class="pp-title">Patrones conductuales</h1>
        <p class="jcs-muted pp-sub">
          @if (state.analysis(); as a) {
            {{ a.windowStart | date: 'shortDate' }} → {{ a.windowEnd | date: 'shortDate' }}
          } @else {
            Sin datos cargados todavía
          }
        </p>
      </header>

      <!-- ============== Period selector ============== -->
      <div class="pp-period-row" role="radiogroup" aria-label="Periodo de análisis">
        @for (p of PERIODS; track p) {
          <button
            type="button"
            class="pp-period"
            [class.pp-period--active]="state.period() === p"
            [attr.aria-pressed]="state.period() === p"
            (click)="onPeriodChange(p)"
            [attr.data-testid]="'period-' + p">
            {{ PERIOD_LABELS[p] }}
          </button>
        }
      </div>

      <!-- ============== Error banner ============== -->
      @if (state.error(); as err) {
        <div class="pp-error" role="alert" data-testid="patterns-error">
          <span>{{ err }}</span>
          <button type="button" class="pp-error-dismiss" (click)="state.clearError()">×</button>
        </div>
      }

      <!-- ============== Loading ============== -->
      @if (state.isLoading()) {
        <div class="pp-loading" aria-live="polite">
          <span class="pp-spinner" aria-hidden="true"></span>
          <span class="jcs-muted">Cargando patrones…</span>
        </div>
      }

      <!-- ============== Empty state ============== -->
      @if (!state.isLoading() && !state.error() && isEmpty()) {
        <div class="pp-empty" data-testid="patterns-empty">
          <p>Sin patrones detectados en este período.</p>
        </div>
      }

      <!-- ============== Events ============== -->
      @if (state.events().length > 0) {
        <section class="pp-section" data-testid="events-section">
          <h2 class="pp-section-title">Eventos detectados</h2>
          <ul class="pp-events">
            @for (e of state.events(); track e.ruleId + '-' + e.occurredAt; let i = $index) {
              <li
                class="pp-event"
                [class.pp-event--high]="e.severity === 'high'"
                [class.pp-event--medium]="e.severity === 'medium'"
                [class.pp-event--low]="e.severity === 'low'"
                data-testid="event-card">
                <header class="pp-event-head">
                  <span class="pp-event-rule">{{ ruleLabel(e.ruleId) }}</span>
                  <span class="pp-event-severity">{{ severityLabel(e.severity) }}</span>
                </header>
                <p class="pp-event-msg">{{ e.message }}</p>
                @if (e.tradeIds.length > 0) {
                  <ul class="pp-event-links">
                    @for (tid of e.tradeIds; track tid) {
                      <li>
                        <a [routerLink]="['/app/trades', tid]" class="pp-event-link">
                          Ver trade {{ shortId(tid) }}
                        </a>
                      </li>
                    }
                  </ul>
                }
                <span class="pp-event-when jcs-muted">{{ e.occurredAt | date: 'short' }}</span>
              </li>
            }
          </ul>
        </section>
      }

      <!-- ============== Aggregations ============== -->
      @if (buckets().length > 0) {
        <section class="pp-section" data-testid="aggregations-section">
          <h2 class="pp-section-title">P&L por emocionalidad</h2>
          <ul class="pp-buckets">
            @for (b of buckets(); track b.key) {
              <li class="pp-bucket" data-testid="bucket-card">
                <header class="pp-bucket-head">
                  <span class="pp-bucket-label">{{ b.label }}</span>
                  <span class="pp-bucket-count">{{ b.count }} ops</span>
                </header>
                <div class="pp-bucket-bar-track" aria-hidden="true">
                  <div
                    class="pp-bucket-bar"
                    [style.width.%]="b.barWidth"
                    [class.pp-bucket-bar--positive]="b.totalPnl > 0"
                    [class.pp-bucket-bar--negative]="b.totalPnl < 0">
                  </div>
                </div>
                <footer class="pp-bucket-stats">
                  <span class="pp-bucket-winrate">
                    Win-rate {{ (b.winRate * 100).toFixed(0) }}%
                  </span>
                  <span
                    class="pp-bucket-pnl"
                    [class.pp-bucket-pnl--positive]="b.totalPnl > 0"
                    [class.pp-bucket-pnl--negative]="b.totalPnl < 0">
                    {{ b.totalPnl | number: '1.2-2' }}
                  </span>
                </footer>
              </li>
            }
          </ul>
        </section>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }

    .pp-page {
      display: flex;
      flex-direction: column;
      gap: var(--sp-5);
      max-width: 760px;
      animation: pp-fade 0.4s ease-out;
    }
    @keyframes pp-fade {
      from { opacity: 0; transform: translateY(6px); }
      to   { opacity: 1; transform: translateY(0); }
    }

    .pp-head { display: flex; flex-direction: column; gap: 4px; }
    .pp-eyebrow { font-size: var(--fs-sm); margin: 0; font-family: var(--font-mono); }
    .pp-title { font-size: var(--fs-3xl); font-weight: 700; letter-spacing: -0.03em; margin: 0; }
    .pp-sub { font-size: var(--fs-sm); margin: var(--sp-1) 0 0; }

    /* ===== Period selector ===== */
    .pp-period-row {
      display: flex;
      gap: var(--sp-2);
      flex-wrap: wrap;
    }
    .pp-period {
      flex: 1 1 auto;
      min-height: 44px;
      padding: var(--sp-2) var(--sp-3);
      background: var(--bg-card-soft);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      color: var(--text-secondary);
      font: inherit;
      font-size: var(--fs-sm);
      font-weight: 500;
      cursor: pointer;
      transition: all 150ms ease;
    }
    .pp-period:hover { border-color: var(--border-active); color: var(--text-main); }
    .pp-period--active {
      background: var(--green-soft);
      border-color: var(--green);
      color: var(--green);
    }

    /* ===== Error banner ===== */
    .pp-error {
      display: flex; align-items: center; justify-content: space-between; gap: var(--sp-3);
      padding: var(--sp-3) var(--sp-4);
      background: rgba(255, 64, 87, 0.08);
      border: 1px solid rgba(255, 64, 87, 0.35);
      border-radius: var(--radius-md);
      color: var(--red);
      font-size: var(--fs-sm);
    }
    .pp-error-dismiss {
      background: transparent; border: 0; color: var(--red);
      font-size: 1.2rem; cursor: pointer; padding: 0 var(--sp-2);
    }

    /* ===== Loading ===== */
    .pp-loading {
      display: flex; align-items: center; gap: var(--sp-3);
      padding: var(--sp-3) 0; font-size: var(--fs-sm);
    }
    .pp-spinner {
      width: 16px; height: 16px;
      border: 2px solid var(--border);
      border-top-color: var(--green);
      border-radius: 50%;
      animation: pp-spin 0.8s linear infinite;
    }
    @keyframes pp-spin { to { transform: rotate(360deg); } }

    /* ===== Empty state ===== */
    .pp-empty {
      padding: var(--sp-4);
      background: var(--bg-card-soft);
      border: 1px dashed var(--border);
      border-radius: var(--radius-md);
      color: var(--text-secondary);
      font-size: var(--fs-base);
      text-align: center;
    }

    /* ===== Sections ===== */
    .pp-section {
      display: flex; flex-direction: column; gap: var(--sp-3);
    }
    .pp-section-title {
      font-size: var(--fs-base);
      font-weight: 600;
      margin: 0;
      color: var(--text-secondary);
    }

    /* ===== Event cards ===== */
    .pp-events {
      list-style: none; padding: 0; margin: 0;
      display: flex; flex-direction: column; gap: var(--sp-3);
    }
    .pp-event {
      position: relative;
      display: flex; flex-direction: column; gap: var(--sp-2);
      padding: var(--sp-3) var(--sp-4);
      background: var(--bg-card-soft);
      border: 1px solid var(--border);
      border-left-width: 4px;
      border-radius: var(--radius-md);
    }
    .pp-event--high { border-left-color: var(--red); }
    .pp-event--medium { border-left-color: #f7c948; }
    .pp-event--low { border-left-color: var(--blue); }

    .pp-event-head {
      display: flex; justify-content: space-between; align-items: center;
      gap: var(--sp-3);
    }
    .pp-event-rule {
      font-weight: 600;
      font-size: var(--fs-base);
      color: var(--text-main);
    }
    .pp-event-severity {
      font-size: var(--fs-xs);
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      padding: 2px var(--sp-2);
      border-radius: 9999px;
    }
    .pp-event--high .pp-event-severity {
      background: rgba(255, 64, 87, 0.15);
      color: var(--red);
    }
    .pp-event--medium .pp-event-severity {
      background: rgba(247, 201, 72, 0.18);
      color: #b8860b;
    }
    .pp-event--low .pp-event-severity {
      background: rgba(80, 156, 245, 0.18);
      color: var(--blue);
    }

    .pp-event-msg {
      font-size: var(--fs-sm);
      color: var(--text-secondary);
      margin: 0;
      line-height: 1.5;
    }
    .pp-event-links {
      list-style: none; padding: 0; margin: 0;
      display: flex; flex-wrap: wrap; gap: var(--sp-2);
    }
    .pp-event-link {
      font-size: var(--fs-xs);
      color: var(--green);
      text-decoration: none;
      padding: 2px var(--sp-2);
      border-radius: var(--radius-sm);
      border: 1px solid rgba(47, 219, 120, 0.35);
      transition: background 150ms;
    }
    .pp-event-link:hover {
      background: rgba(47, 219, 120, 0.12);
    }
    .pp-event-when {
      font-size: var(--fs-xs);
      font-family: var(--font-mono);
    }

    /* ===== Bucket bars ===== */
    .pp-buckets {
      list-style: none; padding: 0; margin: 0;
      display: flex; flex-direction: column; gap: var(--sp-3);
    }
    .pp-bucket {
      display: flex; flex-direction: column; gap: var(--sp-2);
      padding: var(--sp-3) var(--sp-4);
      background: var(--bg-card-soft);
      border: 1px solid var(--border);
      border-radius: var(--radius-md);
    }
    .pp-bucket-head {
      display: flex; justify-content: space-between; align-items: baseline;
      gap: var(--sp-3);
    }
    .pp-bucket-label {
      font-size: var(--fs-sm); font-weight: 600;
      color: var(--text-main);
    }
    .pp-bucket-count {
      font-size: var(--fs-xs);
      color: var(--text-muted);
      font-family: var(--font-mono);
    }
    .pp-bucket-bar-track {
      height: 8px;
      background: var(--bg-main);
      border-radius: 9999px;
      overflow: hidden;
    }
    .pp-bucket-bar {
      height: 100%;
      background: var(--green);
      transition: width 240ms ease-out;
    }
    .pp-bucket-bar--positive { background: var(--green); }
    .pp-bucket-bar--negative { background: var(--red); }

    .pp-bucket-stats {
      display: flex; justify-content: space-between; align-items: baseline;
      gap: var(--sp-3);
    }
    .pp-bucket-winrate {
      font-size: var(--fs-xs);
      color: var(--text-secondary);
    }
    .pp-bucket-pnl {
      font-size: var(--fs-sm);
      font-weight: 600;
      font-family: var(--font-mono);
    }
    .pp-bucket-pnl--positive { color: var(--green); }
    .pp-bucket-pnl--negative { color: var(--red); }
  `],
})
export class PatternsPage {
  readonly state = inject(PatternsState);

  readonly PERIODS = PERIODS;
  readonly PERIOD_LABELS: Record<Period, string> = {
    '7d': '7 días',
    '30d': '30 días',
    '90d': '90 días',
    'all': 'Todo',
  };

  /** Normalized bucket list with rendered bar width. */
  readonly buckets = computed(() => {
    const agg = this.state.aggregations();
    if (!agg) return [];
    const raw = EMOTIONALITY_BUCKET_KEYS.map(key => {
      const data: EmotionalityBucketDto =
        agg.byEmotionality[key] ?? { count: 0, winRate: 0, totalPnl: 0 };
      return {
        key,
        label: EMOTIONALITY_BUCKET_LABELS[key] ?? key,
        count: data.count,
        winRate: data.winRate,
        totalPnl: data.totalPnl,
        barWidth: 0,
      };
    });
    const maxCount = Math.max(1, ...raw.map(r => r.count));
    return raw.map(r => ({ ...r, barWidth: (r.count / maxCount) * 100 }));
  });

  /** True when there are no events AND every bucket is zero. */
  readonly isEmpty = computed(() => {
    if (this.state.events().length > 0) return false;
    const bs = this.buckets();
    return bs.every(b => b.count === 0);
  });

  ngOnInit(): void {
    void this.state.load(this.state.period());
  }

  // ===== Handlers =====

  onPeriodChange(period: Period): void {
    if (this.state.period() === period && this.state.analysis() !== null) return;
    void this.state.load(period);
  }

  ruleLabel(ruleId: string): string {
    return RULE_LABELS[ruleId] ?? ruleId;
  }

  severityLabel(s: Severity): string {
    return s.toUpperCase();
  }

  /** First 8 chars of a Guid — readable in tight rows. */
  shortId(id: string): string {
    return id.length >= 8 ? id.slice(0, 8) : id;
  }
}

const RULE_LABELS: Record<string, string> = {
  RevengeTrade: 'Revenge trading',
  OvertradingDay: 'Sobreoperativa',
  TiltSequence: 'Tilt',
  OverconfidenceAfterWin: 'Exceso de confianza',
};
