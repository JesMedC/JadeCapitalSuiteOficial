import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RiskProfileState } from '@core/state/risk-profile.state';
import { RiskProfileDto, UpsertRiskProfileRequest } from '@core/api/risk-profile.types';

// ============================================================================
//  RiskProfileTab — slice 1a.2 frontend.
//
//  Settings tab that lets the authenticated Trader manage their single active
//  risk profile. Four fields (capital, max drawdown %, risk per trade %,
//  risk-reward target) match the backend VO ranges.
//
//  Render branches (mutually exclusive):
//  1. isLoading                              → "Cargando perfil…"
//  2. error.status !== 422 (banner)          → red banner with the message
//  3. profile === null && !error             → empty state CTA
//  4. profile != null                        → form pre-populated + footer actions
//
//  Save flow: optimistic-ish (fire put, then reload). On 422 we leave the
//  existing profile in place and surface the field-level error inline. On 409
//  we surface a specific copy ("Otro proceso actualizó tu perfil. Recargá…").
// ============================================================================

const CURRENCY_OPTIONS = [
  'USD', 'EUR', 'GBP', 'JPY', 'CHF', 'AUD', 'CAD', 'NZD',
  'XAU', 'XAG', 'BTC', 'ETH',
] as const;

@Component({
  selector: 'jcs-risk-profile-tab',
  standalone: true,
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="jcs-card rpf-card">
      <header class="rpf-head">
        <div>
          <h2>Perfil de riesgo</h2>
          <p class="jcs-muted">
            Tu perfil activo define el tamaño de posición máximo y el drawdown
            tolerado. Lo usa el calculador de position-size y el checklist de
            pre-trade.
          </p>
        </div>
        @if (state.profile()) {
          <span class="jcs-badge status-active">Activo</span>
        }
      </header>

      @if (state.isLoading()) {
        <p class="rpf-loading" role="status">Cargando perfil…</p>
      } @else if (state.error() && state.error()!.status !== 422) {
        <div class="rpf-error" role="alert">
          <span>{{ state.error()!.message }}</span>
          <button type="button" class="rpf-retry" (click)="onReload()">Reintentar</button>
        </div>
      } @else if (state.isEmpty()) {
        <div class="rpf-empty">
          <h3>Aún no configuraste tu perfil</h3>
          <p class="jcs-muted">
            Creá uno para habilitar el trading con riesgo controlado.
          </p>
        </div>
      }

      @if (state.profile() || state.error()?.status === 422) {
        <form [formGroup]="form" (ngSubmit)="onSubmit()" novalidate class="rpf-form">
          <div class="rpf-grid">
            <div class="rpf-field">
              <label class="jcs-label" for="rpf-capital">Capital</label>
              <div class="rpf-capital-row">
                <input
                  id="rpf-capital"
                  class="jcs-input jcs-num"
                  type="number"
                  min="0"
                  step="0.01"
                  formControlName="capitalAmount"
                  [class.jcs-input--error]="isInvalid('capitalAmount')"
                  aria-describedby="rpf-capital-hint" />
                <select
                  class="jcs-input rpf-currency"
                  formControlName="capitalCurrency"
                  aria-label="Moneda del capital">
                  @for (c of currencyOptions; track c) {
                    <option [value]="c">{{ c }}</option>
                  }
                </select>
              </div>
              <span id="rpf-capital-hint" class="rpf-hint">Monto total disponible para operar.</span>
              @if (fieldError('capitalAmount'); as msg) { <span class="rpf-field-error">{{ msg }}</span> }
            </div>

            <div class="rpf-field">
              <label class="jcs-label" for="rpf-maxdd">Drawdown máximo (%)</label>
              <input
                id="rpf-maxdd"
                class="jcs-input jcs-num"
                type="number"
                min="0"
                max="50"
                step="0.5"
                formControlName="maxDrawdownPercent"
                [class.jcs-input--error]="isInvalid('maxDrawdownPercent')" />
              <span class="rpf-hint">Rango permitido: 0 a 50%.</span>
              @if (fieldError('maxDrawdownPercent'); as msg) { <span class="rpf-field-error">{{ msg }}</span> }
            </div>

            <div class="rpf-field">
              <label class="jcs-label" for="rpf-rpt">Riesgo por trade (%)</label>
              <input
                id="rpf-rpt"
                class="jcs-input jcs-num"
                type="number"
                min="0.01"
                max="5"
                step="0.01"
                formControlName="riskPerTradePercent"
                [class.jcs-input--error]="isInvalid('riskPerTradePercent') || !!fieldError('riskPerTradePercent')" />
              <span class="rpf-hint">Rango permitido: 0.01 a 5%.</span>
              @if (fieldError('riskPerTradePercent'); as msg) { <span class="rpf-field-error">{{ msg }}</span> }
            </div>

            <div class="rpf-field">
              <label class="jcs-label" for="rpf-rr">Objetivo riesgo / beneficio</label>
              <input
                id="rpf-rr"
                class="jcs-input jcs-num"
                type="number"
                min="1"
                step="0.1"
                formControlName="riskRewardTarget"
                [class.jcs-input--error]="isInvalid('riskRewardTarget')" />
              <span class="rpf-hint">Mínimo 1.0 (1:1).</span>
              @if (fieldError('riskRewardTarget'); as msg) { <span class="rpf-field-error">{{ msg }}</span> }
            </div>
          </div>

          @if (state.error()?.status === 409) {
            <div class="rpf-error" role="alert">
              {{ state.error()!.message || 'Otro proceso actualizó tu perfil. Recargá e intentá de nuevo.' }}
            </div>
          }

          <footer class="rpf-actions">
            <button type="button" class="jcs-btn jcs-btn--ghost" (click)="onCancel()" [disabled]="state.saving()">
              Cancelar
            </button>
            <button type="submit" class="jcs-btn jcs-btn--primary" [disabled]="state.saving() || form.invalid">
              {{ state.saving() ? 'Guardando…' : 'Guardar perfil' }}
            </button>
          </footer>

          @if (state.saveSuccess()) {
            <p class="rpf-success" role="status" aria-live="polite">Perfil guardado.</p>
          }
        </form>
      }

      <details class="rpf-history">
        <summary>Historial de perfiles</summary>
        <p class="jcs-muted">
          Solo mantenemos tu perfil activo. Las versiones anteriores quedan
          registradas como superseded en el backend pero no se exponen en esta
          versión del frontend.
        </p>
      </details>
    </section>
  `,
  styles: [`
    :host { display: block; }
    .rpf-card {
      display: flex;
      flex-direction: column;
      gap: var(--sp-5);
      padding: var(--sp-6);
    }
    .rpf-head {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--sp-4);
      flex-wrap: wrap;
    }
    .rpf-head h2 { margin: 0 0 var(--sp-1); font-size: var(--fs-xl); }
    .rpf-head p { margin: 0; max-width: 64ch; font-size: var(--fs-sm); }

    .rpf-loading { padding: var(--sp-3) 0; color: var(--text-secondary); }

    .rpf-error {
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
    .rpf-retry {
      padding: var(--sp-1) var(--sp-3);
      border: 1px solid var(--red);
      border-radius: var(--radius-sm);
      background: transparent;
      color: var(--red);
      font: inherit;
      font-size: var(--fs-xs);
      font-weight: 600;
      cursor: pointer;
    }

    .rpf-empty {
      display: flex;
      flex-direction: column;
      gap: var(--sp-2);
      padding: var(--sp-6);
      border: 1px dashed var(--border);
      border-radius: var(--radius-md);
      background: var(--bg-card-soft);
      text-align: center;
    }
    .rpf-empty h3 { margin: 0; font-size: var(--fs-lg); }
    .rpf-empty p { margin: 0 auto; max-width: 48ch; font-size: var(--fs-sm); }

    .rpf-form {
      display: flex;
      flex-direction: column;
      gap: var(--sp-5);
    }
    .rpf-grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--sp-4);
    }
    .rpf-field {
      display: flex;
      flex-direction: column;
      gap: var(--sp-2);
    }
    .rpf-capital-row {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: var(--sp-2);
    }
    .rpf-currency {
      width: 90px;
      padding-left: var(--sp-3);
      padding-right: var(--sp-3);
      text-transform: uppercase;
      font-family: var(--font-mono);
    }
    .rpf-hint { color: var(--text-muted); font-size: var(--fs-xs); }
    .rpf-field-error { color: var(--red); font-size: var(--fs-xs); }

    .rpf-actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--sp-3);
      flex-wrap: wrap;
    }

    .rpf-success {
      margin: 0;
      padding: var(--sp-2) var(--sp-3);
      border: 1px solid rgba(47, 219, 120, 0.4);
      border-radius: var(--radius-sm);
      background: rgba(47, 219, 120, 0.1);
      color: var(--green);
      font-size: var(--fs-sm);
    }

    .rpf-history {
      padding: var(--sp-3) var(--sp-4);
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-md);
      background: var(--bg-card-soft);
    }
    .rpf-history summary {
      cursor: pointer;
      font-weight: 600;
      font-size: var(--fs-sm);
    }
    .rpf-history p { margin: var(--sp-2) 0 0; font-size: var(--fs-xs); }

    @media (max-width: 720px) {
      .rpf-grid { grid-template-columns: 1fr; }
      .rpf-capital-row { grid-template-columns: 1fr; }
      .rpf-actions { flex-direction: column-reverse; }
      .rpf-actions .jcs-btn { width: 100%; }
    }
  `],
})
export class RiskProfileTab {
  private readonly fb = inject(FormBuilder);
  readonly state = inject(RiskProfileState);

  readonly currencyOptions = CURRENCY_OPTIONS;

  readonly form = this.fb.nonNullable.group({
    capitalAmount: this.fb.control<number | null>(null, [Validators.required, Validators.min(0.00000001)]),
    capitalCurrency: ['USD', [Validators.required, Validators.pattern(/^[A-Z]{3}$/)]],
    maxDrawdownPercent: [10, [Validators.required, Validators.min(0), Validators.max(50)]],
    riskPerTradePercent: [1.0, [Validators.required, Validators.min(0.01), Validators.max(5)]],
    riskRewardTarget: [2.0, [Validators.required, Validators.min(1)]],
  });

  /** Snapshot of the profile as last seen, so we can restore on cancel. */
  private snapshot = signal<RiskProfileDto | null>(null);

  constructor() {
    void this.state.load();

    // When the profile changes, sync the form (pre-populate or refresh).
    effect(() => {
      const profile = this.state.profile();
      if (profile) {
        this.snapshot.set(profile);
        this.form.reset({
          capitalAmount: profile.capitalAmount,
          capitalCurrency: profile.capitalCurrency,
          maxDrawdownPercent: profile.maxDrawdownPercent,
          riskPerTradePercent: profile.riskPerTradePercent,
          riskRewardTarget: profile.riskRewardTarget,
        });
      }
    });
  }

  isInvalid(field: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[field];
    return c.invalid && c.touched;
  }

  fieldError(field: string): string | null {
    return this.state.fieldErrors()[field] ?? null;
  }

  onReload(): void {
    this.state.clearError();
    void this.state.load();
  }

  onCancel(): void {
    const snap = this.snapshot();
    this.state.clearError();
    if (snap) {
      this.form.reset({
        capitalAmount: snap.capitalAmount,
        capitalCurrency: snap.capitalCurrency,
        maxDrawdownPercent: snap.maxDrawdownPercent,
        riskPerTradePercent: snap.riskPerTradePercent,
        riskRewardTarget: snap.riskRewardTarget,
      });
    } else {
      this.form.reset({
        capitalAmount: null,
        capitalCurrency: 'USD',
        maxDrawdownPercent: 10,
        riskPerTradePercent: 1.0,
        riskRewardTarget: 2.0,
      });
    }
  }

  async onSubmit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const request: UpsertRiskProfileRequest = {
      capitalAmount: Number(v.capitalAmount),
      capitalCurrency: v.capitalCurrency.toUpperCase(),
      maxDrawdownPercent: Number(v.maxDrawdownPercent),
      riskPerTradePercent: Number(v.riskPerTradePercent),
      riskRewardTarget: Number(v.riskRewardTarget),
    };
    await this.state.save(request);
  }
}
