/**
 * Wire shape for SignalR /hubs/quotes broadcasts. Mirrors
 * JadeCapital.Shared.Kernel.Realtime.QuoteUpdate (server). Property names
 * are camelCase (SignalR's JSON convention).
 */

export interface QuoteUpdate {
  symbol: string;
  bid: number;
  ask: number;
  last: number;
  ts: string;
  source: number;
}

export type QuoteSourceByte = 0 | 1 | 2 | 3;

export type ConnectionState =
  | 'disconnected'
  | 'connecting'
  | 'connected'
  | 'reconnecting';

export interface QuoteErrorPayload {
  code: string;
  message: string;
}