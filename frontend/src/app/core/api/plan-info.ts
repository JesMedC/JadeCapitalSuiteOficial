// ============================================================================
//  PlanInfo — DTO mirrored from
//  src/2.Modules/Billing/JadeCapital.Billing.PublicApi/Contracts/PlanInfo.cs
//
//  Wire shape from GET /api/billing/plans (Wave-1.3). AllowAnonymous
//  endpoint; the frontend consumes this BEFORE the user authenticates, so
//  the marketing pricing pages render the same prices the Admin catalog
//  controls (no more hardcoded PLANS arrays).
// ============================================================================

export interface PlanInfo {
  code: string;
  name: string;
  /** Monthly price in the plan's currency. Decimal on the backend, number here. */
  monthlyPrice: number;
  /** ISO-4217 3-letter currency code (e.g. "USD"). */
  currency: string;
  isEligibleForSelfService: boolean;
}
