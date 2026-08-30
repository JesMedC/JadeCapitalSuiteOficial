import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

// ============================================================================
//  TradeMfeMaeService — slice 2c (Trader Journal Core / MFE/MAE Charts).
//
//  Wraps the single endpoint:
//    GET /api/trades/{tradeId}/mfe-mae  → TradeMfeMaeDto
//
//  Same pattern as JournalService / RiskProfileService / BehavioralAnalysis
//  (slice 2b): async method, firstValueFrom, Auth Bearer via interceptor.
//  No 404 special-casing — the FE caller (the trades list / detail page)
//  decides what to do when the trade is missing or cross-user.
// ============================================================================

export interface MfeMaeBucket {
  count: number;
  totalMagnitude: number;
  avgMagnitude: number;
}

export interface TradeMfeMaeDto {
  tradeId: string;
  direction: 'Long' | 'Short';
  isWinner: boolean;
  mfeAmount: number | null;
  maeAmount: number | null;
  currency: string;
  aggregate: {
    mfeLongWinners: MfeMaeBucket;
    mfeLongLosers: MfeMaeBucket;
    mfeShortWinners: MfeMaeBucket;
    mfeShortLosers: MfeMaeBucket;
    maeLongWinners: MfeMaeBucket;
    maeLongLosers: MfeMaeBucket;
    maeShortWinners: MfeMaeBucket;
    maeShortLosers: MfeMaeBucket;
  };
}

@Injectable({ providedIn: 'root' })
export class TradeMfeMaeService {
  private readonly http = inject(HttpClient);

  /** Loads the MFE/MAE snapshot + user-level histograms for a single trade. */
  async getForTrade(tradeId: string): Promise<TradeMfeMaeDto> {
    return firstValueFrom(
      this.http.get<TradeMfeMaeDto>(`/api/trades/${tradeId}/mfe-mae`),
    );
  }
}