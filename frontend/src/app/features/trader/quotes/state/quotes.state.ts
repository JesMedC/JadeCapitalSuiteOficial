import { computed, inject, Injectable, signal } from '@angular/core';
import { QuoteDto } from '../api/quotes.types';
import { QuotesService } from '../api/quotes.service';

const DEFAULT_SYMBOLS = ['EURUSD', 'GBPJPY', 'BTCUSD', 'USDJPY'];

@Injectable({ providedIn: 'root' })
export class QuotesState {
  private readonly svc = inject(QuotesService);

  readonly quotes = signal<QuoteDto[]>([]);
  readonly isLoading = signal(false);
  readonly error = signal<string | null>(null);
  readonly subscribedSymbols = signal<string[]>(DEFAULT_SYMBOLS);

  readonly hasQuotes = computed(() => this.quotes().length > 0);

  async loadDefault(): Promise<void> {
    await this.loadSymbols(DEFAULT_SYMBOLS);
  }

  async loadSymbols(symbols: string[]): Promise<void> {
    if (symbols.length === 0) {
      this.quotes.set([]);
      return;
    }
    this.isLoading.set(true);
    this.error.set(null);
    try {
      const list = await this.svc.getBulk(symbols);
      this.quotes.set(list);
    } catch (err: any) {
      this.error.set(this.formatError(err));
    } finally {
      this.isLoading.set(false);
    }
  }

  clearError(): void { this.error.set(null); }

  private formatError(err: any): string {
    if (err?.error?.detail) return String(err.error.detail);
    if (err?.error?.title) return String(err.error.title);
    if (err?.message) return String(err.message);
    return 'No pudimos obtener las cotizaciones.';
  }
}
