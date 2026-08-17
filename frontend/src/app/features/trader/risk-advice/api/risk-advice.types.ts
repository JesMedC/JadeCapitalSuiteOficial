// ============================================================================
//  Risk-advice types — slice 5c.1 (Wave 5, AI Risk Advisor Pre-Trade).
//
//  Wire shape for the POST /api/ai/risk-advice and
//  GET /api/ai/risk-advice/{tradeId} endpoints. The action string is
//  lowercase ("allow" | "warning" | "block") to match the BE convention.
// ============================================================================

export interface RiskAdviceRequest {
  symbol: string;
  direction: string;
  volume: number;
  volumeCurrency: string;
  entryPrice: number;
  stopLoss?: number | null;
  riskRewardAtEntry: number;
  setupQuality: string;
}

export interface RiskAdviceDto {
  id: string;
  userId: string;
  tradeId?: string | null;
  action: 'allow' | 'warning' | 'block';
  reason: string;
  model: string;
  latencyMs: number;
  createdAt: string;
}

export interface AiHealthDto {
  status: 'ok' | 'down';
  model?: string | null;
}
