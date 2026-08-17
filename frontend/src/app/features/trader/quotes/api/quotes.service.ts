import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { QuoteDto } from './quotes.types';

@Injectable({ providedIn: 'root' })
export class QuotesService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/quotes';

  async getBySymbol(symbol: string): Promise<QuoteDto> {
    return firstValueFrom(this.http.get<QuoteDto>(`${this.base}/${symbol}`));
  }

  async getBulk(symbols: string[]): Promise<QuoteDto[]> {
    const csv = symbols.join(',');
    return firstValueFrom(this.http.get<QuoteDto[]>(`${this.base}?symbols=${encodeURIComponent(csv)}`));
  }
}
