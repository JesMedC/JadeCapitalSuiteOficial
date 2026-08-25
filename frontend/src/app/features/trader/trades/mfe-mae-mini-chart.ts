import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

// ============================================================================
//  MfeMaeMiniChart — slice 2c (Trader Journal Core / MFE/MAE Charts).
//
//  Reusable presentational component that renders two horizontal bars
//  representing a single trade's MFE (Maximum Favorable Excursion,
//  green) and MAE (Maximum Adverse Excursion, red).
//
//  Inputs:
//    mfeAmount: number | null   — positive magnitude in account currency.
//    maeAmount: number | null   — negative magnitude (≤ 0) in account currency.
//    currency:  string          — ISO 4217-like 3-letter code (e.g. "USD").
//
//  Visual encoding:
//    - Both null → "—" placeholder.
//    - Otherwise, two inline bars (height 8px, flex row).
//    - Bar width is proportional to |amount| / max(|MFE|, |MAE|).
//      The larger magnitude fills the row; the smaller one renders
//      proportionally. This keeps the chart visually comparable across
//      trades with wildly different magnitudes.
//    - MFE always green (var(--green)); MAE always red (var(--red)).
//
//  Standalone, Signals, OnPush — same conventions as the rest of the
//  trader module (PositionSizeCalculator, PreTradeChecklist).
// ============================================================================

@Component({
  selector: 'jcs-mfe-mae-mini-chart',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isEmpty()) {
      <span class="mmc-empty" aria-label="Sin datos de MFE/MAE">—</span>
    } @else {
      <div class="mmc-row" role="img"
           [attr.aria-label]="ariaLabel()"
           [attr.title]="tooltip()">
        @if (hasMfe()) {
          <span
            class="mmc-bar mmc-bar--mfe mmc-bar--positive"
            [style.width.%]="mfeWidthPercent()"
            data-testid="mfe-bar">
          </span>
        }
        @if (hasMae()) {
          <span
            class="mmc-bar mmc-bar--mae mmc-bar--negative"
            [style.width.%]="maeWidthPercent()"
            data-testid="mae-bar">
          </span>
        }
      </div>
    }
  `,
  styles: [`
    :host {
      display: inline-flex;
      align-items: center;
      gap: var(--sp-2, 8px);
      min-width: 60px;
      font-family: var(--font-mono, ui-monospace, monospace);
      font-size: var(--fs-xs, 11px);
      color: var(--text-secondary);
    }

    .mmc-empty {
      color: var(--text-muted, #6b7280);
      font-size: var(--fs-sm, 13px);
    }

    .mmc-row {
      display: inline-flex;
      align-items: center;
      gap: 2px;
      height: 8px;
      flex: 1;
      min-width: 40px;
      max-width: 110px;
    }

    .mmc-bar {
      display: inline-block;
      height: 100%;
      border-radius: 2px;
      min-width: 2px;
      transition: width 180ms ease-out;
    }

    /* MFE → favorable excursion → green (matches the trades-list P&L winner color). */
    .mmc-bar--mfe {
      background-color: var(--green, #2fdb78);
    }

    /* MAE → adverse excursion → red (matches the trades-list P&L loser color). */
    .mmc-bar--mae {
      background-color: var(--red, #ff4057);
    }

    /* Visual contrast for the negative MAE bar so it doesn't disappear on
       a green-bullish theme when magnitudes are small. */
    .mmc-bar--negative {
      box-shadow: inset 0 0 0 1px rgba(255, 64, 87, 0.30);
    }
  `],
})
export class MfeMaeMiniChart {
  readonly mfeAmount = input<number | null>(null);
  readonly maeAmount = input<number | null>(null);
  readonly currency = input<string>('USD');

  // Both null → "—" placeholder (open trade, no MFE/MAE yet).
  readonly isEmpty = computed(() => this.mfeAmount() === null && this.maeAmount() === null);
  readonly hasMfe = computed(() => this.mfeAmount() !== null);
  readonly hasMae = computed(() => this.maeAmount() !== null);

  // Compute the maximum magnitude so the bars stay proportional across
  // the user's history. Defends against |MFE| = 0 or |MAE| = 0 (break-even
  // or winner with MAE=0): the bar collapses to its min-width and the
  // sibling takes the rest of the row.
  private readonly maxMagnitude = computed(() => {
    const mfe = Math.abs(this.mfeAmount() ?? 0);
    const mae = Math.abs(this.maeAmount() ?? 0);
    const max = Math.max(mfe, mae);
    return max > 0 ? max : 1; // avoid divide-by-zero; the side with 0 gets min-width.
  });

  readonly mfeWidthPercent = computed(() => {
    const mfe = this.mfeAmount();
    if (mfe === null) return 0;
    const mag = Math.abs(mfe);
    return Math.max(2, (mag / this.maxMagnitude()) * 100);
  });

  readonly maeWidthPercent = computed(() => {
    const mae = this.maeAmount();
    if (mae === null) return 0;
    const mag = Math.abs(mae);
    return Math.max(2, (mag / this.maxMagnitude()) * 100);
  });

  readonly tooltip = computed(() => {
    const parts: string[] = [];
    const mfe = this.mfeAmount();
    const mae = this.maeAmount();
    const ccy = this.currency();
    if (mfe !== null) parts.push(`MFE: +${mfe} ${ccy}`);
    if (mae !== null) parts.push(`MAE: ${mae} ${ccy}`);
    return parts.length > 0 ? parts.join(' · ') : 'MFE/MAE no disponible';
  });

  readonly ariaLabel = computed(() => {
    const mfe = this.mfeAmount();
    const mae = this.maeAmount();
    const ccy = this.currency();
    const parts: string[] = [];
    if (mfe !== null) parts.push(`Máxima excursión favorable ${mfe} ${ccy}`);
    if (mae !== null) parts.push(`Máxima excursión adversa ${mae} ${ccy}`);
    return parts.length > 0 ? parts.join(', ') : 'MFE y MAE no disponibles';
  });
}