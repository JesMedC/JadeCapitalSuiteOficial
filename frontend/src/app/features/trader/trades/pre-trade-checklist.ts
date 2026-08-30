import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { RiskProfileState } from '@core/state/risk-profile.state';

// ============================================================================
//  PreTradeChecklist — slice 1c.2 frontend.
//
//  Standalone Angular 19 component rendered above the "Crear operación" button
//  inside the New Trade slide-out panel. Captures the four signals required by
//  the backend's `PreTradeChecklistPayload` (emotionality, setupQuality, RR
//  at entry, RR target used, confluences count) and emits them via a single
//  `OutputEmitterRef` shaped exactly like the DTO the API expects.
//
//  - emotionality / setupQuality : 1..5 (mirrors the C# enums byte-backed).
//    Single-select pills, NOT free text. Labels are client-side strings mapped
//    from the underlying integer (System.Text.Json deserializes the API payload
//    as plain numbers — see TradeEndpoints.PreTradeChecklistPayload).
//  - confluencesCount            : 1..10 (range slider).
//  - riskRewardAtEntry           : ≥1.0 (defaults to 2.0; user-editable).
//  - riskRewardTargetUsed        : ≥1.0. Defaults to the active risk profile's
//    `riskRewardTarget` if any, otherwise 2.0. The "user touched" flag prevents
//    overwriting a value the trader has already typed when the profile loads
//    asynchronously.
//
//  The parent (create-trade-form) is the one calling POST /api/trades. On a
//  422 response it forwards `errorCode` (e.g. "pre_trade_checklist.rr_below_target")
//  and `fieldErrors` (e.g. ["riskRewardAtEntry"]) back into the inputs so the
//  banner + inline field error render without coupling this component to the
//  HTTP layer.
// ============================================================================

export interface PreTradeChecklistPayload {
  /** Emotionality byte (1=Fearful, 2=Anxious, 3=Neutral, 4=Confident, 5=Euphoric). */
  emotionality: number;
  /** SetupQuality byte (1=Poor, 2=BelowAverage, 3=Average, 4=Good, 5=Excellent). */
  setupQuality: number;
  /** Decimal RR at entry (must be ≥ target used). */
  riskRewardAtEntry: number;
  /** Decimal RR target the trade was sized against. */
  riskRewardTargetUsed: number;
  /** Confluences count 1..10. */
  confluencesCount: number;
}

const EMOTIONALITY_OPTIONS: ReadonlyArray<{ value: number; label: string; emoji: string }> = [
  { value: 1, label: 'Fearful', emoji: '😨' },
  { value: 2, label: 'Anxious', emoji: '😰' },
  { value: 3, label: 'Neutral', emoji: '😐' },
  { value: 4, label: 'Confident', emoji: '😎' },
  { value: 5, label: 'Euphoric', emoji: '🤩' },
];

const SETUP_QUALITY_OPTIONS: ReadonlyArray<{ value: number; label: string }> = [
  { value: 1, label: 'Poor' },
  { value: 2, label: 'BelowAvg' },
  { value: 3, label: 'Average' },
  { value: 4, label: 'Good' },
  { value: 5, label: 'Excellent' },
];

const DEFAULT_CONFLUENCES = 5;
const DEFAULT_RR = 2.0;

