import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export type MetricsPeriod = '7d' | '30d' | '90d' | 'all';

export interface EquityPoint {
  timestamp: string;
  equity: number;
  drawdown: number;
}

export interface SymbolStat {
  symbol: string;
  trades: number;
  totalPnl: number;
  winRate: number;
}

export interface MetricsDto {
  period: string;
  totalTrades: number;
  totalClosedTrades: number;
  totalOpenTrades: number;
  winRate: number;
  expectancy: number;
  profitFactor: number;
  payoff: number;
  sqn: number;
  maxDrawdown: number;
  maxDrawdownAmount: number;
  maxDrawdownPercent: number;
  equityCurve: EquityPoint[];
  symbolStats: SymbolStat[];
  currency: string;
}

@Injectable({ providedIn: 'root' })
export class MetricsApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/trades/metrics';

  async get(period: MetricsPeriod): Promise<MetricsDto> {
    const params = new HttpParams().set('period', period);
    return firstValueFrom(this.http.get<MetricsDto>(this.base, { params }));
  }
}
