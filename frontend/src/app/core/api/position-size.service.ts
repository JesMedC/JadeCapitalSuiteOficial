import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

// ============================================================================
//  PositionSizeService — slice 1b frontend.
//
//  Wraps `POST /api/trades/position-size/calculate` (slice 1b backend).
//  Returns a PositionSizeCalcResult on success, or throws a typed
//  PositionSizeError so the component can render the right copy.
//
//  Auth: the `auth.interceptor` adds the Bearer token automatically.
// ============================================================================

export interface PositionSizeCalcResult {
  volume: number;
  riskAmount: number;
  riskPerTradePercent: number;
  currency: string;
  calculation: string;
  recommendedStopLossDistance: number | null;
}

export interface PositionSizeRequest {
  stopLossDistance: number;
  /** Null = use the active profile's `riskPerTradePercent`. */
  riskPerTradeOverride: number | null;
  /** Quote currency of the symbol (e.g. "USD" for EUR/USD). */
  currency: string;
}

export interface PositionSizeError {
  status: number;
  code: string;
  message: string;
}

@Injectable({ providedIn: 'root' })
export class PositionSizeService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/trades/position-size/calculate';

  /**
   * Calls the calculator endpoint. Throws a `PositionSizeError` on any
   * non-2xx response (404 = no active risk profile; 422 = validation).
   */
  async calculate(request: PositionSizeRequest): Promise<PositionSizeCalcResult> {
    try {
      return await firstValueFrom(
        this.http.post<PositionSizeCalcResult>(this.base, request),
      );
    } catch (e) {
      throw toPositionSizeError(e);
    }
  }
}

/** Normalizes an HttpErrorResponse (or any error) into a `PositionSizeError`. */
export function toPositionSizeError(e: unknown): PositionSizeError {
  if (e instanceof HttpErrorResponse) {
    const body = (e.error ?? {}) as { code?: string; detail?: string; title?: string };
    const code = body.code ?? '';
    const message = body.detail ?? body.title ?? e.message ?? `Error HTTP ${e.status}.`;
    return { status: e.status, code, message };
  }
  if (e instanceof Error) {
    return { status: 0, code: 'unknown', message: e.message };
  }
  return { status: 0, code: 'unknown', message: 'Ocurrió un error inesperado.' };
}
