import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export type AssetClass = 1 | 2 | 3 | 4 | 5;
export type TradeDirection = 1 | 2; // 1=Long, 2=Short
export type TradeStatus = 1 | 2 | 3; // 1=Open, 2=Closed, 3=Cancelled

export interface TradeDto {
  id: string;
  userId: string;
  accountId: string;
  instrumentId: string;
  symbol: string;
  assetClass: AssetClass;
  direction: TradeDirection;
  status: TradeStatus;
  volume: number;
  volumeCurrency: string;
  entryPrice: number;
  entryPriceCurrency: string;
  exitPrice: number | null;
  exitPriceCurrency: string | null;
  pnl: number | null;
  pnlCurrency: string | null;
  accountCurrency: string;
  strategy: string | null;
  notes: string | null;
  openedAt: string;
  closedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface OpenTradeRequest {
  accountId: string;
  instrumentId: string;
  symbol: string;
  assetClass: AssetClass;
  direction: TradeDirection;
  volume: number;
  volumeCurrency: string;
  entryPrice: number;
  entryPriceCurrency: string;
  strategy: string | null;
  notes: string | null;
}

export interface PagedTradesDto {
  items: TradeDto[];
  total: number;
  page: number;
  pageSize: number;
}

export interface DashboardSummaryDto {
  totalCount: number;
  openCount: number;
  closedCount: number;
  winsCount: number;
  lossesCount: number;
  winRate: number;
  totalPnL: number;
  bestTrade: number;
  worstTrade: number;
  avgTrade: number;
  currency: string;
}

export interface CalendarDayDto {
  date: string;
  pnl: number;
  tradeCount: number;
}

export interface CalendarDto {
  year: number;
  month: number;
  days: CalendarDayDto[];
}

@Injectable({ providedIn: 'root' })
export class TradeApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/trades';

  async list(page: number, pageSize: number, status?: TradeStatus, symbol?: string): Promise<PagedTradesDto> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (status !== undefined && status !== null) params = params.set('status', status);
    if (symbol) params = params.set('symbol', symbol);
    return firstValueFrom(this.http.get<PagedTradesDto>(this.base, { params }));
  }

  async getById(id: string): Promise<TradeDto> {
    return firstValueFrom(this.http.get<TradeDto>(`${this.base}/${id}`));
  }

  async open(req: OpenTradeRequest): Promise<TradeDto> {
    return firstValueFrom(this.http.post<TradeDto>(this.base, req));
  }

  async close(id: string, exitPrice: number, exitPriceCurrency: string): Promise<TradeDto> {
    return firstValueFrom(this.http.put<TradeDto>(`${this.base}/${id}/close`, { exitPrice, exitPriceCurrency }));
  }

  async updateNotes(id: string, strategy: string | null, notes: string | null): Promise<TradeDto> {
    return firstValueFrom(this.http.patch<TradeDto>(`${this.base}/${id}`, { strategy, notes }));
  }

  async delete(id: string): Promise<void> {
    await firstValueFrom(this.http.delete<void>(`${this.base}/${id}`));
  }

  async dashboard(from?: string, to?: string): Promise<DashboardSummaryDto> {
    let params = new HttpParams();
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return firstValueFrom(this.http.get<DashboardSummaryDto>(`${this.base}/dashboard`, { params }));
  }

  async calendar(year?: number, month?: number): Promise<CalendarDto> {
    let params = new HttpParams();
    if (year !== undefined) params = params.set('year', year);
    if (month !== undefined) params = params.set('month', month);
    return firstValueFrom(this.http.get<CalendarDto>(`${this.base}/calendar`, { params }));
  }
}

// ===== UI helpers — mappers de enums enteros a strings =====

export const TRADE_DIRECTION_LABEL: Record<TradeDirection, 'Long' | 'Short'> = {
  1: 'Long',
  2: 'Short',
};

export const TRADE_STATUS_LABEL: Record<TradeStatus, 'Open' | 'Closed' | 'Cancelled'> = {
  1: 'Open',
  2: 'Closed',
  3: 'Cancelled',
};

export const ASSET_CLASS_LABEL: Record<AssetClass, string> = {
  1: 'Forex',
  2: 'Crypto',
  3: 'Binary',
  4: 'Commodity',
  5: 'Other',
};
