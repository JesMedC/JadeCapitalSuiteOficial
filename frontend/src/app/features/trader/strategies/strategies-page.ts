import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
} from '@angular/core';
import { StrategiesState } from './state/strategies.state';
import {
  DESCRIPTION_MAX,
  NAME_MAX,
  RULES_MAX,
  StrategyDto,
  TIMEFRAMES,
  TIMEFRAME_LABELS,
  TimeframeByte,
  UpsertStrategyRequest,
} from './api/strategies.types';

// ============================================================================
//  StrategiesPage — slice 3a frontend (3a.2).
//
//  Mobile-first standalone page (Signals + OnPush + SCSS). Layout:
//   - Header: title "Strategies" + subtitle.
//   - "Nueva strategy" button → opens the create form (in-page modal-ish).
//   - List of cards (one per active strategy). Each card shows name, symbol,
//     timeframe, description preview, and Archivar button.
//   - Clicking a card expands analytics inline (count, winRate, totalP&L,
//     expectancy, profitFactor, avgMfe, avgMae).
//   - Create form: name (required), description (optional), symbol (free-text,
//     instrument identifier; the backend validates against trading.instruments),
//     timeframe dropdown, rules textarea. Inline counters with over-cap warning.
//
//  State:
//   - list() → cards; analytics fetched on expand.
//   - isLoading()/error()/isSaving() → banners/spinner.
//   - createOpen() → toggles the inline form.
// ============================================================================

