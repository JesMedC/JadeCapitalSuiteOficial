import { Injectable, computed, inject, signal } from '@angular/core';
import { AccountApiService, AccountDto, MarketType } from '../api/account-api.service';

const CACHE_TTL_MS = 60_000;

@Injectable({ providedIn: 'root' })
export class AccountState {
  private readonly api = inject(AccountApiService);

  readonly accounts = signal<AccountDto[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  private lastLoaded = signal<number>(0);

  readonly activeAccounts = computed(() => this.accounts().filter(a => a.isActive));
  readonly hasAccounts = computed(() => this.activeAccounts().length > 0);
  readonly hasForexAccounts = computed(() => this.activeAccounts().some(a => a.marketType === 1));
  readonly hasBinaryAccounts = computed(() => this.activeAccounts().some(a => a.marketType === 2));

  async load(force = false): Promise<void> {
    const stale = Date.now() - this.lastLoaded() > CACHE_TTL_MS;
    if (!force && this.accounts().length > 0 && !stale) {
      return;
    }

    this.loading.set(true);
    this.error.set(null);
    try {
      const items = await this.api.list();
      this.accounts.set(items);
      this.lastLoaded.set(Date.now());
    } catch (e: any) {
      this.error.set(e?.error?.detail || e?.message || 'No se pudieron cargar las cuentas.');
    } finally {
      this.loading.set(false);
    }
  }

  async create(req: Omit<AccountDto, 'id' | 'userId' | 'isActive' | 'createdAt' | 'updatedAt'>): Promise<AccountDto> {
    const created = await this.api.create(req);
    await this.load(true);
    return created;
  }

  async update(id: string, req: Parameters<AccountApiService['update']>[1]): Promise<AccountDto> {
    const updated = await this.api.update(id, req);
    await this.load(true);
    return updated;
  }

  async deactivate(id: string): Promise<AccountDto> {
    const updated = await this.api.deactivate(id);
    await this.load(true);
    return updated;
  }

  async reactivate(id: string): Promise<AccountDto> {
    const updated = await this.api.reactivate(id);
    await this.load(true);
    return updated;
  }

  async delete(id: string): Promise<void> {
    await this.api.delete(id);
    await this.load(true);
  }

  findById(id: string): AccountDto | undefined {
    return this.accounts().find(a => a.id === id);
  }

  invalidate(): void {
    this.accounts.set([]);
    this.lastLoaded.set(0);
  }
}
