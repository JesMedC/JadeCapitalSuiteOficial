import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  RiskProfileDto,
  RiskProfileError,
  UpsertRiskProfileRequest,
} from './risk-profile.types';

// ============================================================================
//  RiskProfileService — slice 1a.2 frontend.
//
//  Wraps the two endpoints from slice 1a.1:
//  - GET /api/risk-profile  → 200 with DTO, or 404 when the user has no active profile.
//  - PUT /api/risk-profile  → 200 with DTO, 422 on range validation, 409 on supersede conflict.
//
//  Auth: the `auth.interceptor` adds the Bearer token automatically; do not
//  repeat it here. Same pattern as plan-api.service / instrument-api.service.
// ============================================================================

@Injectable({ providedIn: 'root' })
export class RiskProfileService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/risk-profile';

  /**
   * Returns the active profile, or `null` when the user has none yet (404).
   * Any other HTTP error is rethrown as a `RiskProfileError` so the state can
   * surface it through the banner.
   */
  async getActive(): Promise<RiskProfileDto | null> {
    try {
      return await firstValueFrom(this.http.get<RiskProfileDto>(this.base));
    } catch (e) {
      if (e instanceof HttpErrorResponse && e.status === 404) {
        return null;
      }
      throw toRiskProfileError(e);
    }
  }

  /**
   * Upserts the active profile. Throws a `RiskProfileError` on 422 / 409 / 5xx.
   */
  async upsert(request: UpsertRiskProfileRequest): Promise<RiskProfileDto> {
    try {
      return await firstValueFrom(this.http.put<RiskProfileDto>(this.base, request));
    } catch (e) {
      throw toRiskProfileError(e);
    }
  }
}

/** Normalizes an HttpErrorResponse (or any error) into a `RiskProfileError`. */
export function toRiskProfileError(e: unknown): RiskProfileError {
  if (e instanceof HttpErrorResponse) {
    const body = (e.error ?? {}) as {
      code?: string;
      detail?: string;
      title?: string;
      errors?: Record<string, string[]>;
    };
    const code = body.code ?? '';
    const message = body.detail ?? body.title ?? e.message ?? `Error HTTP ${e.status}.`;
    const field = deriveFieldFromCode(code);
    return { status: e.status, code, message, field: field ?? undefined };
  }
  if (e instanceof Error) {
    return { status: 0, code: 'unknown', message: e.message };
  }
  return { status: 0, code: 'unknown', message: 'Ocurrió un error inesperado.' };
}

const VALIDATION_FIELD_HINTS: Record<string, string> = {
  capital_amount_invalid: 'capitalAmount',
  capital_amount_out_of_range: 'capitalAmount',
  max_drawdown_percent_out_of_range: 'maxDrawdownPercent',
  risk_per_trade_percent_out_of_range: 'riskPerTradePercent',
  risk_reward_target_invalid: 'riskRewardTarget',
  risk_reward_target_out_of_range: 'riskRewardTarget',
  capital_currency_invalid: 'capitalCurrency',
};

/** Pulls a field name out of a `validation.risk_profile.<field>_<reason>` code. */
function deriveFieldFromCode(code: string | undefined): string | null {
  if (!code) return null;
  const m = code.match(/^validation\.risk_profile\.(.+)$/);
  if (!m) return null;
  const suffix = m[1]!;
  return VALIDATION_FIELD_HINTS[suffix] ?? null;
}
