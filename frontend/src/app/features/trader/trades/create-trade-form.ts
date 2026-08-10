import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { AccountApiService, AccountDto } from '@core/api/account-api.service';
import {
  ASSET_CLASS_LABELS,
  AssetClass,
  InstrumentApiService,
  InstrumentDto,
} from '@core/api/instrument-api.service';
import {
  OpenTradeRequest,
  TradeApiService,
  TradeDirection,
  TradeDto,
} from '@core/api/trade-api.service';

@Component({
  selector: 'jcs-create-trade-form',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (visible()) {
      <form
        class="jcs-card jcs-card--glow inline-form"
        [formGroup]="form"
        (ngSubmit)="submit()"
        aria-labelledby="ct-title">
        <header class="form-head">
          <div>
            <h3 id="ct-title">Nueva operación</h3>
            <p class="jcs-muted">Registrar un trade manualmente. La operación queda abierta hasta que la cierres.</p>
          </div>
          <button
            type="button"
            class="close-btn"
            (click)="cancel()"
            [disabled]="submitting()"
            aria-label="Cerrar formulario">×</button>
        </header>

        @if (loading()) {
          <div class="loading-state" aria-live="polite">
            <span class="loading-dot" aria-hidden="true"></span>
            <span>Cargando cuentas e instrumentos…</span>
          </div>
        } @else if (dataError()) {
          <div class="form-error" role="alert">
            <span>{{ dataError() }}</span>
            <button type="button" class="error-retry" (click)="loadData()">Reintentar</button>
          </div>
        } @else if (hasNoData()) {
          <div class="empty-state">
            <span class="empty-icon" aria-hidden="true">⚠</span>
            <h4>No se puede crear la operación</h4>
            @if (activeAccounts().length === 0) {
              <p class="jcs-muted">Necesitás al menos una cuenta activa.</p>
            }
            @if (activeInstruments().length === 0) {
              <p class="jcs-muted">Necesitás al menos un instrumento activo.</p>
            }
            <a class="jcs-btn jcs-btn--primary" routerLink="/app/settings">Ir a Configuración</a>
          </div>
        } @else {
          <div class="form-grid">
            <div class="field">
              <label class="jcs-label" for="ct-account">Cuenta</label>
              <select
                id="ct-account"
                class="jcs-input"
                formControlName="accountId"
                [disabled]="submitting()"
                [class.jcs-input--error]="isInvalid(form.controls.accountId)">
                <option value="">Seleccioná una cuenta</option>
                @for (a of activeAccounts(); track a.id) {
                  <option [value]="a.id">{{ a.name }} · {{ a.broker }} · {{ a.currency }}</option>
                }
              </select>
              @if (isInvalid(form.controls.accountId)) {
                <span class="field-error">Seleccioná una cuenta.</span>
              }
            </div>

            <div class="field">
              <label class="jcs-label" for="ct-instrument">Instrumento</label>
              <select
                id="ct-instrument"
                class="jcs-input"
                formControlName="instrumentId"
                [disabled]="submitting()"
                [class.jcs-input--error]="isInvalid(form.controls.instrumentId)">
                <option value="">Seleccioná un instrumento</option>
                @for (i of activeInstruments(); track i.id) {
                  <option [value]="i.id">{{ i.symbol }} · {{ assetClassLabel(i.assetClass) }}</option>
                }
              </select>
              @if (isInvalid(form.controls.instrumentId)) {
                <span class="field-error">Seleccioná un instrumento.</span>
              }
            </div>

            <div class="field">
              <label class="jcs-label">Dirección</label>
              <div class="dir-toggle" role="group" aria-label="Dirección">
                <button
                  type="button"
                  class="dir-pill"
                  [class.dir-pill--active]="form.controls.direction.value === 1"
                  [disabled]="submitting()"
                  (click)="setDirection(1)"
                  aria-label="Long">
                  <span class="dot dot--long" aria-hidden="true"></span>
                  Long
                </button>
                <button
                  type="button"
                  class="dir-pill"
                  [class.dir-pill--active]="form.controls.direction.value === 2"
                  [disabled]="submitting()"
                  (click)="setDirection(2)"
                  aria-label="Short">
                  <span class="dot dot--short" aria-hidden="true"></span>
                  Short
                </button>
              </div>
            </div>

            <div class="field">
              <label class="jcs-label" for="ct-volume">Volumen</label>
              <div class="input-suffix">
                <input
                  id="ct-volume"
                  class="jcs-input jcs-num"
                  type="number"
                  min="0.00000001"
                  [step]="volumeStep()"
                  formControlName="volume"
                  [disabled]="submitting()"
                  placeholder="0.00"
                  [class.jcs-input--error]="isInvalid(form.controls.volume)">
                <span class="suffix" [class.suffix--empty]="!volumeCurrency()">
                  {{ volumeCurrency() || '—' }}
                </span>
              </div>
              @if (isInvalid(form.controls.volume)) {
                <span class="field-error">Ingresá un volumen mayor a 0.</span>
              } @else {
                <span class="field-hint">Moneda de la cuenta seleccionada.</span>
              }
            </div>

            <div class="field">
              <label class="jcs-label" for="ct-entry">Precio de entrada</label>
              <div class="input-suffix">
                <input
                  id="ct-entry"
                  class="jcs-input jcs-num"
                  type="number"
                  min="0.00000001"
                  [step]="priceStep()"
                  formControlName="entryPrice"
                  [disabled]="submitting()"
                  placeholder="0.00"
                  [class.jcs-input--error]="isInvalid(form.controls.entryPrice)">
                <span class="suffix" [class.suffix--empty]="!entryPriceCurrency()">
                  {{ entryPriceCurrency() || '—' }}
                </span>
              </div>
              @if (isInvalid(form.controls.entryPrice)) {
                <span class="field-error">Ingresá un precio mayor a 0.</span>
              } @else {
                <span class="field-hint">
                  Decimales sugeridos: {{ selectedInstrument()?.decimalPlaces ?? '—' }}
                </span>
              }
            </div>

            <div class="field">
              <label class="jcs-label" for="ct-strategy">Estrategia (opcional)</label>
              <input
                id="ct-strategy"
                class="jcs-input"
                type="text"
                formControlName="strategy"
                [disabled]="submitting()"
                maxlength="80"
                placeholder="Ej.: Breakout Londres, Order block H4…">
              <span class="field-hint">{{ form.controls.strategy.value.length }} / 80</span>
            </div>

            <div class="field field--full">
              <label class="jcs-label" for="ct-notes">Notas (opcional)</label>
              <textarea
                id="ct-notes"
                class="jcs-input notes-input"
                formControlName="notes"
                [disabled]="submitting()"
                maxlength="2000"
                rows="3"
                placeholder="Contexto, setup, por qué tomaste el trade…"></textarea>
              <span class="field-hint">{{ form.controls.notes.value.length }} / 2000</span>
            </div>
          </div>

          @if (submitError()) {
            <div class="form-error" role="alert">{{ submitError() }}</div>
          }

          <footer class="form-actions">
            <button
              type="button"
              class="jcs-btn jcs-btn--ghost"
              (click)="cancel()"
              [disabled]="submitting()">Cancelar</button>
            <button
              type="submit"
              class="jcs-btn jcs-btn--primary"
              [disabled]="submitting() || form.invalid">
              {{ submitting() ? 'Creando…' : 'Crear operación' }}
            </button>
          </footer>
        }
      </form>
    }
  `,
  styles: [`
    :host { display: block; }

    .inline-form {
      display: flex;
      flex-direction: column;
      gap: var(--sp-5);
      padding: var(--sp-6);
      animation: form-expand 220ms ease-out;
    }
    .form-head {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: var(--sp-4);
    }
    .form-head h3 {
      margin: 0 0 var(--sp-1);
      font-size: var(--fs-xl);
    }
    .form-head p {
      margin: 0;
      font-size: var(--fs-sm);
      max-width: 560px;
    }
    .close-btn {
      width: 34px;
      height: 34px;
      flex-shrink: 0;
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      background: transparent;
      color: var(--text-muted);
      font-size: var(--fs-xl);
      line-height: 1;
      cursor: pointer;
      transition: border-color 150ms, color 150ms;
    }
    .close-btn:hover:not(:disabled) {
      border-color: var(--border-active);
      color: var(--text-main);
    }
    .close-btn:disabled { opacity: 0.5; cursor: not-allowed; }

    .form-grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--sp-4);
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: var(--sp-2);
      min-width: 0;
    }
    .field--full { grid-column: 1 / -1; }
    .field-error { color: var(--red); font-size: var(--fs-xs); }
    .field-hint {
      color: var(--text-muted);
      font-size: var(--fs-xs);
      font-family: var(--font-mono);
    }

    .input-suffix {
      position: relative;
      display: flex;
      align-items: stretch;
    }
    .input-suffix .jcs-input {
      flex: 1;
      min-width: 0;
      padding-right: 64px;
    }
    .input-suffix .suffix {
      position: absolute;
      top: 50%;
      right: var(--sp-3);
      transform: translateY(-50%);
      font-family: var(--font-mono);
      font-size: var(--fs-xs);
      font-weight: 600;
      color: var(--green);
      pointer-events: none;
      padding: 2px var(--sp-2);
      background: var(--bg-card-soft);
      border-radius: var(--radius-xs);
      border: 1px solid var(--border-soft);
    }
    .input-suffix .suffix--empty {
      color: var(--text-muted);
      background: transparent;
      border-color: transparent;
    }

    .notes-input {
      font-family: inherit;
      resize: vertical;
      min-height: 88px;
      line-height: 1.5;
    }

    .dir-toggle {
      display: inline-flex;
      gap: var(--sp-1);
      background: var(--bg-card-soft);
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-sm);
      padding: var(--sp-1);
      width: fit-content;
    }
    .dir-pill {
      display: inline-flex;
      align-items: center;
      gap: var(--sp-2);
      padding: var(--sp-2) var(--sp-4);
      background: transparent;
      border: 1px solid transparent;
      border-radius: var(--radius-sm);
      color: var(--text-secondary);
      font: inherit;
      font-size: var(--fs-sm);
      font-weight: 600;
      cursor: pointer;
      transition: background 150ms, color 150ms, border-color 150ms;
    }
    .dir-pill:hover:not(:disabled) { color: var(--text-main); }
    .dir-pill:disabled { opacity: 0.45; cursor: not-allowed; }
    .dir-pill--active {
      background: var(--bg-elevated);
      color: var(--text-main);
      border-color: var(--border-active);
    }
    .dir-toggle .dot {
      width: 6px;
      height: 6px;
      border-radius: 50%;
    }
    .dir-toggle .dot--long { background: var(--green); }
    .dir-toggle .dot--short { background: var(--red); }

    .form-error {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--sp-3);
      padding: var(--sp-3) var(--sp-4);
      background: rgba(255, 64, 87, 0.08);
      border: 1px solid rgba(255, 64, 87, 0.35);
      border-radius: var(--radius-sm);
      color: var(--red);
      font-size: var(--fs-sm);
    }
    .error-retry {
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
    .error-retry:hover { background: rgba(255, 64, 87, 0.15); }

    .form-actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--sp-3);
      flex-wrap: wrap;
    }

    .loading-state {
      display: flex;
      align-items: center;
      gap: var(--sp-3);
      padding: var(--sp-5);
      color: var(--text-muted);
      font-size: var(--fs-sm);
    }
    .loading-dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      background: var(--green);
      animation: pulse 1.2s ease-in-out infinite;
    }
    @keyframes pulse {
      0%, 100% { opacity: 0.4; transform: scale(0.85); }
      50%      { opacity: 1;   transform: scale(1); }
    }

    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: var(--sp-3);
      padding: var(--sp-8) var(--sp-4);
      text-align: center;
    }
    .empty-state h4 {
      margin: 0;
      font-size: var(--fs-lg);
    }
    .empty-state p {
      margin: 0;
      font-size: var(--fs-sm);
      max-width: 460px;
    }
    .empty-icon {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 48px;
      height: 48px;
      border: 1px solid rgba(255, 64, 87, 0.4);
      background: rgba(255, 64, 87, 0.1);
      color: var(--red);
      border-radius: 50%;
      font-size: var(--fs-xl);
      font-weight: 700;
    }

    @media (max-width: 720px) {
      .form-grid { grid-template-columns: 1fr; }
      .inline-form { padding: var(--sp-5); }
      .form-actions { flex-direction: column-reverse; }
      .form-actions .jcs-btn { width: 100%; }
    }

    @keyframes form-expand {
      from { opacity: 0; transform: translateY(-8px); }
      to   { opacity: 1; transform: translateY(0); }
    }
  `],
})
export class CreateTradeForm implements OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly tradeApi = inject(TradeApiService);
  private readonly accountApi = inject(AccountApiService);
  private readonly instrumentApi = inject(InstrumentApiService);

  readonly visible = input.required<boolean>();
  readonly saved = output<TradeDto>();
  readonly cancelled = output<void>();

  readonly accounts = signal<AccountDto[]>([]);
  readonly instruments = signal<InstrumentDto[]>([]);
  readonly loading = signal(true);
  readonly dataError = signal<string | null>(null);
  readonly submitting = signal(false);
  readonly submitError = signal<string | null>(null);

  private destroyed = false;

  readonly form = this.fb.nonNullable.group({
    accountId: ['', Validators.required],
    instrumentId: ['', Validators.required],
    direction: [1 as TradeDirection, Validators.required],
    volume: [0, [Validators.required, Validators.min(0.00000001)]],
    entryPrice: [0, [Validators.required, Validators.min(0.00000001)]],
    strategy: ['', Validators.maxLength(80)],
    notes: ['', Validators.maxLength(2000)],
  });

  // Signals derivados del form para reactividad real con computed().
  private readonly accountIdValue = toSignal(
    this.form.controls.accountId.valueChanges,
    { initialValue: this.form.controls.accountId.value },
  );
  private readonly instrumentIdValue = toSignal(
    this.form.controls.instrumentId.valueChanges,
    { initialValue: this.form.controls.instrumentId.value },
  );

  readonly activeAccounts = computed(() => this.accounts().filter(a => a.isActive));
  readonly activeInstruments = computed(() => this.instruments().filter(i => i.isActive));

  readonly selectedAccount = computed(() => {
    const id = this.accountIdValue();
    if (!id) return null;
    return this.accounts().find(a => a.id === id) ?? null;
  });

  readonly selectedInstrument = computed(() => {
    const id = this.instrumentIdValue();
    if (!id) return null;
    return this.instruments().find(i => i.id === id) ?? null;
  });

  readonly volumeCurrency = computed(() => this.selectedAccount()?.currency ?? '');

  readonly entryPriceCurrency = computed(() => {
    const sym = this.selectedInstrument()?.symbol;
    if (!sym) return '';
    // TODO: usar Instrument.quoteCurrency cuando el backend lo exponga.
    return sym.split('/')[1] ?? 'USD';
  });

  private readonly decimalsForStep = computed(
    () => this.selectedInstrument()?.decimalPlaces ?? 5,
  );

  readonly volumeStep = computed(() => this.stepFor(this.decimalsForStep()));
  readonly priceStep = computed(() => this.stepFor(this.decimalsForStep()));

  readonly hasNoData = computed(() => {
    if (this.loading() || this.dataError()) return false;
    return this.activeAccounts().length === 0 || this.activeInstruments().length === 0;
  });

  constructor() {
    // Cargar datos cada vez que el formulario se vuelve visible.
    effect(() => {
      if (this.visible()) {
        this.resetForm();
        void this.loadData();
      }
    });
  }

  ngOnDestroy(): void {
    this.destroyed = true;
  }

  async loadData(): Promise<void> {
    this.loading.set(true);
    this.dataError.set(null);
    try {
      const [accounts, instruments] = await Promise.all([
        this.accountApi.list(),
        this.instrumentApi.list(),
      ]);
      if (this.destroyed) return;
      this.accounts.set(accounts.filter(a => a.isActive));
      this.instruments.set(instruments.filter(i => i.isActive));
    } catch (e) {
      if (this.destroyed) return;
      this.dataError.set(this.toMessage(e));
    } finally {
      if (!this.destroyed) this.loading.set(false);
    }
  }

  setDirection(dir: TradeDirection): void {
    if (this.submitting()) return;
    this.form.controls.direction.setValue(dir);
    this.form.controls.direction.markAsTouched();
  }

  async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const acc = this.selectedAccount();
    const instr = this.selectedInstrument();
    if (!acc || !instr) {
      this.submitError.set('Seleccioná una cuenta y un instrumento válidos.');
      return;
    }

    this.submitting.set(true);
    this.submitError.set(null);
    try {
      const request: OpenTradeRequest = {
        accountId: acc.id,
        instrumentId: instr.id,
        symbol: instr.symbol,
        assetClass: instr.assetClass,
        direction: this.form.controls.direction.value,
        volume: this.form.controls.volume.value,
        volumeCurrency: acc.currency,
        entryPrice: this.form.controls.entryPrice.value,
        entryPriceCurrency: instr.symbol.split('/')[1] ?? 'USD',
        strategy: this.form.controls.strategy.value.trim() || null,
        notes: this.form.controls.notes.value.trim() || null,
      };
      const created = await this.tradeApi.open(request);
      this.saved.emit(created);
    } catch (e) {
      this.submitError.set(this.toMessage(e));
    } finally {
      this.submitting.set(false);
    }
  }

  cancel(): void {
    if (this.submitting()) return;
    this.cancelled.emit();
  }

  isInvalid(control: { invalid: boolean; touched: boolean }): boolean {
    return control.invalid && control.touched;
  }

  assetClassLabel(ac: AssetClass): string {
    return ASSET_CLASS_LABELS[ac];
  }

  private resetForm(): void {
    this.form.reset({
      accountId: '',
      instrumentId: '',
      direction: 1,
      volume: 0,
      entryPrice: 0,
      strategy: '',
      notes: '',
    });
    this.submitError.set(null);
    this.dataError.set(null);
  }

  private stepFor(dp: number): string {
    if (dp <= 0) return '1';
    return '0.' + '0'.repeat(dp - 1) + '1';
  }

  private toMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 409) {
        const detail = error.error?.detail;
        if (typeof detail === 'string' && detail) return detail;
        return 'Conflicto al crear la operación (¿symbol duplicado o cuenta inactiva?).';
      }
      if (error.status === 400 && error.error?.errors) {
        const errors = error.error.errors as Record<string, unknown>;
        const messages: string[] = [];
        for (const field of Object.keys(errors)) {
          const fieldErrors = errors[field];
          if (Array.isArray(fieldErrors)) {
            const flat = fieldErrors
              .map(v => (typeof v === 'string' ? v : JSON.stringify(v)))
              .join(', ');
            messages.push(`${field}: ${flat}`);
          }
        }
        if (messages.length > 0) return messages.join(' · ');
      }
      const detail = error.error?.detail;
      if (typeof detail === 'string' && detail) return detail;
      if (error.status === 0) return 'No se pudo conectar con el servidor.';
      return error.message || `Error HTTP ${error.status}.`;
    }
    if (error instanceof Error && error.message) return error.message;
    if (typeof error === 'string') return error;
    return 'Ocurrió un error inesperado.';
  }
}
