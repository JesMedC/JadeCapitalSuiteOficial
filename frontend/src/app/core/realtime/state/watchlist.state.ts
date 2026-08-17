import { computed, inject, Injectable, signal } from '@angular/core';
import { QuotesSignalRService } from '../quotes-signalr.service';
import { ConnectionState, QuoteUpdate } from '../quotes-signalr.types';

const DEFAULT_WATCHLIST: string[] = ['EURUSD', 'GBPJPY', 'BTCUSD'];

/**
 * Signals-based store for the realtime watchlist. Owns the
 * QuotesSignalRService lifecycle and exposes:
 *   - quotes: Record<symbol, QuoteUpdate>  (current best-known quote per symbol)
 *   - subscribedSymbols: string[]          (currently-subscribed symbols)
 *   - connectionState: 'connected' | 'reconnecting' | 'disconnected' | 'connecting'
 *
 * The watchlist page subscribes on init (with @Input symbols) and
 * unsubscribes on destroy via this state.
 */
@Injectable({ providedIn: 'root' })
export class WatchlistState {
  private readonly svc = inject(QuotesSignalRService);

  private readonly _quotes = signal<Record<string, QuoteUpdate>>({});
  private readonly _subscribed = signal<string[]>([]);
  private readonly _error = signal<string | null>(null);

  readonly quotes = this._quotes.asReadonly();
  readonly subscribedSymbols = this._subscribed.asReadonly();
  readonly connectionState = this.svc.connectionState;
  readonly error = this._error.asReadonly();

  readonly hasQuotes = computed(() => Object.keys(this._quotes()).length > 0);
  readonly watchlistRows = computed(() =>
    this._subscribed().map((symbol) => ({
      symbol,
      quote: this._quotes()[symbol] ?? null,
    })),
  );

  constructor() {
    this.svc.onQuoteUpdate((u) => {
      this._quotes.update((prev) => ({ ...prev, [u.symbol]: u }));
    });
    this.svc.onError((e) => {
      this._error.set(`${e.code}: ${e.message}`);
    });
  }

  async setWatchlist(symbols: string[]): Promise<void> {
    const normalized = Array.from(new Set(symbols.map((s) => s.trim().toUpperCase())));
    const previous = this._subscribed();
    const toAdd = normalized.filter((s) => !previous.includes(s));
    const toRemove = previous.filter((s) => !normalized.includes(s));

    try {
      await this.svc.start();
      if (toRemove.length > 0) await this.svc.unsubscribe(toRemove);
      if (toAdd.length > 0) await this.svc.subscribe(toAdd);
      this._subscribed.set(normalized);
    } catch (err: any) {
      this._error.set(err?.message ?? 'No se pudo suscribir a las cotizaciones.');
    }
  }

  async teardown(): Promise<void> {
    try {
      if (this._subscribed().length > 0) await this.svc.unsubscribe(this._subscribed());
    } catch {
      // best-effort
    }
    await this.svc.stop();
    this._subscribed.set([]);
    this._quotes.set({});
  }

  loadDefault(): Promise<void> {
    return this.setWatchlist(DEFAULT_WATCHLIST);
  }
}