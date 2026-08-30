import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AiHealthDto, RiskAdviceDto, RiskAdviceRequest } from './risk-advice.types';

// ============================================================================
//  RiskAdviceService — slice 5c.1 (Wave 5, AI Risk Advisor Pre-Trade).
//
//  Three HTTP wrappers:
//   - GET  /api/ai/health                          — slice 5b.1 (shared)
//   - POST /api/ai/risk-advice                     — slice 5c.1 (manual)
//   - GET  /api/ai/risk-advice/{tradeId}           — slice 5c.1 (cached)
//
//  Strategy: thin pass-through to the HTTP client. The state layer
//  (risk-advice.state.ts) handles the signals + loading flags.
// ============================================================================

@Injectable({ providedIn: 'root' })
export class RiskAdviceService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/ai';

  /** Probe the AI provider (Ollama / cloud). Returns 200 on healthy, 503 on down. */
  async getHealth(): Promise<AiHealthDto> {
    return firstValueFrom(this.http.get<AiHealthDto>(`${this.base}/health`));
  }

  /** Run a manual AI risk-advisor pass over the supplied trade parameters. */
  async requestAdvice(payload: RiskAdviceRequest): Promise<RiskAdviceDto> {
    return firstValueFrom(this.http.post<RiskAdviceDto>(`${this.base}/risk-advice`, payload));
  }

  /** Fetch the cached advisory attached to a trade at OpenTrade time. */
  async getCachedAdvice(tradeId: string): Promise<RiskAdviceDto> {
    return firstValueFrom(this.http.get<RiskAdviceDto>(`${this.base}/risk-advice/${tradeId}`));
  }
}
