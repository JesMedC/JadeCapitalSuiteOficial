import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { MarketType } from '@core/api/account-api.service';

/**
 * AssetClass flags. Stored as a bitmask on the backend so a single instrument
 * can belong to multiple classes (e.g. EUR/USD trading on both Forex and Binary).
 *
 *   1  = Forex
 *   2  = Crypto
 *   4  = Binary
 *   8  = Commodity
 *   16 = Other
 */
export type AssetClass = 1 | 2 | 4 | 8 | 16;

export const ASSET_CLASS_FLAGS: ReadonlyArray<{
  value: AssetClass;
  label: string;
  short: string;
  dotClass: string;
}> = [
  { value: 1,  label: 'Forex',     short: 'FX',  dotClass: 'asset-dot--forex' },
  { value: 2,  label: 'Cripto',    short: 'CR',  dotClass: 'asset-dot--crypto' },
  { value: 4,  label: 'Binarias',  short: 'BIN', dotClass: 'asset-dot--binary' },
  { value: 8,  label: 'Commodity', short: 'COM', dotClass: 'asset-dot--commodity' },
  { value: 16, label: 'Otro',      short: 'OTH', dotClass: 'asset-dot--other' },
];

export const ASSET_CLASS_LABELS: Record<AssetClass, string> = ASSET_CLASS_FLAGS.reduce(
  (acc, flag) => {
    acc[flag.value] = flag.label;
    return acc;
  },
  {} as Record<AssetClass, string>,
);

export function hasAssetClass(bitmask: number, flag: AssetClass): boolean {
  return (bitmask & flag) === flag;
}

export function toggleAssetClass(bitmask: number, flag: AssetClass): number {
  return bitmask ^ flag;
}

export function setAssetClass(bitmask: number, flag: AssetClass, on: boolean): number {
  return on ? bitmask | flag : bitmask & ~flag;
}

export function activeAssetClasses(bitmask: number): AssetClass[] {
  return ASSET_CLASS_FLAGS.filter(f => hasAssetClass(bitmask, f.value)).map(f => f.value);
}

export function assetClassLabel(flag: AssetClass): string {
  return ASSET_CLASS_LABELS[flag];
}

/**
 * Map an Account's MarketType to the AssetClass bit the user expects on its
 * instruments. Forex accounts see instruments with the Forex bit; Binary
 * accounts see instruments with the Binary bit.
 */
export const MARKET_TYPE_TO_ASSET_CLASS: Record<MarketType, AssetClass> = {
  1: 1,  // Forex  -> Forex bit
  2: 4,  // Binary -> Binary bit
};

export interface InstrumentDto {
  id: string;
  symbol: string;
  /** Bitmask: see {@link AssetClass}. */
  assetClasses: number;
  contractSize: number;
  decimalPlaces: number;
  pipValue: number;
  payoutPercent: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateInstrumentRequest {
  symbol: string;
  assetClasses: number;
  contractSize?: number;
  decimalPlaces?: number;
  pipValue?: number;
  payoutPercent?: number;
}

export interface UpdateInstrumentRequest {
  symbol: string;
  assetClasses: number;
  contractSize: number;
  decimalPlaces: number;
  pipValue: number;
  payoutPercent: number;
}

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

  async create(req: CreateInstrumentRequest): Promise<InstrumentDto> {
    return firstValueFrom(this.http.post<InstrumentDto>(this.base, req));
  }

  async update(id: string, req: UpdateInstrumentRequest): Promise<InstrumentDto> {
    return firstValueFrom(this.http.patch<InstrumentDto>(`${this.base}/${id}`, req));
  }

  async deactivate(id: string): Promise<InstrumentDto> {
    return firstValueFrom(this.http.post<InstrumentDto>(`${this.base}/${id}/deactivate`, {}));
  }

  async delete(id: string): Promise<void> {
    await firstValueFrom(this.http.delete<void>(`${this.base}/${id}`));
  }
}
