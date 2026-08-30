import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AiCoachingPromptsDto, AiCoachingPromptDto, CoachingPromptsDto, Period } from './coaching.types';

// ============================================================================
//  CoachingService — slice 2d.2 frontend + slice 5b.2 (AI prompts).
//
//  Two endpoint wrappers:
//   - GET /api/coaching/prompts       — rule-based prompts (Wave 3b).
//   - GET /api/coaching/ai-prompts    — AI prompts (slice 5b.2).
//
//  The FE merges both on the page side (per design D5) so each endpoint
//  stays independent and the eventual server-side merge in a later slice
//  is non-breaking.
// ============================================================================

@Injectable({ providedIn: 'root' })
export class CoachingService {
  private readonly http = inject(HttpClient);

  /** Rule-based prompts (Wave 3b). */
  async getPrompts(period: Period): Promise<CoachingPromptsDto> {
    const params = new HttpParams().set('period', period);
    return firstValueFrom(
      this.http.get<CoachingPromptsDto>('/api/coaching/prompts', { params })
    );
  }

  /**
   * AI-generated prompts (slice 5b.2). Sorted by createdAt DESC. Empty
   * payloads (`prompts: []`) are valid — the page renders the empty state.
   */
  async getAiPrompts(period: Period): Promise<AiCoachingPromptsDto> {
    const params = new HttpParams().set('period', period);
    return firstValueFrom(
      this.http.get<AiCoachingPromptsDto>('/api/coaching/ai-prompts', { params })
    );
  }
}
