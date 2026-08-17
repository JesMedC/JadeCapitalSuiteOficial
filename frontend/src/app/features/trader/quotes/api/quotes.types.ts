/** Wire shape returned by /api/quotes endpoints. Mirrors backend QuoteDto. */

export interface QuoteDto {
  symbol: string;
  bid: number;
  ask: number;
  spread: number;
  volume24h: number;
  timestamp: string;
  source: number;
}

export type QuoteSourceByte = 0 | 1 | 2 | 3;
