import { Injectable, computed, inject, signal } from '@angular/core';
import {
  InstrumentApiService,
  InstrumentDto,
  MARKET_TYPE_TO_ASSET_CLASS,
  hasAssetClass,
} from '../api/instrument-api.service';
import { MarketType } from '../api/account-api.service';

const CACHE_TTL_MS = 60_000;

@Injectable({ providedIn: 'root' })
export class InstrumentState {
  private readonly api = inject(InstrumentApiService);

  readonly instruments = signal<InstrumentDto[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  private lastLoaded = signal<number>(0);

  readonly activeInstruments = computed(() => this.instruments().filter(i => i.isActive));
  readonly hasInstruments = computed(() => this.activeInstruments().length > 0);

  forMarketType(marketType: MarketType | null | undefined): InstrumentDto[] {
    if (!marketType) return this.activeInstruments();
    const requiredBit = MARKET_TYPE_TO_ASSET_CLASS[marketType];
    return this.activeInstruments().filter(i => hasAssetClass(i.assetClasses, requiredBit));
  }

  async load(force = false): Promise<void> {
    const stale = Date.now() - this.lastLoaded() > CACHE_TTL_MS;
    if (!force && this.instruments().length > 0 && !stale) return;

    this.loading.set(true);
    this.error.set(null);
    try {
      const items = await this.api.list();
      this.instruments.set(items);
      this.lastLoaded.set(Date.now());
    } catch (e: any) {
      this.error.set(e?.error?.detail || e?.message || 'No se pudieron cargar los instrumentos.');
    } finally {
      this.loading.set(false);
    }
  }

  async create(req: Parameters<InstrumentApiService['create']>[0]): Promise<InstrumentDto> {
    const created = await this.api.create(req);
    await this.load(true);
    return created;
  }

  async update(id: string, req: Parameters<InstrumentApiService['update']>[1]): Promise<InstrumentDto> {
    const updated = await this.api.update(id, req);
    await this.load(true);
    return updated;
  }

  async deactivate(id: string): Promise<InstrumentDto> {
    const updated = await this.api.deactivate(id);
    await this.load(true);
    return updated;
  }

  async delete(id: string): Promise<void> {
    await this.api.delete(id);
    await this.load(true);
  }

  findById(id: string): InstrumentDto | undefined {
    return this.instruments().find(i => i.id === id);
  }

  invalidate(): void {
    this.instruments.set([]);
    this.lastLoaded.set(0);
  }
}
