import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DecimalPipe } from '@angular/common';
import { ScannerState } from './state/scanner.state';
import { ScannerFilterDto, UpsertScannerFilterRequest, VOLATILITY_LABELS } from './api/scanner.types';

@Component({
  selector: 'jcs-scanner-page',
  standalone: true,
  imports: [FormsModule, DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="scanner-page">
      <header class="scanner-header">
        <h1 class="scanner-title">Scanner de instrumentos</h1>
        <p class="scanner-subtitle jcs-muted">
          Filtros guardados por vos para correr contra el universo de instrumentos.
          <span class="todo">Spread/volume se activan en Wave 4b.</span>
        </p>
      </header>

      @if (state.error(); as err) {
        <div class="jcs-card jcs-card--soft planner-error" role="alert">
          <strong>Error:</strong> {{ err }}
          <button type="button" class="jcs-btn jcs-btn--ghost jcs-btn--sm" (click)="state.clearError()">Cerrar</button>
        </div>
      }

      <div class="scanner-new">
        <button type="button" class="jcs-btn jcs-btn--primary" (click)="toggleNew()">
          @if (showNew()) { Cancelar } @else { + Nuevo filtro }
        </button>
      </div>

      @if (showNew()) {
        <form class="new-form jcs-card" (submit)="$event.preventDefault(); saveNew()">
          <div class="form-row">
            <label>Nombre</label>
            <input type="text" required maxlength="64" placeholder="Momentum EUR/USD"
                   [value]="newName()"
                   (input)="newName.set(asInputValue($event))" />
          </div>
          <div class="form-row form-row--double">
            <div>
              <label>Min R/R (≥ 1.0)</label>
              <input type="number" step="0.1" min="1.0"
                     [value]="newMinRiskReward()"
                     (input)="newMinRiskReward.set(asNumber($event))" />
            </div>
            <div>
              <label>Volatilidad (días)</label>
              <select [value]="newVolatilityWindow()"
                      (change)="newVolatilityWindow.set(asNumber($event))">
                <option [value]="1">{{ volatilityLabel(1) }}</option>
                <option [value]="7">{{ volatilityLabel(7) }}</option>
                <option [value]="30">{{ volatilityLabel(30) }}</option>
              </select>
            </div>
          </div>
          <div class="form-row form-row--double">
            <div>
              <label>Min Spread (TODO 4b)</label>
              <input type="number" step="0.00001" min="0" disabled
                     [value]="newMinSpread()"
                     (input)="newMinSpread.set(asNumber($event))" />
            </div>
            <div>
              <label>Min Volume (TODO 4b)</label>
              <input type="number" step="0.01" min="0" disabled
                     [value]="newMinVolume()"
                     (input)="newMinVolume.set(asNumber($event))" />
            </div>
          </div>
          <div class="form-actions">
            <button type="submit" class="jcs-btn jcs-btn--primary" [disabled]="state.isSaving()">
              @if (state.isSaving()) { Guardando... } @else { Guardar filtro }
            </button>
          </div>
        </form>
      }

      @if (state.isLoading() && state.filters().length === 0) {
        <p class="jcs-muted">Cargando filtros...</p>
      }

      @if (!state.isLoading() && state.filters().length === 0 && state.hasFilters() === false) {
        <div class="empty-state jcs-card">
          <p class="jcs-muted">No hay filtros guardados. Tocá "+ Nuevo filtro" para empezar.</p>
        </div>
      }

      <ul class="filter-list">
        @for (f of state.filters(); track f.id) {
          <li class="filter-card jcs-card">
            <div class="filter-head">
              <strong class="filter-name">{{ f.name }}</strong>
              <span class="vol-badge">{{ volatilityLabel(f.volatilityWindow) }}</span>
              @if (f.minRiskReward !== null) {
                <span class="vol-badge">R/R ≥ {{ f.minRiskReward }}</span>
              }
            </div>
            <div class="filter-actions">
              <button type="button" class="jcs-btn jcs-btn--primary jcs-btn--sm" (click)="runScanner(f)">
                Correr ahora
              </button>
              <button type="button" class="jcs-btn jcs-btn--ghost jcs-btn--sm" (click)="deleteFilter(f.id)">
                Borrar
              </button>
            </div>
          </li>
        }
      </ul>

      @if (state.results().length > 0) {
        <h2 class="results-title">Resultados del scan</h2>
        <div class="jcs-table-scroll">
          <table class="jcs-table">
            <thead>
              <tr>
                <th>Symbol</th>
                <th>R/R Histórico</th>
                <th>Trades</th>
                <th>P&amp;L Total</th>
                <th>Criterios</th>
              </tr>
            </thead>
            <tbody>
              @for (r of state.results(); track r.symbol) {
                <tr>
                  <td><strong>{{ r.symbol }}</strong></td>
                  <td class="jcs-num">{{ r.historicalRiskReward | number:'1.2-2' }}</td>
                  <td class="jcs-num">{{ r.totalTrades }}</td>
                  <td class="jcs-num"
                      [class.pos]="r.totalPnl > 0"
                      [class.neg]="r.totalPnl < 0">
                    {{ r.totalPnl | number:'1.2-2' }}
                  </td>
                  <td class="jcs-muted jcs-small">{{ r.matchedCriteria.join(', ') }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      @if (state.results().length === 0 && !state.isLoading() && !state.error()) {
        <p class="jcs-muted results-empty">Corré un filtro para ver resultados acá.</p>
      }
    </section>
  `,
  styles: [`
    :host { display: block; }
    .scanner-page { padding: var(--sp-6); max-width: 1100px; margin: 0 auto; }
    .scanner-header { margin-bottom: var(--sp-6); }
    .scanner-title { margin: 0 0 var(--sp-2); font-size: var(--fs-3xl); font-weight: 700; }
    .scanner-subtitle { margin: 0; font-size: var(--fs-sm); }
    .todo { color: var(--text-soft); font-style: italic; }
    .scanner-new { margin-bottom: var(--sp-4); }
    .new-form { display: flex; flex-direction: column; gap: var(--sp-3); margin-bottom: var(--sp-4); }
    .form-row { display: flex; flex-direction: column; gap: var(--sp-2); }
    .form-row--double { display: grid; grid-template-columns: 1fr 1fr; gap: var(--sp-3); }
    .form-row label { font-size: 0.65rem; text-transform: uppercase; letter-spacing: 0.08em; color: var(--text-muted); }
    .form-row input, .form-row select {
      padding: var(--sp-2) var(--sp-3);
      background: var(--bg-elevated);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      color: var(--text-main);
      font-family: inherit;
    }
    .form-row input:focus, .form-row select:focus { outline: 2px solid var(--border-active); outline-offset: 2px; }
    .form-row input[disabled] { opacity: 0.5; cursor: not-allowed; }
    .form-actions { display: flex; justify-content: flex-end; }
    .empty-state { text-align: center; padding: var(--sp-8); }
    .filter-list { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: var(--sp-3); margin-bottom: var(--sp-6); }
    .filter-card { display: flex; justify-content: space-between; align-items: center; gap: var(--sp-4); padding: var(--sp-4); }
    .filter-head { display: flex; align-items: center; gap: var(--sp-3); flex-wrap: wrap; }
    .filter-name { font-size: var(--fs-base); }
    .vol-badge { padding: 2px var(--sp-2); background: var(--bg-elevated); border-radius: var(--radius-xs); font-size: 0.65rem; color: var(--text-muted); }
    .filter-actions { display: flex; gap: var(--sp-2); }
    .results-title { margin: var(--sp-6) 0 var(--sp-3); font-size: var(--fs-xl); }
    .results-empty { text-align: center; padding: var(--sp-6); }
    .pos { color: var(--green); }
    .neg { color: var(--red); }
    .jcs-num { font-variant-numeric: tabular-nums; }
    .jcs-small { font-size: var(--fs-xs); }
    @media (max-width: 767px) {
      .scanner-page { padding: var(--sp-4); }
      .form-row--double { grid-template-columns: 1fr; }
    }
  `],
})
export class ScannerPage {
  readonly state = inject(ScannerState);

  readonly showNew = signal(false);
  readonly newName = signal('');
  readonly newMinRiskReward = signal(1.5);
  readonly newVolatilityWindow = signal(7);
  readonly newMinSpread = signal<number | null>(null);
  readonly newMinVolume = signal<number | null>(null);

  constructor() {
    this.state.loadFilters();
  }

  toggleNew(): void { this.showNew.update(v => !v); }

  async saveNew(): Promise<void> {
    if (!this.newName().trim()) return;
    const body: UpsertScannerFilterRequest = {
      name: this.newName().trim(),
      minSpread: this.newMinSpread(),
      maxSpread: null,
      minVolume: this.newMinVolume(),
      minRiskReward: this.newMinRiskReward(),
      volatilityWindow: this.newVolatilityWindow() as 1 | 7 | 30,
      activeHours: null,
    };
    const ok = await this.state.saveFilter(body);
    if (ok) {
      this.showNew.set(false);
      this.newName.set('');
      this.newMinRiskReward.set(1.5);
      this.newVolatilityWindow.set(7);
    }
  }

  async runScanner(filter: ScannerFilterDto): Promise<void> {
    await this.state.run({ filterId: filter.id, limit: 20 });
  }

  async deleteFilter(id: string): Promise<void> {
    await this.state.deleteFilter(id);
  }

  volatilityLabel(w: number): string {
    const key = w as 1 | 7 | 30;
    return VOLATILITY_LABELS[key] ?? `${w}d`;
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value;
  }

  asNumber(event: Event): number {
    const raw = (event.target as HTMLInputElement | HTMLSelectElement).value;
    const n = Number(raw);
    return isNaN(n) ? 0 : n;
  }
}
