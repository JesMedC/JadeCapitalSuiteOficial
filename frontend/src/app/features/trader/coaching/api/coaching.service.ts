import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { CoachingPromptsDto, Period } from './coaching.types';

// ============================================================================
//  CoachingService — slice 2d.2 frontend.
//
//  Single endpoint wrapper for GET /api/coaching/prompts. Mirrors the
//  conventions of PatternsService / JournalService (auth interceptor adds
//  the Bearer token transparently).
// ============================================================================

@Injectable({ providedIn: 'root' })
export class CoachingService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/coaching/prompts';

  async getPrompts(period: Period): Promise<CoachingPromptsDto> {
    const params = new HttpParams().set('period', period);
    return firstValueFrom(this.http.get<CoachingPromptsDto>(this.base, { params }));
  }
}
