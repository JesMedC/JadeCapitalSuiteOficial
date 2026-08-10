import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export type AssetClass = 1 | 2 | 3 | 4 | 5;

export interface InstrumentDto {
  id: string;
  symbol: string;
  assetClass: AssetClass;
  contractSize: number;
  decimalPlaces: number;
  pipValue: number;
  payoutPercent: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export const ASSET_CLASS_LABELS: Record<AssetClass, string> = {
  1: 'Forex',
  2: 'Crypto',
  3: 'Binary',
  4: 'Commodity',
  5: 'Other',
};

@Injectable({ providedIn: 'root' })
export class InstrumentApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/instruments';

  async list(activeOnly = true): Promise<InstrumentDto[]> {
    const params = new HttpParams().set('activeOnly', String(activeOnly));
    return firstValueFrom(this.http.get<InstrumentDto[]>(this.base, { params }));
  }

  async getById(id: string): Promise<InstrumentDto> {
    return firstValueFrom(this.http.get<InstrumentDto>(`${this.base}/${id}`));
  }

  async create(req: Omit<InstrumentDto, 'id' | 'isActive' | 'createdAt' | 'updatedAt'>): Promise<InstrumentDto> {
    return firstValueFrom(this.http.post<InstrumentDto>(this.base, req));
  }

  async update(id: string, req: Pick<InstrumentDto, 'symbol' | 'assetClass' | 'contractSize' | 'decimalPlaces' | 'pipValue' | 'payoutPercent'>): Promise<InstrumentDto> {
    return firstValueFrom(this.http.patch<InstrumentDto>(`${this.base}/${id}`, req));
  }

  async deactivate(id: string): Promise<InstrumentDto> {
    return firstValueFrom(this.http.post<InstrumentDto>(`${this.base}/${id}/deactivate`, {}));
  }

  async delete(id: string): Promise<void> {
    await firstValueFrom(this.http.delete<void>(`${this.base}/${id}`));
  }
}