@Component({
  selector: 'jcs-strategies-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sp-page">
      <header class="sp-head">
        <p class="jcs-muted sp-eyebrow">Setups del trader</p>
        <h1 class="sp-title">Strategies</h1>
        <p class="jcs-muted sp-sub">Configuraciones nombradas: instrumento, timeframe, reglas.</p>
      </header>

      <!-- ============== Error banner ============== -->
      @if (state.error(); as err) {
        <div class="sp-error" role="alert" data-testid="strategies-error">
          <span>{{ err }}</span>
          <button type="button" class="sp-error-dismiss" (click)="state.clearError()">×</button>
        </div>
      }

      <!-- ============== Loading ============== -->
      @if (state.isLoading()) {
        <div class="sp-loading" aria-live="polite">
          <span class="sp-spinner" aria-hidden="true"></span>
          <span class="jcs-muted">Cargando strategies…</span>
        </div>
      }

      <!-- ============== Empty state ============== -->
      @if (!state.isLoading() && state.list().length === 0 && !state.error()) {
        <div class="sp-empty" data-testid="strategies-empty">
          <p>Aún no creaste ninguna strategy.</p>
          <p class="jcs-muted">Una strategy describe tu setup: instrumento, timeframe y reglas de entrada.</p>
        </div>
      }

      <!-- ============== Create / Edit form ============== -->
      @if (createOpen()) {
        <section class="sp-form" data-testid="strategies-form">
          <h2 class="sp-form-title">Nueva strategy</h2>
          <form (submit)="$event.preventDefault(); onCreate()">
            <label class="sp-field">
              <span class="sp-label">Nombre</span>
              <input
                #nameInput
                type="text"
                class="sp-input"
                [attr.maxlength]="NAME_MAX"
                [value]="formName()"
                (input)="formName.set($any(nameInput).value)"
                required
                data-testid="strategies-form-name" />
              <span class="sp-counter" [class.jcs-counter--over]="formName().length > NAME_MAX">
                {{ formName().length }} / {{ NAME_MAX }}
              </span>
            </label>

            <label class="sp-field">
              <span class="sp-label">Descripción (opcional)</span>
              <textarea
                class="sp-textarea"
                rows="2"
                [attr.maxlength]="DESCRIPTION_MAX"
                [value]="formDescription()"
                (input)="formDescription.set($any($event.target).value)"
                data-testid="strategies-form-description"></textarea>
              <span class="sp-counter" [class.jcs-counter--over]="formDescription().length > DESCRIPTION_MAX">
                {{ formDescription().length }} / {{ DESCRIPTION_MAX }}
              </span>
            </label>

            <label class="sp-field">
              <span class="sp-label">Símbolo (opcional, ej. EUR/USD)</span>
              <input
                type="text"
                class="sp-input"
                [value]="formSymbol()"
                (input)="formSymbol.set($any($event.target).value.toUpperCase())"
                data-testid="strategies-form-symbol" />
            </label>

            <label class="sp-field">
              <span class="sp-label">Timeframe</span>
              <select
                class="sp-input"
                [value]="formTimeframe()"
                (change)="onTimeframeChange($any($event.target).value)"
                data-testid="strategies-form-timeframe">
                <option [value]="''">— Sin especificar —</option>
                @for (tf of TIMEFRAMES; track tf) {
                  <option [value]="tf">{{ TIMEFRAME_LABELS[tf] }}</option>
                }
              </select>
            </label>

            <label class="sp-field">
              <span class="sp-label">Reglas (opcional)</span>
              <textarea
                class="sp-textarea"
                rows="3"
                [attr.maxlength]="RULES_MAX"
                [value]="formRules()"
                (input)="formRules.set($any($event.target).value)"
                data-testid="strategies-form-rules"></textarea>
              <span class="sp-counter" [class.jcs-counter--over]="formRules().length > RULES_MAX">
                {{ formRules().length }} / {{ RULES_MAX }}
              </span>
            </label>

            <div class="sp-actions">
              <button
                type="submit"
                class="sp-btn sp-btn--primary"
                [disabled]="state.isSaving() || !isFormValid()"
                data-testid="strategies-form-submit">
                @if (state.isSaving()) { Guardando… } @else { Crear }
              </button>
              <button
                type="button"
                class="sp-btn sp-btn--ghost"
                (click)="cancelCreate()">
                Cancelar
              </button>
            </div>
          </form>
        </section>
      } @else if (!state.isLoading()) {
        <div class="sp-toolbar">
          <button
            type="button"
            class="sp-btn sp-btn--primary"
            (click)="openCreate()"
            data-testid="strategies-new">
            + Nueva strategy
          </button>
        </div>
      }

      <!-- ============== Cards list ============== -->
      <ul class="sp-list" data-testid="strategies-list">
        @for (s of state.list(); track s.id) {
          <li class="sp-card" [class.sp-card--expanded]="expandedId() === s.id">
            <button
              type="button"
              class="sp-card-head"
              (click)="toggleExpand(s)"
              [attr.aria-expanded]="expandedId() === s.id">
              <div class="sp-card-title-row">
                <span class="sp-card-name">{{ s.name }}</span>
                <span class="sp-card-meta">
                  @if (s.symbol) { <span class="sp-tag">{{ s.symbol }}</span> }
                  @if (s.timeframe) { <span class="sp-tag">{{ TIMEFRAME_LABELS[s.timeframe] }}</span> }
                </span>
              </div>
              @if (s.description) {
                <p class="jcs-muted sp-card-desc">{{ s.description }}</p>
              }
            </button>

            @if (expandedId() === s.id) {
              <div class="sp-card-body" data-testid="strategies-analytics">
                @if (analyticsLoadingId() === s.id) {
                  <span class="jcs-muted">Cargando analytics…</span>
                } @else {
                  @if (analyticsFor(s.id); as a) {
                    <div class="sp-stats">
                      <div class="sp-stat">
                        <span class="sp-stat-num">{{ a.tradeCount }}</span>
                        <span class="sp-stat-lbl">Trades</span>
                      </div>
                      <div class="sp-stat">
                        <span class="sp-stat-num">{{ (a.winRate * 100).toFixed(1) }}%</span>
                        <span class="sp-stat-lbl">Win rate</span>
                      </div>
                      <div class="sp-stat">
                        <span class="sp-stat-num">{{ a.totalPnl }}</span>
                        <span class="sp-stat-lbl">P&amp;L total</span>
                      </div>
                      <div class="sp-stat">
                        <span class="sp-stat-num">{{ a.expectancy }}</span>
                        <span class="sp-stat-lbl">Expectancy</span>
                      </div>
                      <div class="sp-stat">
                        <span class="sp-stat-num">{{ a.profitFactor }}</span>
                        <span class="sp-stat-lbl">Profit factor</span>
                      </div>
                      @if (a.avgMfe !== null) {
                        <div class="sp-stat">
                          <span class="sp-stat-num">{{ a.avgMfe }}</span>
                          <span class="sp-stat-lbl">Avg MFE</span>
                        </div>
                      }
                      @if (a.avgMae !== null) {
                        <div class="sp-stat">
                          <span class="sp-stat-num">{{ a.avgMae }}</span>
                          <span class="sp-stat-lbl">Avg MAE</span>
                        </div>
                      }
                    </div>
                  } @else {
                    <span class="jcs-muted">Sin analytics todavía.</span>
                  }
                }

                <div class="sp-card-actions">
                  <button
                    type="button"
                    class="sp-btn sp-btn--danger"
                    (click)="onArchive(s)"
                    [disabled]="state.isSaving()"
                    data-testid="strategies-archive">
                    Archivar
                  </button>
                </div>
              </div>
            }
          </li>
        }
      </ul>
    </div>
  `,
  styles: [`
    :host { display: block; }

    .sp-page {
      display: flex;
      flex-direction: column;
      gap: var(--sp-4, 16px);
      max-width: 960px;
    }

    .sp-head { margin-bottom: var(--sp-2, 8px); }
    .sp-eyebrow { font-size: 0.7rem; text-transform: uppercase; letter-spacing: 0.08em; margin: 0; }
    .sp-title { font-size: 1.8rem; font-weight: 700; margin: var(--sp-1, 4px) 0; letter-spacing: -0.02em; }
    .sp-sub { margin: 0; font-size: 0.9rem; }

    .sp-error {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: var(--sp-3, 12px) var(--sp-4, 16px);
      background: rgba(255, 64, 87, 0.12);
      border: 1px solid var(--red, #ff4057);
      border-radius: var(--radius-md, 8px);
      color: var(--red, #ff4057);
      font-size: var(--fs-sm, 0.875rem);
    }
    .sp-error-dismiss {
      background: transparent;
      border: 0;
      color: inherit;
      cursor: pointer;
      font-size: 1.2rem;
      line-height: 1;
    }

    .sp-loading {
      display: flex;
      align-items: center;
      gap: var(--sp-3, 12px);
      padding: var(--sp-4, 16px);
    }
    .sp-spinner {
      display: inline-block;
      width: 16px;
      height: 16px;
      border: 2px solid var(--border, #e5e5e5);
      border-top-color: var(--green, #2fdb78);
      border-radius: 50%;
      animation: sp-rotate 0.7s linear infinite;
    }
    @keyframes sp-rotate { to { transform: rotate(360deg); } }

    .sp-empty {
      padding: var(--sp-5, 24px);
      background: var(--bg-card-soft, #f7f7f7);
      border: 1px dashed var(--border, #e5e5e5);
      border-radius: var(--radius-md, 8px);
      text-align: center;
    }
    .sp-empty p { margin: 0; }
    .sp-empty p + p { margin-top: var(--sp-2, 8px); }

    .sp-toolbar {
      display: flex;
      justify-content: flex-end;
    }

    .sp-btn {
      display: inline-flex;
      align-items: center;
      gap: var(--sp-2, 8px);
      padding: var(--sp-2, 8px) var(--sp-4, 16px);
      border: 1px solid transparent;
      border-radius: var(--radius-sm, 6px);
      font-size: var(--fs-sm, 0.875rem);
      font-weight: 500;
      cursor: pointer;
      transition: background 150ms;
    }
    .sp-btn--primary {
      background: var(--green, #2fdb78);
      color: #050B10;
    }
    .sp-btn--primary:hover { background: var(--green-hover, #28c068); }
    .sp-btn--primary:disabled { opacity: 0.5; cursor: not-allowed; }
    .sp-btn--ghost {
      background: transparent;
      color: var(--text-main, #1a1a1a);
      border-color: var(--border, #e5e5e5);
    }
    .sp-btn--ghost:hover { background: var(--bg-hover, #f0f0f0); }
    .sp-btn--danger {
      background: transparent;
      color: var(--red, #ff4057);
      border-color: var(--red, #ff4057);
    }
    .sp-btn--danger:hover { background: rgba(255, 64, 87, 0.12); }

    .sp-form {
      padding: var(--sp-4, 16px);
      background: var(--bg-card, #fff);
      border: 1px solid var(--border, #e5e5e5);
      border-radius: var(--radius-md, 8px);
      display: flex;
      flex-direction: column;
      gap: var(--sp-3, 12px);
    }
    .sp-form-title { font-size: 1rem; font-weight: 600; margin: 0; }

    .sp-field { display: flex; flex-direction: column; gap: var(--sp-1, 4px); }
    .sp-label { font-size: 0.75rem; font-weight: 600; color: var(--text-muted, #777); text-transform: uppercase; letter-spacing: 0.06em; }
    .sp-input,
    .sp-textarea {
      padding: var(--sp-2, 8px) var(--sp-3, 12px);
      border: 1px solid var(--border, #e5e5e5);
      border-radius: var(--radius-sm, 6px);
      background: var(--bg-input, #fff);
      color: var(--text-main, #1a1a1a);
      font-size: var(--fs-sm, 0.875rem);
      font-family: inherit;
      resize: vertical;
    }
    .sp-input:focus,
    .sp-textarea:focus { outline: 2px solid var(--green, #2fdb78); outline-offset: -1px; }

    .sp-counter {
      align-self: flex-end;
      font-size: 0.7rem;
      color: var(--text-muted, #777);
    }
    .jcs-counter--over { color: var(--red, #ff4057); font-weight: 600; }

    .sp-actions { display: flex; gap: var(--sp-2, 8px); justify-content: flex-end; }

    .sp-list {
      display: flex;
      flex-direction: column;
      gap: var(--sp-2, 8px);
      padding: 0;
      margin: 0;
      list-style: none;
    }
    .sp-card {
      background: var(--bg-card, #fff);
      border: 1px solid var(--border, #e5e5e5);
      border-radius: var(--radius-md, 8px);
      overflow: hidden;
      transition: border-color 150ms;
    }
    .sp-card:hover { border-color: var(--border-active, #c0c0c0); }
    .sp-card--expanded { border-color: var(--green, #2fdb78); }

    .sp-card-head {
      width: 100%;
      display: flex;
      flex-direction: column;
      gap: var(--sp-1, 4px);
      padding: var(--sp-3, 12px) var(--sp-4, 16px);
      background: transparent;
      border: 0;
      text-align: left;
      cursor: pointer;
      color: inherit;
    }
    .sp-card-title-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--sp-3, 12px);
    }
    .sp-card-name { font-weight: 600; font-size: var(--fs-base, 1rem); }
    .sp-card-meta { display: inline-flex; gap: var(--sp-1, 4px); }
    .sp-tag {
      display: inline-block;
      padding: 2px var(--sp-2, 8px);
      background: var(--bg-card-soft, #f7f7f7);
      border: 1px solid var(--border, #e5e5e5);
      border-radius: 999px;
      font-size: 0.7rem;
      color: var(--text-muted, #777);
    }
    .sp-card-desc { margin: 0; font-size: 0.85rem; }

    .sp-card-body {
      padding: var(--sp-3, 12px) var(--sp-4, 16px);
      border-top: 1px solid var(--border, #e5e5e5);
      background: var(--bg-card-soft, #f7f7f7);
      display: flex;
      flex-direction: column;
      gap: var(--sp-3, 12px);
    }

    .sp-stats {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(110px, 1fr));
      gap: var(--sp-2, 8px);
    }
    .sp-stat {
      display: flex;
      flex-direction: column;
      padding: var(--sp-2, 8px);
      background: var(--bg-card, #fff);
      border: 1px solid var(--border, #e5e5e5);
      border-radius: var(--radius-sm, 6px);
    }
    .sp-stat-num { font-weight: 700; font-size: 1rem; }
    .sp-stat-lbl { font-size: 0.7rem; color: var(--text-muted, #777); }

    .sp-card-actions { display: flex; justify-content: flex-end; }
  `],
})
export class StrategiesPage {
  readonly state = inject(StrategiesState);

  protected readonly NAME_MAX = NAME_MAX;
  protected readonly DESCRIPTION_MAX = DESCRIPTION_MAX;
  protected readonly RULES_MAX = RULES_MAX;
  protected readonly TIMEFRAMES = TIMEFRAMES;
  protected readonly TIMEFRAME_LABELS = TIMEFRAME_LABELS;

  readonly createOpen = signal(false);
  readonly expandedId = signal<string | null>(null);
  readonly analyticsLoadingId = signal<string | null>(null);

  readonly formName = signal('');
  readonly formDescription = signal('');
  readonly formSymbol = signal('');
  readonly formTimeframe = signal<TimeframeByte | null>(null);
  readonly formRules = signal('');

  readonly isFormValid = computed(() => {
    const name = this.formName().trim();
    return name.length >= 1 && name.length <= NAME_MAX;
  });

  constructor() {
    void this.state.loadAll(true);
  }

  openCreate(): void {
    this.resetForm();
    this.createOpen.set(true);
  }

  cancelCreate(): void {
    this.createOpen.set(false);
    this.resetForm();
  }

  async onCreate(): Promise<void> {
    if (!this.isFormValid()) return;
    const req: UpsertStrategyRequest = {
      name: this.formName().trim(),
      description: this.formDescription().trim() || null,
      symbol: this.formSymbol().trim() || null,
      timeframe: this.formTimeframe(),
      rules: this.formRules().trim() || null,
    };

    const saved = await this.state.create(req);
    if (saved) {
      this.createOpen.set(false);
      this.resetForm();
    }
  }

  async toggleExpand(s: StrategyDto): Promise<void> {
    const current = this.expandedId();
    if (current === s.id) {
      this.expandedId.set(null);
      return;
    }
    this.expandedId.set(s.id);
    if (!this.state.analyticsById()[s.id]) {
      this.analyticsLoadingId.set(s.id);
      await this.state.ensureAnalytics(s.id);
      this.analyticsLoadingId.set(null);
    }
  }

  analyticsFor(id: string) {
    return this.state.analyticsById()[id] ?? null;
  }

  async onArchive(s: StrategyDto): Promise<void> {
    if (!confirm(`¿Archivar la strategy "${s.name}"?`)) return;
    await this.state.archive(s.id);
  }

  onTimeframeChange(raw: string): void {
    if (!raw) {
      this.formTimeframe.set(null);
      return;
    }
    const parsed = Number(raw);
    if (parsed >= 1 && parsed <= 9) {
      this.formTimeframe.set(parsed as TimeframeByte);
    } else {
      this.formTimeframe.set(null);
    }
  }

  private resetForm(): void {
    this.formName.set('');
    this.formDescription.set('');
    this.formSymbol.set('');
    this.formTimeframe.set(null);
    this.formRules.set('');
  }
}