@Component({
  selector: 'jcs-pre-trade-checklist',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="jcs-card ptc-card" data-testid="pre-trade-checklist">
      <header class="ptc-head">
        <h3 class="ptc-title">Checklist pre-trade</h3>
        <p class="ptc-sub jcs-muted">
          Antes de abrir el trade, confirmá que estás operando bajo tu plan.
        </p>
      </header>

      @if (errorCode(); as code) {
        <div class="ptc-banner" role="alert" data-testid="checklist-banner">
          {{ bannerMessage(code) }}
        </div>
      }

      <fieldset class="ptc-section">
        <legend class="ptc-legend">Estado emocional</legend>
        <div class="ptc-pills" role="radiogroup" aria-label="Estado emocional">
          @for (opt of emotionalityOptions; track opt.value) {
            <button
              type="button"
              class="ptc-pill"
              role="radio"
              [class.ptc-pill--active]="selectedEmotionality() === opt.value"
              [attr.aria-checked]="selectedEmotionality() === opt.value"
              [attr.data-value]="opt.value"
              (click)="setEmotionality(opt.value)">
              <span class="ptc-pill-emoji" aria-hidden="true">{{ opt.emoji }}</span>
              <span class="ptc-pill-label">{{ opt.label }}</span>
            </button>
          }
        </div>
      </fieldset>

      <fieldset class="ptc-section">
        <legend class="ptc-legend">Calidad del setup</legend>
        <div class="ptc-pills" role="radiogroup" aria-label="Calidad del setup">
          @for (opt of setupQualityOptions; track opt.value) {
            <button
              type="button"
              class="ptc-pill"
              role="radio"
              [class.ptc-pill--active]="selectedSetupQuality() === opt.value"
              [attr.aria-checked]="selectedSetupQuality() === opt.value"
              [attr.data-value]="opt.value"
              (click)="setSetupQuality(opt.value)">
              <span class="ptc-pill-label">{{ opt.label }}</span>
            </button>
          }
        </div>
      </fieldset>

      <fieldset class="ptc-section">
        <legend class="ptc-legend">
          Confluencias <span class="ptc-count jcs-num">{{ confluences() }}</span>
        </legend>
        <input
          type="range"
          min="1"
          max="10"
          step="1"
          class="ptc-slider"
          [value]="confluences()"
          (input)="onConfluencesInput($event)"
          aria-label="Cantidad de confluencias (1 a 10)" />
        <span class="ptc-hint">Rango permitido: 1 a 10.</span>
      </fieldset>

      <fieldset class="ptc-section">
        <legend class="ptc-legend">Riesgo / Beneficio</legend>
        <div class="ptc-grid">
          <div class="ptc-field">
            <label class="jcs-label" for="ptc-rr-entry">R/R al entrar</label>
            <input
              id="ptc-rr-entry"
              type="number"
              min="1"
              step="0.1"
              class="jcs-input jcs-num"
              [class.jcs-input--error]="hasFieldError('riskRewardAtEntry')"
              [value]="riskRewardAtEntry()"
              (input)="onRrEntryInput($event)"
              aria-describedby="ptc-rr-entry-hint" />
            <span id="ptc-rr-entry-hint" class="ptc-hint">Mínimo 1.0 (1:1).</span>
            @if (hasFieldError('riskRewardAtEntry')) {
              <span class="ptc-error" role="alert">
                R/R al entrar debe ser ≥ al target.
              </span>
            }
          </div>

          <div class="ptc-field">
            <label class="jcs-label" for="ptc-rr-target">Target R/R</label>
            <input
              id="ptc-rr-target"
              type="number"
              min="1"
              step="0.1"
              class="jcs-input jcs-num"
              [value]="riskRewardTargetUsed()"
              (input)="onRrTargetInput($event)"
              aria-describedby="ptc-rr-target-hint" />
            <span id="ptc-rr-target-hint" class="ptc-hint">
              Recomendado: <strong class="jcs-num">{{ recommendedTarget() }}</strong>
              @if (userTouchedTarget()) {
                <span class="ptc-hint-tag"> (custom)</span>
              }
            </span>
          </div>
        </div>
      </fieldset>

      <footer class="ptc-actions">
        <button
          type="button"
          class="jcs-btn jcs-btn--ghost"
          (click)="onCancel()"
          data-testid="checklist-cancel">
          Cancelar
        </button>
        <button
          type="button"
          class="jcs-btn jcs-btn--primary"
          [disabled]="!isValid()"
          (click)="onAccept()"
          data-testid="checklist-accept">
          Aceptar y abrir trade
        </button>
      </footer>
    </section>
  `,
  styles: [`
    :host { display: block; }

    .ptc-card {
      display: flex;
      flex-direction: column;
      gap: var(--sp-5);
      padding: var(--sp-5);
    }

    .ptc-head { display: flex; flex-direction: column; gap: var(--sp-1); }
    .ptc-title { margin: 0; font-size: var(--fs-lg); }
    .ptc-sub { margin: 0; font-size: var(--fs-sm); max-width: 56ch; }

    .ptc-banner {
      padding: var(--sp-3) var(--sp-4);
      border: 1px solid rgba(255, 64, 87, 0.45);
      border-radius: var(--radius-sm);
      background: rgba(255, 64, 87, 0.10);
      color: var(--red);
      font-size: var(--fs-sm);
    }

    .ptc-section {
      display: flex;
      flex-direction: column;
      gap: var(--sp-2);
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-md);
      padding: var(--sp-4);
      margin: 0;
    }
    .ptc-legend {
      padding: 0 var(--sp-2);
      font-size: var(--fs-xs);
      font-weight: 600;
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.06em;
    }
    .ptc-count {
      margin-left: var(--sp-2);
      padding: 2px var(--sp-2);
      border-radius: var(--radius-xs);
      background: var(--green-soft);
      color: var(--green);
      font-weight: 700;
      text-transform: none;
      letter-spacing: 0;
    }

    /* Pills */
    .ptc-pills {
      display: flex;
      flex-wrap: wrap;
      gap: var(--sp-2);
    }
    .ptc-pill {
      display: inline-flex;
      align-items: center;
      gap: var(--sp-2);
      padding: var(--sp-2) var(--sp-4);
      background: var(--bg-card-soft);
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-pill, 9999px);
      color: var(--text-secondary);
      font: inherit;
      font-size: var(--fs-sm);
      font-weight: 600;
      cursor: pointer;
      transition: all 150ms ease;
    }
    .ptc-pill:hover { color: var(--text-main); border-color: var(--border-active); }
    .ptc-pill--active {
      background: var(--green-soft);
      border-color: var(--border-active);
      color: var(--green);
    }
    .ptc-pill-emoji { font-size: var(--fs-base); line-height: 1; }
    .ptc-pill-label { letter-spacing: 0.02em; }

    /* Slider */
    .ptc-slider {
      width: 100%;
      accent-color: var(--green);
      cursor: pointer;
    }
    .ptc-hint {
      color: var(--text-muted);
      font-size: var(--fs-xs);
    }
    .ptc-hint-tag {
      color: var(--yellow);
      font-style: italic;
      margin-left: var(--sp-1);
    }

    /* RR grid */
    .ptc-grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--sp-4);
    }
    .ptc-field { display: flex; flex-direction: column; gap: var(--sp-2); min-width: 0; }
    .ptc-error {
      color: var(--red);
      font-size: var(--fs-xs);
    }

    /* Actions */
    .ptc-actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--sp-3);
      flex-wrap: wrap;
    }

    @media (max-width: 540px) {
      .ptc-grid { grid-template-columns: 1fr; }
      .ptc-actions { flex-direction: column-reverse; }
      .ptc-actions .jcs-btn { width: 100%; }
    }
  `],
})
export class PreTradeChecklist {
  /** Domain code from a 422 RFC 7807 problem (e.g. "pre_trade_checklist.rr_below_target"). */
  readonly errorCode = input<string | null>(null);

  /**
   * Field names flagged as invalid in the last 422 (e.g. ["riskRewardAtEntry"]).
   * Surfaced as inline error classes on the corresponding inputs.
   */
  readonly fieldErrors = input<readonly string[]>([]);

  /** Emitted when the user clicks "Aceptar y abrir trade". */
  readonly submission = output<PreTradeChecklistPayload>();

  /** Emitted when the user clicks "Cancelar". */
  readonly cancelled = output<void>();

  readonly emotionalityOptions = EMOTIONALITY_OPTIONS;
  readonly setupQualityOptions = SETUP_QUALITY_OPTIONS;

  /** Active risk profile state — used to seed the recommended RR target. */
  private readonly riskProfile = inject(RiskProfileState);

  // ===== Internal state =====
  private readonly _emotionality = signal<number | null>(null);
  private readonly _setupQuality = signal<number | null>(null);
  private readonly _confluences = signal<number>(DEFAULT_CONFLUENCES);
  private readonly _rrEntry = signal<number>(DEFAULT_RR);
  private readonly _rrTarget = signal<number>(DEFAULT_RR);
  /** True once the user types into the target input — prevents overwriting the user's value. */
  private readonly _userTouchedTarget = signal<boolean>(false);

  // ===== Public read-only views =====
  readonly selectedEmotionality = this._emotionality.asReadonly();
  readonly selectedSetupQuality = this._setupQuality.asReadonly();
  readonly confluences = this._confluences.asReadonly();
  readonly riskRewardAtEntry = this._rrEntry.asReadonly();
  readonly riskRewardTargetUsed = this._rrTarget.asReadonly();
  readonly userTouchedTarget = this._userTouchedTarget.asReadonly();

  /** Recommended RR target from the active risk profile (or 2.0 fallback). */
  readonly recommendedTarget = computed<number>(() => {
    const profile = this.riskProfile.profile();
    return profile?.riskRewardTarget ?? DEFAULT_RR;
  });

  /** Disabled until every required field has a valid value. */
  readonly isValid = computed<boolean>(() => {
    if (this._emotionality() === null) return false;
    if (this._setupQuality() === null) return false;
    const c = this._confluences();
    if (!Number.isFinite(c) || c < 1 || c > 10) return false;
    const entry = this._rrEntry();
    if (!Number.isFinite(entry) || entry < 1.0) return false;
    const target = this._rrTarget();
    if (!Number.isFinite(target) || target < 1.0) return false;
    return true;
  });

  constructor() {
    // Load the active risk profile in the background so the target input
    // can pre-fill. We don't surface the load state — the trader can always
    // edit the target manually.
    void this.riskProfile.load();

    // When the recommended target arrives (or changes), seed the target input
    // only if the user hasn't touched it. Avoids stomping a value the trader
    // is actively editing while the profile fetch is in flight.
    effect(() => {
      const recommended = this.recommendedTarget();
      if (!this._userTouchedTarget()) {
        this._rrTarget.set(recommended);
      }
    });
  }

  // ===== Selectors =====
  setEmotionality(value: number): void {
    this._emotionality.set(value);
  }

  setSetupQuality(value: number): void {
    this._setupQuality.set(value);
  }

  // ===== Inputs =====
  onConfluencesInput(event: Event): void {
    const target = event.target as HTMLInputElement;
    const value = Number(target.valueAsNumber ?? target.value);
    if (!Number.isFinite(value)) return;
    this._confluences.set(Math.min(10, Math.max(1, Math.round(value))));
  }

  onRrEntryInput(event: Event): void {
    const target = event.target as HTMLInputElement;
    const value = Number(target.valueAsNumber ?? target.value);
    if (!Number.isFinite(value)) return;
    this._rrEntry.set(value);
  }

  onRrTargetInput(event: Event): void {
    const target = event.target as HTMLInputElement;
    const value = Number(target.valueAsNumber ?? target.value);
    if (!Number.isFinite(value)) return;
    this._userTouchedTarget.set(true);
    this._rrTarget.set(value);
  }

  // ===== Validation surfaces =====
  /** True when the parent flagged this field as failing validation. */
  hasFieldError(field: string): boolean {
    return this.fieldErrors().includes(field);
  }

  /** Human-readable copy for the top banner. */
  bannerMessage(code: string): string {
    switch (code) {
      case 'pre_trade_checklist.rr_below_target':
        return 'R/R insuficiente: tu R/R al entrar es menor al target. Ajustá los números y reintentá.';
      case 'pre_trade_checklist.confluences_out_of_range':
        return 'Confluencias fuera de rango (1–10).';
      case 'pre_trade_checklist.emotionality_out_of_range':
        return 'Estado emocional inválido.';
      case 'pre_trade_checklist.setup_quality_out_of_range':
        return 'Calidad del setup inválida.';
      default:
        return 'No se pudo validar el checklist. Revisá los valores.';
    }
  }

  // ===== Actions =====
  onAccept(): void {
    if (!this.isValid()) return;
    const emotionality = this._emotionality();
    const setupQuality = this._setupQuality();
    if (emotionality === null || setupQuality === null) return;
    const payload: PreTradeChecklistPayload = {
      emotionality,
      setupQuality,
      riskRewardAtEntry: this._rrEntry(),
      riskRewardTargetUsed: this._rrTarget(),
      confluencesCount: this._confluences(),
    };
    this.submission.emit(payload);
  }

  onCancel(): void {
    this.cancelled.emit();
  }
}