import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { WatchlistPage } from '../watchlist-page';
import { WatchlistState } from '@core/realtime/state/watchlist.state';
import { QuoteUpdate } from '@core/realtime/quotes-signalr.types';

jest.spyOn(console, 'warn').mockImplementation(() => {});

/**
 * Fake WatchlistState — uses real Angular Signals so OnPush re-renders
 * fire when the test mutates state. Skips the SignalR HubConnection setup
 * (the real QuotesSignalRService is replaced via the providers below).
 */
class FakeWatchlistState {
  readonly subscribedSymbols: string[] = [];
  readonly connectionState = signal<'connected' | 'reconnecting' | 'disconnected' | 'connecting'>('disconnected');
  readonly error = signal<string | null>(null);
  readonly watchlistRows = signal<{ symbol: string; quote: QuoteUpdate | null }[]>([]);

  async setWatchlist(symbols: string[]): Promise<void> {
    this.subscribedSymbols.splice(0, this.subscribedSymbols.length, ...symbols);
  }

  async teardown(): Promise<void> {
    this.subscribedSymbols.length = 0;
  }

  loadDefault = jest.fn();
}

describe('WatchlistPage', () => {
  let fixture: ComponentFixture<WatchlistPage>;
  let component: WatchlistPage;
  let state: FakeWatchlistState;

  beforeEach(async () => {
    state = new FakeWatchlistState();
    TestBed.configureTestingModule({
      imports: [WatchlistPage],
      providers: [
        provideRouter([]),
        { provide: WatchlistState, useValue: state },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(WatchlistPage);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('symbols', ['EURUSD', 'GBPJPY']);
    fixture.detectChanges();
  });

  it('renders the watchlist title', () => {
    const h1 = fixture.nativeElement.querySelector('.watchlist-title');
    expect(h1?.textContent).toContain('Watchlist');
  });

  it('subscribes to the input symbols on init', () => {
    expect(state.subscribedSymbols).toEqual(['EURUSD', 'GBPJPY']);
  });

  it('renders one row per subscribed symbol', () => {
    state.watchlistRows.set([
      { symbol: 'EURUSD', quote: null },
      { symbol: 'GBPJPY', quote: null },
    ]);
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('.watchlist-row');
    expect(rows).toHaveLength(2);
    expect(rows[0].getAttribute('data-symbol')).toBe('EURUSD');
    expect(rows[1].getAttribute('data-symbol')).toBe('GBPJPY');
  });

  it('renders a live bid/ask when the state has a quote for a symbol', () => {
    state.watchlistRows.set([
      {
        symbol: 'EURUSD',
        quote: {
          symbol: 'EURUSD',
          bid: 1.085,
          ask: 1.0851,
          last: 1.08505,
          ts: '2026-08-19T14:32:00Z',
          source: 0,
        },
      },
    ]);
    fixture.detectChanges();

    const row = fixture.nativeElement.querySelector('.watchlist-row');
    expect(row.textContent).toContain('EURUSD');
    expect(row.textContent).toContain('1.085');
  });

  it('tears down the subscription on destroy', () => {
    component.ngOnDestroy();
    expect(state.subscribedSymbols).toHaveLength(0);
  });
});