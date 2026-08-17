import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, inject, input } from '@angular/core';
import { DecimalPipe, NgClass } from '@angular/common';
import { Router } from '@angular/router';
import { WatchlistState } from '@core/realtime/state/watchlist.state';

const DEFAULT_SYMBOLS: string[] = ['EURUSD', 'GBPJPY', 'BTCUSD', 'USDJPY'];

/**
 * Standalone OnPush page that subscribes to a configurable list of symbols
 * via the realtime SignalR service and renders live bid/ask/spread rows.
 *
 * Mobile-first: rows collapse to a compact card on narrow viewports.
 * On destroy, the component tears down the watchlist subscription so the
 * broadcast service stops polling for symbols the page no longer renders.
 */
@Component({
  selector: 'jcs-watchlist-page',
  standalone: true,
  imports: [DecimalPipe, NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="watchlist">
      <header class="watchlist-header">
        <h1 class="watchlist-title">Watchlist</h1>
        <span
          class="connection-pill"
          [ngClass]="'conn-' + state.connectionState()"
          [attr.data-state]="state.connectionState()"
          data-testid="watchlist-connection">
          {{ state.connectionState() }}
        </span>
      </header>

      @if (state.error(); as err) {
        <div class="watchlist-error" role="alert">{{ err }}</div>
      }

      <div class="watchlist-rows" data-testid="watchlist-rows">
        @for (row of state.watchlistRows(); track row.symbol) {
          <button
            type="button"
            class="watchlist-row"
            (click)="openSymbol(row.symbol)"
            [attr.data-symbol]="row.symbol">
            <span class="symbol">{{ row.symbol }}</span>
            @if (row.quote; as q) {
              <span class="bid jcs-num">{{ q.bid | number: '1.4-5' }}</span>
              <span class="ask jcs-num">{{ q.ask | number: '1.4-5' }}</span>
              <span class="spread jcs-num">{{ q.last | number: '1.4-5' }}</span>
            } @else {
              <span class="bid jcs-num jcs-muted">—</span>
              <span class="ask jcs-num jcs-muted">—</span>
              <span class="spread jcs-num jcs-muted">—</span>
            }
          </button>
        }
      </div>
    </section>
  `,
  styles: [`
    .watchlist { display: flex; flex-direction: column; gap: var(--sp-4); }
    .watchlist-header {
      display: flex; align-items: center; justify-content: space-between;
      gap: var(--sp-3);
    }
    .watchlist-title { margin: 0; font-size: var(--fs-xl); }
    .connection-pill {
      font-size: 0.7rem; padding: 2px var(--sp-2);
      border-radius: var(--radius-sm);
      background: var(--bg-card-soft);
      border: 1px solid var(--border);
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }
    .conn-connected { color: var(--green); border-color: var(--green); }
    .conn-reconnecting { color: var(--amber); border-color: var(--amber); }
    .conn-disconnected { color: var(--red); border-color: var(--red); }

    .watchlist-error {
      padding: var(--sp-3); background: rgba(255, 64, 87, 0.1);
      border: 1px solid var(--red); border-radius: var(--radius-sm);
      color: var(--red); font-size: var(--fs-sm);
    }

    .watchlist-rows {
      display: grid;
      grid-template-columns: 1fr;
      gap: var(--sp-2);
    }
    @media (min-width: 720px) {
      .watchlist-rows { grid-template-columns: 1fr 1fr; }
    }

    .watchlist-row {
      display: grid;
      grid-template-columns: 1.2fr 1fr 1fr 1fr;
      align-items: center;
      gap: var(--sp-3);
      padding: var(--sp-3) var(--sp-4);
      background: var(--bg-card);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      color: var(--text-main);
      font-family: inherit;
      font-size: var(--fs-sm);
      cursor: pointer;
      transition: border-color 150ms;
      text-align: left;
    }
    .watchlist-row:hover { border-color: var(--green); }

    .symbol { font-weight: 600; }
    .bid { color: var(--text-muted); }
    .ask { color: var(--text-muted); }
    .spread { color: var(--green); font-weight: 600; }

    @media (max-width: 480px) {
      .watchlist-row { grid-template-columns: 1.2fr 1fr 1fr; }
      .watchlist-row .spread { display: none; }
    }
  `],
})
export class WatchlistPage implements OnInit, OnDestroy {
  readonly symbols = input<string[]>(DEFAULT_SYMBOLS);
  readonly state = inject(WatchlistState);
  private readonly router = inject(Router);

  readonly connectionState = computed(() => this.state.connectionState());

  async ngOnInit(): Promise<void> {
    await this.state.setWatchlist(this.symbols());
  }

  async ngOnDestroy(): Promise<void> {
    await this.state.teardown();
  }

  openSymbol(symbol: string): void {
    this.router.navigate(['/app/quotes'], { queryParams: { symbol } });
  }
}