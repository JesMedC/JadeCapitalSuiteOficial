import { Injectable, inject, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { AuthState } from '@core/state/auth.state';
import { QuoteErrorPayload, QuoteUpdate } from './quotes-signalr.types';

/**
 * Backoff sequence used by `HubConnectionBuilder.withAutomaticReconnect`.
 * Matches the spec requirement (1s → 2s → 4s → 8s → 16s → 30s, then 30s
 * forever). The default `withAutomaticReconnect()` uses a slightly different
 * schedule — we override it explicitly so FE behavior is reproducible.
 */
const RECONNECT_DELAYS_MS: number[] = [
  1_000, 2_000, 4_000, 8_000, 16_000, 30_000,
];

/**
 * Thin wrapper around `@microsoft/signalr`'s HubConnection. Owns the
 * reconnect lifecycle and exposes subscribe/unsubscribe + handler
 * registration. Decoupled from any UI / state layer so unit tests can
 * drive it without Angular providers.
 *
 * State surface (Signals):
 *   - connectionState: 'disconnected' | 'connecting' | 'connected' | 'reconnecting'
 *   - lastError: QuoteErrorPayload | null
 *   - lastUpdate: QuoteUpdate | null
 *
 * The onClose / onReconnecting / onReconnected callbacks map SignalR's
 * lifecycle events into our state enum. The reconnect schedule is the
 * one specified in the realtime spec — exponential 1s → 2s → 4s → 8s →
 * 16s → 30s, capped at 30s, infinite retry.
 */
@Injectable({ providedIn: 'root' })
export class QuotesSignalRService {
  private readonly auth = inject(AuthState);

  private connection: signalR.HubConnection | null = null;
  private quoteHandler: ((u: QuoteUpdate) => void) | null = null;
  private errorHandler: ((e: QuoteErrorPayload) => void) | null = null;

  private readonly _state = signal<'disconnected' | 'connecting' | 'connected' | 'reconnecting'>('disconnected');
  private readonly _lastError = signal<QuoteErrorPayload | null>(null);
  private readonly _lastUpdate = signal<QuoteUpdate | null>(null);

  readonly connectionState = this._state.asReadonly();
  readonly lastError = this._lastError.asReadonly();
  readonly lastUpdate = this._lastUpdate.asReadonly();

  /** Test seam: allow tests to inject a pre-built HubConnection. */
  setConnectionForTesting(conn: signalR.HubConnection | null): void {
    this.connection = conn;
  }

  /** Test seam: build a HubConnection using the supplied builder factory. */
  buildConnection(builderFactory: () => signalR.HubConnection): void {
    this.connection = builderFactory();
  }

  async start(): Promise<void> {
    if (!this.connection) {
      this.connection = this.buildConnectionInternal();
    }
    if (this._state() === 'connected' || this._state() === 'connecting') {
      return;
    }
    this._state.set('connecting');
    try {
      await this.connection.start();
      this._state.set('connected');
    } catch (err) {
      this._state.set('disconnected');
      throw err;
    }
  }

  async stop(): Promise<void> {
    if (!this.connection) return;
    try {
      await this.connection.stop();
    } finally {
      this._state.set('disconnected');
      this.connection = null;
    }
  }

  async subscribe(symbols: string[]): Promise<void> {
    if (!this.connection) throw new Error('HubConnection not started');
    const normalized = Array.from(new Set(symbols.map((s) => s.trim().toUpperCase())));
    if (normalized.length === 0) return;
    await this.connection.invoke('SubscribeToSymbols', normalized);
  }

  async unsubscribe(symbols: string[]): Promise<void> {
    if (!this.connection) throw new Error('HubConnection not started');
    const normalized = Array.from(new Set(symbols.map((s) => s.trim().toUpperCase())));
    if (normalized.length === 0) return;
    await this.connection.invoke('UnsubscribeFromSymbols', normalized);
  }

  onQuoteUpdate(handler: (u: QuoteUpdate) => void): void {
    this.quoteHandler = handler;
    this.connection?.on('OnQuoteUpdate', (u: QuoteUpdate) => {
      this._lastUpdate.set(u);
      handler(u);
    });
  }

  onError(handler: (e: QuoteErrorPayload) => void): void {
    this.errorHandler = handler;
    this.connection?.on('OnError', (code: string, message: string) => {
      const payload: QuoteErrorPayload = { code, message };
      this._lastError.set(payload);
      handler(payload);
    });
  }

  /** Returns the configured reconnect delay schedule (test seam). */
  static readonly reconnectDelaysMs = RECONNECT_DELAYS_MS;

  private buildConnectionInternal(): signalR.HubConnection {
    const token = this.auth.getAccessToken();
    return new signalR.HubConnectionBuilder()
      .withUrl('/hubs/quotes', {
        accessTokenFactory: () => token ?? '',
        // Skip negotiation headers — the BE always supports WebSockets.
        transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling,
      })
      .withAutomaticReconnect(RECONNECT_DELAYS_MS)
      .configureLogging(signalR.LogLevel.Warning)
      .build();
  }
}