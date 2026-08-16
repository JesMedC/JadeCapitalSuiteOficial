// ============================================================================
//  Alerts API types — slice 3b frontend.
//
//  Mirror of:
//   src/2.Modules/Trading/JadeCapital.Trading.Contracts/Alerts/AlertDtos.cs
//
//  Severity is a string ("Low" | "Medium" | "High") on the wire (System.Text.Json
//  enum-string policy is configured at the API host for these enums).
//  The FE maps string → color via SEVERITY_COLORS.
// ============================================================================

export interface AlertDto {
  id: string;
  ruleId: string;
  severity: 'Low' | 'Medium' | 'High';
  title: string;
  body: string;
  cta: AlertCtaDto | null;
  acknowledgedAt: string | null;
  expiresAt: string | null;
  createdAt: string;
}

export interface AlertCtaDto {
  route: string;
  label: string;
}

/** RFC 7807 problem shape returned by the backend on 422 / 404 / 5xx. */
export interface AlertProblem {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  errors?: Record<string, string[]>;
}

/** Severity color tokens. Match the SCSS tokens used by other trader pages. */
export const SEVERITY_COLORS: Record<AlertDto['severity'], string> = {
  High: 'var(--red, #ff4057)',
  Medium: 'var(--yellow, #f5b400)',
  Low: 'var(--blue, #4a90ff)',
};

/** Human-readable labels for the rule ids (used as a fallback when FE has no copy). */
export const RULE_LABELS: Record<string, string> = {
  NoTradesInDays: 'Sin trades hace días',
  DrawdownExceeded: 'Drawdown elevado',
  RRAverageBelow: 'R/R bajo',
  CurrentPriceNearStop: 'Trade abierto sin actualizar',
  OpenTradeOffPlan: 'Trade fuera del plan',
};