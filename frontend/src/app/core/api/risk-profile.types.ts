// ============================================================================
//  Risk Profile API types — slice 1a.2 frontend.
//
//  Mirror of:
//  - src/2.Modules/Identity/JadeCapital.Identity.Contracts/RiskProfiles/RiskProfileContracts.cs
//  - src/2.Modules/Identity/JadeCapital.Identity.Api/Endpoints/RiskProfileEndpoints.cs
//
//  RFC 7807 problem shape returned by the backend. The `code` is the
//  domain-level error code (e.g. "validation.risk_profile.risk_per_trade_percent_out_of_range",
//  "conflict.risk_profile.concurrent_supersede"). The component uses it to
//  render field-level errors vs. banner-level errors.
// ============================================================================

export interface RiskProfileDto {
  id: string;
  userId: string;
  capitalAmount: number;
  capitalCurrency: string;
  maxDrawdownPercent: number;
  riskPerTradePercent: number;
  riskRewardTarget: number;
  isActive: boolean;
  supersededAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface UpsertRiskProfileRequest {
  capitalAmount: number;
  capitalCurrency: string;
  maxDrawdownPercent: number;
  riskPerTradePercent: number;
  riskRewardTarget: number;
}

/** Shape of an RFC 7807 problem. Backend returns `type`, `title`, `detail`, `status` and a `code` extension. */
export interface RiskProfileProblem {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  /** Domain error code (e.g. "validation.risk_profile.risk_per_trade_percent_out_of_range"). */
  code?: string;
  /** Optional field-level errors keyed by field name. Backend may emit these as part of `errors`. */
  errors?: Record<string, string[]>;
}

/** Normalized error type used by the state and the component. */
export interface RiskProfileError {
  status: number;
  code: string;
  message: string;
  /** Field name produced by 422 validation errors (e.g. "riskPerTradePercent"). Derived from the `code` suffix. */
  field?: string;
}
