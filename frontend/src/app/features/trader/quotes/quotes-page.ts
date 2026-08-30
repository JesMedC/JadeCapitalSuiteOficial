import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { DecimalPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { QuotesState } from './state/quotes.state';
import { QuoteDto } from './api/quotes.types';

@Component({
  selector: 'jcs-quotes-page',
  standalone: true,
  imports: [FormsModule, DecimalPipe, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="quotes-page">
      <header class="quotes-header">
        <h1 class="quotes-title">Cotizaciones</h1>
        <p class="quotes-subtitle jcs-muted">
          Quotes en vivo de los pares seguidos. Refrescá manual o usá el filtro por symbol.
        </p>
      </header>

      @if (state.error(); as err) {
        <div class="jcs-card jcs-card--soft quotes-error" role="alert">
          <strong>Error:</strong> {{ err }}
          <button type="button" class="jcs-btn jcs-btn--ghost jcs-btn--sm" (click)="state.clearError()">Cerrar</button>
        </div>
      }

      <div class="quotes-toolbar">
        <form class="filter-form" (submit)="$event.preventDefault(); addSymbol()">
          <input type="text" placeholder="Symbol (e.g. EURUSD)"
                 [value]="newSymbol()"
                 (input)="newSymbol.set(asInputValue($event))"
                 maxlength="20" />
          <button type="submit" class="jcs-btn jcs-btn--primary jcs-btn--sm" [disabled]="!canAdd()">Agregar</button>
        </form>
        <button type="button" class="jcs-btn jcs-btn--ghost jcs-btn--sm" (click)="refresh()">
          @if (state.isLoading()) { Cargando... } @else { Refrescar }
        </button>
      </div>

      @if (state.isLoading() && !state.hasQuotes()) {
        <p class="jcs-muted">Cargando cotizaciones...</p>
      }

      @if (!state.isLoading() && !state.hasQuotes()) {
        <div class="empty-state jcs-card">
          <p class="jcs-muted">Sin cotizaciones. Agregá un symbol arriba.</p>
        </div>
      }

      <div class="jcs-table-scroll">
        <table class="jcs-table">
          <thead>
            <tr>
              <th>Symbol</th>
              <th>Bid</th>
              <th>Ask</th>
              <th>Spread</th>
              <th>Vol 24h</th>
              <th>Timestamp (UTC)</th>
            </tr>
          </thead>
          <tbody>
            @for (q of state.quotes(); track q.symbol) {
              <tr>
                <td><strong>{{ q.symbol }}</strong></td>
                <td class="jcs-num">{{ q.bid | number:'1.4-6' }}</td>
                <td class="jcs-num">{{ q.ask | number:'1.4-6' }}</td>
                <td class="jcs-num">{{ q.spread | number:'1.4-8' }}</td>
                <td class="jcs-num">{{ q.volume24h | number:'1.0-0' }}</td>
                <td class="jcs-muted jcs-small">{{ q.timestamp | date:'HH:mm:ss':'UTC' }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </section>
  `,
  styles: [`
    :host { display: block; }
    .quotes-page { padding: var(--sp-6); max-width: 1100px; margin: 0 auto; }
    .quotes-header { margin-bottom: var(--sp-6); }
    .quotes-title { margin: 0 0 var(--sp-2); font-size: var(--fs-3xl); font-weight: 700; }
    .quotes-subtitle { margin: 0; font-size: var(--fs-sm); }
    .quotes-toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: var(--sp-4); gap: var(--sp-3); flex-wrap: wrap; }
    .filter-form { display: flex; gap: var(--sp-2); align-items: center; }
    .filter-form input {
      padding: var(--sp-2) var(--sp-3);
      background: var(--bg-elevated);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      color: var(--text-main);
      font-family: inherit;
      min-width: 180px;
    }
    .empty-state { text-align: center; padding: var(--sp-8); }
    .jcs-num { font-variant-numeric: tabular-nums; }
    .jcs-small { font-size: var(--fs-xs); }
    @media (max-width: 767px) {
      .quotes-page { padding: var(--sp-4); }
    }
  `],
})
export class QuotesPage {
  readonly state = inject(QuotesState);
  readonly newSymbol = signal('');

  constructor() {
    this.state.loadDefault();
  }

  canAdd(): boolean {
    const v = this.newSymbol().trim();
    return v.length > 0 && !this.state.subscribedSymbols().includes(v.toUpperCase());
  }

  async addSymbol(): Promise<void> {
    const raw = this.newSymbol().trim();
    if (!raw) return;
    const list = [...this.state.subscribedSymbols(), raw.toUpperCase()];
    this.state.subscribedSymbols.set(list);
    this.newSymbol.set('');
    await this.state.loadSymbols(list);
  }

  async refresh(): Promise<void> {
    await this.state.loadSymbols(this.state.subscribedSymbols());
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
  }
}
