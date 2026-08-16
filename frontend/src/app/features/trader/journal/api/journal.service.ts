import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { JournalEntryDto, UpsertJournalEntryRequest } from './journal.types';

// ============================================================================
//  JournalService — slice 2a.2 frontend.
//
//  Wraps the 4 endpoints from slice 2a.1:
//  - GET    /api/journal/today         → 200 DTO | 404 (no entry yet)
//  - GET    /api/journal?from=&to=     → 200 DTO[]
//  - POST   /api/journal/today         → 200 DTO | 400 | 422
//  - DELETE /api/journal/{id}          → 204 | 404
//
//  Auth: the auth.interceptor adds the Bearer token; we never set it here.
//  Same pattern as trade-api.service / risk-profile.service.
// ============================================================================

@Injectable({ providedIn: 'root' })
export class JournalService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/journal';

  /**
   * Returns today's entry or `null` when the user hasn't written one yet.
   * Any non-404 error is rethrown so the state can surface it via the banner.
   */
  async getToday(): Promise<JournalEntryDto | null> {
    try {
      return await firstValueFrom(this.http.get<JournalEntryDto>(`${this.base}/today`));
    } catch (e) {
      if (e instanceof HttpErrorResponse && e.status === 404) return null;
      throw e;
    }
  }

  /** Lists entries between two dates inclusive. `from` and `to` are YYYY-MM-DD. */
  async getRange(from: string, to: string): Promise<JournalEntryDto[]> {
    const params = new HttpParams().set('from', from).set('to', to);
    return firstValueFrom(this.http.get<JournalEntryDto[]>(this.base, { params }));
  }

  /** Upserts today's entry. Partial body is OK — the backend merges with existing fields. */
  async upsertToday(body: UpsertJournalEntryRequest): Promise<JournalEntryDto> {
    return firstValueFrom(this.http.post<JournalEntryDto>(`${this.base}/today`, body));
  }

  /** Hard-deletes an entry. Returns void on 204; throws on any non-2xx. */
  async delete(id: string): Promise<void> {
    await firstValueFrom(this.http.delete<void>(`${this.base}/${id}`));
  }
}
