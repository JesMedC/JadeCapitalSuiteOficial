import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  PositionSizeCalcResult,
  PositionSizeRequest,
  PositionSizeService,
} from '@core/api/position-size.service';
import { RiskProfileDto } from '@core/api/risk-profile.types';

// ============================================================================
//  PositionSizeCalculator — slice 1b frontend.
//
//  Read-only calculator (no persistence) that calls
//  `POST /api/trades/position-size/calculate` and displays the suggested
//  volume + risk amount. Wired above the "Crear operación" button inside
//  create-trade-form; its `calculated` OutputEvent pre-populates the form's
//  `volume` and `stopLoss` fields when the trader accepts the result.
//
//  Profile input: the parent passes the active profile from
//  `RiskProfileState.profile()`. If null, the component shows the empty
//  state copy "Configurá tu perfil de riesgo primero" with a link to
//  /app/settings — there's nothing to compute without a capital base + a
//  risk-per-trade.
//
//  State pattern (matches pre-trade-checklist.ts): the inputs (`profile` and
//  `defaultStopLossDistance`) feed computed signals. User-typed overrides
//  live in a private signal; the public computed falls back to the input
//  when the override is null. This way the parent can update the profile
//  asynchronously and the UI reflects it without us manually re-seeding
//  internal state via `effect`.
// ============================================================================

