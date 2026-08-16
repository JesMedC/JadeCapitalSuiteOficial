// ============================================================================
//  Patterns API types — slice 2b.2 frontend.
//
//  Mirror of:
//   src/2.Modules/Trading/JadeCapital.Trading.Contracts/Behavioral/BehavioralDtos.cs
//   src/2.Modules/Trading/JadeCapital.Trading.Domain/Behavioral/BehavioralModels.cs
//   GET /api/trades/behavioral?period=7d|30d|90d|all
//
//  Severity renders as: 'low' (blue), 'medium' (yellow), 'high' (red).
//  Bucket keys are exactly the wire shape: 'low_1_2' | 'mid_3' | 'high_4_5'.
// ============================================================================

export type Period = '7d' | '30d' | '90d' | 'all';
export const PERIODS: Period[] = ['7d', '30d', '90d', 'all'];

export type Severity = 'low' | 'medium' | 'high';

export interface BehavioralEventDto {
  ruleId: string;
  severity: Severity;
  tradeIds: string[];
  occurredAt: string;
  message: string;
}

export interface EmotionalityBucketDto {
  count: number;
  winRate: number;
  totalPnl: number;
}

export interface BehavioralAggregationsDto {
  byEmotionality: Record<string, EmotionalityBucketDto>;
}

export interface BehavioralAnalysisDto {
  period: Period;
  windowStart: string;
  windowEnd: string;
  events: BehavioralEventDto[];
  aggregations: BehavioralAggregationsDto;
}

/** Stable order for rendering buckets. */
export const EMOTIONALITY_BUCKET_KEYS: readonly string[] = [
  'low_1_2',
  'mid_3',
  'high_4_5',
] as const;

/** Human-readable bucket labels for the patterns page. */
export const EMOTIONALITY_BUCKET_LABELS: Record<string, string> = {
  low_1_2: 'Bajo (1-2)',
  mid_3: 'Medio (3)',
  high_4_5: 'Alto (4-5)',
};

/** RFC 7807 problem shape returned by the backend on 422 / 401. */
export interface PatternsProblem {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
}
