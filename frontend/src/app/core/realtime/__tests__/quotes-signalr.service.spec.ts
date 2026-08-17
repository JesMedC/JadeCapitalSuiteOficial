import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import * as signalR from '@microsoft/signalr';
import { QuotesSignalRService } from '../quotes-signalr.service';
import { QuoteUpdate } from '../quotes-signalr.types';

jest.spyOn(console, 'warn').mockImplementation(() => {});

/**
 * HubConnection mock — captures the registered handler chains so tests
 * can simulate server-pushed OnQuoteUpdate events.
 */
function makeConnectionMock(): signalR.HubConnection & {
  _emit: (event: string, ...args: any[]) => void;
  _invoke: jest.Mock;
  _on: jest.Mock;
  _start: jest.Mock;
  _stop: jest.Mock;
} {
  const handlers = new Map<string, ((...args: any[]) => void)[]>();
  const conn = {
    on: jest.fn((event: string, handler: (...args: any[]) => void) => {
      const list = handlers.get(event) ?? [];
      list.push(handler);
      handlers.set(event, list);
      return conn;
    }),
    off: jest.fn((event: string) => {
      handlers.delete(event);
      return conn;
    }),
    invoke: jest.fn(async (_method: string, _args: any) => undefined),
    start: jest.fn(async () => undefined),
    stop: jest.fn(async () => undefined),
    _emit(event: string, ...args: any[]) {
      for (const h of handlers.get(event) ?? []) h(...args);
    },
  };
  return conn as any;
}

describe('QuotesSignalRService', () => {
  let svc: QuotesSignalRService;
  let conn: ReturnType<typeof makeConnectionMock>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient()],
    });
    svc = TestBed.inject(QuotesSignalRService);
    conn = makeConnectionMock();
    svc.setConnectionForTesting(conn);
  });

  it('starts the underlying HubConnection and flips state to connected', async () => {
    await svc.start();

    expect(conn.start).toHaveBeenCalledTimes(1);
    expect(svc.connectionState()).toBe('connected');
  });

  it('subscribe() forwards normalized + deduped symbols via SubscribeToSymbols', async () => {
    await svc.subscribe(['eurusd', 'EURUSD', 'GBPJPY', 'gbpjpy']);

    expect(conn.invoke).toHaveBeenCalledTimes(1);
    const [method, symbols] = conn.invoke.mock.calls[0];
    expect(method).toBe('SubscribeToSymbols');
    expect(symbols).toEqual(['EURUSD', 'GBPJPY']);
  });

  it('subscribe() with empty array is a no-op (no server round-trip)', async () => {
    await svc.subscribe([]);

    expect(conn.invoke).not.toHaveBeenCalled();
  });

  it('unsubscribe() routes through UnsubscribeFromSymbols with normalized symbols', async () => {
    await svc.unsubscribe([' eurusd ', 'EURUSD']);

    expect(conn.invoke).toHaveBeenCalledWith('UnsubscribeFromSymbols', ['EURUSD']);
  });

  it('onQuoteUpdate() captures server-pushed QuoteUpdate payloads', async () => {
    const updates: QuoteUpdate[] = [];
    svc.onQuoteUpdate((u) => updates.push(u));

    const fixture: QuoteUpdate = {
      symbol: 'EURUSD',
      bid: 1.085,
      ask: 1.0851,
      last: 1.08505,
      ts: '2026-08-19T14:32:00Z',
      source: 0,
    };
    conn._emit('OnQuoteUpdate', fixture);

    expect(updates).toEqual([fixture]);
    expect(svc.lastUpdate()).toEqual(fixture);
  });

  it('onError() captures server-pushed errors', async () => {
    const errors: { code: string; message: string }[] = [];
    svc.onError((e) => errors.push(e));

    conn._emit('OnError', 'invalid.symbol', 'EUR/USD is not a known symbol');

    expect(errors).toEqual([{ code: 'invalid.symbol', message: 'EUR/USD is not a known symbol' }]);
    expect(svc.lastError()).toEqual({ code: 'invalid.symbol', message: 'EUR/USD is not a known symbol' });
  });

  it('exposes the spec-mandated reconnect delay schedule', () => {
    expect(QuotesSignalRService.reconnectDelaysMs).toEqual([1_000, 2_000, 4_000, 8_000, 16_000, 30_000]);
  });

  it('stop() tears down the HubConnection and resets state', async () => {
    await svc.start();
    expect(svc.connectionState()).toBe('connected');

    await svc.stop();

    expect(conn.stop).toHaveBeenCalledTimes(1);
    expect(svc.connectionState()).toBe('disconnected');
  });
});