@Component({
  selector: 'jcs-position-size-calculator',
  standalone: true,
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (profile(); as p) {
      <section class="jcs-card psc-card" data-testid="psc-root">
        <header class="psc-head">
          <h3 class="psc-title">Calculadora de posición</h3>
          <p class="psc-sub jcs-muted">
            Sugerencia según tu perfil activo. El valor nunca se enforce — siempre podés override manual.
          </p>
        </header>

        <div class="psc-grid">
          <div class="psc-field">
            <span class="jcs-label">Capital</span>
            <span
              class="psc-readonly jcs-num"
              data-testid="capital-amount">
              {{ p.capitalAmount }} {{ p.capitalCurrency }}
            </span>
          </div>

          <div class="psc-field">
            <label class="jcs-label" for="psc-risk">Riesgo por trade (%)</label>
            <input
              id="psc-risk"
              class="jcs-input jcs-num"
              type="number"
              min="0.01"
              max="5"
              step="0.01"
              data-testid="risk-percent"
              [value]="riskPercent()"
              (input)="onRiskInput($event)" />
            <span class="psc-hint">
              @if (riskOverridden()) {
                Override manual (perfil: {{ p.riskPerTradePercent }}%).
              } @else {
                Desde tu perfil activo.
              }
            </span>
          </div>

          <div class="psc-field">
            <label class="jcs-label" for="psc-stop">Distancia del stop</label>
            <input
              id="psc-stop"
              class="jcs-input jcs-num"
              type="number"
              min="0.00000001"
              step="0.0001"
              data-testid="stop-loss-distance"
              [value]="stopLossDistance()"
              (input)="onStopInput($event)" />
            <span class="psc-hint">En unidades del quote (ej. EUR/USD pip = 0.0001).</span>
          </div>

          <div class="psc-field">
            <span class="jcs-label">Moneda</span>
            <span class="psc-readonly jcs-num">{{ p.capitalCurrency }}</span>
          </div>
        </div>

        <div class="psc-actions">
          <button
            type="button"
            class="jcs-btn jcs-btn--primary"
            data-testid="calc-button"
            [disabled]="!canCalculate() || loading()"
            (click)="onCalculate()">
            @if (loading()) {
              <span class="btn-spinner" aria-hidden="true"></span>
              Calculando…
            } @else {
              Calcular
            }
          </button>
        </div>

        @if (error(); as err) {
          <div class="psc-banner" role="alert" data-testid="psc-error">
            {{ err }}
          </div>
        }

        @if (result(); as r) {
          <div class="psc-result" data-testid="psc-result">
            <div class="psc-result-grid">
              <div class="psc-result-cell">
                <span class="psc-result-label">Volumen sugerido</span>
                <span class="psc-result-value jcs-num" data-testid="result-volume">
                  {{ r.volume }}
                </span>
                <span class="psc-result-hint">unidades base</span>
              </div>
              <div class="psc-result-cell">
                <span class="psc-result-label">Riesgo</span>
                <span class="psc-result-value jcs-num" data-testid="result-risk-amount">
                  {{ r.riskAmount }} {{ r.currency }}
                </span>
                <span class="psc-result-hint">{{ r.riskPerTradePercent }}% del capital</span>
              </div>
            </div>
            <p class="psc-formula jcs-muted">{{ r.calculation }}</p>
          </div>
        }
      </section>
    } @else {
      <section class="jcs-card psc-empty" data-testid="empty-state">
        <span class="psc-empty-icon" aria-hidden="true">⚠</span>
        <h4 class="psc-empty-title">Calculadora no disponible</h4>
        <p class="psc-empty-copy jcs-muted">
          Configurá tu perfil de riesgo primero para poder sugerir el tamaño de posición.
        </p>
        <a class="jcs-btn jcs-btn--primary" routerLink="/app/settings">Ir a Configuración</a>
      </section>
    }
  `,
  styles: [`
    :host { display: block; }

    .psc-card {
      display: flex;
      flex-direction: column;
      gap: var(--sp-4);
      padding: var(--sp-5);
    }

    .psc-head { display: flex; flex-direction: column; gap: var(--sp-1); }
    .psc-title { margin: 0; font-size: var(--fs-lg); }
    .psc-sub { margin: 0; font-size: var(--fs-sm); max-width: 56ch; }

    .psc-grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--sp-4);
    }

    .psc-field {
      display: flex;
      flex-direction: column;
      gap: var(--sp-2);
      min-width: 0;
    }

    .psc-readonly {
      padding: var(--sp-2) var(--sp-3);
      background: var(--bg-card-soft);
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-sm);
      font-size: var(--fs-base);
      color: var(--text-secondary);
    }

    .psc-hint {
      color: var(--text-muted);
      font-size: var(--fs-xs);
      font-family: var(--font-mono);
    }

    .psc-actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--sp-3);
    }

    .psc-banner {
      padding: var(--sp-3) var(--sp-4);
      border: 1px solid rgba(255, 64, 87, 0.45);
      border-radius: var(--radius-sm);
      background: rgba(255, 64, 87, 0.10);
      color: var(--red);
      font-size: var(--fs-sm);
    }

    .psc-result {
      display: flex;
      flex-direction: column;
      gap: var(--sp-3);
      padding: var(--sp-4);
      border: 1px solid var(--border-active);
      background: var(--green-soft);
      border-radius: var(--radius-md);
    }

    .psc-result-grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--sp-4);
    }

    .psc-result-cell {
      display: flex;
      flex-direction: column;
      gap: var(--sp-1);
    }

    .psc-result-label {
      font-size: var(--fs-xs);
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--text-muted);
      font-weight: 600;
    }

    .psc-result-value {
      font-size: var(--fs-xl);
      font-weight: 700;
      color: var(--green);
    }

    .psc-result-hint {
      font-size: var(--fs-xs);
      color: var(--text-muted);
    }

    .psc-formula {
      margin: 0;
      font-size: var(--fs-xs);
      font-family: var(--font-mono);
      word-break: break-all;
    }

    .psc-empty {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--sp-3);
      padding: var(--sp-6);
      text-align: center;
    }
    .psc-empty-title { margin: 0; font-size: var(--fs-lg); }
    .psc-empty-copy { margin: 0; font-size: var(--fs-sm); max-width: 36ch; }
    .psc-empty-icon {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 40px;
      height: 40px;
      border-radius: 50%;
      border: 1px solid var(--yellow);
      background: rgba(255, 211, 0, 0.10);
      color: var(--yellow);
      font-weight: 700;
    }

    .btn-spinner {
      display: inline-block;
      width: 14px;
      height: 14px;
      border: 2px solid rgba(5, 11, 16, 0.3);
      border-top-color: #050B10;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
      margin-right: var(--sp-2);
    }
    @keyframes spin {
      from { transform: rotate(0); }
      to   { transform: rotate(360deg); }
    }

    @media (max-width: 540px) {
      .psc-grid { grid-template-columns: 1fr; }
      .psc-result-grid { grid-template-columns: 1fr; }
    }
  `],
})
export class PositionSizeCalculator {
  /** Active risk profile (null → empty state copy). Provided by the parent. */
  readonly profile = input<RiskProfileDto | null>(null);
  /** Suggested stop-loss distance from the form's |entry - sl|. */
  readonly defaultStopLossDistance = input<number>(0);

  /**
   * Emitted when a calculation succeeds. The parent pre-populates the form's
   * `volume` field with `result.volume` (and `stopLoss` if applicable).
   */
  readonly calculated = output<PositionSizeCalcResult>();

  private readonly service = inject(PositionSizeService);

  // ===== Internal state =====
  /** User-typed override of the risk percent. null = use profile value. */
  private readonly _riskOverride = signal<number | null>(null);
  /** User-typed override of the stop loss distance. null = use default input. */
  private readonly _stopOverride = signal<number | null>(null);

  /** Latest successful result (null until first successful "Calcular"). */
  readonly result = signal<PositionSizeCalcResult | null>(null);
  /** Latest error message (null when no error / cleared on success). */
  readonly error = signal<string | null>(null);
  /** True while the HTTP request is in flight. */
  readonly loading = signal(false);

  // ===== Public read-only computed views =====

  /** Effective risk percent: user override if present, else profile value. */
  readonly riskPercent = computed<number>(() => {
    const override = this._riskOverride();
    if (override !== null) return override;
    return this.profile()?.riskPerTradePercent ?? 0;
  });

  /** True when the user has typed a custom risk percent. */
  readonly riskOverridden = computed<boolean>(() => this._riskOverride() !== null);

  /** Effective stop loss distance: user override if present, else parent's default. */
  readonly stopLossDistance = computed<number>(() => {
    const override = this._stopOverride();
    if (override !== null) return override;
    return this.defaultStopLossDistance();
  });

  /** Whether "Calcular" should be enabled. */
  readonly canCalculate = computed<boolean>(() => {
    return this.riskPercent() > 0 && this.stopLossDistance() > 0;
  });

  // ===== Inputs =====
  onRiskInput(event: Event): void {
    const target = event.target as HTMLInputElement;
    const value = Number(target.valueAsNumber ?? target.value);
    if (!Number.isFinite(value)) return;
    // Only treat it as an override if the user diverged from the profile's value.
    const profileValue = this.profile()?.riskPerTradePercent ?? 0;
    this._riskOverride.set(value === profileValue ? null : value);
  }

  onStopInput(event: Event): void {
    const target = event.target as HTMLInputElement;
    const value = Number(target.valueAsNumber ?? target.value);
    if (!Number.isFinite(value)) return;
    // Override semantics: store null when the typed value matches the parent's
    // default, otherwise store the typed value.
    const defaultValue = this.defaultStopLossDistance();
    this._stopOverride.set(value === defaultValue ? null : value);
  }

  // ===== Actions =====
  async onCalculate(): Promise<void> {
    const profile = this.profile();
    if (!profile) return;
    if (!this.canCalculate()) return;

    // The override equals the typed risk% if the user diverged from the
    // profile's riskPerTradePercent; otherwise null (use the profile value).
    const profileRisk = profile.riskPerTradePercent;
    const effectiveRisk = this.riskPercent();
    const override = effectiveRisk !== profileRisk ? effectiveRisk : null;

    const request: PositionSizeRequest = {
      stopLossDistance: this.stopLossDistance(),
      riskPerTradeOverride: override,
      currency: profile.capitalCurrency,
    };

    this.loading.set(true);
    this.error.set(null);
    try {
      const dto = await this.service.calculate(request);
      this.result.set(dto);
      this.calculated.emit(dto);
    } catch (e) {
      // Render a friendly message; the calculator's HTTP error path doesn't
      // need to expose the raw status code to the user.
      const msg =
        e && typeof e === 'object' && 'message' in e
          ? String((e as { message: unknown }).message)
          : 'No se pudo calcular el tamaño de posición.';
      this.error.set(msg);
      this.result.set(null);
    } finally {
      this.loading.set(false);
    }
  }
}
