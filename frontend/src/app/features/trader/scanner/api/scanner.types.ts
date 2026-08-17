/** Shape returned by the scanner endpoints. Mirrors ScannerFilterDto + ScanResultDto backend. */

export interface ScannerFilterDto {
  id: string;
  name: string;
  minSpread: number | null;
  maxSpread: number | null;
  minVolume: number | null;
  minRiskReward: number | null;
  volatilityWindow: VolatilityWindow;
  activeHours: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface UpsertScannerFilterRequest {
  name: string;
  minSpread: number | null;
  maxSpread: number | null;
  minVolume: number | null;
  minRiskReward: number | null;
  volatilityWindow: VolatilityWindow;
  activeHours: string | null;
}

export interface ScanResultDto {
  symbol: string;
  assetClass: number;
  historicalRiskReward: number;
  totalTrades: number;
  totalPnl: number;
  matchedCriteria: string[];
}

export interface RunScannerRequest {
  filterId: string;
  limit?: number;
}

export type VolatilityWindow = 1 | 7 | 30;

export const VOLATILITY_LABELS: Readonly<Record<VolatilityWindow, string>> = {
  1: 'Diario (1d)',
  7: 'Semanal (7d)',
  30: 'Mensual (30d)',
};
