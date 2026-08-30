// ============================================================================
//  Strategies API types — slice 3a frontend.
//
//  Mirror of:
//   src/2.Modules/Trading/JadeCapital.Trading.Contracts/Strategies/StrategyDtos.cs
//
//  System.Text.Json serializes the Timeframe enum as its underlying byte
//  on the wire. The FE maps byte → label (M1..MN) via TIMEFRAME_LABELS.
// ============================================================================

/** Timeframe byte from the wire. 1..9 maps to M1..MN; null = unspecified. */
export type TimeframeByte = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9;

/** Human-readable Timeframe labels keyed by the wire byte (1..9). */
export const TIMEFRAME_LABELS: Record<TimeframeByte, string> = {
  1: 'M1',
  2: 'M5',
  3: 'M15',
  4: 'M30',
  5: 'H1',
  6: 'H4',
  7: 'D1',
  8: 'W1',
  9: 'MN',
};

/** All valid timeframes for dropdown rendering. */
export const TIMEFRAMES: TimeframeByte[] = [1, 2, 3, 4, 5, 6, 7, 8, 9];

export interface StrategyDto {
  id: string;
  userId: string;
  name: string;
  description: string | null;
  symbol: string | null;
  timeframe: TimeframeByte | null;
  rules: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

/** Body for POST /api/strategies and PATCH /api/strategies/{id}. */
export interface UpsertStrategyRequest {
  name: string;
  description?: string | null;
  symbol?: string | null;
  timeframe?: TimeframeByte | null;
  rules?: string | null;
}

export interface StrategyAnalyticsDto {
  strategyId: string;
  name: string;
  tradeCount: number;
  winCount: number;
  lossCount: number;
  /** Decimal 0..1 (e.g. 0.5926). */
  winRate: number;
  totalPnl: string;
  expectancy: string;
  profitFactor: string | number;
  avgMfe: string | number | null;
  avgMae: string | number | null;
  lastTradeAt: string | null;
}

/** Body for PUT /api/trades/{tradeId}/strategy. null = untag. */
export interface SetTradeStrategyRequest {
  strategyId: string | null;
}

/** RFC 7807 problem shape returned by the backend on 422 / 404 / 5xx. */
export interface StrategyProblem {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  errors?: Record<string, string[]>;
}

/** Hard caps — mirror the backend validator so the trader gets instant feedback. */
export const NAME_MAX = 64;
export const DESCRIPTION_MAX = 1000;
export const RULES_MAX = 2000;