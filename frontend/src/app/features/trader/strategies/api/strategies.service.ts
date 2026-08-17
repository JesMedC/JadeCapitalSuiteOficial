import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  SetTradeStrategyRequest,
  StrategyAnalyticsDto,
  StrategyDto,
  UpsertStrategyRequest,
} from './strategies.types';

// ============================================================================
//  StrategiesService — slice 3a frontend.
//
//  Wraps the 6 strategy endpoints + 1 trade-tagging endpoint:
//   - GET    /api/strategies?activeOnly=        → 200 StrategyDto[]
//   - GET    /api/strategies/{id}/analytics    → 200 StrategyAnalyticsDto
//   - POST   /api/strategies                   → 200 StrategyDto
//   - PATCH  /api/strategies/{id}              → 200 StrategyDto
//   - DELETE /api/strategies/{id}              → 204
//   - PUT    /api/trades/{tradeId}/strategy    → 200 StrategyDto
//
//  Auth: auth.interceptor adds Bearer; we never set it here.
// ============================================================================

@Injectable({ providedIn: 'root' })
export class StrategiesService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/strategies';

  async list(activeOnly: boolean = true): Promise<StrategyDto[]> {
    const params = new HttpParams().set('activeOnly', String(activeOnly));
    return firstValueFrom(this.http.get<StrategyDto[]>(this.base, { params }));
  }

  async create(body: UpsertStrategyRequest): Promise<StrategyDto> {
    return firstValueFrom(this.http.post<StrategyDto>(this.base, body));
  }

  async update(id: string, body: UpsertStrategyRequest): Promise<StrategyDto> {
    return firstValueFrom(this.http.patch<StrategyDto>(`${this.base}/${id}`, body));
  }

  async archive(id: string): Promise<void> {
    await firstValueFrom(this.http.delete<void>(`${this.base}/${id}`));
  }

  async getAnalytics(id: string): Promise<StrategyAnalyticsDto> {
    return firstValueFrom(this.http.get<StrategyAnalyticsDto>(
      `${this.base}/${id}/analytics`));
  }

  async setTradeStrategy(
    tradeId: string,
    body: SetTradeStrategyRequest,
  ): Promise<StrategyDto> {
    try {
      return await firstValueFrom(this.http.put<StrategyDto>(
        `/api/trades/${tradeId}/strategy`, body));
    } catch (e) {
      // Untag returns a sentinel DTO (id = Guid.Empty). We rethrow anything else.
      if (e instanceof HttpErrorResponse) throw e;
      throw e;
    }
  }
}