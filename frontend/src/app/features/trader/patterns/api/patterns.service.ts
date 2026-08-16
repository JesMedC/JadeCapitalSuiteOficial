import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { BehavioralAnalysisDto, Period } from './patterns.types';

// ============================================================================
//  PatternsService — slice 2b.2 frontend.
//
//  Wraps the single endpoint from slice 2b.1:
//   GET /api/trades/behavioral?period=7d|30d|90d|all → 200 BehavioralAnalysisDto
//
//  Auth: the auth.interceptor adds the Bearer token; we never set it here.
//  Same pattern as journal.service / trade-api.service.
// ============================================================================

@Injectable({ providedIn: 'root' })
export class PatternsService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/trades/behavioral';

  /**
   * Returns the user's behavioral analysis for the given period.
   * Empty history returns `{ events: [], aggregations: { ...zeros... } }`
   * with HTTP 200 — the page handles that as the empty state.
   */
  async getAnalysis(period: Period): Promise<BehavioralAnalysisDto> {
    const params = new HttpParams().set('period', period);
    return firstValueFrom(
      this.http.get<BehavioralAnalysisDto>(this.base, { params }),
    );
  }
}
